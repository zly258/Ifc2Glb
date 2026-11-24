<h1 align="center">Ifc2Glb</h1>

<p align="center">基于 .NET 9 与 WPF 的 Windows 桌面工具，用于将 IFC / IFCZIP 模型批量转换为带几何复用和 BIM 元数据的 glTF 2.0 GLB 文件。</p>

<p align="center">
  <a href="./README.md">English</a> | <a href="./README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-3776AB?style=flat-square" alt=".NET 9.0"> <img src="https://img.shields.io/badge/C%23-WPF-3776AB?style=flat-square" alt="C# WPF"> <img src="https://img.shields.io/badge/xBIM%20Geometry-6.3.891--netcore-3776AB?style=flat-square" alt="xBIM Geometry 6.3.891-netcore"> <img src="https://img.shields.io/badge/Platform-Windows-4B5563?style=flat-square" alt="Platform: Windows">
</p>

Ifc2Glb 专注解决单一工程场景：批量选择 `.ifc` / `.ifczip` 文件并转换为完整的 `.glb`（glTF 2.0）文件，便于接入 Three.js、Babylon.js、Unity 等 Web 三维引擎与数字孪生平台。工具基于 .NET 9、WPF 与 xBIM Geometry 构建，通过临时文件流式拼接二进制与 JSON 数据块，在转换大型 BIM 模型时有效控制内存峰值。

## 核心特性

| 特性 | 实际价值 |
|---|---|
| 批量转换 IFC / IFCZIP | 在 WPF 任务列表中批量添加 `.ifc` 与 `.ifczip` 文件，实时查看状态、进度、构件数、三角面数与日志，并支持随时取消。 |
| 几何与 Mesh 复用 | 按 `ShapeGeometryLabel` 缓存解码后的几何资源，按 `(ShapeGeometryLabel, materialIndex)` 复用 glTF Mesh，显著减少重复族实例的体积与耗时。 |
| 保留 BIM 元数据 | 将 `GlobalId`、`EntityLabel`、`IfcType`、`Name`、`ObjectType` 及样式材质信息写入 `node.extras`，方便下游按节点反查构件数据。 |
| 分层 PBR 材质解析 | 优先读取 IFC `IIfcSurfaceStyle` 颜色与透明度，递归提取 IFC 材质名称，无显式样式时按构件类型生成稳定默认色。 |
| 流式低内存 GLB 写入 | 串行处理批量任务，每个文件完成后立即释放 xBIM 模型与几何存储，并将 BIN/JSON 块写入临时文件后再流式拼接输出。 |

## 架构设计

项目将界面交互、IFC 几何与材质适配以及流式 GLB 写入解耦为独立模块：

```text
┌───────────────────┐     ┌────────────────────────────────────────┐
│   WPF Desktop UI  │     │         Conversion Pipeline            │
│    (Src/App)      │────▶│    IfcToGlbConverter (Src/Conversion)  │
│  MainWindow &     │     └───────────────────┬────────────────────┘
│  ConversionJob    │                         │
└───────────────────┘                         │
                       ┌──────────────────────┴──────────────────────┐
                       ▼                                             ▼
┌────────────────────────────────────────┐   ┌────────────────────────────────────────┐
│          xBIM Adapters (Src/Ifc)       │   │        GLB 2.0 Writer (Src/Glb)        │
│  ┌──────────────────────────────────┐  │   │  ┌──────────────────────────────────┐  │
│  │ IfcExportFilter / TypeResolver   │  │   │  │ GlbStreamingSceneBuilder         │  │
│  │ IfcMaterialResolver              │  │   │  │ BinaryFileBuffer (temp .tmp)     │  │
│  │ IfcProductMetadataFactory        │  │   │  │ GltfModels / GltfGeometryResource│  │
│  │ IfcMeshPrimitiveReader           │  │   │  └────────────────┬─────────────────┘  │
│  └──────────────────────────────────┘  │   └───────────────────┼────────────────────┘
└────────────────────────────────────────┘                       │
                       ▲                                         ▼
              .ifc / .ifczip                         Temp BIN + JSON ──▶ .glb
```

### 目录结构

```text
Ifc2Glb/
├── Src/
│   ├── App/              WPF 主窗口、任务列表与交互逻辑
│   ├── Conversion/       IFC 到 GLB 的主转换流程
│   ├── Glb/              流式 GLB 2.0 写入器、glTF 数据模型与临时二进制缓冲
│   ├── Ifc/              xBIM 适配器（过滤、材质、类型名、元数据、网格读取）
│   ├── Models/           转换选项、进度、结果、材质与元数据 DTO
│   ├── Utilities/        通用工具（含默认构件类型配色）
│   └── App.xaml          WPF 应用入口
├── Ifc2Glb.csproj        .NET 9 Windows WPF 项目文件
└── ThirdPartyNotices.txt xBIM Toolkit 第三方组件与许可证声明
```

## 快速安装

### 环境要求

- Windows (x64)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)（`net9.0-windows`）

### 从源码编译

```powershell
git clone https://github.com/zly258/Ifc2Glb.git
cd Ifc2Glb
dotnet restore
dotnet build
```

### 发布 Windows 可执行文件

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

发布产物路径：

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

## 快速开始

