# TEngine Repository Instructions

使用中文交流、记录决策和交付结果。以当前源码、测试和实际工具结果为依据，不把旧文档当作 API 保证。

## 工作方式

1. 先读相关实现与 `git status`，保留已有修改；不得恢复用户删除的文件。
2. 根据影响面选择验证。小改直接实现；跨模块、高风险、多阶段任务先明确目标和验收，用一份任务文档记录决策、授权与证据。
3. 读取下表对应技能的 `SKILL.md`，再按需读取 reference。未出现在技能菜单时直接读取文件，不依赖用户级安装。
4. 源码用代码编辑工具修改。Unity 对象、序列化、资源数据库和 Editor 生命周期通过本项目 Unity CLI 验证。
5. 交付说明变更、实际执行的验证、剩余风险和阻塞。没有证据的检查不能标成通过。

| 任务 | 项目技能入口 |
|---|---|
| TEngine 框架、UIWindow、事件、资源、HybridCLR | [tengine-dev](UnityProject/.codex/skills/tengine-dev/SKILL.md) |
| Unity Editor/Player 查询、资产操作、测试、构建预检 | [unity-cli](UnityProject/.codex/skills/unity-cli/SKILL.md) |
| Luban 配置结构、数据、导出及分片能力核对 | [luban-dev](UnityProject/.codex/skills/luban-dev/SKILL.md) |
| HTML/UI-DSL 转 UGUI Prefab | [html-to-ugui](UnityProject/.codex/skills/html-to-ugui/SKILL.md) |

## 授权边界

- 普通源码修改按用户任务实施。Unity 写入先预览具体目标和范围，再集中确认。
- 删除、覆盖、包依赖/项目设置变更、平台切换、真实构建须单独明确授权；计划获准不等于获准发布。
- 不自动安装/升级工具，不提交、推送、上传、签名或发布，不修改用户级技能配置。
- 不使用旧的规范管理工具或 Unity MCP。其他项目/用途的全局技能不属于本仓库依赖。
- CLI 的 `confirm` 参数不是用户授权本身。脚本审批记录用于防止误操作，不是对恶意本地用户的安全沙箱。

## 验证入口

从仓库根执行 `python UnityProject/.codex/scripts/workflow.py --help`。
完整说明见 [工作流指南](Books/AI-Development-Workflow.md)，Unity 专项约束见 [项目入口](UnityProject/AGENTS.md)。
任务模板见 [task.md](UnityProject/.codex/templates/task.md)。不要求小改创建任务文件。

## Review

优先报告可复现的缺陷、资源/事件生命周期问题、生成代码与模板漂移、序列化兼容性、授权和测试缺口。
源码 API 与文档冲突时先核实源码；本次范围内修正文档，否则在任务记录中列出具体差异，不自动积累无关记忆文件。
