# BlueType 重构执行计划

## 1. 执行目标

本文档是 `md/refactor_plan.md` 的落地版，按可排期、可提交、可验收的粒度拆分任务。

执行原则：

- 每个 PR 保持可构建。
- 每个阶段都要有测试或手工验证路径。
- 先加护栏，再搬代码。
- 第一阶段不改变 wire format 和用户可见行为。

---

## 2. 当前基线修正

旧计划中提到的 Android `ConnectionForegroundService.kt` 已不再是当前核心巨类。当前 Android 会话核心主要在：

- `BlueType.Android/app/src/main/java/com/bluetype/android/bluetooth/ConnectionSessionRuntime.kt`
- `BlueType.Android/app/src/main/java/com/bluetype/android/bluetooth/SessionClient.kt`
- `BlueType.Android/app/src/main/java/com/bluetype/android/bluetooth/ConnectionOrchestrator.kt`
- `BlueType.Android/app/src/main/java/com/bluetype/android/bluetooth/ConnectionCommandDispatcher.kt`

当前 Windows 关键点：

- `BlueType.Agent/Tray/AgentApplicationHost.cs`
- `BlueType.Agent/Core/SessionProcessor.cs`
- `BlueType.Agent/Core/SessionHelloHandler.cs`
- `BlueType.Agent/Transport/ConnectionServerBase.cs`
- `BlueType.Protocol/*`

当前 Mac 关键点：

- `BlueType.Mac/Sources/BlueTypeMacCore/MacAgent.swift`
- `BlueType.Mac/Sources/BlueTypeMacCore/SessionProcessor.swift`
- `BlueType.Mac/Sources/BlueTypeMacCore/Protocol.swift`
- `BlueType.Mac/Sources/BlueTypeMacCore/FrameCodec.swift`

---

## 3. 里程碑

### M1 协议护栏

完成条件：

- `protocol/spec` 覆盖当前实际消息和错误码。
- 三端都有协议 example 契约测试。
- wire format 不变。

### M2 Android 会话核心收口

完成条件：

- `ConnectionSessionRuntime` 的状态迁移、auth 响应、action 编码被拆出。
- Android 重连/授权/手动断开测试补齐。
- 高频输入具备合并策略。

### M3 桌面端会话对齐

完成条件：

- Windows/Mac 对 hello/auth/heartbeat/session replaced 场景有测试。
- 两端错误码和 session lifecycle 行为对齐。

### M4 宿主层清理

完成条件：

- Windows Tray 层不直接承载业务装配。
- Android ViewModel 不直接承载协议和平台细节。
- Mac 保持组合根清晰，核心对象可测试。

### M5 存储安全和命令扩展性

完成条件：

- token 和授权设备存储有 repository 边界。
- 存储迁移和损坏恢复有测试。
- command handler/registry 可逐步引入。

---

## 4. PR 拆分

### PR1：补齐协议文档和 examples

范围：

- `protocol/spec/messages.md`
- `protocol/spec/errors.md`
- `protocol/spec/session-flow.md`
- `protocol/spec/examples/*.json`

任务：

1. 补齐消息类型：
   - `hello`
   - `auth_pending`
   - `auth_result`
   - `ack`
   - `error`
   - `ping`
   - `pong`
   - `text_insert`
   - `key_tap`
   - `key_down`
   - `key_up`
   - `combo`
   - `mouse_move`
   - `mouse_button`
   - `mouse_click`
   - `mouse_scroll`
   - `clipboard_set`
   - `clipboard_get`
   - `clipboard_value`
   - `shortcut_profile`

2. 补齐错误码：
   - `BUSY`
   - `NOT_AUTHORIZED`
   - `AUTH_TIMEOUT`
   - `AUTH_UI_UNAVAILABLE`
   - `INVALID_PAYLOAD`
   - `SERVER_ERROR`
   - `SESSION_REPLACED`
   - `INPUT_BLOCKED`
   - `CLIPBOARD_FAILED`

