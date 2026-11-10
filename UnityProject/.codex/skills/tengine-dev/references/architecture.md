# 程序集与启动

| 路径（Unity 项目内） | 边界 |
|---|---|
| `Assets/TEngine/Runtime` | [TEngine.Runtime.asmdef](../../../../Assets/TEngine/Runtime/TEngine.Runtime.asmdef)，框架 |
| `Assets/TEngine/Editor` | [TEngine.Editor.asmdef](../../../../Assets/TEngine/Editor/TEngine.Editor.asmdef)，Editor 专用 |
| `Assets/Launcher` | [Launcher.asmdef](../../../../Assets/Launcher/Launcher.asmdef)，主包启动器 |
| `Assets/GameScripts/GameEntry.cs`、`Procedure` | 主包，按最近 asmdef/预定义程序集确认 |
| `Assets/GameScripts/HotFix/GameProto` | [GameProto.asmdef](../../../../Assets/GameScripts/HotFix/GameProto/GameProto.asmdef)，协议和配置 |
| `Assets/GameScripts/HotFix/GameLogic` | [GameLogic.asmdef](../../../../Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef)，业务和 UI |
| `Assets/TEngine/Extension/HtmlToUGUI` | 共用配置及 Editor 烘焙工具 |

不要发明 `GameScripts/Main` 或 `GameScripts.Main`。程序集事实来自 asmdef，而不是目录名“看起来像”。
GameLogic 可依赖 GameProto/TEngine.Runtime；底层与 GameProto 不反向引用 GameLogic。

## 沿入口定位

入口：[GameEntry](../../../../Assets/GameScripts/GameEntry.cs)、
[ProcedureLoadAssembly](../../../../Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs)、
[GameApp](../../../../Assets/GameScripts/HotFix/GameLogic/GameApp.cs)、
[GameModule](../../../../Assets/GameScripts/HotFix/GameLogic/GameModule.cs)。

`GameEntry.Awake` 取得启动所需框架模块并启动 Procedure；
`ProcedureLoadAssembly` 根据设置和运行模式查找或加载程序集，再反射调用 `GameApp.Entrance(object[])`。
其首项为程序集列表，当前入口先初始化接口事件，再启动业务。
框架初始化可调用 ModuleSystem；GameLogic 业务层通过 GameModule 访问模块。

修改跨程序集调用时同时核对引用方向、条件编译和主包能否在热更代码加载前独立运行。
新增源码后由 Unity 导入并更新工程文件，旧 csproj 构建成功不能证明新增文件参与编译。

YooAsset 收集规则、HybridCLR 列表、当前 BuildTarget 和定义符共同决定产物。
[UpdateSetting](../../../../Assets/TEngine/Runtime/Core/UpdateSetting.cs) 的字段初值不等于当前序列化设置，
实际 Editor 状态交给 [unity-cli](../../unity-cli/SKILL.md) 查询。
`.NET` 编译不能验证资源地址、IL2CPP/AOT 或真机热更；按 [验证路由](verification.md) 补足相关证据。
