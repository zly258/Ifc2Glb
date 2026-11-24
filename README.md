<h1 align="center">Ifc2Glb</h1>

<p align="center">A Windows WPF desktop tool for batch converting IFC and IFCZIP models into glTF 2.0 GLB files with geometry reuse and BIM metadata.</p>

<p align="center">
  <a href="./README.md">English</a> | <a href="./README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-3776AB?style=flat-square" alt=".NET 9.0"> <img src="https://img.shields.io/badge/C%23-WPF-3776AB?style=flat-square" alt="C# WPF"> <img src="https://img.shields.io/badge/xBIM%20Geometry-6.3.891--netcore-3776AB?style=flat-square" alt="xBIM Geometry 6.3.891-netcore"> <img src="https://img.shields.io/badge/Platform-Windows-4B5563?style=flat-square" alt="Platform: Windows">
</p>

Ifc2Glb focuses on a single workflow: selecting multiple `.ifc` or `.ifczip` files and converting them into complete `.glb` (glTF 2.0) assets ready for Web 3D viewers, digital twin pipelines, and downstream engines such as Three.js, Babylon.js, and Unity. Built on .NET 9, WPF, and xBIM Geometry, it streams binary and JSON chunks through temporary files to keep memory usage controlled on large BIM models.

## Highlights

| Highlight | Why it matters |
|---|---|
| Batch IFC and IFCZIP conversion | Queue multiple `.ifc` and `.ifczip` files in a WPF job table with per-file progress, product count, triangle count, logs, and cancellation. |
| Geometry and mesh reuse | Caches decoded geometry by `ShapeGeometryLabel` and glTF meshes by `(ShapeGeometryLabel, materialIndex)`, avoiding duplicate buffers for repeated family instances. |
| BIM metadata in `node.extras` | Preserves `GlobalId`, `EntityLabel`, `IfcType`, `Name`, `ObjectType`, style, and material metadata on exported nodes for downstream picking and lookup. |
| Layered PBR material resolution | Reads IFC `IIfcSurfaceStyle` colors and transparency first, extracts IFC material names recursively, and falls back to deterministic per-type colors. |
| Streaming low-memory GLB writer | Processes files serially, disposes xBIM stores per file, and writes GLB BIN and JSON chunks to temporary files instead of holding the full payload in memory. |

## Architecture

Ifc2Glb separates UI orchestration, IFC geometry/material adaptation, and streaming GLB serialization into independent modules:

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

### Project Layout

```text
Ifc2Glb/
├── Src/
│   ├── App/              WPF window, job list, and UI interaction
│   ├── Conversion/       Main IFC-to-GLB conversion pipeline
│   ├── Glb/              Streaming GLB 2.0 writer, glTF records, and binary temp buffer
│   ├── Ifc/              xBIM adapters for filtering, materials, types, metadata, and meshes
│   ├── Models/           DTOs for options, progress, results, materials, and metadata
│   ├── Utilities/        Shared helpers including deterministic fallback color palette
│   └── App.xaml          WPF application entry point
├── Ifc2Glb.csproj        .NET 9 Windows WPF project file
└── ThirdPartyNotices.txt xBIM Toolkit third-party notices
```

## Quick Install

### Prerequisites

- Windows (x64)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (`net9.0-windows`)

### Build from Source

```powershell
git clone https://github.com/zly258/Ifc2Glb.git
cd Ifc2Glb
dotnet restore
dotnet build
```

### Publish a Windows Executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

The compiled executable is written to:

```text
bin\Release\net9.0-windows\win-x64\publish\ifc2glb.exe
```

## Quick Start

Launch the WPF application from the repository root:

```powershell
dotnet run --project .
```

Or run `ifc2glb.exe` from the published output folder.

1. Click **添加 IFC** (Add IFC) and select one or more `.ifc` or `.ifczip` files.
2. Keep the default output directory (`Documents\Ifc2Glb`) or click **浏览** (Browse) to choose another folder.
3. Review the conversion toggles in **转换设置** (Conversion Settings).
4. Click **开始转换** (Start Conversion) to convert queued files serially. Progress, product count, triangle count, and conversion logs update live; click **取消** (Cancel) at any time to stop the batch.

