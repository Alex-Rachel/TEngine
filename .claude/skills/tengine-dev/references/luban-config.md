# TEngine Luban 配置表指南

## 目录

- [技术栈说明](#技术栈说明)
- [配置工程目录结构](#配置工程目录结构)
- [数据表定义规范](#数据表定义规范)
- [代码生成](#代码生成)
- [ConfigSystem 加载器](#configsystem-加载器)
- [配置数据访问](#配置数据访问)
- [配置管理器封装（推荐）](#配置管理器封装推荐)
- [类型支持](#类型支持)
- [添加新配置表流程](#添加新配置表流程)

---

## 技术栈说明

- **Luban**：高性能游戏配置表工具，支持 Excel/JSON/YAML → C# 代码 + 二进制数据
- **生成格式**：`cs-bin`（C# 代码）+ `bin`（二进制数据）
- **数据位置**：`Assets/AssetRaw/Configs/bytes/`（由 YooAsset 管理加载）
- **代码位置**：`GameScripts/HotFix/GameProto/GameConfig/`（热更程序集）

---

## 配置工程目录结构

```
TEngine/Configs/GameConfig/
├── luban.conf                      # Luban 主配置文件
├── gen_code_bin_to_project.bat     # 代码生成脚本（Windows）
├── Datas/                          # Excel 数据源目录
│   ├── __tables__.xlsx             # 表索引（所有表都在这里注册）
│   ├── __beans__.xlsx              # Bean 复合类型定义
│   ├── __enums__.xlsx              # 枚举类型定义
│   └── item.xlsx                   # 各业务数据表（示例）
└── CustomTemplate/                 # 自定义生成模板（通常不需要修改）
    ├── ConfigSystem.cs             # 配置加载器模板
    └── ExternalTypeUtil.cs         # Unity 类型转换工具模板
```

---

## 数据表定义规范

### __tables__.xlsx 表索引

每个业务数据表需要在此文件注册：

| full_name | value_type | read_mode | comment |
|-----------|-----------|-----------|---------|
| cfg.TbItem | Item | map | 道具表 |
| cfg.TbSkill | Skill | map | 技能表 |
| cfg.TbLevel | Level | list | 关卡表 |

- `full_name`：`cfg.表名`（会生成 `GameConfig.Tables.TbXxx`）
- `value_type`：对应 Excel 中的行类型（与 bean 名或自定义类名一致）
- `read_mode`：`map`（按 id 索引）/ `list`（列表）

### Excel 数据表结构

第1行：字段名（如 `id`, `name`, `hp`）
第2行：字段类型（如 `int`, `string`, `float`, `bool`, `int[]`, `vector3`）
第3行：分组（`c`=客户端, `s`=服务端, `cs`=双端，不填=全部）
第4行：注释（可选）
第5行起：数据行

```
| id    | name   | hp   | atk  | desc          |
| int   | string | int  | int  | string        |
| c     | c      | c    | c    | c             |
| 道具ID| 名称   | 血量 | 攻击 | 描述          |
| 1001  | 木剑   | 0    | 10   | 新手木剑      |
| 1002  | 铁剑   | 0    | 25   | 普通铁剑      |
```

### __beans__.xlsx 复合类型

自定义复合类型（类似 struct）：

| full_name | fields.name | fields.type | fields.comment |
|-----------|-------------|-------------|----------------|
| ItemDrop | itemId | int | 道具ID |
| ItemDrop | count | int | 数量 |
| ItemDrop | probability | float | 概率 |

在表中使用：`ItemDrop` 或 `ItemDrop[]`（数组）

---

## 代码生成

### Windows 执行生成脚本

```bat
# 在 TEngine/Configs/GameConfig/ 目录下运行
gen_code_bin_to_project.bat
```

脚本核心命令（内容）：

```bat
dotnet Luban.dll ^
    -t client ^
    -c cs-bin ^
    -d bin ^
    --conf luban.conf ^
    -x outputCodeDir=../../UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig ^
    -x outputDataDir=../../UnityProject/Assets/AssetRaw/Configs/bytes
```

### 生成产物

```
GameProto/GameConfig/
├── Tables.cs          # 主访问类（包含所有表的属性）
├── Item.cs            # Item 数据类
├── Skill.cs           # Skill 数据类
├── TbItem.cs          # TbItem 表类（含 Get/DataList 等方法）
├── TbSkill.cs
└── ...

AssetRaw/Configs/bytes/
├── item.bytes          # Item 表二进制数据
├── skill.bytes         # Skill 表二进制数据
└── ...
```

**重要**：`GameConfig/` 目录下的代码全部是**自动生成**的，不要手动修改，改动会在下次生成时覆盖。

---

## ConfigSystem 加载器

`ConfigSystem.cs` 在 `GameProto/` 中，桥接 Luban 生成代码与 YooAsset 资源系统：

```csharp
// GameScripts/HotFix/GameProto/ConfigSystem.cs
public class ConfigSystem
{
    private static ConfigSystem _instance;
    public static ConfigSystem Instance => _instance ??= new ConfigSystem();

    private Tables _tables;
    private bool   _init;

    /// <summary>访问所有配置表（懒加载）</summary>
    public Tables Tables
    {
        get
        {
            if (!_init) Load();
            return _tables;
        }
    }

    public void Load()
    {
        _tables = new Tables(LoadByteBuf);
        _init = true;
    }

    private ByteBuf LoadByteBuf(string file)
    {
        // 通过 YooAsset 同步加载（预加载阶段调用）
        var textAsset = GameModule.Resource.LoadAsset<TextAsset>(file);
        return new ByteBuf(textAsset.bytes);
    }
}
```

### 初始化时机

在 `ProcedurePreload` 中，配置数据标记为 `PRELOAD`，框架会自动预加载：

```csharp
// ProcedurePreload.cs（主包）
// 加载完所有 PRELOAD 资源后，配置数据已在内存中

// 在 ProcedureStartGame 或 GameApp.Entrance 中主动初始化：
ConfigSystem.Instance.Load();
```

---

## 配置数据访问

```csharp
// 单例访问
var tables = ConfigSystem.Instance.Tables;

// 按 ID 查询（map 模式）
var itemCfg = tables.TbItem.Get(1001);
if (itemCfg != null)
{
    Log.Info($"{itemCfg.Name}, ATK={itemCfg.Atk}");
}

// 遍历所有行（DataList）
foreach (var item in tables.TbItem.DataList)
{
    Log.Info(item.Id, item.Name);
}

// 安全获取（TryGet 方式，自己封装）
if (tables.TbSkill.TryGet(skillId, out var skillCfg))
{
    ApplySkill(skillCfg);
}
```

### 常见访问模式

```csharp
// 获取等级成长曲线（list 模式）
var level5Cfg = tables.TbLevel.DataList.FirstOrDefault(l => l.Level == 5);

// 按条件过滤
var rareSwords = tables.TbItem.DataList
    .Where(i => i.Type == ItemType.Sword && i.Rarity >= 3)
    .ToList();
```

---

## 配置管理器封装（推荐）

**对于有一定复杂度的模块，不应在业务代码中直接调用 `ConfigSystem.Instance.Tables.TbXxx`**，而应额外封装一个配置管理器（如 `LevelConfigMgr`），将查询逻辑集中管理。

### 好处

- 业务代码与配置结构解耦，Luban 字段改名只改管理器一处
- 可在管理器中做缓存、预处理、校验等逻辑
- 接口语义清晰，调用方无需了解表结构

### 封装模板

```csharp
// GameLogic/Config/LevelConfigMgr.cs
public class LevelConfigMgr
{
    private static LevelConfigMgr _instance;
    public static LevelConfigMgr Instance => _instance ??= new LevelConfigMgr();

    // 可在初始化时做预处理或缓存
    public void Initialize() { }

    /// <summary>获取关卡配置，找不到返回 null</summary>
    public Level GetLevel(int levelId)
    {
        return ConfigSystem.Instance.Tables.TbLevel.GetOrDefault(levelId);
    }

    /// <summary>获取某章节所有关卡（示例：带过滤的查询）</summary>
    public List<Level> GetChapterLevels(int chapterId)
    {
        return ConfigSystem.Instance.Tables.TbLevel.DataList
            .Where(l => l.ChapterId == chapterId)
            .ToList();
    }
}
```

### 使用

```csharp
// ✅ 推荐：通过管理器访问
var cfg = LevelConfigMgr.Instance.GetLevel(levelId);

// ❌ 不推荐直接在业务代码散落：
var cfg = ConfigSystem.Instance.Tables.TbLevel.GetOrDefault(levelId);
```

### 何时可以直接访问

简单的、只读一次的场景（如初始化时批量读取）可以直接访问 `ConfigSystem.Instance.Tables`。复杂模块、需要多处查询同一张表时，封装管理器。

---

## 类型支持

| Excel 类型 | C# 类型 | 示例 |
|-----------|---------|------|
| `int` | `int` | `100` |
| `long` | `long` | `1000000` |
| `float` | `float` | `1.5` |
| `bool` | `bool` | `true` |
| `string` | `string` | `"剑士"` |
| `int[]` | `int[]` | `1,2,3`（逗号分隔）|
| `string[]` | `string[]` | `a,b,c` |
| `vector2` | `UnityEngine.Vector2` | `1,2` |
| `vector3` | `UnityEngine.Vector3` | `1,2,3` |
| 自定义 Bean | 对应 C# 类 | `{id:1001,count:5}` |
| Bean 数组 | `Bean[]` | `[{...},{...}]` |
| 枚举 | 枚举类型 | `Sword`（枚举名） |

**Unity 类型说明**：`vector2` / `vector3` 在生成时通过 `ExternalTypeUtil` 转换为 `UnityEngine.Vector2/Vector3`。

---

## 添加新配置表流程

```
1. 在 Datas/__tables__.xlsx 中注册新表
   full_name: cfg.TbNewTable   value_type: NewTableRow   read_mode: map

2. 创建 Datas/new_table.xlsx，按规范添加列定义和数据

3. （如需复合类型）在 __beans__.xlsx 中定义 Bean

4. 运行 gen_code_bin_to_project.bat 生成代码和数据

5. 验证生成结果：
   - GameProto/GameConfig/ 下新增 NewTableRow.cs 和 TbNewTable.cs
   - AssetRaw/Configs/bytes/ 下新增 new_table.bytes

6. 在 Unity Editor 中确认 .bytes 文件被 YooAsset 正确收集

7. 在 GameLogic/Config/ 下创建对应的配置管理器（如 NewTableConfigMgr.cs）
   封装查询方法，业务代码通过管理器访问，不直接调用 ConfigSystem.Instance.Tables
```

**注意事项**：
- 字段名和类型一旦确定后不轻易修改（会影响热更包的兼容性）
- 新增字段可以前向兼容（旧客户端忽略新字段），删除字段不可前向兼容
- 测试时先在 Editor 模式加载验证格式，再发布热更包
