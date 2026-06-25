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
- 支持几何复用：相同 `ShapeGeometryLabel` 只解码和写入一次，重复构件通过 glTF mesh/node 复用。
- 支持取消当前批量转换任务。
- 支持转换日志、任务状态、构件数量、三角面数量显示。
- 使用 WPF Fluent 风格界面，统一字体为微软雅黑。

### 2. 目录结构

```text
Ifc2Glb/
  Src/
    App/                  WPF 界面、任务列表、窗口交互
    Conversion/           IFC 到 GLB 的主转换流程
    Glb/                  GLB 2.0 Writer、glTF 数据结构、二进制临时缓冲
    Ifc/                  IFC 几何、材质、类型、导出过滤等 xBIM 适配代码
    Models/               转换参数、进度、结果、材质、构件元数据 DTO
    Utilities/            通用工具，例如默认颜色生成
    App.xaml              WPF 应用入口
  Ifc2Glb.csproj          WPF 项目文件
  README.md               中文 / English 文档
  ThirdPartyNotices.txt   第三方许可证说明
```

设计原则：UI 不直接依赖 xBIM 细节；GLB 写入器不直接读取 IFC；IFC 解析、材质解析、网格读取和 GLB 生成分开维护，便于后续继续优化。

### 3. GLB 输出内容

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

当前采用“产品节点 + 几何子节点”的结构：

```text
Root_Z_UP_to_Y_UP
  IfcWall #12345              // 产品节点，保存 GlobalId 和 BIM 元数据
    IfcWall_12345_Shape_678   // 几何节点，引用可复用 mesh
```

这样可以同时保证 BIM 元数据清晰、构件层级稳定、重复几何可复用。

### 4. 构件元数据写入

每个产品节点和几何节点的 `extras` 中包含：

- `GlobalId`：IFC 构件全局唯一 ID。
- `EntityLabel`：xBIM 内部实体编号。
- `IfcType`：IFC 类型，例如 `IfcWall`、`IfcBeam`、`IfcColumn`。
- `Name`：IFC 构件名称。
- `ObjectType`：IFC ObjectType。
- `StyleLabel`：xBIM 几何样式编号。
- `ShapeGeometryLabel`：几何节点额外包含，用于定位复用几何。
- `MaterialName`：主要材质显示名称。
- `MaterialNames`：解析到的 IFC 材质名称数组。
- `MaterialSource`：材质来源，例如 `IfcSurfaceStyle` 或 `DefaultIfcTypePalette`。

下游系统可以通过 Three.js / Babylon.js / Unity 加载 GLB 后，根据 node extras 反查 BIM 构件数据。

### 5. 颜色与材质解析规则

转换器采用分层解析策略：

1. **优先解析 IFC SurfaceStyle**  
   xBIM 几何实例中包含 `StyleLabel`。当 `StyleLabel` 指向 `IIfcSurfaceStyle` 时，程序直接读取 SurfaceStyle 中的 `SurfaceColour / DiffuseColour / Transparency`，并写入 glTF PBR 材质。

2. **解析 IFC Material 名称**  
   程序会从构件关联的 material select 中递归提取材质名称，包括常见的单一材质、材料层、材料列表、材料剖面、材料组成等结构。解析到的名称写入 `node.extras.MaterialNames`。

3. **使用构件类型默认色兜底**  
   如果 IFC 文件没有显式表面样式，则根据 IFC 类型名称生成稳定的构件类型颜色，例如墙、梁、柱、板、门窗等会得到可区分的默认颜色。

### 6. glTF 材质写入规则

每个解析到的材质会写入 glTF `materials`：

- 使用 `pbrMetallicRoughness.baseColorFactor` 表示颜色和透明度。
- `metallicFactor` 固定为 `0.0`，适合常规建筑模型。
- `roughnessFactor` 固定为 `0.85`，避免默认材质过亮。
- 当透明度小于 1 时，自动设置 `alphaMode = BLEND`。
- `doubleSided = true`，减少 IFC 三角面法线差异导致的背面不可见问题。

### 7. 转换速度优化

本版重点优化了重复几何和重复数据读取：

