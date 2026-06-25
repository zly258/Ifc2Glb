using System.IO;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace Ifc2Glb;

internal static class IfcToGlbConverter
{
    public static ConversionResult Convert(ConversionOptions options, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        var counters = new ConversionCounters();

        progress?.Report(new ConversionProgress { Percent = 3, Status = "打开 IFC", LogLine = "Opening IFC..." });
        cancellationToken.ThrowIfCancellationRequested();

        using var model = IfcStore.Open(options.InputPath);
        var oneMeter = model.ModelFactors.OneMeter;

        progress?.Report(new ConversionProgress { Percent = 12, Status = "生成 xBIM 几何上下文", LogLine = "Creating xBIM geometry context..." });
        cancellationToken.ThrowIfCancellationRequested();

        var context = new Xbim3DModelContext(model);
        context.CreateContext();

        progress?.Report(new ConversionProgress { Percent = 22, Status = "读取几何、颜色和材质", LogLine = "Reading geometry, surface styles and materials..." });
        cancellationToken.ThrowIfCancellationRequested();

        using var glb = new GlbStreamingSceneBuilder(options.TempDirectory)
        {
            AddYUpRootTransform = options.AddYUpRootTransform
        };

        var productNodeMap = new Dictionary<int, int>();
        var productMetadataMap = new Dictionary<int, ProductMetadata>();
        var typeNameCache = new Dictionary<short, string>();
        var materialResolver = new IfcMaterialResolver(model);

        using (var geometryStore = model.GeometryStore)
        using (var geometryReader = geometryStore.BeginRead())
        {
            var instances = geometryReader.ShapeInstances
                .Where(shape => shape.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded);

            foreach (var shapeInstance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var product = model.Instances[shapeInstance.IfcProductLabel] as IIfcProduct;
                if (product == null || !IfcExportFilter.ShouldExport(product, options))
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                var ifcTypeName = IfcTypeNameResolver.GetIfcTypeName(model, shapeInstance.IfcTypeId, typeNameCache);
                var materialInfo = materialResolver.Resolve(product, shapeInstance.StyleLabel, ifcTypeName);
                var materialIndex = glb.GetOrAddMaterial(materialInfo);

                if (!productNodeMap.TryGetValue(shapeInstance.IfcProductLabel, out var productNodeIndex))
                {
                    var metadata = IfcProductMetadataFactory.Build(product, shapeInstance, ifcTypeName, materialInfo);
                    productNodeIndex = glb.AddProductNode(metadata);
                    productNodeMap.Add(shapeInstance.IfcProductLabel, productNodeIndex);
                    productMetadataMap.Add(shapeInstance.IfcProductLabel, metadata);
                    counters.ProductCount++;
                }

                var productMetadata = productMetadataMap[shapeInstance.IfcProductLabel];
                var geometryKey = CreateGeometryKey(shapeInstance.ShapeGeometryLabel);
                if (glb.TryGetGeometry(geometryKey, out var geometry))
                {
                    counters.ReusedGeometryCount++;
                }
                else
                {
                    geometry = GetOrCreateGeometryResource(geometryReader, shapeInstance.ShapeGeometryLabel, geometryKey, oneMeter, glb, counters);
                    if (geometry == null)
                    {
                        counters.SkippedShapeCount++;
                        continue;
                    }

                }

                var meshKey = CreateMeshKey(shapeInstance.ShapeGeometryLabel, materialIndex);
                var meshName = GlbStreamingSceneBuilder.CreateMeshName(productMetadata, shapeInstance.ShapeGeometryLabel);
                var meshIndex = glb.GetOrAddMesh(meshKey, meshName, geometry, materialIndex);
                var matrix = IfcMeshPrimitiveReader.CreateNodeMatrixInMeters(shapeInstance.Transformation, oneMeter);
                glb.AddShapeNode(productNodeIndex, productMetadata, matrix, meshIndex, shapeInstance.ShapeGeometryLabel);

                counters.ShapeInstanceCount++;
                counters.PrimitiveCount++;
                counters.MeshCount = glb.MeshCount;
                counters.UniqueGeometryCount = glb.UniqueGeometryCount;
                counters.TriangleCount += geometry.IndexCount / 3;
                counters.VertexCount += geometry.VertexCount;

                ReportProgress(progress, counters);
            }
        }

        progress?.Report(new ConversionProgress
        {
            Percent = 92,
            Status = "写入 GLB",
            LogLine = $"Writing GLB... unique geometry: {counters.UniqueGeometryCount}, reused geometry: {counters.ReusedGeometryCount}, meshes: {counters.MeshCount}",
            ProductCount = counters.ProductCount,
            TriangleCount = counters.TriangleCount
        });
        cancellationToken.ThrowIfCancellationRequested();

        glb.Save(options.OutputPath);

        progress?.Report(new ConversionProgress
        {
            Percent = 100,
            Status = "完成",
            ProductCount = counters.ProductCount,
            TriangleCount = counters.TriangleCount
        });

        return new ConversionResult
        {
            InputPath = options.InputPath,
            OutputPath = options.OutputPath,
            ProductCount = counters.ProductCount,
            ShapeInstanceCount = counters.ShapeInstanceCount,
            PrimitiveCount = counters.PrimitiveCount,
            UniqueGeometryCount = counters.UniqueGeometryCount,
            ReusedGeometryCount = counters.ReusedGeometryCount,
            MeshCount = counters.MeshCount,
            TriangleCount = counters.TriangleCount,
            VertexCount = counters.VertexCount,
            SkippedShapeCount = counters.SkippedShapeCount
        };
    }

