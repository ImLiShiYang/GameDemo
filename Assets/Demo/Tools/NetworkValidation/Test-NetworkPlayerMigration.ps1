[CmdletBinding()]
param(
    [string]$EditorData = 'C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t7/Editor/Data',
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$mono = Join-Path $EditorData 'MonoBleedingEdge/bin/mono.exe'
$compiler = Join-Path $EditorData 'MonoBleedingEdge/lib/mono/msbuild/Current/bin/Roslyn/csc.exe'
$response = Get-ChildItem (Join-Path $projectRoot 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $response) { throw 'Open the project in Tuanjie and let scripts compile first.' }
$priorMonoPath = $env:MONO_PATH
Push-Location $projectRoot
try {
    # Unity 尚未自动刷新时，将新增的联机运行时脚本补入现有编译响应文件。
    $responseText = [IO.File]::ReadAllText($response.FullName).Replace('\', '/')
    $extraSources = @(Get-ChildItem (Join-Path $projectRoot 'Assets/Demo/Scripts/NetworkGameplay') -Filter '*.cs' -Recurse |
        ForEach-Object { $_.FullName.Substring($projectRoot.Length + 1).Replace('\', '/') } |
        Where-Object { -not $responseText.Contains($_) })
    $compileOutput = & $mono $compiler "@$($response.FullName)" @extraSources '-out:Temp/PlayerMigrationVerify.dll' '-refout:Temp/PlayerMigrationVerify.ref.dll' 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($compileOutput -join [Environment]::NewLine) }
    Write-Output 'Gameplay compilation passed.'
    & $mono $compiler '-noconfig' '-nologo' '-nostdlib+' '-define:NETWORK_PLAYER_MIGRATION_CHECKS' '-out:Temp/NetworkPlayerMigrationChecks.exe' `
        '-r:Temp/PlayerMigrationVerify.dll' "-r:$EditorData/Managed/UnityEngine/UnityEngine.CoreModule.dll" `
        "-r:$EditorData/NetStandard/ref/2.1.0/netstandard.dll" 'Assets/Demo/Tools/NetworkValidation/NetworkPlayerMigrationChecks.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Check harness compilation failed.' }
    $env:MONO_PATH = "$EditorData/Managed/UnityEngine;$EditorData/Managed"
    & $mono 'Temp/NetworkPlayerMigrationChecks.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Network player migration checks failed.' }
}
finally {
    $env:MONO_PATH = $priorMonoPath
    if (-not $KeepArtifacts) {
        foreach ($name in @('PlayerMigrationVerify.dll', 'PlayerMigrationVerify.ref.dll', 'PlayerMigrationVerify.pdb', 'NetworkPlayerMigrationChecks.exe')) {
            $artifact = [IO.Path]::GetFullPath((Join-Path $projectRoot "Temp/$name"))
            $tempRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Temp')) + [IO.Path]::DirectorySeparatorChar
            if (-not $artifact.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
            if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact -Force }
        }
    }
    Pop-Location
}