Each input file produces a corresponding `<filename>.glb` in the selected output directory.

## Configuration and Output Details

### Conversion Settings

| UI Option | Internal Property | Default | Behavior |
|---|---|---|---|
| `导出 IfcSpace` | `IncludeSpaces` | Off | Exports `IfcSpace` room and space volumes when enabled. |
| `导出 Opening` | `IncludeOpenings` | Off | Exports opening geometry when enabled; normally left off because host walls already contain cutouts. |
| `glTF Y-up` | `AddYUpRootTransform` | On | Wraps scene nodes under `Root_Z_UP_to_Y_UP` to convert IFC Z-up coordinates to glTF Y-up. |
| `完成后释放内存` | `ForceGcCheckBox` | On | Triggers `GC.Collect()` and finalizer wait after each file to reduce peak memory across large batches. |

### GLB Node Hierarchy and `node.extras`

Generated GLB files include standard glTF 2.0 structures (`asset`, `scene`, `nodes`, `meshes`, `materials`, `buffers`, `bufferViews`, `accessors`, and `BIN` chunk) organized into a product-node plus geometry-child hierarchy:

```text
Root_Z_UP_to_Y_UP
  IfcWall #12345              // Product node carrying GlobalId and BIM metadata
    IfcWall_12345_Shape_678   // Geometry node referencing a reusable glTF mesh
```

Product nodes and geometry child nodes populate `node.extras` with:

| Key | Description |
|---|---|
| `GlobalId` | IFC product global unique identifier |
| `EntityLabel` | xBIM internal entity label |
| `IfcType` | IFC type name (for example `IfcWall`, `IfcBeam`, `IfcColumn`) |
| `Name` | IFC product name |
| `ObjectType` | IFC `ObjectType` value |
| `StyleLabel` | xBIM geometry style label |
| `ShapeGeometryLabel` | Present on geometry child nodes to identify shared geometry |
| `MaterialName` | Primary display material name |
| `MaterialNames` | Array of extracted IFC material names |
| `MaterialSource` | Resolution source, such as `IfcSurfaceStyle` or `DefaultIfcTypePalette` |

### Material Resolution and PBR Rules

Materials are resolved in three layers:

1. **IFC `IIfcSurfaceStyle` first**: When `StyleLabel` resolves to an `IIfcSurfaceStyle`, `SurfaceColour`, `DiffuseColour`, and `Transparency` are read directly.
2. **IFC material names**: Material names are recursively extracted from product material associations (single materials, layer sets, lists, profile sets, and constituents) and stored in `node.extras.MaterialNames`.
3. **Deterministic type palette fallback**: If no explicit surface style exists, a stable color is generated from the IFC type name via `ColorPalette.FromText`.

Every exported glTF material uses `pbrMetallicRoughness` with `metallicFactor = 0.0`, `roughnessFactor = 0.85`, `doubleSided = true`, and `alphaMode = "BLEND"` when alpha is below `0.999`.

### Large-Model Memory Strategy and Scope

- Batch jobs run serially so only one IFC model and `GeometryStore` are open at a time, and both are disposed immediately after each file completes.
- Binary vertex/normal/index data and serialized JSON chunks are written to temporary files under `<OutputDirectory>\.ifc2glb-temp` and streamed into the final `.glb`.
- `ShapeInstances` are iterated without global sorting or full `ToList()` materialization.
- GLB 2.0 uses 32-bit chunk lengths; if a single output file exceeds 4 GB, the converter fails explicitly and the source IFC should be split or filtered.
- Current scope is strictly IFC / IFCZIP to GLB conversion: no IFC editing, built-in 3D viewer, texture image export, full property-set export, or OBJ/FBX/DXF/Excel export.

## Documentation

| Topic | What it covers | Link |
|---|---|---|
| Third-party notices | xBIM Toolkit packages and distribution notice | [ThirdPartyNotices.txt](./ThirdPartyNotices.txt) |
| Project configuration | Target framework, WPF settings, and NuGet dependencies | [Ifc2Glb.csproj](./Ifc2Glb.csproj) |
