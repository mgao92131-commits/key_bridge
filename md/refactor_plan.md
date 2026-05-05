# BlueType 重构方案

## 1. 文档目标

本文档描述 BlueType 当前代码基线下的重构方向。目标不是重新排目录或推翻重写，而是先解决跨端协议、会话状态和宿主边界这三个真正影响后续演进的耦合点。

适用范围：

- `BlueType.Agent` Windows 托盘 agent
- `BlueType.Mac` macOS 菜单栏 agent
- `BlueType.Android` Android client
- `BlueType.Protocol` 与 `protocol/spec`
- 相关测试项目和测试样例

核心原则：

- 第一阶段保持 wire format 完全兼容。
- 三端一等对待，Windows、Mac、Android 都要通过同一批协议样例。
- 先建立协议和行为测试护栏，再移动代码。
- 每轮重构都要可独立合并、可构建、可回滚。

---

## 2. 当前代码基线

当前代码已经不是早期单个巨类 MVP。Android 端已经拆出：

- `ConnectionSessionRuntime`
- `SessionClient`
- `ConnectionOrchestrator`
- `ConnectionCommandDispatcher`
- `ConnectionUiStateStore`
- `PersistedSessionCoordinator`

Windows 端已经有：

- `BlueType.Protocol`
- `ConnectionServerBase`
- `SessionProcessor`
- `SessionHelloHandler`
- `SessionHeartbeat`
- keyboard/mouse/clipboard command handlers

Mac 端通过 Swift Package 拆成：

- `BlueTypeMacCore`
- `BlueTypeMac`
- `BlueTypeMacCLI`

因此当前主要问题不是“完全没有模块化”，而是模块边界还没有收口到稳定核心。

---

## 3. 当前主要问题

### 3.1 协议事实源分散

协议定义仍然散在三端：

- `.NET`: `BlueType.Protocol/Commands.cs`
- Android: `Commands.kt`
- Mac: `Protocol.swift`
- 文档: `protocol/spec/*.md`

这些定义靠人工同步。新增消息、错误码或 payload 字段时，任何一端漏改都会造成跨端漂移。

### 3.2 会话状态机仍然隐式

Windows 的 `SessionProcessor` 同时处理读循环、hello、鉴权、heartbeat、active session、命令路由和输入释放。

Android 的 `ConnectionSessionRuntime` 同时处理连接、重连、hello、auth result、错误映射、UI 状态和持久化。

Mac 的 `SessionProcessor` 也有类似的会话循环和授权逻辑。

这些状态流转散在 `if`、`switch`、`when` 中，很难系统验证：

- 首次授权
- 已信任设备快速授权
- 授权超时
- 重复 hello
- session replaced
- 手动断开
- 异常断开重连
- heartbeat timeout

### 3.3 Android 会话核心仍偏重

Android 已经完成第一轮拆分，但 `ConnectionSessionRuntime` 仍承担过多职责：

- connect/disconnect/reconnect 编排
- incoming envelope 处理
- hello/auth/error 处理
- remote action 到 envelope 的转换
- UI 状态更新
- persisted session 更新
- 高频输入发送

下一步不应继续加功能到这个类，而应先抽状态 reducer、action encoder 和 auth response handler。

### 3.4 高频输入缺少明确背压策略

Android 端已有队列容量限制，但鼠标移动和滚轮仍然是普通事件发送模型：

- `mouse_move` 没有 coalescing
- `mouse_scroll` 没有窗口聚合
- 队列满时缺少按命令类型的丢弃策略

高频输入链路应该优先保证低延迟，而不是完整保留每一个移动事件。

### 3.5 宿主层和业务装配仍混杂

Windows `AgentApplicationHost` 直接装配 input、clipboard、registry、auth、session、tcp、bluetooth。

Android `MainViewModel` 仍包含蓝牙设备枚举、剪贴板访问、默认快捷键 profile、Wi-Fi host 处理等业务/平台细节。

Mac `MacAgent` 作为组合根是合理的，但后续也要保持 AppKit 边界和核心逻辑隔离。

### 3.6 存储和安全模型偏 MVP

需要逐步明确：

- Android token repository 是否由 Keystore-backed 存储承载
- Windows/Mac 授权设备存储是否原子写入
- 配置损坏时是否备份并记录日志
- 旧 token 和旧授权数据如何迁移

这部分不应抢在协议和状态测试之前大改，但 repository 边界应尽早定义。

---

## 4. 目标架构

### 4.1 协议层

目标：

- `protocol/spec` 是跨端协议事实源。
- 三端实现都通过同一批 `protocol/spec/examples/*.json` 契约测试。
- wire format 第一阶段保持不变：
  - 4-byte big-endian frame length
  - JSON envelope
  - `v/id/type/token/payload`

### 4.2 会话核心

目标：

- 会话状态显式化。
- 状态迁移可单测。
- UI 只消费状态，不推导协议语义。

建议最小状态集：

- `Idle`
- `Connecting`
- `AwaitingApproval`
- `Connected`
- `Reconnecting`
- `Disconnecting`
- `Disconnected`
- `Error`

建议最小事件集：

- `ConnectRequested`
- `TransportOpened`
- `HelloSent`
- `AuthPending`
- `AuthSucceeded`
- `AuthFailed`
- `EnvelopeReceived`
- `TransportClosed`
- `ManualDisconnect`
- `HeartbeatTimeout`
- `ReconnectStarted`

### 4.3 Android 目标边界

建议收口为：

