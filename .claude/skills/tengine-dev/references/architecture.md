# TEngine 架构与项目结构

## 目录

- [分层架构](#分层架构)
- [目录结构](#目录结构)
- [程序集划分](#程序集划分)
- [启动流程](#启动流程)
- [核心模块列表](#核心模块列表)
- [资源目录组织](#资源目录组织)

---

## 分层架构

```
┌─────────────────────────────────────────────┐
│         游戏业务层 (HotFix)                  │
│  GameLogic / GameProto                      │
│  ↑ 热更代码，业务逻辑，高频变更              │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│         框架应用层 (Main)                    │
│  流程管理 / 热更新加载 / 启动器              │
│  ↑ 主包代码，不参与热更                      │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│         框架核心层 (TEngine)                 │
│  ModuleSystem / EventSystem / ResourceMgr   │
│  ↑ TEngine.Runtime 程序集                   │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│         基础设施层 (Unity + 第三方)           │
│  YooAsset / HybridCLR / UniTask / Luban     │
└─────────────────────────────────────────────┘
```

**依赖规则**：单向向下依赖，上层不能被下层引用，热更代码不能反向依赖主工程内部类型。

---

## 目录结构

```
TEngine/
├── Configs/GameConfig/           # Luban 配置表工程
│   ├── luban.conf                # Luban 配置入口
│   ├── Datas/                    # Excel 数据源
│   │   ├── __tables__.xlsx       # 表索引
│   │   ├── __beans__.xlsx        # Bean 复合类型定义
│   │   ├── __enums__.xlsx        # 枚举类型定义
│   │   └── item.xlsx             # 业务数据（示例）
│   └── gen_code_bin_to_project.bat  # 代码生成脚本（Windows）
│
└── UnityProject/Assets/
    ├── AssetRaw/                 # 热更资源目录（YooAsset 打包来源）
    │   ├── Actor/                # 角色资源
    │   ├── Audios/               # 音频资源
    │   ├── Configs/bytes/        # Luban 生成的二进制配置数据
    │   ├── Effects/              # 特效资源
    │   ├── Fonts/                # 字体资源
    │   ├── Materials/            # 材质资源
    │   ├── Scenes/               # 场景资源
    │   ├── Shaders/              # 着色器资源
    │   └── UI/                   # UI Prefab 和贴图
    ├── Atlas/                    # 自动生成图集目录
    ├── HybridCLRData/            # HybridCLR 相关数据
    ├── Plugins/Sirenix/          # Odin Inspector 插件（需购买）
    ├── TEngine/                  # 框架核心代码
    │   ├── Editor/               # 编辑器工具代码
    │   └── Runtime/              # 运行时核心代码
    │       ├── Core/             # 基础系统（事件/内存池/日志/工具）
    │       ├── Extension/        # 扩展功能（Json/Material/Tween）
    │       └── Module/           # 功能模块
    └── GameScripts/              # 游戏逻辑脚本
        ├── GameEntry.cs          # 游戏启动入口
        ├── Procedure/            # 主包流程状态机
        └── HotFix/               # 热更代码目录
            ├── GameLogic/        # 业务逻辑入口
            └── GameProto/        # Luban 生成的配置代码
```

---

## 程序集划分

| 程序集 | 路径 | 热更 | 职责 |
|--------|------|------|------|
| `TEngine.Runtime` | Assets/TEngine/Runtime/ | 否 | 框架核心，不依赖热更 |
| `GameScripts.Main` | Assets/GameScripts/ | 否 | 启动器、流程（Procedure） |
| `GameProto` | HotFix/GameProto/ | 是 | Luban 配置代码（自动生成） |
| `GameLogic` | HotFix/GameLogic/ | 是 | 业务逻辑，热更主入口 |

**约束**：
- `GameLogic` 可依赖 `GameProto`
- 热更代码不能引用主工程（`GameScripts.Main`）的 internal 类型

---

## 启动流程

```
GameEntry.Awake()
  ├── ModuleSystem.GetModule<IUpdateDriver>()    // 1. 帧驱动
  ├── ModuleSystem.GetModule<IResourceModule>()  // 2. 资源系统
  ├── ModuleSystem.GetModule<IDebuggerModule>()  // 3. 调试器
  ├── ModuleSystem.GetModule<IFsmModule>()       // 4. 状态机
  └── Settings.ProcedureSetting.StartProcedure() // 5. 启动流程

流程状态机（主包，非热更）：
ProcedureLaunch
  → ProcedureInitResources     // 初始化 YooAsset 资源系统
  → ProcedureInitPackage       // 初始化资源包
  → ProcedureCreateDownloader  // 创建热更下载器
  → ProcedureDownloadFile      // 下载热更资源
  → ProcedurePreload           // 预加载 tag="PRELOAD" 的资源
  → ProcedureLoadAssembly      // 加载热更 DLL（HybridCLR）
  → ProcedureStartGame         // 调用 GameApp.Entrance() 进入热更域

热更入口（GameLogic/GameApp.cs）：
GameApp.Entrance(assemblies)
  ├── GameEventHelper.Init()   // 初始化接口事件代理
  ├── 注册所有业务系统
  └── 进入游戏主流程
       → ProcedureLogin → ProcedureSelectRole → ProcedureMain
```

---

## 核心模块列表

通过 `GameModule.XXX` 访问（已缓存，推荐）：

| 属性 | 接口 | 职责 |
|------|------|------|
| `GameModule.Resource` | `IResourceModule` | YooAsset 资源加载/卸载/热更 |
| `GameModule.UI` | `UIModule` | UI 窗口管理 |
| `GameModule.Audio` | `IAudioModule` | 音频播放/音量控制 |
| `GameModule.Timer` | `ITimerModule` | 计时器（循环/单次/非缩放时间）|
| `GameModule.Scene` | `ISceneModule` | 场景加载/卸载 |
| `GameModule.Fsm` | `IFsmModule` | 有限状态机 |
| `GameModule.Procedure` | `IProcedureModule` | 流程状态机 |
| `GameModule.Localization` | `ILocalizationModule` | 多语言本地化 |

直接通过 `ModuleSystem.GetModule<T>()` 获取（用于 TEngine 内部或框架扩展）：
```csharp
var res = ModuleSystem.GetModule<IResourceModule>();
```

---

## 资源目录组织

```
Assets/AssetRaw/              # 所有热更资源的根目录
├── Actor/                    # 按角色分子目录
│   └── Hero/Prefabs/
├── Audios/
│   ├── BGM/                  # 背景音乐（循环）
│   └── SFX/                  # 音效（单次）
├── Configs/bytes/            # Luban 生成数据（*.bytes）
├── Effects/                  # 粒子特效 Prefab
├── Scenes/                   # 场景 Asset（非 StreamingAssets）
├── UI/
│   ├── Atlas/                # UI 图集贴图
│   └── Prefabs/              # UI Prefab
└── ...
```

**资源标签约定**：
- `PRELOAD`：启动时预加载资源（配置数据、公共 UI 等）
- 资源 location（地址）等于资源文件名（不含路径和扩展名），YooAsset 通过 `AssetBundleCollector` 自动收集

**重要**：所有游戏运行时使用的资源必须放在 `AssetRaw/` 下，由 YooAsset 管理加载；不要使用 `Resources.Load()`。
