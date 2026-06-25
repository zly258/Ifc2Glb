# ifc2glb WPF 批量转换器

ifc2glb 是一个轻量级 Windows 桌面工具，只专注一件事：**批量选择 IFC / IFCZIP 文件，并转换为完整 GLB 2.0 文件**。

本工具基于 .NET / WPF / xBIM Geometry 开发，适合 BIM 模型轻量化、Web 端三维展示、数字孪生平台前处理、IFC 模型批量格式转换等场景。

---

## 中文说明

### 1. 核心功能

- 支持选择多个 `.ifc` / `.ifczip` 文件。
- 支持批量输出 `.glb` 文件。
- 支持 IFC 几何三角化并写入 glTF 2.0 / GLB 结构。
- 支持 IFC 构件语义信息写入 glTF `node.extras`。
- 支持 IFC 表面颜色和材质解析并写入 glTF `materials`。
- 支持大 IFC 模型转换时降低内存峰值。
- 支持取消当前批量转换任务。
- 支持转换日志、任务状态、构件数量、三角面数量显示。
- 使用 WPF Fluent 风格界面，统一字体为微软雅黑。

### 2. GLB 输出内容

生成的 GLB 文件包含完整 glTF 2.0 结构：

- `asset`
- `scene`
- `nodes`
- `meshes`
- `materials`
- `buffers`
- `bufferViews`
- `accessors`
- `BIN chunk`

每个 IFC 构件会尽量映射为一个 glTF node。构件的几何网格写入 mesh primitive，构件的身份信息写入 node extras。

### 3. 构件元数据写入

每个 glTF node 的 `extras` 中包含：

- `GlobalId`：IFC 构件全局唯一 ID。
- `EntityLabel`：xBIM 内部实体编号。
- `IfcType`：IFC 类型，例如 `IfcWall`、`IfcBeam`、`IfcColumn`。
- `Name`：IFC 构件名称。
- `ObjectType`：IFC ObjectType。
- `StyleLabel`：xBIM 几何样式编号。
- `MaterialName`：主要材质显示名称。
- `MaterialNames`：解析到的 IFC 材质名称数组。
- `MaterialSource`：材质来源，例如 `IfcSurfaceStyle` 或 `XbimProductTypeColourMap`。

这样下游系统可以通过 Three.js / Babylon.js / Unity 加载 GLB 后，根据 node extras 反查 BIM 构件数据。

### 4. 颜色与材质解析规则

转换器采用分层解析策略：

1. **优先解析 IFC SurfaceStyle**  
   xBIM 几何实例中包含 `StyleLabel`。当 `StyleLabel` 指向 `IIfcSurfaceStyle` 时，程序通过 xBIM 的 `XbimTexture.Create(surfaceStyle)` 读取表面颜色、透明度等信息，并写入 glTF PBR 材质。

2. **解析 IFC Material 名称**  
   程序会从构件关联的 material select 中递归提取材质名称，包括常见的单一材质、材料层、材料列表、材料剖面、材料组成等结构。解析到的名称写入 `node.extras.MaterialNames`。

3. **使用 xBIM 构件类型默认色兜底**  
   如果 IFC 文件没有显式表面样式，则使用 xBIM 的 `IfcProductTypeMap` 默认构件类型配色，例如墙、梁、柱、板、门窗等会得到稳定的默认颜色。

4. **最后使用哈希色兜底**  
   如果类型色也不可用，则根据 IFC 类型名称生成稳定颜色，避免所有构件都是同一种灰色。

### 5. glTF 材质写入规则

每个解析到的材质会写入 glTF `materials`：

- 使用 `pbrMetallicRoughness.baseColorFactor` 表示颜色和透明度。
- `metallicFactor` 固定为 `0.0`，适合常规建筑模型。
- `roughnessFactor` 固定为 `0.85`，避免默认材质过亮。
- 当透明度小于 1 时，自动设置 `alphaMode = BLEND`。
- `doubleSided = true`，减少 IFC 三角面法线差异导致的背面不可见问题。

### 6. 大模型内存策略

为了支持超大 IFC，本工具默认采用保守的串行转换策略：

- 批量转换时同一时间只打开一个 IFC。
- 每个 IFC 转换完成后立即释放 xBIM 模型、GeometryStore 和临时文件。
- GLB 二进制数据先写入临时 `.bin` 文件，最后再以流方式拼接为 `.glb`。
- 不在内存中保存完整 GLB 二进制块。
- 不对全部 `ShapeInstances` 做排序或全量 `ToList()`。
- 每个 Shape 只短暂创建顶点、法线、索引列表，写入后即可被 GC 回收。
- 界面提供“每个文件后强制释放内存”选项，适合连续批量处理大模型。

注意：GLB 2.0 本身使用 32 位 chunk length。单个 GLB 文件超过 4GB 时，程序会明确报错。此时需要拆分 IFC 或增加导出过滤规则。

### 7. 转换设置说明

界面中的“转换设置”面板包含：

- **导出 IfcSpace**  
  导出房间、空间等空间体量。默认关闭，减少 GLB 体积。需要空间体块或房间点击时再开启。

- **导出 Opening**  
  导出门窗洞口等 Opening 几何。默认关闭。常规展示通常只需要扣洞后的墙体，不需要单独显示洞口对象。

- **转换为 glTF Y-up**  
  默认开启。IFC 通常按 Z-up 表达，glTF 生态常用 Y-up。开启后更适合 Three.js、Babylon.js、Unity 等下游工具直接加载。

- **每个文件后强制释放内存**  
  默认开启。批量转换大 IFC 时建议保持开启。

### 8. 技术栈

