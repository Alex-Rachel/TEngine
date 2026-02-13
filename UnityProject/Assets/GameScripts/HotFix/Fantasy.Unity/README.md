# Fantasy.Unity

**Unity 客户端高性能网络框架 | 支持全平台发布（iOS/Android/WebGL/PC）**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/qq362946/Fantasy/blob/main/LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2022.3.62+-black.svg)](https://unity.com/)
[![OpenUPM](https://img.shields.io/npm/v/com.fantasy.unity?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.fantasy.unity/)
[![.NET](https://img.shields.io/badge/.NET-Standard_2.1-purple.svg)](https://dotnet.microsoft.com/)

**[📖 官方文档](https://github.com/qq362946/Fantasy/tree/main/Docs)** | **[🚀 快速开始](#-快速开始)** | **[💬 QQ群: 569888673](http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=yourqrcode)**

---

## ⭐ Fantasy.Unity 是什么？

Fantasy.Unity 是 Fantasy Framework 的 **Unity 客户端版本**，为 Unity 游戏提供高性能、易用的网络通信能力。

**核心特点：**
- 🎮 **Unity 全平台支持** - iOS/Android/WebGL/PC/Mac 一套代码
- ⚡ **零反射架构** - 使用 Roslyn Source Generator，性能极致
- 🌐 **多协议支持** - TCP/KCP/WebSocket 自动适配
- 🔥 **与服务器共享代码** - 网络协议、实体定义可直接复用
- 🎯 **ECS 架构** - Entity-Component-System，灵活扩展
- 📦 **开箱即用** - 一行代码连接服务器

---

## 🚀 快速开始

### 安装 Fantasy.Unity

#### 方式一：通过 OpenUPM 安装（推荐）

**使用 Package Manager UI：**

1. 打开 `Edit` → `Project Settings` → `Package Manager`
2. 添加 Scoped Registry：
   - **Name**: `package.openupm.com`
   - **URL**: `https://package.openupm.com`
   - **Scope(s)**: `com.fantasy.unity`
3. 打开 `Window` → `Package Manager`
4. 点击 `+` → `Add package by name`
5. 输入 `com.fantasy.unity`，点击 `Add`

**或通过 manifest.json：**

编辑 `Packages/manifest.json`：

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.fantasy.unity"]
    }
  ],
  "dependencies": {
    "com.fantasy.unity": "2024.2.25"
  }
}
```

#### 方式二：通过 Git URL 安装

在 Package Manager 中点击 `+` → `Add package from git URL`，输入：

```
https://github.com/qq362946/Fantasy.git?path=Fantasy.Packages/Fantasy.Unity
```

### 配置编译符号

安装后需要添加编译符号：

1. 打开 `Fantasy` → `Fantasy Settings`
2. 安装 `FANTASY_UNITY` 编译符号
3. （WebGL 平台）安装 `FANTASY_WEBGL` 编译符号

### 创建服务器（可选）

如果你还没有服务器，可以使用 Fantasy CLI 快速创建：

```bash
# 安装 Fantasy CLI
dotnet tool install -g Fantasy.Cli

# 创建服务器项目
fantasy init -n MyGameServer
```

详见 [服务器快速开始](https://github.com/qq362946/Fantasy/blob/main/Docs/00-GettingStarted/01-QuickStart-Server.md)

---

## 💡 基础用法

### 1. 初始化框架

```csharp
using Fantasy;
using UnityEngine;

public class GameEntry : MonoBehaviour
{
    async void Start()
    {
        // 初始化 Fantasy 框架
        await Fantasy.Platform.Unity.Entry.Initialize();

        Debug.Log("Fantasy.Unity 初始化完成!");
    }
}
```

### 2. 连接服务器

```csharp
using Fantasy;
using Fantasy.Network;

public class NetworkManager : MonoBehaviour
{
    private Session _session;

    async void Start()
    {
        // 连接到 Gate 服务器
        _session = await Session.Create(
            "127.0.0.1:20000",
            NetworkProtocolType.KCP,  // 或 TCP / WebSocket
            () => Debug.Log("断线回调")
        );

        Debug.Log("连接成功!");
    }
}
```

### 3. 发送消息（一行代码）

假设你已经定义了协议（使用服务器的协议导出工具）：

```csharp
// 发送登录请求（自动生成的扩展方法）
var response = await _session.C2G_Login("player123", "password");

if (response.ErrorCode == 0)
{
    Debug.Log($"登录成功! Token: {response.Token}");
}
else
{
    Debug.LogError($"登录失败: {response.ErrorCode}");
}
```

### 4. 接收服务器主动推送

```csharp
// 定义消息处理器
public class G2C_NoticeHandler : Message<Session, G2C_Notice>
{
    protected override async FTask Run(Session session, G2C_Notice message)
    {
        Debug.Log($"收到服务器公告: {message.Content}");
        await FTask.CompletedTask;
    }
}
```

---

## 🌐 多协议支持

### KCP - 低延迟（推荐实时对战）

```csharp
var session = await Session.Create(
    "127.0.0.1:20000",
    NetworkProtocolType.KCP
);
```

### TCP - 稳定可靠

```csharp
var session = await Session.Create(
    "127.0.0.1:20000",
    NetworkProtocolType.TCP
);
```

### WebSocket - H5/WebGL 必选

```csharp
var session = await Session.Create(
    "ws://127.0.0.1:20000",
    NetworkProtocolType.WebSocket
);
```

**WebGL 平台自动使用 WebSocket，无需修改代码！**

---

## 🎮 平台支持

| 平台 | 支持 | 协议支持 | 说明 |
|------|------|---------|------|
| 🍎 **iOS** | ✅ | TCP/KCP/WebSocket | 完全支持 |
| 🤖 **Android** | ✅ | TCP/KCP/WebSocket | 完全支持 |
| 🌐 **WebGL** | ✅ | WebSocket only | 浏览器安全限制 |
| 🖥️ **Windows** | ✅ | TCP/KCP/WebSocket | 完全支持 |
| 🍎 **macOS** | ✅ | TCP/KCP/WebSocket | 完全支持 |
| 🐧 **Linux** | ✅ | TCP/KCP/WebSocket | 完全支持 |

---

## 🔧 进阶功能

### ECS 实体组件系统

```csharp
// 定义实体
public class Player : Entity
{
    public string Name { get; set; }
    public int Level { get; set; }
}

// 添加组件
var player = scene.AddEntity<Player>();
player.AddComponent<BagComponent>();
player.AddComponent<SkillComponent>();

// 系统自动执行
public class PlayerAwakeSystem : AwakeSystem<Player>
{
    protected override void Awake(Player self)
    {
        Debug.Log($"玩家 {self.Name} 创建成功!");
    }
}
```

### 事件系统

```csharp
// 发布事件
await EventSystem.Instance.PublishAsync(new PlayerLevelUpEvent
{
    PlayerId = 123,
    NewLevel = 10
});

// 监听事件
public class PlayerLevelUpHandler : EventSystem<PlayerLevelUpEvent>
{
    protected override async FTask Run(PlayerLevelUpEvent args)
    {
        Debug.Log($"玩家升级到 {args.NewLevel} 级!");
        await FTask.CompletedTask;
    }
}
```

### 与服务器共享协议

1. 使用 Fantasy CLI 创建服务器项目（包含协议工具）
2. 在服务器项目中定义 `.proto` 协议文件
3. 运行协议导出工具生成 C# 代码
4. 将生成的代码复制到 Unity 项目
5. 服务器和客户端使用完全相同的消息定义

---

## 📋 环境要求

| 组件 | 版本 | 说明 |
|------|------|------|
| **Unity** | 2022.3.62+ | 推荐使用 LTS 版本 |
| **Scripting Backend** | Mono / IL2CPP | 都支持 |
| **.NET Standard** | 2.1 | Unity 默认配置 |

---

## 📚 文档与教程

- 📖 **[官方文档](https://github.com/qq362946/Fantasy/tree/main/Docs)** - 完整使用指南
- 🚀 **[Unity 快速开始](https://github.com/qq362946/Fantasy/blob/main/Docs/00-GettingStarted/02-QuickStart-Unity.md)** - 5分钟上手
- 📝 **[Unity 启动代码编写](https://github.com/qq362946/Fantasy/blob/main/Docs/02-Unity/01-WritingStartupCode-Unity.md)** - 详细教程
- 💡 **[示例项目](https://github.com/qq362946/Fantasy/tree/main/Examples/Client/Unity)** - 可运行的完整示例
- 🎬 **[B站视频教程](https://space.bilibili.com/382126312)** - 视频讲解

---

## 🎯 与其他框架对比

| 功能 | Fantasy.Unity | 传统网络框架 |
|------|---------------|------------|
| **网络消息** | 1 行代码 | 50+ 行代码 |
| **协议定义** | 与服务器共享 | 需要手动同步 |
| **多协议** | 配置切换 | 需要重写 |
| **WebGL 支持** | 自动适配 | 需要特殊处理 |
| **性能** | 零反射 | 大量反射 |
| **跨平台** | 一套代码 | 平台特定代码 |

---

## 💬 社区与支持

- **QQ 讨论群**: **569888673**
- **联系邮箱**: 362946@qq.com
- **GitHub Issues**: [提交问题](https://github.com/qq362946/Fantasy/issues)
- **官方网站**: [www.code-fantasy.com](https://www.code-fantasy.com/)
- **B站**: [@Fantasy框架](https://space.bilibili.com/382126312)

---

## 🔗 相关包

- **[Fantasy-Net](https://www.nuget.org/packages/Fantasy-Net/)** - .NET 服务器框架
- **[Fantasy.Cli](https://www.nuget.org/packages/Fantasy.Cli/)** - 脚手架工具

---

## 🎁 示例代码

完整的 Unity 示例项目包含：

- ✅ 连接服务器示例
- ✅ 登录/注册流程
- ✅ 实时消息推送
- ✅ 聊天系统
- ✅ Addressable 路由消息
- ✅ 事件系统使用
- ✅ ECS 实体管理

查看示例：[Examples/Client/Unity](https://github.com/qq362946/Fantasy/tree/main/Examples/Client/Unity)

---

## ⚠️ 常见问题

### Q: WebGL 平台无法连接？

**A:** WebGL 只支持 WebSocket 协议，确保：
1. 安装了 `FANTASY_WEBGL` 编译符号
2. 服务器监听 WebSocket 端口
3. 使用 `ws://` 或 `wss://` 协议

### Q: IL2CPP 编译报错？

**A:** Fantasy.Unity 完全支持 IL2CPP，确保：
1. 已安装 `FANTASY_UNITY` 编译符号
2. Unity 版本 >= 2022.3.62
3. 查看 [常见问题文档](https://github.com/qq362946/Fantasy/tree/main/Docs)

### Q: 如何与服务器同步协议？

**A:** 推荐流程：
1. 使用 Fantasy CLI 创建服务器项目
2. 在服务器中定义 `.proto` 协议文件
3. 运行协议导出工具生成代码
4. 复制生成的代码到 Unity 项目

---

## 📄 开源协议

本项目采用 [MIT License](https://github.com/qq362946/Fantasy/blob/main/LICENSE) 开源协议。

---

## 🙏 感谢

感谢所有为 Fantasy 做出贡献的开发者！

[![Contributors](https://contrib.rocks/image?repo=qq362946/Fantasy)](https://github.com/qq362946/Fantasy/graphs/contributors)

---

**Built with ❤️ by Fantasy Team | Made for Unity Developers**

🎉 **如果 Fantasy.Unity 对你有帮助，请给项目一个 Star ⭐**

**[⭐ Star on GitHub](https://github.com/qq362946/Fantasy)**