- 按 `ShapeGeometryLabel` 缓存几何资源，相同几何只解码一次。
- glTF mesh 可被多个 node 引用，避免成百上千个相同构件重复写入顶点和索引。
- 按构件缓存 IFC 材质名称，减少反射式 material select 遍历。
- 按 IFC type id 缓存类型名称，减少 metadata 查询。
- 按材质 key 缓存 glTF material，避免重复材质对象。
- JSON chunk 先写入临时文件，再流式拼接 GLB，避免一次性分配大 JSON byte[]。
- BIN chunk 始终写入临时文件，最终流式拼接。

对于包含大量 `IfcMappedItem` 或重复族实例的模型，GLB 体积和转换耗时通常会明显下降。

### 8. 大模型内存策略

为了支持超大 IFC，本工具默认采用保守的串行转换策略：

- 批量转换时同一时间只打开一个 IFC。
- 每个 IFC 转换完成后立即释放 xBIM 模型、GeometryStore 和临时文件。
- GLB 二进制数据先写入临时 `.bin` 文件，最后再以流方式拼接为 `.glb`。
- JSON chunk 也通过临时文件写入，避免序列化时生成大型内存数组。
- 不在内存中保存完整 GLB 二进制块。
- 不对全部 `ShapeInstances` 做排序或全量 `ToList()`。
- 每个唯一 Shape 只短暂创建顶点、法线、索引列表，写入后即可被 GC 回收。
- 重复 Shape 直接复用已写入的 accessor / mesh，不再重复解析二进制三角网格。
- 界面提供“每个文件后强制释放内存”选项，适合连续批量处理大模型。

注意：GLB 2.0 本身使用 32 位 chunk length。单个 GLB 文件超过 4GB 时，程序会明确报错。此时需要拆分 IFC 或增加导出过滤规则。

### 9. 转换设置说明

界面中的“转换设置”面板包含：

- **导出 IfcSpace**  
  导出房间、空间等空间体量。默认关闭，减少 GLB 体积。需要空间体块或房间点击时再开启。

- **导出 Opening**  
  导出门窗洞口等 Opening 几何。默认关闭。常规展示通常只需要扣洞后的墙体，不需要单独显示洞口对象。

- **转换为 glTF Y-up**  
  默认开启。IFC 通常按 Z-up 表达，glTF 生态常用 Y-up。开启后更适合 Three.js、Babylon.js、Unity 等下游工具直接加载。

- **每个文件后强制释放内存**  
  默认开启。批量转换大 IFC 时建议保持开启。

### 10. 技术栈

- 语言：C#
- 运行时：.NET 9
- UI：WPF
- IFC 解析与几何生成：xBIM Geometry / xBIM Essentials
- GLB 写入：自研 GLB 2.0 Writer
- 几何格式：三角网格、法线、索引
- 材质格式：glTF PBR Metallic-Roughness
- 编码：UTF-8 with BOM

### 11. 编译

项目不包含 `scripts`，不包含 `global.json`，不会锁定特定 SDK 小版本。

```powershell
dotnet restore
dotnet build
```

### 12. 运行

```powershell
dotnet run --project .
```

### 13. 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

输出路径：

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

### 14. 当前边界

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
- Reuse shared geometry by `ShapeGeometryLabel` to reduce GLB size and conversion work.
- Use a memory-safe serial conversion pipeline for large IFC files.
- Support canceling active batch conversion.
- Show per-file status, progress, product count and triangle count.
- Use a clean WPF Fluent-like interface with Microsoft YaHei / Segoe UI fonts.

### 3. Project Structure

```text
Ifc2Glb/
  Src/
    App/                  WPF window, job list and UI interaction
    Conversion/           Main IFC-to-GLB conversion pipeline
    Glb/                  GLB 2.0 writer, glTF model records and binary temp buffers
    Ifc/                  xBIM adapters for geometry, materials, type names and filters
    Models/               DTOs for options, progress, results, materials and metadata
    Utilities/            Shared utilities such as fallback color generation
    App.xaml              WPF application entry
  Ifc2Glb.csproj          WPF project file
  README.md               Chinese and English documentation
  ThirdPartyNotices.txt   Third-party license notices
```

The UI does not read xBIM details directly; the GLB writer does not know IFC; material parsing, mesh reading and GLB generation are separated for maintainability.

### 4. GLB Output

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

The current node structure is product node plus geometry child nodes:

