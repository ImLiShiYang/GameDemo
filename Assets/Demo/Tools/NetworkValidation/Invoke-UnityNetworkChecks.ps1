[CmdletBinding()]
param([ValidateSet('State', 'Compile', 'Physics')][string]$Action = 'Physics')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$bridge = Get-Content (Join-Path $projectRoot '.com-unity-codely.json') -Raw | ConvertFrom-Json
if ($bridge.unity_port -le 0) { throw 'The project Unity editor bridge is not ready.' }
$command = switch ($Action) {
    'State' { @{type='manage_editor'; params=@{action='get_state'}} }
    'Compile' { @{type='manage_editor'; params=@{action='start_compilation_pipeline'; timeoutSeconds=120}} }
    'Physics' { @{type='execute_csharp_script'; params=@{script='return NetworkCharacterPhysicsChecks.Run();'; execution_mode='editor'; capture_logs=$true}} }
}
$client = [Net.Sockets.TcpClient]::new()
$client.ReceiveTimeout = 150000
$client.SendTimeout = 10000
try {
    $client.Connect('127.0.0.1', [int]$bridge.unity_port)
    $stream = $client.GetStream()
    $greeting = [Collections.Generic.List[byte]]::new()
    do { $value = $stream.ReadByte(); if ($value -lt 0) { throw 'Bridge closed during handshake.' }; $greeting.Add([byte]$value) } while ($value -ne 10 -and $greeting.Count -lt 512)
    if (-not [Text.Encoding]::ASCII.GetString($greeting.ToArray()).Contains('FRAMING=1')) { throw 'Unsupported Unity bridge framing.' }
    $payload = [Text.Encoding]::UTF8.GetBytes(($command | ConvertTo-Json -Depth 8 -Compress))
    $header = [BitConverter]::GetBytes([uint64]$payload.Length)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($header) }
    $stream.Write($header, 0, 8)
    $stream.Write($payload, 0, $payload.Length)
    function Read-Exact([int]$count) {
        $buffer = [byte[]]::new($count)
        $offset = 0
        while ($offset -lt $count) { $read = $stream.Read($buffer, $offset, $count - $offset); if ($read -le 0) { throw 'Bridge closed before responding.' }; $offset += $read }
        return ,$buffer
    }
    $responseHeader = Read-Exact 8
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($responseHeader) }
    $length = [BitConverter]::ToUInt64($responseHeader, 0)
    if ($length -gt 10485760) { throw 'Unexpectedly large bridge response.' }
    $json = [Text.Encoding]::UTF8.GetString((Read-Exact ([int]$length)))
    Write-Output $json
    $result = $json | ConvertFrom-Json
    if ($result.success -eq $false -or $result.status -eq 'error' -or $result.data.success -eq $false -or $result.result.success -eq $false) { throw 'Unity validation command failed. See response above.' }
}
finally { $client.Dispose() }
