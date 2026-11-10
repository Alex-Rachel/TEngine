# 命名与绑定

- 跟随相邻代码：类型/方法 PascalCase，普通私有字段 `_camelCase`；不要批量改名无关代码。
- 窗口使用项目 `[Window]`，Widget 继承 UIWidget；事件接口用现有事件组及生成器。
- 异步业务方法返回 UniTask，Async 后缀；调用方 await 或显式 Forget 并安排异常观察。

## 绑定以资源为准

读取 [ScriptGeneratorSetting](../../../../Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs)、
[当前设置资源](../../../../Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset)、
[ScriptGenerator](../../../../Assets/Editor/UIScriptGenerator/ScriptGenerator.cs) 后确认字段风格、命名空间、
Widget 遍历边界和前缀规则。设置类的默认值可能与序列化资源不同。

`ScriptGenerator.WriteScript` 使用首个 StartsWith 匹配，不会自动按长度排序。
TMP_InputField/TMP_Dropdown、Scrollbar 等较长前缀须位于重叠短前缀前；
不能仅改名而不检查实际组件与生成字段类型。

HTML 烘焙由 [HtmlToUGUIBakeService](../../../../Assets/TEngine/Extension/HtmlToUGUI/Editor/HtmlToUGUIBakeService.cs)
按当前规则归一名称；首次处理可能改变节点名。流程交给 [html-to-ugui](../../html-to-ugui/SKILL.md)，
生成后重新核对绑定路径，Unity 资源变更通过 CLI 操作，不手改 Prefab YAML。

业务资源优先框架 API；Editor 内置字体/配置加载与业务资源职责不同，不全仓替换 Resources.Load 或 Instantiate。
业务通信沿用现有事件和公开接口，不为满足命名规则增加虚构抽象。

先核对签名再调用，尤其不要凭名称补造 `SetSpriteAsync`、`ReleaseSprite`、`OnClose` 或 string AddUIEvent。
生成代码回到生成器或模板修正，用户已删除的规范和资源不恢复。