3. 更新 session flow：
   - 首次连接
   - 已信任设备快速授权
   - 授权超时
   - 未授权命令
   - 重复 hello
   - session replaced
   - heartbeat ping/pong

不做：

- 不改代码行为。
- 不改 frame format。

验收：

- 文档与当前代码消息集合一致。
- 每个核心 server response 至少一个 example。

---

### PR2：三端协议契约测试

范围：

- `.NET` 测试
- Android JVM 测试
- Swift test
- `protocol/spec/examples`

任务：

1. `.NET` 读取 examples，验证 `Envelope` decode。
2. Android 读取 examples，验证 `Envelope` decode 和 `MsgType.fromWire`。
3. Mac 读取 examples，验证 `Envelope` decode 和 `MessageType` 覆盖。
4. 对 frame codec 增加 round-trip 测试。

不做：

- 不生成代码。
- 不改协议字段。

验收命令：

```powershell
dotnet test BlueType.Agent.Tests\BlueType.Agent.Tests.csproj
cd BlueType.Android
.\gradlew.bat :app:testDebugUnitTest
```

Mac:

```bash
cd BlueType.Mac
swift test
```

---

### PR3：Android 抽 `RemoteActionEncoder`

范围：

- `ConnectionSessionRuntime.kt`
- 新增 `RemoteActionEncoder.kt`
- Android tests

任务：

1. 把 `RemoteAction -> MsgType + payload` 从 `handleRemoteAction` 中移出。
2. encoder 不直接发送，只返回可测试的 command envelope data。
3. 覆盖以下 action：
   - text insert
   - key tap/down/up
   - combo
   - mouse move/button/click/scroll
   - clipboard set/get

不做：

- 不改 `SessionClient`。
- 不改 UI。
- 不改消息字段。

验收：

- `handleRemoteAction` 只负责调用 encoder 和 dispatcher。
- encoder 测试与协议 examples 一致。

---

### PR4：Android 抽 `SessionStateReducer`

范围：

- `ConnectionSessionRuntime.kt`
- 新增 `SessionStateReducer.kt`
- Android tests

任务：

1. 定义最小状态：
   - `Idle`
   - `Connecting`
   - `AwaitingApproval`
   - `Connected`
   - `Reconnecting`
   - `Error`

2. 定义最小事件：
   - `ConnectRequested`
   - `AuthPending`
   - `AuthSucceeded`
   - `AuthFailed`
   - `ManualDisconnect`
   - `UnexpectedDisconnect`
   - `ReconnectStarted`

3. 先让 reducer 只决定状态，不直接做 IO。
4. `ConnectionSessionRuntime` 负责把 reducer 输出同步到 `ConnectionUiStateStore`。

测试：

- 授权通过进入 `Connected`
- `NOT_AUTHORIZED` 进入 `Error` 并清恢复意图
- `BUSY` 进入 `Error` 且不自动重连
- 手动断开进入 `Idle`
- 异常断开进入 `Reconnecting`

验收：

- 状态迁移可纯单测。
- 现有 UI 状态展示不变。

---

### PR5：Android auth 响应和重连逻辑收口

范围：

- `ConnectionSessionRuntime.kt`
- 新增 `AuthResponseHandler.kt`
- 可选新增 `ReconnectPolicy.kt`

任务：

1. 抽 `auth_pending` 处理。
2. 抽 `auth_result` 处理。
3. 抽 hello 阶段 `error` 处理。
4. 将 `BUSY`、`NOT_AUTHORIZED`、`AUTH_TIMEOUT` 的恢复策略集中到一处。
5. 可选抽 `ReconnectPolicy`，先只表达是否允许重连，不做复杂 backoff。

验收：

- hello/auth 相关分支不再散在 `handleIncoming` 和 `handleError`。
- 授权失败、busy、手动断开、异常断开的行为有测试。

---

### PR6：Android 高频输入 coalescing

范围：

- `ConnectionSessionRuntime.kt`
- `ConnectionCommandDispatcher.kt`
- 新增 `InputBackpressureController.kt`
- Android tests

