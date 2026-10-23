# 资源 API 与所有权

权威签名：[IResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs)、
[ResourceModule](../../../../Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs)、
[SetSpriteExtensions](../../../../Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteExtensions.cs)。

| 需求 | API | 释放责任 |
|---|---|---|
| Image/SpriteRenderer 显示图片 | `SetSprite`、`SetSubSprite` | 扩展组件管理；同目标新请求替换旧请求 |
| 实例化 Prefab | `LoadGameObjectAsync` | 销毁实例，由 AssetsReference 释放 |
| 普通 Asset | `LoadAssetAsync<T>` | 每次成功取得后配对 `UnloadAsset` |
| 原始句柄 | `LoadAssetAsyncHandle<T>` | 调用方持有并释放句柄，不能与对象池 API 混用 |

```csharp
// Image callback 是 Action<Image>，不是 Action<Sprite>。
image.SetSprite(location, setNativeSize: true, cancellationToken: token);
image.SetSubSprite(atlasLocation, spriteName, cancellationToken: token);
// SetSubSprite 没有 callback 重载。
var instance = await GameModule.Resource.LoadGameObjectAsync(location, parent, token);
var asset = await GameModule.Resource.LoadAssetAsync<TextAsset>(location, token);
try
{
    if (asset == null) throw new InvalidOperationException("Resource load failed.");
    Consume(asset.text);
}
finally
{
    if (asset != null) GameModule.Resource.UnloadAsset(asset);
}
```

上述为嵌入业务的片段；`Consume`、`token`、地址由调用方提供。
`SetSubSprite` 支持 SpriteAtlas 与纹理切片，共享句柄并管理图集 Sprite 克隆。清空、换图或销毁目标后，既有使用扫描归还引用；隐藏但保留图片时继续持有。两种图片入口共用请求替换状态，取消或失败保留当前图片；直接清空图片不会取消未完成请求。
取消可能返回 null，也可能抛异常，核实当前实现，不能只捕获 `OperationCanceledException`。
异步后的所有权转移见 [resource-patterns.md](resource-patterns.md)。

`location` 取决于当前 YooAsset 收集器的 AddressRule，不保证总是文件名，也不保证任意路径都有效。
先检查 `CheckLocationValid(location, packageName)` 和收集配置；包外地址不能靠去掉后缀“修复”。
同步 API 确实存在；大资源优先异步，Editor 工具和启动层不能机械套用业务层限制。
`UnloadUnusedAssets` 不替代配对释放；全包强制卸载、缓存删除需单独授权。