    private static GltfGeometryResource? GetOrCreateGeometryResource(
        IGeometryStoreReader geometryReader,
        int shapeGeometryLabel,
        string geometryKey,
        double oneMeter,
        GlbStreamingSceneBuilder glb,
        ConversionCounters counters)
    {
        var shapeGeometry = geometryReader.ShapeGeometry(shapeGeometryLabel);
        if (shapeGeometry == null)
        {
            return null;
        }

        if (shapeGeometry.Format != XbimGeometryType.PolyhedronBinary)
        {
            return null;
        }

        var geometryData = shapeGeometry as IXbimShapeGeometryData;
        if (geometryData?.ShapeData == null || geometryData.ShapeData.Length == 0)
        {
            return null;
        }

        var primitive = IfcMeshPrimitiveReader.ReadMeshPrimitive(geometryData.ShapeData, oneMeter);
        if (primitive == null || primitive.Indices.Count == 0 || primitive.Positions.Count == 0)
        {
            return null;
        }

        var resource = glb.GetOrAddGeometry(geometryKey, primitive);
        counters.UniqueGeometryCount = glb.UniqueGeometryCount;
        return resource;
    }

    private static void ValidateOptions(ConversionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            throw new ArgumentException("Input path is empty.", nameof(options));
        }

        if (!File.Exists(options.InputPath))
        {
            throw new FileNotFoundException("Input IFC file does not exist.", options.InputPath);
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("Output path is empty.", nameof(options));
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Directory.CreateDirectory(options.TempDirectory);
    }

    private static string CreateGeometryKey(int shapeGeometryLabel)
    {
        return $"geometry:{shapeGeometryLabel}";
    }

    private static string CreateMeshKey(int shapeGeometryLabel, int materialIndex)
    {
        return $"mesh:{shapeGeometryLabel}:material:{materialIndex}";
    }

    private static void ReportProgress(IProgress<ConversionProgress>? progress, ConversionCounters counters)
    {
        if (counters.ShapeInstanceCount % 200 == 0)
        {
            var percent = Math.Min(88, 22 + counters.ShapeInstanceCount / 80);
            progress?.Report(new ConversionProgress
            {
                Percent = percent,
                Status = $"已处理 Shape {counters.ShapeInstanceCount}",
                ProductCount = counters.ProductCount,
                TriangleCount = counters.TriangleCount
            });
        }

        if (counters.ShapeInstanceCount % 1000 == 0)
        {
            progress?.Report(new ConversionProgress
            {
                Percent = Math.Min(90, 22 + counters.ShapeInstanceCount / 80),
                Status = $"已处理 Shape {counters.ShapeInstanceCount}",
                LogLine = $"Processed shapes: {counters.ShapeInstanceCount}, products: {counters.ProductCount}, unique geometry: {counters.UniqueGeometryCount}, reused geometry: {counters.ReusedGeometryCount}, triangles: {counters.TriangleCount}",
                ProductCount = counters.ProductCount,
                TriangleCount = counters.TriangleCount
            });
        }
    }
}