- 语言：C#
- 运行时：.NET 9
- UI：WPF
- IFC 解析与几何生成：xBIM Geometry / xBIM Essentials
- GLB 写入：自研 GLB 2.0 Writer
- 几何格式：三角网格、法线、索引
- 材质格式：glTF PBR Metallic-Roughness
- 编码：UTF-8 with BOM

### 9. 编译

项目不包含 `scripts`，不包含 `global.json`，不会锁定特定 SDK 小版本。

```powershell
dotnet restore
dotnet build
```

### 10. 运行

```powershell
dotnet run --project .
```

### 11. 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

输出路径：

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

### 12. 当前边界

当前版本只做 IFC / IFCZIP 到 GLB：

- 不做 IFC 编辑。
- 不做 IFC 浏览器。
- 不做 Excel / OBJ / FBX / DXF 导出。
- 不做云端转换。
- 不做纹理贴图图片导出。
- 暂不做构件属性全集导出，只保留关键元数据到 `node.extras`。

---

## English Documentation

### 1. Overview

ifc2glb is a focused Windows desktop converter for **batch converting IFC / IFCZIP files to complete GLB 2.0 files**.

It is built with .NET, WPF and xBIM Geometry. Typical use cases include BIM model lightweighting, Web 3D delivery, digital twin preprocessing and batch IFC format conversion.

### 2. Main Features

- Select multiple `.ifc` / `.ifczip` files.
- Batch convert files to `.glb`.
- Tessellate IFC geometry into glTF-compatible triangle meshes.
- Preserve key BIM identity data in glTF `node.extras`.
- Parse IFC surface colors and material names.
- Write colors and transparency into glTF `materials`.
- Use a memory-safe serial conversion pipeline for large IFC files.
- Support canceling active batch conversion.
- Show per-file status, progress, product count and triangle count.
- Use a clean WPF Fluent-like interface with Microsoft YaHei / Segoe UI fonts.

### 3. GLB Output

The generated GLB contains a complete glTF 2.0 structure:

- `asset`
- `scene`
- `nodes`
- `meshes`
- `materials`
- `buffers`
- `bufferViews`
- `accessors`
- `BIN chunk`

Each IFC product is mapped to a glTF node when possible. Geometry is stored as mesh primitives, while BIM identity data is stored in node extras.

### 4. Node Extras

Each glTF node may contain:

- `GlobalId`: IFC global unique identifier.
- `EntityLabel`: xBIM internal entity label.
- `IfcType`: IFC product type, such as `IfcWall`, `IfcBeam`, `IfcColumn`.
- `Name`: IFC object name.
- `ObjectType`: IFC object type.
- `StyleLabel`: xBIM geometry style label.
- `MaterialName`: primary display material name.
- `MaterialNames`: extracted IFC material names.
- `MaterialSource`: material source, such as `IfcSurfaceStyle` or `XbimProductTypeColourMap`.

This allows downstream applications such as Three.js, Babylon.js or Unity to pick a GLB node and map it back to BIM data.

### 5. Color and Material Strategy

The converter uses a layered material strategy:

1. **IFC SurfaceStyle first**  
   xBIM shape instances expose a `StyleLabel`. If it points to an `IIfcSurfaceStyle`, the converter uses xBIM texture utilities to read surface color and alpha, then writes them to glTF PBR materials.

2. **IFC material names**  
   Material names are recursively extracted from the product material select structure, including single materials, material layers, material lists, material profiles and material constituents where available.

3. **xBIM product type color map fallback**  
   If no explicit IFC surface style is available, the converter uses xBIM product type colors to keep walls, slabs, beams, columns, doors and windows visually distinguishable.

4. **Stable hash color fallback**  
   If no type color is available, a stable color is generated from the IFC type name.

### 6. glTF Material Rules

Each material is written into glTF `materials`:

- `pbrMetallicRoughness.baseColorFactor` stores RGBA color.
- `metallicFactor = 0.0` for regular BIM surfaces.
- `roughnessFactor = 0.85` to avoid overly shiny output.
- `alphaMode = BLEND` when alpha is less than 1.
- `doubleSided = true` to reduce backface visibility issues.

### 7. Large IFC Memory Strategy

The converter is designed to avoid common large-model memory spikes:

- Batch conversion is serial by default.
- Only one IFC model is opened at a time.
- xBIM model and geometry store are disposed after each file.
- GLB binary payload is written to a temporary `.bin` file first.
- The final GLB is assembled by streaming the temporary binary file.
- The full GLB binary chunk is never kept in memory.
- Shape instances are not globally sorted or materialized with `ToList()`.
- Mesh vertex, normal and index lists are temporary per shape.
- Optional forced GC can be enabled after each file.

GLB 2.0 uses 32-bit chunk lengths. If the final GLB exceeds 4GB, the converter will fail explicitly and the model should be split or filtered.

### 8. Technology Stack

- Language: C#
- Runtime: .NET 9
- UI: WPF
- IFC engine: xBIM Geometry / xBIM Essentials
- GLB writer: custom GLB 2.0 writer
- Geometry: triangle meshes, normals and indices
- Material model: glTF PBR Metallic-Roughness
- Encoding: UTF-8 with BOM

### 9. Build

The project does not include `scripts` or `global.json`.

```powershell
dotnet restore
dotnet build
```

### 10. Run

```powershell
dotnet run --project .
```

### 11. Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

Output:

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

### 12. Current Scope

This version only converts IFC / IFCZIP to GLB:

- No IFC editing.
- No IFC viewer.
- No Excel / OBJ / FBX / DXF export.
- No cloud conversion.
- No image texture export yet.
- No full property-set export yet; only key metadata is preserved in `node.extras`.
