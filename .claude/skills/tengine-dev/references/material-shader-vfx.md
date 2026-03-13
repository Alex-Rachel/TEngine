# 材质、Shader、VFX 与动画操作（unity-mcp）

## 目录
- [manage_material：材质管理](#manage_material材质管理)
- [manage_shader：Shader 文件管理](#manage_shaderShader-文件管理)
- [manage_texture：纹理设置](#manage_texture纹理设置)
- [manage_vfx：粒子与特效](#manage_vfx粒子与特效)
- [manage_animation：动画控制器与片段](#manage_animation动画控制器与片段)

---

## manage_material：材质管理

### 创建材质
```json
{
  "tool": "manage_material",
  "params": {
    "action": "create",
    "materialName": "EnemyMaterial",
    "shaderName": "Universal Render Pipeline/Lit",
    "savePath": "Assets/AssetRaw/Materials/EnemyMaterial.mat"
  }
}
```
常用 shader 名称：
- `Universal Render Pipeline/Lit`（URP 标准）
- `Universal Render Pipeline/Unlit`（URP 无光照）
- `Standard`（内置管线）
- `Sprites/Default`（2D 精灵）
- `UI/Default`（UGUI）

### 设置颜色
```json
{
  "tool": "manage_material",
  "params": {
    "action": "set_material_color",
    "materialPath": "Assets/AssetRaw/Materials/EnemyMaterial.mat",
    "colorProperty": "_BaseColor",
    "r": 1.0, "g": 0.2, "b": 0.2, "a": 1.0
  }
}
```
常用颜色属性名：`_BaseColor`（URP）、`_Color`（标准）、`_EmissionColor`（自发光）

### 设置 Shader 属性
```json
{
  "tool": "manage_material",
  "params": {
    "action": "set_material_shader_property",
    "materialPath": "Assets/AssetRaw/Materials/EnemyMaterial.mat",
    "propertyName": "_Metallic",
    "propertyType": "float",
    "value": 0.8
  }
}
```
`propertyType` 可选：`float`、`int`、`color`、`vector`、`texture`

### 将材质赋给渲染器
```json
{
  "tool": "manage_material",
  "params": {
    "action": "assign_material_to_renderer",
    "target": "EnemyMesh",
    "materialPath": "Assets/AssetRaw/Materials/EnemyMaterial.mat",
    "materialIndex": 0
  }
}
```

### 设置渲染器颜色（快捷方式）
```json
{
  "tool": "manage_material",
  "params": {
    "action": "set_renderer_color",
    "target": "Enemy",
    "r": 1.0, "g": 0.0, "b": 0.0, "a": 1.0
  }
}
```

### 获取材质信息
```json
{
  "tool": "manage_material",
  "params": {
    "action": "get_material_info",
    "materialPath": "Assets/AssetRaw/Materials/EnemyMaterial.mat"
  }
}
```

---

## manage_shader：Shader 文件管理

### 创建 Shader 文件
```json
{
  "tool": "manage_shader",
  "params": {
    "action": "create",
    "name": "GlowEffect",
    "path": "Assets/AssetRaw/Shaders",
    "contents": "Shader \"Custom/GlowEffect\" {\n    Properties {\n        _MainTex (\"Texture\", 2D) = \"white\" {}\n        _GlowColor (\"Glow Color\", Color) = (1,1,0,1)\n        _GlowIntensity (\"Intensity\", Range(0,5)) = 1\n    }\n    SubShader {\n        Tags { \"RenderType\"=\"Opaque\" }\n        CGPROGRAM\n        #pragma surface surf Lambert\n        sampler2D _MainTex;\n        fixed4 _GlowColor;\n        float _GlowIntensity;\n        struct Input { float2 uv_MainTex; };\n        void surf (Input IN, inout SurfaceOutput o) {\n            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);\n            o.Albedo = c.rgb;\n            o.Emission = _GlowColor.rgb * _GlowIntensity;\n        }\n        ENDCG\n    }\n}"
  }
}
```

### 删除 Shader
```json
{
  "tool": "manage_shader",
  "params": {
    "action": "delete",
    "name": "GlowEffect",
    "path": "Assets/AssetRaw/Shaders"
  }
}
```

---

## manage_texture：纹理设置

### 调整纹理导入设置
```json
{
  "tool": "manage_texture",
  "params": {
    "action": "set_import_settings",
    "path": "Assets/AssetRaw/Textures/EnemyIcon.png",
    "maxSize": 512,
    "format": "RGBA32",
    "generateMipMaps": false,
    "textureType": "Sprite"
  }
}
```
`textureType` 可选：`Default`、`Sprite`、`NormalMap`、`GUI`、`Cubemap`

---

## manage_vfx：粒子与特效

### ParticleSystem（粒子系统）

#### 创建粒子系统
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_create",
    "target": "HitEffect",
    "autoAssignMaterial": true
  }
}
```

#### 设置粒子主模块
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_set_main",
    "target": "HitEffect",
    "duration": 1.0,
    "looping": false,
    "startLifetime": 0.5,
    "startSpeed": 5.0,
    "startSize": 0.3,
    "startColor": { "r": 1, "g": 0.5, "b": 0, "a": 1 },
    "maxParticles": 50,
    "simulationSpace": "World",
    "playOnAwake": false
  }
}
```

#### 设置发射模块
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_set_emission",
    "target": "HitEffect",
    "rateOverTime": 0,
    "rateOverDistance": 0
  }
}
```

#### 添加爆发（Burst）
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_add_burst",
    "target": "HitEffect",
    "time": 0,
    "count": 30,
    "cycles": 1
  }
}
```

#### 设置形状（Shape）
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_set_shape",
    "target": "HitEffect",
    "shapeType": "Sphere",
    "radius": 0.5,
    "arc": 360
  }
}
```

#### 播放控制
```json
{ "tool": "manage_vfx", "params": { "action": "particle_play", "target": "HitEffect" } }
{ "tool": "manage_vfx", "params": { "action": "particle_stop", "target": "HitEffect" } }
{ "tool": "manage_vfx", "params": { "action": "particle_clear", "target": "HitEffect" } }
```

### LineRenderer（线条渲染器）

#### 创建/配置 LineRenderer
```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "line_create",
    "target": "TrajectoryLine",
    "positions": [
      { "x": 0, "y": 0, "z": 0 },
      { "x": 5, "y": 2, "z": 0 },
      { "x": 10, "y": 0, "z": 0 }
    ],
    "startWidth": 0.1,
    "endWidth": 0.05,
    "startColor": { "r": 1, "g": 1, "b": 0, "a": 1 },
    "endColor": { "r": 1, "g": 0, "b": 0, "a": 0 }
  }
}
```

---

## manage_animation：动画控制器与片段

### 创建 AnimatorController
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "create_controller",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller"
  }
}
```

