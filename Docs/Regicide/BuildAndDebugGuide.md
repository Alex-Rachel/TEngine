# Regicide 客户端/服务端构建与联调手册

## 1. 前置条件

1. Unity 版本：`2022.3.60f1`。
2. 已导入 Mirror（`Assets/Mirror`）与 TEngine 依赖。
3. OpenSpec 变更：`build-regicide-replica-with-tengine-mirror` 已存在并完成 artifacts。

## 2. 运行模式

- 单机调试：`--regicide-single`
- 联机客户端：默认（不传 dedicated/single）
- Dedicated Server：`--regicide-dedicated --regicide-port 7777`

可选参数：

- `--regicide-address <ip>`
- `--regicide-port <port>`
- `--regicide-player <playerId>`

## 3. 客户端启动流程

1. 启动后进入 `GameApp.Entrance`。
2. `RegicideBootstrap.Start()` 读取 `RegicideRuntimeConfig`。
3. 客户端通过 `RegicideNetworkModule.ConnectAsync` 发起连接。
4. UI 导航：
   - Lobby -> Room -> RegicideBattle -> Result

## 4. 服务端权威链路

1. 主包 `RegicideMirrorBridge` 接收客户端意图消息。
2. 桥接层转发 `RegicideBridgeEvents.ServerIntentReceived`。
3. `RegicideAuthorityModule` 执行规则校验和结算。
4. 权威快照通过 `ServerPublishRoomSnapshot/ServerPublishState/ServerPublishError` 广播。

## 5. 资源生命周期检查

1. 战斗资产清单定义在 `RegicideResourceScope.BattleAssetChecklist`。
2. `RegicideBattleUI` 创建时异步加载清单资源。
3. `RegicideBattleUI` 销毁时调用 `ReleaseBattleAssets` 完成释放。

## 6. 压测与自测

1. 调用 `RegicideSelfTests.LogAll()` 执行规则与回放自检。
2. 调用 `RegicideLongSessionProfiler.RunAsync(maxActions)` 进行长局压测。
3. 查看输出指标：
   - `TotalMessagesIn/Out`
   - `TotalBytesIn/Out`
   - `PeakFrameMillis`
   - `GcAllocatedBytesDelta`

## 7. 常见故障排查

1. 连接失败：确认 `Transport.active` 与端口配置一致。
2. 入房无响应：检查是否收到 `ClientRoomSnapshot` 事件。
3. 状态不同步：对比 `StateHash`，定位对应 `ServerSequence`。
4. 重连后无状态：检查桥接层 `_latestRoom/_latestState` 是否存在。
5. UI 无法操作：确认 `RegicideBattleModule.IsMyTurn` 逻辑与权威状态一致。