任务：

1. `mouse_move` 在短窗口内合并 dx/dy。
2. `mouse_scroll` 在短窗口内合并 deltaX/deltaY。
3. 高频命令不进入 pending request。
4. 队列满时优先丢弃旧高频事件，不堆积延迟。

建议策略：

- 窗口先使用 8-16 ms。
- 合并后仍保留顺序命令边界，避免 key/clipboard 被高频输入重排。

验收：

- 多个 mouse move 事件合并成少量 envelope。
- 高频输入不会持续增加 pending request。
- Android JVM 测试通过。

---

### PR7：Windows session lifecycle 测试

范围：

- `BlueType.Agent.Tests`
- `SessionProcessor`
- `SessionHelloHandler`
- `SessionHeartbeat`

任务：

1. 增加或补齐以下测试：
   - hello payload 缺字段
   - 重复 hello
   - 未授权命令
   - known device fast auth
   - authorization timeout
   - session replaced
   - heartbeat timeout
   - command handler exception -> `SERVER_ERROR`

不做：

- 先不抽新类。
- 先用测试锁定当前行为。

验收：

```powershell
dotnet test BlueType.Agent.Tests\BlueType.Agent.Tests.csproj
```

---

### PR8：Windows 抽 `SessionLifecycle`

范围：

- `BlueType.Agent/Core/SessionProcessor.cs`
- 新增 `BlueType.Agent/Core/SessionLifecycle.cs`

任务：

1. 把 authorized flag、active session check、shutdown cleanup 收口。
2. `SessionProcessor` 保留 read loop 和 response write。
3. input release 仍保持 finally 语义。
4. 保持错误码和 wire response 不变。

验收：

- PR7 测试全部通过。
- 行为不变，类职责更清晰。

---

### PR9：Mac session lifecycle 测试与对齐

范围：

- `BlueType.Mac/Sources/BlueTypeMacCore/SessionProcessor.swift`
- `BlueType.Mac/Tests/BlueTypeMacCoreTests`

任务：

1. 补齐 Mac 对应场景测试：
   - invalid hello
   - duplicate hello
   - unauthorized command
   - session replaced
   - heartbeat timeout
2. 对齐 Windows 错误码和消息。
3. 必要时抽轻量 lifecycle helper。

验收：

```bash
cd BlueType.Mac
swift test
```

---

### PR10：Windows 宿主层清理

范围：

- `AgentApplicationHost.cs`
- `TrayAppContext.cs`
- 新增 `CompositionRoot` 或 `AgentHostFactory`

任务：

1. 把业务对象装配从 Tray 命名空间移出。
2. Tray 只处理：
   - 菜单
   - 状态展示
   - 授权弹窗
   - disconnect 用户操作
3. 保持现有 app 启动和托盘行为不变。

验收：

- Tray 层不直接 new `SessionProcessor`。
- `dotnet build BlueType.sln` 通过。

---

### PR11：Android ViewModel 瘦身

范围：

- `MainViewModel.kt`
- data/domain/platform helper

任务：

1. 蓝牙设备枚举下沉到 platform adapter。
2. 默认快捷键 profile 下沉到 domain factory。
3. clipboard 操作封装为 use case 或 adapter。
4. ViewModel 只聚合 UI state 和调用 use case。

验收：

- ViewModel 不直接承载协议语义和平台细节。
- UI 行为不变。

---

### PR12：存储 repository 边界

范围：

- Android data
- Windows `DeviceRegistry`
- Mac `DeviceRegistry`

任务：

1. 定义 Android:
   - `TokenRepository`
   - `SessionRepository`
   - `RecentDeviceRepository`
   - `DeviceIdentityRepository`

2. 定义 Windows/Mac:
   - `AuthorizedDeviceRepository`

3. 先做接口和适配，不立即迁移存储格式。

验收：

- 调用方依赖 repository 接口。
- 现有数据格式仍兼容。

---

