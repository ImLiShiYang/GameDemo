# 远程角色快照时间轴插值

## 运行方式

- ClientEntityRegistry 把世界快照的 ServerTick 交给 NetworkTransformInterpolator。远程玩家、普通敌人和 Boss 使用相同的快照缓冲实现；投射物仍沿用原来的独立表现逻辑。
- SnapshotInterpolationBuffer 最多保留 32 帧。服务器时间为 Tick / 20，默认播放延迟为 0.2 秒（两个 10 Hz 快照周期）。这是主动增加的显示缓冲，不包含网络传输延迟。
- 每个显示帧推进播放时间，寻找覆盖该时间的两份快照，用时间比例进行位置 Lerp 和旋转 Slerp，不再从当前显示位置指数追赶最新目标。网络包到达时只入队，不反复重置播放时间。
- 缓冲长度偏离目标超过 0.1 秒时用 0.95/1.05 倍播放速度温和纠偏。启动时显示首帧并等待播放时间追上；缓冲耗尽后进入有限外推，默认最多猜测 0.15 秒、最多离开最新权威位置 1 米，到达上限后冻结，不能因长时间断网一直向前走。
- 普通敌人和 Boss 优先使用快照内的服务器速度；玩家快照没有速度字段，因此使用最近两帧的位置差估算。速度上限为 10 米/秒，并且只外推水平位置，不猜测高度和旋转，避免角色浮空、钻地或在未知输入下转错方向。
- 玩家最新状态为停止、死亡或受击硬直时禁用外推。翻滚仅外推到 `RollTicks` 声明的剩余时间。外推不生成新的开火、技能、音效、粒子或伤害事件。
- 外推位置会使用角色胶囊对静态场景做一次无分配扫掠，避免显示模型穿墙；不会查询远程角色碰撞代理或动态刚体，因为显示时间轴与最新权威碰撞时间不同，混用会产生假阻挡。
- 新快照在外推期间到达时，先保留当前画面位置，再以指数衰减方式收回到服务器时间轴；传送、长中断重建和死亡则立即清除旧误差。
- 重复/过期 Tick 被过滤。相邻快照位移超过 5 米、快照间隔超过 1 秒或播放严重落后时重建基线，避免沿过期路径长时间追赶。重新初始化/对象池复用会清空历史。
- 远程玩家的翻滚、移动和开火持续状态随播放时间切换，离散动作不会提前取下一帧。死亡快照优先生效，历史动作不会恢复存活表现。技能、枪口音效等 BattleEvent 仍走原来的即时事件通道，本次不增加事件延迟队列。
- 本地玩家的预测/校正显示继续使用原来的平滑路径，不加入这 200 毫秒延迟。碰撞代理仍使用最新权威位置，不跟随延迟的显示模型。

## 参数及接口

- NetworkTransformInterpolator 的 `snapshotDelay`：默认 0.2 秒，Inspector 范围 0.05–0.5 秒。
- `maxExtrapolationTime`：默认 0.15 秒，Inspector 范围 0–0.25 秒；设为 0 可关闭外推，便于做 A/B 对比。
- `extrapolationCorrectionRate`：默认 20，控制恢复包到达后的视觉误差衰减速度；20 大约在 0.15 秒内消除 95% 误差。
- 原 `interpolationRate` 仅用于本地预测表现的指数平滑，不能用它调节远程时间轴延迟。
- 调试属性：`BufferedSnapshotCount`、`PlaybackServerTime`、`SnapshotBufferStarved`、`IsExtrapolating`、`ExtrapolatedSeconds`。
- `ApplyState` 要求显式传入世界快照 ServerTick；`ApplySpawn` 使用消息包 ServerTick，不使用可能为 0 的实体出生 Tick。
- 不修改网络数据格式，协议仍为 v7。

## 验证

编辑器处于非播放状态时，在项目根目录运行：

```powershell
& Assets/Demo/Tools/NetworkValidation/Invoke-UnityNetworkChecks.ps1 -Action Compile
& Assets/Demo/Tools/NetworkValidation/Invoke-UnityNetworkChecks.ps1 -Action Interpolation
& Assets/Demo/Tools/NetworkValidation/Invoke-UnityNetworkChecks.ps1 -Action Physics
& Assets/Demo/Tools/NetworkValidation/Test-NetworkPlayerMigration.ps1
```

也可以用菜单 `Tools > Network Validation > Run Snapshot Interpolation Checks`。

本次验证：编译通过；36 项时间轴插值与外推检查、39 项协议/动作检查、33 项原生物理/预测检查通过。当前未打开包含玩家的主场景，物理套件的另外 3 项主场景出生检查未执行。

时间轴专项检查包含两帧中点采样、最短旋转路径、旧 Tick 过滤、传送、长中断、容量限制、离散动作时机、不同帧率和抖动输入、本地预测隔离及组件重用；外推部分覆盖限时限距、停止/死亡/硬直冻结、翻滚剩余 Tick、敌人权威速度、非法浮点过滤、恢复纠偏和静态墙阻挡。

尚需双客户端画面验收：匀速移动与转向、连续翻滚、短暂卡网恢复、死亡表现、远程瞄准线。注意延迟显示模型与即时碰撞代理之间会有位置差，不能把模型位置用于权威碰撞。