### 创建动画层
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "create_layer",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller",
    "layerName": "UpperBody",
    "blendingMode": "Override",
    "weight": 1.0
  }
}
```

### 添加参数
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "add_parameter",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller",
    "parameterName": "Speed",
    "parameterType": "Float",
    "defaultValue": 0
  }
}
```
`parameterType` 可选：`Float`、`Int`、`Bool`、`Trigger`

### 添加状态
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "add_state",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller",
    "stateName": "Idle",
    "clipPath": "Assets/AssetRaw/Animations/PlayerIdle.anim",
    "isDefault": true,
    "layerIndex": 0
  }
}
```

### 添加过渡
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "add_transition",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller",
    "fromState": "Idle",
    "toState": "Run",
    "hasExitTime": false,
    "conditions": [
      { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
    ]
  }
}
```

### 创建动画 Clip
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "create_clip",
    "clipPath": "Assets/AssetRaw/Animations/PlayerIdle.anim",
    "frameRate": 30,
    "isLooping": true
  }
}
```

### 创建混合树（BlendTree）
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "create_blend_tree",
    "controllerPath": "Assets/AssetRaw/Animations/PlayerController.controller",
    "stateName": "MovementBlend",
    "blendType": "1D",
    "blendParameter": "Speed",
    "motions": [
      { "clipPath": "Assets/AssetRaw/Animations/Idle.anim", "threshold": 0 },
      { "clipPath": "Assets/AssetRaw/Animations/Walk.anim", "threshold": 0.5 },
      { "clipPath": "Assets/AssetRaw/Animations/Run.anim", "threshold": 1.0 }
    ]
  }
}
```

### 控制 Animator（PlayMode）
```json
{
  "tool": "manage_animation",
  "params": {
    "action": "set_parameter",
    "target": "Player",
    "parameterName": "Speed",
    "value": 1.5
  }
}
```