### PR13：存储安全和迁移

范围：

- Android token storage
- Windows/Mac authorized device storage

任务：

1. Android token 迁移到 Keystore-backed encrypted storage。
2. Windows/Mac 授权文件使用原子写入。
3. 配置损坏时备份原文件并记录日志。
4. 旧数据读取成功后迁移，新旧数据保留一个版本周期。

验收：

- 老用户不需要无故重新授权。
- 存储损坏不会静默变成空授权列表。
- 存储测试通过。

---

### PR14：命令 handler/registry

范围：

- Windows `CommandRouter`
- Mac `CommandRouter`
- Android command metadata 可选

任务：

1. 建立 command registry。
2. 每个 handler 声明：
   - type
   - payload parser
   - ack strategy
   - high-frequency strategy
3. 先迁低风险命令：
   - `clipboard_get`
   - `key_tap`
4. 再迁复杂 payload 命令。

验收：

- 新增命令不需要修改大 switch。
- handler 可独立测试。

---

## 5. 验证清单

### 每个 PR 必跑

Windows:

```powershell
dotnet test BlueType.Agent.Tests\BlueType.Agent.Tests.csproj
dotnet build BlueType.sln
```

Android:

```powershell
cd BlueType.Android
.\gradlew.bat :app:testDebugUnitTest
```

涉及 Android runtime 行为时额外跑：

```powershell
cd BlueType.Android
.\gradlew.bat :app:assembleDebug
```

Mac:

```bash
cd BlueType.Mac
swift test
```

### 手工联调场景

- Wi-Fi 首次连接并授权
- Wi-Fi 已信任设备快速连接
- Bluetooth 首次连接并授权
- 手动断开后不自动重连
- 异常断开后自动重连
- 文本输入
- 组合键
- 鼠标移动/点击/滚轮
- clipboard set/get
- 第二设备连接触发 busy 或 takeover

---

## 6. 排期建议

单人执行建议：

- 第 1 周：PR1、PR2
- 第 2 周：PR3、PR4
- 第 3 周：PR5、PR6
- 第 4 周：PR7、PR8、PR9
- 第 5 周：PR10、PR11
- 第 6 周：PR12、PR13
- 第 7 周：PR14

如果两人并行：

- 一人负责 Android PR3-PR6。
- 一人负责协议 PR1-PR2 和桌面端 PR7-PR9。
- 存储和命令 handler 在协议护栏稳定后再并行。

---

## 7. 风险控制

### 协议风险

控制方式：

- 第一轮不改 wire format。
- 所有 examples 被三端测试读取。
- 新字段必须向后兼容，旧端忽略未知字段。

### 状态机风险

控制方式：

- reducer 先只做纯状态迁移。
- IO 和 side effect 仍由 runtime 执行。
- 每迁一个分支就补一个测试。

### Android 高频输入风险

控制方式：

- 只对 mouse move/scroll 做 coalescing。
- key、combo、clipboard 保持保序可靠。
- 先用 8-16 ms 小窗口，保留调参入口。

### 存储迁移风险

控制方式：

- 先抽接口，后迁数据。
- 迁移失败不删除旧数据。
- 旧数据保留一个版本周期。

---

## 8. 不做事项

短期不做：

- 不重写 Android 连接层。
- 不重写 Windows Agent。
- 不重做 UI 视觉。
- 不切换 wire format。
- 不把目录结构大搬家作为独立目标。
- 不把 token 迁移混进协议或状态机 PR。

---

## 9. 完成判定

本轮重构完成时，应满足：

- `protocol/spec` 是协议事实源。
- 三端协议契约测试通过。
- Android `ConnectionSessionRuntime` 不再承担所有会话细节。
- Android 高频输入不会长期排队积压。
- Windows/Mac 会话生命周期和错误码对齐。
- UI/Tray/Menu 层不承载会话规则。
- token 和授权设备存储具备 repository 边界、迁移策略和损坏恢复。
- 命令扩展有 handler/registry 路径。

