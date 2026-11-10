# HtmlToUGUI 的 UI-DSL

此规范限定当前浏览器烘焙脚本和 `HtmlToUGUIBakeService` 支持的 HTML 子集。
HTML 测量得到 JSON；生成 Prefab、资源导入和 Unity 视觉验收是后续独立阶段。

## 根节点与分辨率

- 使用唯一的命名根节点，声明 `data-u-type="div"` 和 `data-u-name`。
- 根节点通过 CSS `width`、`height` 定义设计尺寸，并与烘焙参数一致。
- 将生成节点放在根节点范围内；滚动内容超出可视区域时检查实际 ScrollRect 层级。
- 烘焙尺寸参数接受 `1..8192` 的整数，不随意放大画布掩盖布局问题。

例如：

```html
<div data-u-type="div" data-u-name="m_mainWindow"
     data-u-layout="stretch"
     style="width: 1920px; height: 1080px; position: relative;">
</div>
```

## 节点属性

每个需要生成的节点声明：

- `data-u-name`：稳定名称；不含 `/`，同级名称唯一。
- `data-u-type`：`div`、`image`、`text`、`button`、`input`、`scroll`、`toggle`、`slider` 或 `dropdown`。

未标记的布局元素仍会影响浏览器测量，不要假定每个 DOM 元素都会生成同名 Unity 对象。
名称会经过项目 UI 脚本生成器规则规范化，最终名称和组件以 Prefab 为准。建议前缀：

| data-u-type | 前缀 | 示例 |
| --- | --- | --- |
| `div` | `m_` | `m_panelRoot` |
| `image` | `m_img` | `m_imgAvatar` |
| `text` | `m_text` | `m_textTitle` |
| `button` | `m_btn` | `m_btnClose` |
| `input` | `m_input` | `m_inputAccount` |
| `scroll` | `m_scroll` | `m_scrollItems` |
| `toggle` | `m_toggle` | `m_toggleSound` |
| `slider` | `m_slider` | `m_sliderVolume` |
| `dropdown` | `m_dropdown` | `m_dropdownQuality` |

## 图片

图片绑定仅支持 `image`、`div` 节点，例如：

```html
<img data-u-type="image" data-u-name="m_imgAvatar" src="images/avatar.png">

<div data-u-type="image" data-u-name="m_imgIcon" data-u-src="images/icon.png"></div>

<div data-u-type="image" data-u-name="m_imgBg"
     style="background-image: url(images/bg.png); background-size: cover;"></div>
```

支持的输入形式：

- Unity JSON 输入可使用 `Assets/...` 或 `Packages/...` 资源路径。
- 相对本地路径由 HTML/JSON 的来源目录或配置的来源根目录解析。
- 工程外绝对路径须位于显式来源根目录内；生成 Prefab 时复制到配置的 Unity 导入目录。
- `Assets`、`Packages` 内的图片可直接引用；Package 图片须已导入为 Sprite，不修改其 importer。

浏览器烘焙使用 HTML 相对本地图片路径，或浏览器能解析的 file URL。
`Assets/...`、`Packages/...` 不是浏览器别名；浏览器能访问文件也不代表它符合 Unity 预检的来源范围。
所有引用图片都须实际存在，示例中的路径需配套本地文件。预览中的图片哈希、目标路径和 importer 变化属于写入范围。

不支持：

- `http://`、`https://` 远程图片。
- `data:` URI。
- 来源范围外或经过符号链接、junction 的自动化资源路径。

用 `data-u-fit` 声明图片意图：

| data-u-fit | 当前 Unity 行为 |
| --- | --- |
| `contain`、`scale-down` | 赋 Sprite 并保留宽高比 |
| `cover` | 保留宽高比，当前不实现浏览器式裁切铺满 |
| `stretch` | 不保留宽高比 |
| `slice` | Sprite 有 border 时使用 Sliced |

## 布局

优先通过 `data-u-layout` 声明多端布局意图：

| data-u-layout | 含义 |
| --- | --- |
| `stretch` | 拉伸至父节点四边 |
| `top-bar` | 横向拉伸并贴顶 |
| `bottom-bar` | 横向拉伸并贴底 |
| `left-panel` | 纵向拉伸并贴左 |
| `right-panel` | 纵向拉伸并贴右 |
| `center` | 固定尺寸居中 |
| `fixed` | 与 v1 兼容的左上固定坐标 |

需要精确 RectTransform 时可显式给出：

- `data-u-anchor-min="0,0"`
- `data-u-anchor-max="1,1"`
- `data-u-pivot="0.5,0.5"`
- `data-u-offset-min="0,0"`
- `data-u-offset-max="0,0"`

显式布局需同时给出两组 anchors 和两组 offsets；pivot 可按需声明。

`data-u-safe-area="true"` 当前只映射为拉伸意图，不生成运行时安全区组件，也不证明刘海屏适配通过。
生成 Prefab 不包含临时 Canvas，宿主 CanvasScaler 按预览的 `canvasIntegration` 配置并另行验证。

## 控件属性

| 控件 | 属性 | 含义 |
| --- | --- | --- |
| `scroll` | `data-u-dir="v"` 或 `"h"` | 滚动方向 |
| `toggle` | `data-u-checked="true"` 或 `"false"` | 初始勾选状态 |
| `slider` | `data-u-value="0"` 等 `0..1` 数值 | 初始值；0 必须保留 |
| `dropdown` | `<select>` 内的 `<option>` | 选项文本 |
| `input` | `placeholder` | 占位文字 |

## CSS 与文字

- 设计尺寸优先用 `px`；flex、绝对定位、margin、padding 由浏览器计算最终矩形，不转换成对应 Unity 布局组件。
- 烘焙时避免 animation、transition。脚本、事件属性、外部 frame、link 和导航 metadata 会被拒绝。
- 字体使用本地 CSS `@font-face`；脚本等待字体与图片加载，缺失资源应使烘焙失败。
- `background-color`、`color`、`font-size`、`text-align` 可进入样式数据；浏览器支持的其他 CSS 不保证在 UGUI 复现。
- CSS 字体文件不会自动转换为 TMP 字体资源；实际 Unity 字体、字形覆盖和排版须另验。
- PC/mobile/pad 需要分别检查布局；单张浏览器截图不能证明 Unity 多端视觉通过。

## 示例

```html
<div data-u-type="div" data-u-name="m_mainWindow" data-u-layout="stretch"
     style="width: 1920px; height: 1080px; position: relative; overflow: hidden;">
  <div data-u-type="image" data-u-name="m_imgBg"
       data-u-layout="stretch" data-u-fit="cover"
       style="position:absolute; inset:0; background-image:url(images/bg.png); background-size:cover;"></div>

  <div data-u-type="div" data-u-name="m_topBar" data-u-layout="top-bar"
       style="position:absolute; left:0; top:0; width:1920px; height:96px; background-color:#00000080;">
    <span data-u-type="text" data-u-name="m_textTitle"
          style="position:absolute; left:40px; top:24px; width:400px; height:48px; color:#fff; font-size:36px; text-align:left;">
      Main
    </span>
  </div>

  <img data-u-type="image" data-u-name="m_imgAvatar"
       src="images/avatar.png" data-u-layout="fixed" data-u-fit="contain"
       style="position:absolute; left:40px; top:120px; width:96px; height:96px;">

  <button data-u-type="button" data-u-name="m_btnStart" data-u-layout="center"
          style="position:absolute; left:810px; top:500px; width:300px; height:96px; background-color:#27ae60; color:#fff; font-size:32px;">
    Start
  </button>
</div>
```
