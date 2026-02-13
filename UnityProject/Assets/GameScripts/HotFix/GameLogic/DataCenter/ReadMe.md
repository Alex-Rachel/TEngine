# Fantasy 服务器框架更新注意事项

## Fantasy 框架更新后需要手动修改的内容

> 更新 Fantasy 框架时，以下修改可能会被覆盖，需要人工确认功能是否正常，如不正常，需要手动重新添加以确保功能的正确性。

---

### 1. Scene.cs - MessageDispatcherComponent 属性修改

**文件位置**: `Fantasy/Scene/Scene.cs`

**修改内容**: 将 `MessageDispatcherComponent` 属性改为 `public`

```csharp
/// <summary>
/// Scene下的网络消息派发组件 改成 public 方便客户端通过GameClient和协议号直接监听服务器数据的下发
/// </summary>
public MessageDispatcherComponent MessageDispatcherComponent { get; internal set; }
```

---

### 2. MessageDispatcherComponent.cs - 添加客户端消息注册支持

**文件位置**: `Fantasy/Network/MessageDispatcherComponent.cs`

#### 2.1 添加字段和方法（在类的适当位置）

```csharp
#if FANTASY_UNITY
        /*
         * 方便客户端通过 GameClient RegisterMsgHandler 注册的回调
         * 客户端可以通过 GameClient 和协议号直接监听服务器数据的下发
         */

        public readonly Dictionary<uint, List<Action<IMessage>>> MsgHandles = new Dictionary<uint, List<Action<IMessage>>>();

        public void RegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (!MsgHandles.ContainsKey(protocolCode))
            {
                MsgHandles[protocolCode] = new List<Action<IMessage>>();
            }

            MsgHandles[protocolCode].Add(ctx);
        }

        public void UnRegisterMsgHandler(uint protocolCode, Action<IMessage> ctx)
        {
            if (MsgHandles.TryGetValue(protocolCode, out var handle))
            {
                handle.Remove(ctx);
            }
        }
#endif
```

#### 2.2 修改消息派发逻辑

在 `MessageHandler()` 方法的消息处理部分，**在现有逻辑之前**添加：

```csharp
#if FANTASY_UNITY
            // 先触发通过 GameClient RegisterMsgHandler 注册的回调
            // 方便客户端通过 GameClient 和协议号直接监听服务器数据的下发
            if (MsgHandles.TryGetValue(protocolCode, out var handlers))
            {
                for (int i = handlers.Count - 1; i >= 0; i--)
                {
                    var handler = handlers[i];
                    try
                    {
                        handler.Invoke((IMessage)message);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"MsgHandler error, protocolCode:{protocolCode}, error:{e}");
                    }
                }
                return;
            }
#endif
```

---

### 3. Log.cs - 确认条件宏定义与 DLogger 保持一致

**文件位置**: `Fantasy.Unity/Runtime/Core/Log/Log.cs`

**检查内容**: 确认 `Debug`、`Info`、`Warning`、`Error` 方法的 `[Conditional]` 属性与 `TEngine.Log` 保持一致（`Trace` 和 `TraceInfo` 除外）

#### 各日志级别应包含的 Conditional 属性：

**Debug 方法**:
```csharp
[Conditional("ENABLE_LOG")]
[Conditional("ENABLE_DEBUG_LOG")]
[Conditional("ENABLE_DEBUG_AND_ABOVE_LOG")]
public static void Debug(string msg) { ... }
```

**Info 方法**:
```csharp
[Conditional("ENABLE_LOG")]
[Conditional("ENABLE_INFO_LOG")]
[Conditional("ENABLE_DEBUG_AND_ABOVE_LOG")]
[Conditional("ENABLE_INFO_AND_ABOVE_LOG")]
public static void Info(string msg) { ... }
```

**Warning 方法**:
```csharp
[Conditional("ENABLE_LOG")]
[Conditional("ENABLE_WARNING_LOG")]
[Conditional("ENABLE_DEBUG_AND_ABOVE_LOG")]
[Conditional("ENABLE_INFO_AND_ABOVE_LOG")]
[Conditional("ENABLE_WARNING_AND_ABOVE_LOG")]
public static void Warning(string msg) { ... }
```

**Error 方法**:
```csharp
[Conditional("ENABLE_LOG")]
[Conditional("ENABLE_ERROR_LOG")]
[Conditional("ENABLE_DEBUG_AND_ABOVE_LOG")]
[Conditional("ENABLE_INFO_AND_ABOVE_LOG")]
[Conditional("ENABLE_WARNING_AND_ABOVE_LOG")]
[Conditional("ENABLE_ERROR_AND_ABOVE_LOG")]
public static void Error(string msg) { ... }
```

> **参考文件**: `TEngine/Runtime/Core/Log/Log.cs`

---

## 使用示例

### 客户端监听服务器消息

```csharp
// 注册消息监听
GameClient.Instance.RegisterMsgHandler.RegisterMsgHandler(protocolCode, (message) =>
{
    // 处理消息
    var msg = (YourMessageType)message;
    // ...
});

// 取消监听
GameClient.Instance.UnRegisterMsgHandler.UnRegisterMsgHandler(protocolCode, handler);
```

---

## 检查清单

更新 Fantasy 框架后，请确认：

- [ ] Scene.cs 中 `MessageDispatcherComponent` 已改为 `public`
- [ ] MessageDispatcherComponent.cs 中已添加 `#if FANTASY_UNITY` 代码块
- [ ] `MessageHandler()` 方法中已添加客户端回调触发逻辑
- [ ] **Fantasy.Log 中 `Debug`、`Info`、`Warning`、`Error` 方法已添加条件宏定义**（`Trace` 和 `TraceInfo` 除外），防止打包输出相关的Debug代码
- [ ] 编译无错误