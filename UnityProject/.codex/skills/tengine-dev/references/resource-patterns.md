# 刷新、取消与资源释放

本页处理业务持有的普通 Asset。API 和图片扩展的边界见 [资源 API](resource-api.md)。
先回答：谁发起、何时取消、谁接收成功结果、未接收结果由谁归还。

## 取消与接收边界

[ResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs) 的
`LoadAssetAsync`/`LoadGameObjectAsync` 缓存命中分支没有完整检查业务 token；
`TryWaitingLoading` 不接收业务 token。传入 token 不能保证不再获得资源。
某些取消分支释放句柄并返回 null，其他异常沿调用链传播。

因此 await 后同时检查结果、取消和请求身份；在 finally 中归还所有未转移的成功结果。
GameObject 结果过期时销毁实例，让 AssetsReference 归还源资源，不能对实例再调用 UnloadAsset。
不要仅捕获 `OperationCanceledException`，也不要在取消后直接丢弃返回值。

`UIWindow` 是普通 C# 对象，`this == null` 无法检测面板销毁。
`UserData` 为首项参数，先做类型匹配。仅在调用 ResourceModule 前检查宿主不够，完成时还可能已经关闭或刷新。

## 窗口刷新示例

下面是嵌入窗口的片段，版本号阻止旧请求写回；finally 释放未转移资源。
`Render` 是业务方法，地址来自已核对的收集规则。

```csharp
private TextAsset _asset;
private int _revision;
private bool _disposed;
private CancellationTokenSource _load;

protected override void OnRefresh()
{
    int revision = ++_revision;
    _load?.Cancel();
    _load?.Dispose();
    _load = null;
    if (_disposed || UserData is not string location || string.IsNullOrWhiteSpace(location)) return;
    _load = new CancellationTokenSource();
    LoadAsync(location, revision, _load.Token).Forget();
}

private async UniTask LoadAsync(string location, int revision, CancellationToken token)
{
    TextAsset pending = null;
    try
    {
        pending = await GameModule.Resource.LoadAssetAsync<TextAsset>(location, token);
        if (pending == null || token.IsCancellationRequested || _disposed || revision != _revision)
            return;
        TextAsset previous = _asset;
        _asset = pending;
        pending = null;
        if (previous != null) GameModule.Resource.UnloadAsset(previous);
        Render(_asset.text);
    }
    catch (OperationCanceledException) { }
    catch (Exception exception) { Log.Error(exception.ToString()); }
    finally
    {
        if (pending != null) GameModule.Resource.UnloadAsset(pending);
    }
}

protected override void OnDestroy()
{
    _disposed = true;
    ++_revision;
    _load?.Cancel();
    _load?.Dispose();
    _load = null;
    if (_asset != null) GameModule.Resource.UnloadAsset(_asset);
    _asset = null;
}
```

需要 `System`、`System.Threading`、`Cysharp.Threading.Tasks`、`UnityEngine`、`TEngine`。
片段选择保留上次成功资源，直到新资源成功接收或窗口销毁；业务需要清空时应显式调整此策略。
需要隐藏即取消时，在 `OnSetVisible(false)` 明确实现；Hide 也可能立即 Close，见 [窗口生命周期](ui-lifecycle.md)。
只需读取的数据可在 try/finally 内直接消费，不必长期持有。
并发批量加载中某项失败时，也要回收已经成功的其他项。
音频交给 Audio 模块持有，不要先额外 LoadAsset 再调用 Audio.Play 导致双重加载。

依据本次影响选择连续刷新、慢请求覆盖快请求、取消、失败、隐藏或销毁与完成同帧的回归。
观察实际节点内容和引用归还，不能只证明调用了 Cancel 或 UnloadAsset。验证入口见 [verification.md](verification.md)。
