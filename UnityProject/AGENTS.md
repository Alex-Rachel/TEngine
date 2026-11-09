# Unity Project Instructions

公共工作方式见 [仓库入口](../AGENTS.md)。本文件只补充 Unity 项目约束。

## 工程事实

- 项目根是本目录，CLI 为 `Tools/unity.exe`，解决方案为 `UnityProject.sln`。
- 配置源位于仓库根 `Configs/GameConfig`，不是 Unity 项目内的 `Configs`。
- Unity 版本取 `ProjectSettings/ProjectVersion.txt`；Pipeline 能力以本项目注册命令及包内 `Documentation~` 为准。
- `.codex/skills` 是技能唯一源。按仓库入口路由读取；不要复制到第二套目录，也不要假设所有 Codex 客户端都会自动发现此目录。

## 编码边界

- 业务模块通过 `GameModule` 访问；框架启动层按其现有模块初始化方式工作。
- 业务异步使用 UniTask，明确取消、失败和资源归属。不要把框架现存的同步 API 当作不存在。
- Sprite 优先使用 `SetSprite`；实例化资源使用 `LoadGameObjectAsync`；普通 Asset 加载与释放配对。
- `Assets/GameScripts/GameEntry.cs`、`Procedure` 和 `Assets/Launcher` 属于主包；热更业务位于 `Assets/GameScripts/HotFix`。以 asmdef 验证边界，不虚构 `GameScripts/Main`。
- UI 使用 `AddUIEvent` 管理监听；非 UI 类管理自身订阅。隐藏不等于销毁。
- 修改配置加载器、生成类时追溯 `Configs/GameConfig` 中模板；不直接修补生成产物。
- 不直接编辑 Scene/Prefab YAML、GUID 或 `.meta` 来替代资源数据库操作；新增源文件的 `.meta` 由 Unity 生成。

## 验证选择

```powershell
python .codex/scripts/workflow.py doctor
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py verify --profile docs
python .codex/scripts/workflow.py verify --profile code
python .codex/scripts/workflow.py verify --profile unity
python .codex/scripts/workflow.py verify --profile full
```

`docs` 不连接 Editor；`code` 包含 docs 检查及完整解决方案构建；`unity` 包含 Editor 编译、指定资源和明确测试集；
`full` 组合全部并运行隔离的真实 Luban 普通/整表懒加载导出、当前配置源生成校验、浏览器图片/字体/多分辨率回归。
C# / 资源变更交付需同时取得 .NET 与相关 Unity 验证证据。纯文档或纯 Python 修改不必形式化触发 Unity。
Editor 断开、忙、多实例或选中零用例时，必需验证返回阻塞/失败，不能降级声称验收通过。

Unity 自动化以 `Packages/com.unity.pipeline` 为执行基础。命令使用实时发现的 `<group> <name>` 与参数 schema；
服务 ready 不等于 Editor 空闲，状态观察与新操作分别判断。具体规程只在项目 unity-cli 技能维护。
当前场景资产校验不遍历场景内部，不能替代场景内容验收；Luban lazyload 当前是整表懒加载，不代表分片能力。

Pipeline 描述文件含鉴权信息，禁止读出到日志、提交或分享。不得连接本机其他工程代替本项目验证。
