# Day 17 Mock HTTP API

在项目根目录打开 PowerShell，启动默认服务：

```powershell
python Assets/Demo/Tools/MockServer/mock_server.py
```

默认地址是 `http://127.0.0.1:8080`，测试账号为 `demo`，密码为 `123456`。

可用接口：

- `POST /api/login`：验证账号密码，返回 Token 和玩家 ID。
- `GET /api/player/player-1001`：要求 Header `Authorization: Bearer demo-token-player-1001`。
- `GET /health`：服务存活检查。

故障验收命令：

```powershell
# 每次响应延迟 8 秒；客户端默认 5 秒超时
python Assets/Demo/Tools/MockServer/mock_server.py --delay 8

# 前两次 API 请求返回 HTTP 500，第三次成功
python Assets/Demo/Tools/MockServer/mock_server.py --fail-first 2

# 成功请求返回损坏的 JSON
python Assets/Demo/Tools/MockServer/mock_server.py --malformed-json
```

断网测试直接停止服务。恢复服务后再次点击登录即可重新请求。

`LoginPanelController` 已经带有默认 API 地址、5 秒超时和最多 3 次尝试。需要修改时可在 Inspector 的
`HTTP Network` 分组中调整，不需要增加新的场景组件。
