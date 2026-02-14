# TEngine

<div align="center">

![TEngine Logo](Books/src/TEngine.png)

**Unity 框架解决方案**

[![Unity Version](https://img.shields.io/badge/Unity-2021.3.20%2B-blue.svg?style=flat-square)](https://unity3d.com/)
[![License](https://img.shields.io/github/license/ALEXTANGXIAO/TEngine?style=flat-square)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine)
[![Issues](https://img.shields.io/github/issues/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine/issues)
[![Top Language](https://img.shields.io/github/languages/top/ALEXTANGXIAO/TEngine?style=flat-square)](https://github.com/ALEXTANGXIAO/TEngine)
[![DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Alex-Rachel/TEngine)

</div>

---

## 📖 简介

**TEngine** 是一个简单（新手友好、开箱即用）且强大的 Unity 框架全平台解决方案。对于需要一套上手快、文档清晰、高性能且可拓展性极强的商业级解决方案的开发者或团队来说，TEngine 是一个很好的选择。

### ✨ 核心特性

- 🚀 **开箱即用** - 5 分钟即可上手整套开发流程，代码整洁，思路清晰
- 🔥 **高性能** - 基于 UniTask 的异步系统，零 GC 事件分发，严格的内存管理
- 🧩 **高内聚低耦合** - 模块化设计，可轻松移除或替换不需要的模块
- 🔄 **热更新支持** - 集成 HybridCLR，全平台热更新流程已跑通
- 📦 **资源管理** - 集成 YooAsset，支持 LRU、ARC 缓存策略，自动资源释放
- 📊 **配置表系统** - 集成 Luban，支持懒加载、异步加载、同步加载
- 🎨 **UI 框架** - 商业化 UI 开发流程，支持代码自动生成
- 🌍 **全平台支持** - Windows、Android、iOS、WebGL、微信小游戏等

---

## 📚 目录

- [快速开始](#-快速开始)
- [文档导航](#-文档导航)
- [核心模块](#-核心模块)
- [项目结构](#-项目结构)
- [系统要求](#-系统要求)
- [服务器支持](#-服务器支持)
- [开源项目推荐](#-开源项目推荐)
- [Demo 项目](#-demo-项目)
- [贡献与支持](#-贡献与支持)

---

## 🚀 快速开始

### 环境要求

- **Unity 版本**: 2021.3.20f1c1（推荐）或更高
- **支持版本**: Unity 2019.4 / 2020.3 / 2021.3 / 2022.3
- **开发环境**: .NET 4.x
- **支持平台**: Windows、OSX、Android、iOS、WebGL

### 快速上手

1. **克隆项目**
   ```bash
   git clone https://github.com/ALEXTANGXIAO/TEngine.git
   ```

2. **打开项目**
   - 使用 Unity 2021.3.20f1c1 打开项目

3. **编辑器模式运行**
   - 选择顶部栏目 `EditorMode` 编辑器下的模拟模式
   - 点击 `Launcher` 开始运行

4. **打包运行**（热更新流程）
   - 运行菜单 `HybridCLR/Install...` 安装 HybridCLR
   - 运行菜单 `HybridCLR/Define Symbols/Enable HybridCLR` 开启热更新
   - 运行菜单 `HybridCLR/Generate/All` 进行必要的生成操作
   - 运行菜单 `HybridCLR/Build/BuildAssets And CopyTo AssemblyPath` 生成热更新 DLL
   - 运行菜单 `YooAsset/AssetBundle Builder` 构建 AB
   - 打开 Build Settings，点击 Build And Run

> 💡 **提示**: 遇到问题请查看 [HybridCLR 常见错误](https://hybridclr.doc.code-philosophy.com/docs/help/commonerrors)

详细教程请参考：[快速开始指南](Books/1-快速开始.md)

---

## 📚 文档导航

### 基础文档

| 文档 | 描述 |
|------|------|
| [📖 介绍](Books/0-介绍.md) | TEngine 框架介绍与核心特性 |
| [🏗️ 框架概览](Books/2-框架概览.md) | 框架架构与设计理念 |
| [🚀 快速开始](Books/1-快速开始.md) | 5 分钟快速上手教程 |
| [🌍 全平台运行](Books/99-各平台运行RunAble.md) | 各平台运行截图展示 |

### 核心模块文档

| 模块 | 文档 | 描述 |
|------|------|------|
| 📦 **资源模块** | [3-1-资源模块](Books/3-1-资源模块.md) | YooAsset 资源管理，支持 LRU/ARC 缓存 |
| 🎯 **事件模块** | [3-2-事件模块](Books/3-2-事件模块.md) | 零 GC 事件系统，支持 MVE 架构 |
| 💾 **内存池模块** | [3-3-内存池模块](Books/3-3-内存池模块.md) | 轻量级内存池管理 |
| 🎮 **对象池模块** | [3-4-对象池模块](Books/3-4-对象池模块.md) | 游戏对象池管理 |
| 🎨 **UI 模块** | [3-5-UI模块](Books/3-5-UI模块.md) | 商业化 UI 框架，支持代码生成 |
| 📊 **配置表模块** | [3-6-配置表模块](Books/3-6-配置表模块.md) | Luban 配置表系统 |
| 🔄 **流程模块** | [3-7-流程模块](Books/3-7-流程模块.md) | 商业化启动流程 |
| 🌐 **网络模块** | [3-8-网络模块](Books/3-8-网络模块.md) | 网络通信模块 |

---

## 🧩 核心模块

### 资源模块 (ResourceModule)

- ✅ 基于 YooAsset 的资源管理系统
- ✅ 支持 EditorSimulateMode、OfflinePlayMode、HostPlayMode
- ✅ AssetReference 资源引用标识，自动管理资源生命周期
- ✅ AssetGroup 资源组管理
- ✅ LRU/ARC 缓存策略
- ✅ 同步/异步加载支持

### 事件模块 (GameEvent)

- ✅ 零 GC 事件系统
- ✅ 支持 string/int 事件 ID
- ✅ 支持 MVE（Model-View-Event）架构
- ✅ UI 生命周期自动绑定事件清理

### UI 模块 (UIModule)

- ✅ 纯 C# 实现，脱离 Mono 生命周期
- ✅ 代码自动生成工具
- ✅ UIWindow/UIWidget 分层设计
- ✅ 支持全屏面板管理
- ✅ 事件驱动架构

### 配置表模块 (ConfigSystem)

- ✅ 集成 Luban 配置表解决方案
- ✅ 支持懒加载、异步加载、同步加载
- ✅ 强大的数据校验能力
- ✅ 完善的本地化支持

### 流程模块 (ProcedureModule)

完整的商业化启动流程：
- ProcedureLaunch → ProcedureSplash → ProcedureInitPackage
- ProcedurePreload → ProcedureInitResources
- ProcedureUpdateVersion → ProcedureUpdateManifest
- ProcedureCreateDownloader → ProcedureDownloadFile
- ProcedureDownloadOver → ProcedureClearCache
- ProcedureLoadAssembly → ProcedureStartGame

---

## 📁 项目结构

```
Assets/
├── AssetArt/              # 美术资源目录
│   └── Atlas/            # 自动生成图集目录
├── AssetRaw/             # 热更资源目录
│   ├── UIRaw/            # UI 图片目录
│   │   ├── Atlas/        # 需要自动生成图集的 UI 素材目录
│   │   └── Raw/          # 不需要自动生成图集的 UI 素材目录
│   ├── Audios/           # 音频资源
│   ├── Effects/          # 特效资源
│   └── Scenes/           # 场景资源
├── Editor/               # 编辑器脚本目录
├── HybridCLRData/        # HybridCLR 相关目录
├── Scenes/               # 主场景目录
├── TEngine/              # 框架核心目录
│   ├── Editor/           # TEngine 编辑器核心代码
│   ├── Runtime/          # TEngine 运行时核心代码
│   └── AssetSetting/     # YooAsset 资源设置
└── GameScripts/          # 程序集目录
    ├── Main/             # 主程序程序集（启动器与流程）
    └── HotFix/           # 游戏热更程序集目录
        ├── GameBase/     # 游戏基础框架程序集 [Dll]
        ├── GameProto/    # 游戏配置协议程序集 [Dll]
        └── GameLogic/    # 游戏业务逻辑程序集 [Dll]
            ├── GameApp.cs                  # 热更主入口
            └── GameApp_RegisterSystem.cs   # 热更主入口注册系统
```

---

## 💻 系统要求

### Unity 版本

- **推荐版本**: Unity 2021.3.20f1c1
- **支持版本**: Unity 2019.4 / 2020.3 / 2021.3 / 2022.3

### 平台支持

- ✅ Windows (Standalone)
- ✅ macOS (Standalone)
- ✅ Android
- ✅ iOS
- ✅ WebGL
- ✅ 微信小游戏

### 开发环境

- .NET 4.x
- Visual Studio 2019+ 或 Rider

---

## 🌐 服务器支持

TEngine 本身为**纯净的客户端框架**，不强绑定任何服务器。但针对个人开发以及中小型公司开发双端，我们推荐使用 **C# 服务器**。

### 为什么选择 .NET Core？

.NET Core 8.0 在性能和设计上具有显著优势：
- ⚡ **高性能** - AOT、JIT 混合编译
- 🔧 **组件化结构** - 模块化设计
- 🔥 **热重载** - 提升开发效率
- 📈 **性能测试** - 除 C++ 外，性能表现优异

### 服务器框架推荐

- **[GameNetty](https://github.com/ALEXTANGXIAO/GameNetty)** - 源于 ETServer，首次拆分最新的 ET8.1 的前后端解决方案（包），客户端最精简约 750k，几乎零成本无侵入嵌入
- **[Fantasy](https://github.com/Alex-Rachel/TEngine)** - 源于 ETServer 但极为简洁，更好上手的商业级服务器框架（Fantasy 分支已集成）

---

## 🌟 开源项目推荐

| 项目 | 描述 | 链接 |
|------|------|------|
| **YooAsset** | 商业级经历百万 DAU 游戏验证的资源管理系统 | [GitHub](https://github.com/tuyoogame/YooAsset) |
| **HybridCLR** | 特性完整、零成本、高性能、低内存的近乎完美的 Unity 全平台原生 C# 热更方案 | [GitHub](https://github.com/focus-creative-games/hybridclr) |
| **Luban** | 最佳游戏配置解决方案 | [GitHub](https://github.com/focus-creative-games/luban) |
| **Fantasy** | 源于 ETServer 但极为简洁，更好上手的商业级服务器框架 | [GitHub](https://github.com/Alex-Rachel/TEngine) |
| **GameNetty** | 源于 ETServer，首次拆分最新的 ET8.1 的前后端解决方案 | [GitHub](https://github.com/ALEXTANGXIAO/GameNetty) |
| **JEngine** | 使 Unity 开发的游戏支持热更新的解决方案 | [GitHub](https://github.com/JasonXuDeveloper/JEngine) |

### 社区 Demo

- **[TowerDefense-TEngine-Demo](https://github.com/daydayasobi/TowerDefense-TowerDefense-TEngine-Demo)** - 群友大佬的塔防 Demo

---

## 🎮 Demo 项目

最新的 Demo 飞机大战位于 **demo 分支**，欢迎体验！

```bash
git checkout demo
```

---

## 💡 为什么要使用 TEngine？

### 1. 开箱即用
- ✅ 5 分钟即可上手整套开发流程
- ✅ 代码整洁，思路清晰，功能强大
- ✅ 高内聚低耦合，可轻松移除或替换不需要的模块

### 2. 商业级解决方案
- ✅ 严格按照商业要求使用次世代的 **HybridCLR** 进行热更新
- ✅ 最佳的 **Luban** 配置表（支持懒加载、异步加载、同步加载）
- ✅ 百万 DAU 游戏验证过的 **YooAsset** 资源框架
- ✅ 全平台热更新流程已跑通

### 3. 严格的内存管理
- ✅ YooAsset 资源自动释放
- ✅ 支持 LRU、ARC 严格管理资源内存
- ✅ 防止内存泄漏

### 4. 商业化流程
- ✅ 商业化的热更新流程
- ✅ 商业化的 UI 开发流程
- ✅ 商业化的资源管理

### 5. 全平台验证
- ✅ 已有项目使用 TEngine 上架 **Steam**
- ✅ 已有项目使用 TEngine 上架 **微信小游戏**
- ✅ 已有项目使用 TEngine 上架 **App Store**

---

## 🤝 贡献与支持

## 🙏 感谢所有为 TEngine 做出贡献的开发者

[![Contributors](https://contrib.rocks/image?repo=Alex-Rachel/TEngine)](https://github.com/Alex-Rachel/TEngine/graphs/contributors)

### 贡献

欢迎提交 Issue 和 Pull Request！

### 支持项目

如果 TEngine 对您有帮助，欢迎支持项目发展：

[☕ 请我喝杯奶茶](Books/Donate.md)

您的赞助会让我们做得更快更好！如果觉得 TEngine 对您有帮助，不妨请我可爱的女儿买杯奶茶吧~ 🥤

---

<div align="center">

**Made with ❤️ by TEngine Team**

#### <a href="https://github.com/qq362946/Fantasy"><strong>Fantasy</strong></a> - Fantasy是一套源于ETServer但极为简洁，性能更强，更好上手的一套商业级服务器框架。