在仓库根目录启动 WPF 桌面应用：

```powershell
dotnet run --project .
```

或者直接运行发布目录下的 `ifc2glb.exe`。

1. 点击 **添加 IFC**，选择一个或多个 `.ifc` / `.ifczip` 文件。
2. 使用默认输出目录（`Documents\Ifc2Glb`），或点击 **浏览** 指定目标目录。
3. 按需调整 **转换设置** 中的勾选项。
4. 点击 **开始转换** 启动串行批量转换，界面将实时更新任务状态、进度、构件数、三角面数与日志；如需中止可随时点击 **取消**。

转换完成后，每个输入文件会在输出目录生成同名的 `<文件名>.glb`。

## 转换设置与输出说明

### 转换设置

| 界面选项 | 内部属性 | 默认值 | 说明 |
|---|---|---|---|
| `导出 IfcSpace` | `IncludeSpaces` | 关闭 | 开启后导出房间、空间等 `IfcSpace` 体量；默认关闭以减小文件体积。 |
| `导出 Opening` | `IncludeOpenings` | 关闭 | 开启后导出门窗洞口几何；常规展示通常仅需扣洞后的主体构件。 |
| `glTF Y-up` | `AddYUpRootTransform` | 开启 | 添加 `Root_Z_UP_to_Y_UP` 根节点，将 IFC Z-up 坐标系转换为 glTF Y-up。 |
| `完成后释放内存` | `ForceGcCheckBox` | 开启 | 每个文件转换完成后主动触发 `GC.Collect()` 并等待终结器，降低连续批处理时的内存峰值。 |

### GLB 节点层级与 `node.extras`

生成的 GLB 文件包含完整 glTF 2.0 结构（`asset`、`scene`、`nodes`、`meshes`、`materials`、`buffers`、`bufferViews`、`accessors` 与 `BIN` chunk），采用“产品节点 + 几何子节点”的层级：

```text
Root_Z_UP_to_Y_UP
  IfcWall #12345              // 产品节点，承载 GlobalId 与 BIM 元数据
    IfcWall_12345_Shape_678   // 几何子节点，引用可复用的 glTF Mesh
```

产品节点与几何子节点的 `node.extras` 包含以下字段：

| 字段 | 说明 |
|---|---|
| `GlobalId` | IFC 构件全局唯一 ID |
| `EntityLabel` | xBIM 内部实体编号 |
| `IfcType` | IFC 构件类型（如 `IfcWall`、`IfcBeam`、`IfcColumn`） |
| `Name` | IFC 构件名称 |
| `ObjectType` | IFC `ObjectType` 值 |
| `StyleLabel` | xBIM 几何样式编号 |
| `ShapeGeometryLabel` | 几何子节点附加字段，用于定位共享几何资源 |
| `MaterialName` | 主要材质显示名称 |
| `MaterialNames` | 解析到的 IFC 材质名称数组 |
| `MaterialSource` | 材质来源，如 `IfcSurfaceStyle` 或 `DefaultIfcTypePalette` |

### 颜色材质解析与 PBR 规则

转换器按三层策略解析材质：

1. **优先读取 IFC `IIfcSurfaceStyle`**：当 `StyleLabel` 指向 `IIfcSurfaceStyle` 时，直接读取 `SurfaceColour`、`DiffuseColour` 与 `Transparency`。
2. **提取 IFC 材质名称**：从构件关联材质中递归提取单一材质、材料层、材料列表、材料剖面及材料组成名称，并写入 `node.extras.MaterialNames`。
3. **构件类型默认色兜底**：当模型缺少显式表面样式时，通过 `ColorPalette.FromText` 按 IFC 类型名称生成稳定可区分的颜色。

写入 glTF `materials` 时统一采用 `pbrMetallicRoughness`，固定 `metallicFactor = 0.0`、`roughnessFactor = 0.85`、`doubleSided = true`，并在透明度小于 `0.999` 时设置 `alphaMode = "BLEND"`。

### 大模型内存策略与当前边界

- 批量任务默认串行执行，同一时间仅打开一个 IFC 模型与 `GeometryStore`，单文件结束后立即释放资源。
- 顶点、法线、索引二进制数据与序列化 JSON 均先写入 `<输出目录>\.ifc2glb-temp` 下的临时文件，再流式拼接为最终 `.glb`。
- 遍历 `ShapeInstances` 时不做全局排序或全量 `ToList()` 物化。
- GLB 2.0 规范采用 32 位 chunk 长度；若单个输出 GLB 超过 4 GB，程序将明确报错，需拆分源 IFC 或增加过滤。
- 当前版本仅覆盖 IFC / IFCZIP 到 GLB 转换：不包含 IFC 编辑、内置三维预览、纹理图片导出、完整属性集导出，也不支持 OBJ / FBX / DXF / Excel 导出。

## 相关文档

| 主题 | 内容说明 | 链接 |
|---|---|---|
| 第三方组件声明 | xBIM Toolkit 相关包与分发声明 | [ThirdPartyNotices.txt](./ThirdPartyNotices.txt) |
| 项目配置 | 目标框架、WPF 设置与 NuGet 依赖 | [Ifc2Glb.csproj](./Ifc2Glb.csproj) |
