# CLAUDE.md

请使用中文写提案和回答
这个文件为 Claude Code (claude.ai/code) 提供指导，用于处理此代码库中的代码。

TEngine 基于 HybridCLR + YooAsset + UniTask + Luban 构建。

---

## ⚡ 强制工作流（所有任务必须遵守）

> **禁止跳过** — 无论任务大小，必须按此顺序执行：

### 第零步：判断任务等级

在执行任何操作前，先判断任务等级：

| 等级 | 判断标准 | 知识查询策略 |
|------|---------|-------------|
| **L1 简单** | typo 修正、注释修改、日志输出、单行变量改名（**前提：不涉及框架 API 名称、UI 节点前缀、事件定义或资源路径**） | ❌ 跳过查询，直接编码 |
| **L2 调用** | 调用已知 API、单一模块的局部修改 | ✅ 触发 `tengine-dev` skill（只查该主题） |
| **L3 功能** | 新功能开发、跨文件修改、新增 UI/资源/事件逻辑 | ✅ 触发 `tengine-dev` skill（全量相关主题） |
| **L4 架构** | 模块设计、系统重构、多模块协作、架构决策 | ✅ 触发 `tengine-dev` skill（并行多主题） |

> **判断原则**：宁可高估等级，不可低估——不确定时上调一级。

---

### 第一步：按等级获取规范（使用 tengine-dev skill）

**L1 任务直接跳到第二步。L2-L4 必须先触发 `tengine-dev` skill。**

**知识源**：`.claude/skills/tengine-dev/references/`（AI 专用精炼文档，唯一权威来源）

#### 调用方式

```
使用 Skill 工具，skill = "tengine-dev"
描述需要查询的技术问题或功能点
```

#### 会话内缓存（避免重复查询）

同一会话中已查询过的主题无需重复触发 skill：
- 直接引用本次会话已获取的规范摘要
- 仅当任务涉及**本次会话未覆盖的新主题**时才重新触发

#### 触发时机

| 场景 | 必须查询主题 |
|------|------------|
| UI 开发 (UGUI) | ui-lifecycle.md — UIWindow 生命周期、UIWidget 规范 |
| UI 开发 (UIToolkit) | uitk-module-design.md — UITKWindow/Widget、自动绑定、MVVM、动画 |
| 资源加载 | resource-api.md — LoadAssetAsync API、释放时机 |
| 热更代码 | hotfix-workflow.md — 程序集划分、GameApp 入口、热更边界 |
| 事件系统 | event-system.md — GameEvent 用法、AddUIEvent 规范 |
| 模块使用 | modules.md — GameModule.XXX API、模块生命周期 |
| Luban 配置 | luban-config.md — 配置表生成流程、访问方式 |
| 代码规范 | naming-rules.md — 命名约定、节点前缀、设计模式 |

---

### 第二步：输出代码/方案

基于 tengine-dev skill 返回的规范编写实现。

**当 references 规范与代码实际 API 冲突时**：
1. 使用 Grep 搜索实际方法签名验证（例：`Grep "ForceUnloadUnusedAssets"` 确认参数名）
2. 优先信任代码中的实际实现
3. 在输出中标注冲突点，并记录到 `.claude/memory/` 供后续修正

---

## 核心原则（编码红线）

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`
6. **UIToolkit 开发**：新 UI 使用 `GameModule.UITK` (UITKModule)，旧 UI 保持 `GameModule.UI` (UIModule)
7. **UIToolkit 绑定**：使用 `[Q]` + `[OnClick]` + `partial class`，通过菜单 `TEngine/UITK/Generate All Bindings` 生成绑定代码
8. **UIToolkit 音效**：通过 `IUITKClickSoundHandler` 全局处理，不在单个按钮上手动注册音效

---

## 📚 References 参考文档

> **AI 唯一权威来源：`.claude/skills/tengine-dev/references/`**

| 文档 | 内容 | 层级 |
|-----|------|------|
| architecture.md | 项目结构/启动流程 | 核心 |
| modules.md | 模块 API（Timer/Scene/Audio/Fsm）| 核心 |
| ui-lifecycle.md | UI 开发（生命周期/层级/属性）| 核心 |
| event-system.md | 事件系统（两种模式/核心接口）| 核心 |
| resource-api.md | 资源加载/卸载 | 核心 |
| hotfix-workflow.md | 热更代码（HybridCLR/程序集划分/热更包）| 核心 |
| luban-config.md | 配置表 | 核心 |
| naming-rules.md | 代码规范/命名约定/节点前缀 | 核心 |
| ui-patterns.md | UI 进阶（Widget 模板/节点绑定）| 进阶 |
| event-antipatterns.md | 事件避坑（内存泄漏/接口无响应/风暴）| 进阶 |
| resource-patterns.md | 资源管理模式/生命周期/泄漏根因 | 进阶 |
| mcp-tools.md | MCP 场景/GameObject/UI Prefab/脚本/Editor/测试 | MCP |
| mcp-visual.md | MCP 材质/Shader/VFX/动画 | MCP |
| troubleshooting.md | 问题排查 | 排障 |
| **UITKModule 相关** | | |
| uitk-module-design.md | UITKModule 完整设计文档 | 核心 |
| uitk-lifecycle.md | UITKWindow/Widget 生命周期、自动绑定流程 | 核心 |
| uitk-binding.md | [Q]/[OnClick]/[OnChange] 用法、Editor 生成工具 | 核心 |
| uitk-mvvm.md | BindableProperty/Command/List、ViewModel 模式 | 核心 |

---

## 🔧 自我优化机制

### 问题记录

**触发条件**（满足任一即记录）：
1. 发现 references 文档描述与实际代码 API 不符（通过 Grep/Read 验证）
2. AI 生成的代码在编译/运行时报错，根因是知识库描述有误
3. 用户明确指出某文档描述有误

**记录规范**：
- 文件名：`problem_YYYY-MM-DD.md`（如 `problem_2026-04-21.md`）
- 必填字段：
  - **问题现象**：错误表现或报错信息
  - **文档位置**：哪篇 reference 文档哪一节
  - **正确 API**：经代码验证后的正确用法
  - **建议修正**：文档应改成什么表述
