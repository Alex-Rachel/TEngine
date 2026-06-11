# UITKModule Plan 2: Binding + MVVM + ListView + Editor Code Generator

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Editor pre-generation auto-binding, MVVM data binding, and UITKListView to the UITKModule core (Plan 1).

**Architecture:** Editor 脚本扫描带 [Q]/[OnClick]/[OnChange]/[Bind]/[BindCommand] 标记的 partial class，生成 `.bindgen.cs` 到同目录。生成的代码是普通 C#，正常参与 HybridCLR 热更新。零运行时反射。

**Tech Stack:** Unity 2022.3 Editor Scripts, UI Toolkit, UniTask, HybridCLR

**Spec Reference:** `docs/superpowers/specs/2026-06-11-uitk-module-design.md` sections 5-8, 12

**Depends On:** Plan 1 (Core Framework) fully implemented

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| ✅ Done | `UITKModule/Binding/QAttribute.cs` | [Q] attribute |
| ✅ Done | `UITKModule/Binding/OnClickAttribute.cs` | [OnClick] attribute |
| ✅ Done | `UITKModule/Binding/OnChangeAttribute.cs` | [OnChange] attribute |
| ✅ Done | `UITKModule/Binding/BindAttribute.cs` | [Bind] MVVM attribute |
| ✅ Done | `UITKModule/Binding/BindCommandAttribute.cs` | [BindCommand] attribute |
| ✅ Done | `UITKModule/Binding/BindingMode.cs` | Enum |
| ✅ Done | `UITKModule/MVVM/BindableProperty.cs` | Reactive property |
| ✅ Done | `UITKModule/MVVM/BindableCommand.cs` | Command |
| ✅ Done | `UITKModule/MVVM/BindableList.cs` | Observable collection |
| ✅ Done | `UITKModule/MVVM/ViewModelBase.cs` | Base class |
| ✅ Done | `UITKModule/MVVM/IValueConverter.cs` | Converters |
| ✅ Done | `UITKModule/ListView/UITKListView.cs` | Virtualized list |
| ✅ Done | `UITKModule/Base/UITKBase.cs` | BindContext/UnbindContext |
| Create | `Assets/Editor/UITKBindingGenerator/UITKBindingGenerator.cs` | 主生成器 |
| Create | `Assets/Editor/UITKBindingGenerator/UITKNamingHelper.cs` | 命名转换 |
| Create | `Assets/Editor/UITKBindingGenerator/UITKUXMLValidator.cs` | UXML 校验 |
| Create | Test: `TestAutoBindWindow.cs` + `.bindgen.cs` | 自动绑定测试 |
| Delete | `Packages/UITKSourceGenerator/` | 移除旧 Source Generator 项目 |
| Delete | `Assets/Plugins/UITKSourceGenerator/` | 移除旧 DLL |

---

## Task 1: Editor 代码生成工具 — UITKNamingHelper

**Files:**
- Create: `Assets/Editor/UITKBindingGenerator/UITKNamingHelper.cs`

功能：camelCase → kebab-case 转换 + 方法名推导目标元素

---

## Task 2: Editor 代码生成工具 — UITKUXMLValidator

**Files:**
- Create: `Assets/Editor/UITKBindingGenerator/UITKUXMLValidator.cs`

功能：解析 UXML 文件，提取所有带 name 的元素及类型，校验 [Q] 字段是否匹配

---

## Task 3: Editor 代码生成工具 — UITKBindingGenerator (核心)

**Files:**
- Create: `Assets/Editor/UITKBindingGenerator/UITKBindingGenerator.cs`

功能：
1. 菜单项 `TEngine/UITK/Generate All Bindings` — 扫描所有 partial class
2. 菜单项 `TEngine/UITK/Generate Binding (Selected)` — 仅当前选中文件
3. 扫描继承 UITKWindow/UITKWidget 的 partial class
4. 解析 [Q]、[OnClick]、[OnChange]、[Bind]、[BindCommand]
5. 生成 `XXX.bindgen.cs` 到源文件同目录
6. 包含 __UITKAutoBind、__UITKAutoBindEvents、__UITKAutoUnbindEvents
7. 包含 __UITKAutoBindViewModel、__UITKAutoUnbindViewModel (MVVM)
8. 可选：校验对应 UXML 文件中元素是否存在

---

## Task 4: 更新 UITKBase — 调用生成的方法

**Files:**
- Modify: `UITKModule/Base/UITKBase.cs`

在 BindContext 中调用 __UITKAutoBindViewModel（已由生成代码提供），移除运行时反射调用。

---

## Task 5: 自动绑定测试 — 生成 bindgen 文件并验证

**Files:**
- Modify: `TestAutoBindWindow.cs` — 使用 [Q]/[OnClick]/[OnChange]
- Create: `TestAutoBindWindow.bindgen.cs` — 由 Editor 工具生成

验证：打开 Unity → 菜单 Generate Bindings → 确认生成文件 → 运行测试窗口

---

## Task 6: MVVM 自动绑定测试

**Files:**
- Create: `TestMVVMAutoBindWindow.cs` — 使用 [Q] + [Bind] + [BindCommand]
- Create: `TestMVVMAutoBindWindow.bindgen.cs` — 由 Editor 工具生成

验证：ViewModel 数据变化 → UI 自动更新，按钮 CanExecute 状态正确

---

## Task 7: 清理旧 Source Generator 文件

**Files:**
- Delete: `Packages/UITKSourceGenerator/` 整个目录
- Delete: `Assets/Plugins/UITKSourceGenerator/` 整个目录
- Delete: `UITKModule/Binding/UITKAutoBindHelper.cs` (运行时反射，已不需要)

---

## Summary

Plan 2 (修订版) 交付:
- ✅ 所有 Binding Attributes (已完成)
- ✅ MVVM Core (已完成)
- ✅ UITKListView (已完成)
- ✅ BindContext/UnbindContext (已完成)
- 🔲 Editor 代码生成工具 (UITKBindingGenerator)
- 🔲 自动绑定测试 + MVVM 自动绑定测试
- 🔲 清理旧文件

剩余工作主要是 Task 1-3 (Editor 生成器) + Task 5-6 (测试) + Task 7 (清理)。
