# TEngine 热更包管理

## 目录

- [核心概念](#核心概念)
- [热更流程](#热更流程)
- [API 速查](#api-速查)
- [完整示例](#完整示例)

---

## 核心概念

热更包管理基于 **YooAsset** 实现，整体流程由主包流程状态机驱动（不可热更），分为以下阶段：

```
ProcedureInitResources     → 初始化 YooAsset 资源系统
ProcedureInitPackage       → 初始化资源包（决定运行模式）
ProcedureCreateDownloader  → 创建下载器（检查是否需要更新）
ProcedureDownloadFile      → 下载热更资源文件
ProcedurePreload           → 预加载 tag="PRELOAD" 的资源
ProcedureLoadAssembly      → 加载热更 DLL（HybridCLR）
```

完整启动流程见 [architecture.md](architecture.md#启动流程)。

---

## 热更流程

### 1. 获取版本信息

```csharp
// 获取当前本地资源包版本
string localVersion = GameModule.Resource.GetPackageVersion();

// 请求远端最新版本
var op = GameModule.Resource.RequestPackageVersionAsync();
await op.Task;
string remoteVersion = op.PackageVersion;
```

### 2. 更新 Manifest

```csharp
// 下载资源清单（对比本地与远端差异）
var updateOp = GameModule.Resource.UpdatePackageManifestAsync(remoteVersion);
await updateOp.Task;
```

### 3. 创建下载器并下载

```csharp
// 创建下载器（获取差量文件列表）
ResourceDownloaderOperation downloader = GameModule.Resource.CreateResourceDownloader();

int fileCount   = downloader.TotalDownloadCount;
long totalBytes = downloader.TotalDownloadBytes;

if (fileCount == 0)
{
    // 无需下载，直接进入下一流程
    return;
}

// 注册进度/错误回调，开始下载
downloader.OnDownloadProgressCallback = OnProgress;
downloader.OnDownloadErrorCallback    = OnError;
downloader.BeginDownload();
await downloader.Task;
```

### 4. 清理缓存（可选）

```csharp
// 清理本地冗余缓存（版本更新后调用）
GameModule.Resource.ClearCacheFilesAsync();
```

---

## API 速查

| 方法 | 说明 |
|------|------|
| `GetPackageVersion()` | 获取当前本地资源包版本号 |
| `RequestPackageVersionAsync()` | 向远端请求最新版本号 |
| `UpdatePackageManifestAsync(version)` | 更新资源清单到指定版本 |
| `CreateResourceDownloader()` | 创建差量下载器 |
| `downloader.TotalDownloadCount` | 待下载文件数量 |
| `downloader.TotalDownloadBytes` | 待下载总字节数 |
| `downloader.BeginDownload()` | 开始下载 |
| `ClearCacheFilesAsync()` | 清理本地冗余缓存 |

---

## 完整示例

```csharp
// 典型热更流程（简化版，对应 ProcedureCreateDownloader + ProcedureDownloadFile）
public async UniTask CheckAndDownloadUpdate()
{
    // 1. 请求远端版本
    var versionOp = GameModule.Resource.RequestPackageVersionAsync();
    await versionOp.Task;
    string remoteVersion = versionOp.PackageVersion;

    // 2. 更新 Manifest
    var manifestOp = GameModule.Resource.UpdatePackageManifestAsync(remoteVersion);
    await manifestOp.Task;

    // 3. 创建下载器
    var downloader = GameModule.Resource.CreateResourceDownloader();
    if (downloader.TotalDownloadCount == 0)
        return; // 已是最新，无需下载

    // 4. 显示下载 UI，绑定回调
    ShowDownloadUI(downloader.TotalDownloadCount, downloader.TotalDownloadBytes);
    downloader.OnDownloadProgressCallback = OnDownloadProgress;
    downloader.OnDownloadErrorCallback    = OnDownloadError;

    // 5. 执行下载
    downloader.BeginDownload();
    await downloader.Task;

    // 6. 清理旧缓存
    await GameModule.Resource.ClearCacheFilesAsync();
}
```