```text
Root_Z_UP_to_Y_UP
  IfcWall #12345              // Product node with GlobalId and BIM metadata
    IfcWall_12345_Shape_678   // Geometry node referencing a reusable mesh
```

This keeps BIM metadata clear while allowing repeated geometry to be reused by multiple nodes.

### 5. Node Extras

Each product node and geometry node may contain:

- `GlobalId`: IFC global unique identifier.
- `EntityLabel`: xBIM internal entity label.
- `IfcType`: IFC product type, such as `IfcWall`, `IfcBeam`, `IfcColumn`.
- `Name`: IFC object name.
- `ObjectType`: IFC object type.
- `StyleLabel`: xBIM geometry style label.
- `ShapeGeometryLabel`: included on geometry nodes for shared geometry tracing.
- `MaterialName`: primary display material name.
- `MaterialNames`: extracted IFC material names.
- `MaterialSource`: material source, such as `IfcSurfaceStyle` or `DefaultIfcTypePalette`.

This allows downstream applications such as Three.js, Babylon.js or Unity to pick a GLB node and map it back to BIM data.

### 6. Color and Material Strategy

The converter uses a layered material strategy:

1. **IFC SurfaceStyle first**  
   xBIM shape instances expose a `StyleLabel`. If it points to an `IIfcSurfaceStyle`, the converter directly reads `SurfaceColour / DiffuseColour / Transparency` from the IFC surface style and writes the result to glTF PBR materials.

2. **IFC material names**  
   Material names are recursively extracted from the product material select structure, including single materials, material layers, material lists, material profiles and material constituents where available.

3. **Stable IFC type color fallback**  
   If no explicit IFC surface style is available, a stable color is generated from the IFC type name to keep walls, slabs, beams, columns, doors and windows visually distinguishable.

### 7. glTF Material Rules

Each material is written into glTF `materials`:

- `pbrMetallicRoughness.baseColorFactor` stores RGBA color.
- `metallicFactor = 0.0` for regular BIM surfaces.
- `roughnessFactor = 0.85` to avoid overly shiny output.
- `alphaMode = BLEND` when alpha is less than 1.
- `doubleSided = true` to reduce backface visibility issues.

### 8. Performance Optimizations

This version focuses on repeated geometry and large output stability:

- Geometry resources are cached by `ShapeGeometryLabel`.
- The same geometry is decoded and written only once.
- Multiple glTF nodes can reference the same mesh.
- IFC material names are cached per product to reduce reflection overhead.
- IFC type names are cached by type id.
- glTF materials are cached by material key.
- The JSON chunk is serialized to a temporary file instead of a large in-memory byte array.
- The BIN chunk is written to a temporary file and streamed into the final GLB.

For IFC models with many mapped items or repeated family instances, this can significantly reduce file size and conversion time.

### 9. Large IFC Memory Strategy

The converter is designed to avoid common large-model memory spikes:

- Batch conversion is serial by default.
- Only one IFC model is opened at a time.
- xBIM model and geometry store are disposed after each file.
- GLB binary payload is written to a temporary `.bin` file first.
- GLB JSON payload is also written to a temporary file.
- The full GLB binary chunk is never kept in memory.
- Shape instances are not globally sorted or materialized with `ToList()`.
- Repeated shapes reuse existing accessors and meshes.
- Optional forced GC can be enabled after each file.

GLB 2.0 uses 32-bit chunk lengths. If the final GLB exceeds 4GB, the converter will fail explicitly and the model should be split or filtered.

### 10. Technology Stack

- Language: C#
- Runtime: .NET 9
- UI: WPF
- IFC engine: xBIM Geometry / xBIM Essentials
- GLB writer: custom GLB 2.0 writer
- Geometry: triangle meshes, normals and indices
- Material model: glTF PBR Metallic-Roughness
- Encoding: UTF-8 with BOM

### 11. Build

The project does not include `scripts` or `global.json`.

```powershell
dotnet restore
dotnet build
```

### 12. Run

```powershell
dotnet run --project .
```

### 13. Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

Output:

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

### 14. Current Scope

This version only converts IFC / IFCZIP to GLB:

- No IFC editing.
- No IFC viewer.
- No Excel / OBJ / FBX / DXF export.
- No cloud conversion.
- No image texture export yet.
- No full property-set export yet; only key metadata is preserved in `node.extras`.