- `ConnectionSessionRuntime`: 顶层协调
- `SessionStateReducer`: 状态迁移
- `RemoteActionEncoder`: `RemoteAction -> Envelope`
- `AuthResponseHandler`: auth 相关响应处理
- `InputBackpressureController`: mouse move/scroll 合并
- `ConnectionOrchestrator`: transport 打开
- `SessionClient`: reader/writer/heartbeat

### 4.4 Windows/Mac 目标边界

建议收口为：

- `SessionLifecycle`: 会话状态和关闭清理
- `Hello/Auth handler`: hello 解析、授权、active session 激活
- `Heartbeat supervisor`: 心跳与超时
- `Command dispatcher`: 命令分发
- `Transport acceptor`: TCP/Bluetooth 只处理连接接入
- `Host/CompositionRoot`: 装配和启停
- `Tray/Menu UI`: 用户交互和状态展示

---

## 5. 分轮重构计划

### 第一轮：协议和测试护栏

目标：先防止三端继续漂移。

范围：

- `protocol/spec/messages.md`
- `protocol/spec/errors.md`
- `protocol/spec/session-flow.md`
- `protocol/spec/examples/*.json`
- `.NET`、Android、Mac 协议契约测试

明确不做：

- 不改 wire format
- 不改认证流程
- 不改 UI
- 不改 token 存储
- 不移动大目录

验收：

- 三端都能读取同一批协议样例。
- 文档覆盖当前实际消息和错误码。
- 新增消息必须先补 spec 和 example。

### 第二轮：Android 会话核心收口

目标：降低 Android 连接、重连、授权逻辑复杂度。

范围：

- `ConnectionSessionRuntime.kt`
- `ConnectionCommandDispatcher.kt`
- `SessionClient.kt`
- Android JVM tests

工作：

- 抽 `SessionStateReducer`
- 抽 `RemoteActionEncoder`
- 抽 `AuthResponseHandler`
- 增加手动断开、异常断开、授权失败、busy、reconnect 测试
- 为 `mouse_move` 和 `mouse_scroll` 增加 coalescing

验收：

- `ConnectionSessionRuntime` 明显变薄。
- Android 单测覆盖关键状态分支。
- 发送出的 wire message 与协议 examples 保持一致。

### 第三轮：桌面端会话对齐

目标：Windows 和 Mac 会话生命周期结构对齐，但行为不变。

范围：

- `BlueType.Agent/Core/SessionProcessor.cs`
- `BlueType.Agent/Core/SessionHelloHandler.cs`
- `BlueType.Mac/Sources/BlueTypeMacCore/SessionProcessor.swift`
- `BlueType.Mac/Sources/BlueTypeMacCore/Protocol.swift`

工作：

- Windows 抽 `SessionLifecycle`
- Mac 抽对应 lifecycle 边界
- 对齐错误码和状态命名
- 补 hello/auth/heartbeat/session replaced 测试

验收：

- Windows/Mac 对同一 session 场景返回一致错误码。
- 现有授权和输入行为不变。
- `.NET` 和 Swift 测试通过。

### 第四轮：宿主层清理

目标：降低 UI/Tray/Menu 和业务装配耦合。

工作：

- Windows 新增 `CompositionRoot` 或 `AgentHostFactory`
- `TrayAppContext` 只处理菜单、状态展示、授权弹窗
- Android 继续瘦身 `MainViewModel`
- Mac 保持 `MacAgent` 作为组合根，但保持核心对象可测试

验收：

- UI/Tray/Menu 层不直接承载会话规则。
- 用户可见行为不变。

### 第五轮：存储和安全

目标：增强 token 和授权设备存储可靠性。

工作：

- 抽 repository 接口
- Android token 迁移到 Keystore-backed encrypted storage
- Windows/Mac 授权存储原子写入
- 配置损坏时备份原文件并记录日志
- 设计旧数据迁移路径

验收：

- 老用户不需要无故重新授权。
- 存储损坏不会静默变成空授权列表。
- 存储层有单测。

### 第六轮：命令处理器化

目标：为后续扩展命令降低改动成本。

工作：

- 建立 command registry
- 每个 handler 声明 type、payload parser、ack 策略、高频策略
- 统一错误码映射

验收：

- 新增命令不需要修改大 switch。
- 每类命令能独立测试。

---

## 6. 推荐首批 PR

1. PR1：补齐协议文档和 examples。
2. PR2：增加 `.NET`、Android、Mac 协议契约测试。
3. PR3：Android 抽 `RemoteActionEncoder` 和测试。
4. PR4：Android 抽 `SessionStateReducer` 和授权/重连测试。
5. PR5：Android mouse move/scroll coalescing。
6. PR6：Windows/Mac session lifecycle 对齐测试。

---

## 7. 每轮验证命令

Windows:

```powershell
dotnet test BlueType.Agent.Tests\BlueType.Agent.Tests.csproj
dotnet build BlueType.sln
```

Android:

```powershell
cd BlueType.Android
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :app:assembleDebug
```

Mac:

```bash
cd BlueType.Mac
swift test
```

---

## 8. 不建议做法

- 不要先做目录大搬家。
- 不要在协议测试前改 wire format。
- 不要同时重构 Android、Windows、Mac 的会话核心。
- 不要把安全存储迁移和协议/状态机重构混在同一个提交。
- 不要为了 handler 化而先动命令路由，当前更高风险是协议和状态漂移。

---

## 9. 完成标准

本轮重构完成时，应满足：

- `protocol/spec` 成为协议事实源。
- 三端协议契约测试通过。
- Android 会话状态可单测。
- Windows/Mac 会话错误码和核心时序对齐。
- 高频输入有明确背压和合并策略。
- UI/Tray/Menu 不承载会话规则。
- token 和授权设备存储具备迁移、恢复和测试。

