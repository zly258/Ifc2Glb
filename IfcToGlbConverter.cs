using System.Collections;
using System.IO;
using System.Reflection;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace Ifc2Glb;

internal static class IfcToGlbConverter
{
    private static readonly string[] MaterialChildPropertyNames =
    [
        "Material",
        "Materials",
        "RelatingMaterial",
        "ForLayerSet",
        "ForProfileSet",
        "MaterialLayers",
        "MaterialProfiles",
        "MaterialConstituents",
        "AssociatedTo"
    ];

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

        using (var geometryStore = model.GeometryStore)
        using (var geometryReader = geometryStore.BeginRead())
        {
            var instances = geometryReader.ShapeInstances
                .Where(shape => shape.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded);

            foreach (var shapeInstance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var product = model.Instances[shapeInstance.IfcProductLabel] as IIfcProduct;
                if (product == null)
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                if (!ShouldExport(product, options))
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                var shapeGeometry = geometryReader.ShapeGeometry(shapeInstance.ShapeGeometryLabel);
                if (shapeGeometry == null)
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                if (shapeGeometry.Format != XbimGeometryType.PolyhedronBinary)
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                var geometryData = shapeGeometry as IXbimShapeGeometryData;
                if (geometryData?.ShapeData == null || geometryData.ShapeData.Length == 0)
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                var primitive = ReadMeshPrimitive(geometryData.ShapeData, oneMeter);
                if (primitive == null || primitive.Indices.Count == 0 || primitive.Positions.Count == 0)
                {
                    counters.SkippedShapeCount++;
                    continue;
                }

                var ifcTypeName = GetIfcTypeName(model, shapeInstance.IfcTypeId);
                var materialInfo = ResolveMaterial(model, product, shapeInstance, ifcTypeName);

                if (!productNodeMap.TryGetValue(shapeInstance.IfcProductLabel, out var nodeIndex))
                {
                    var metadata = BuildMetadata(product, shapeInstance, ifcTypeName, materialInfo);
                    var matrix = CreateNodeMatrixInMeters(shapeInstance.Transformation, oneMeter);
                    nodeIndex = glb.AddProductNode(metadata, matrix);
                    productNodeMap.Add(shapeInstance.IfcProductLabel, nodeIndex);
                    productMetadataMap.Add(shapeInstance.IfcProductLabel, metadata);
                    counters.ProductCount++;
                }

                var materialIndex = glb.GetOrAddMaterial(materialInfo);
                glb.AddPrimitiveToNode(nodeIndex, primitive, materialIndex);

                counters.ShapeInstanceCount++;
                counters.PrimitiveCount++;
                counters.TriangleCount += primitive.Indices.Count / 3;
                counters.VertexCount += primitive.Positions.Count / 3;

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
                        LogLine = $"Processed shape instances: {counters.ShapeInstanceCount}, triangles: {counters.TriangleCount}",
                        ProductCount = counters.ProductCount,
                        TriangleCount = counters.TriangleCount
                    });
                }
            }
        }

        progress?.Report(new ConversionProgress
        {
            Percent = 92,
            Status = "写入 GLB",
            LogLine = "Writing GLB...",
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
            TriangleCount = counters.TriangleCount,
            VertexCount = counters.VertexCount,
            SkippedShapeCount = counters.SkippedShapeCount
        };
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

    private static bool ShouldExport(IIfcProduct product, ConversionOptions options)
    {
        if (!options.IncludeSpaces && product is IIfcSpace)
        {
            return false;
        }

        if (!options.IncludeOpenings && product is IIfcFeatureElement)
        {
            return false;
        }

        return true;
    }

    private static ProductMetadata BuildMetadata(IIfcProduct product, XbimShapeInstance shapeInstance, string ifcTypeName, MaterialInfo materialInfo)
    {
        return new ProductMetadata
        {
            EntityLabel = product.EntityLabel,
            GlobalId = product.GlobalId.ToString() ?? string.Empty,
            IfcType = ifcTypeName,
            Name = product.Name?.ToString() ?? string.Empty,
            ObjectType = product.ObjectType?.ToString() ?? string.Empty,
            StyleLabel = shapeInstance.StyleLabel,
            MaterialName = materialInfo.Name,
            MaterialNames = materialInfo.MaterialNames,
            MaterialSource = materialInfo.Source
        };
    }

    private static MaterialInfo ResolveMaterial(IModel model, IIfcProduct product, XbimShapeInstance shapeInstance, string ifcTypeName)
    {
        var materialNames = ExtractMaterialNames(product);
        if (TryCreateSurfaceStyleMaterial(model, shapeInstance.StyleLabel, ifcTypeName, materialNames, out var styleMaterial))
        {
            return styleMaterial;
        }

        if (TryCreateTypePaletteMaterial(ifcTypeName, materialNames, out var typeMaterial))
        {
            return typeMaterial;
        }

        return MaterialInfo.CreateFallback(ifcTypeName, materialNames);
    }

    private static bool TryCreateSurfaceStyleMaterial(IModel model, int styleLabel, string ifcTypeName, string[] materialNames, out MaterialInfo material)
    {
        material = default!;
        if (styleLabel <= 0)
        {
            return false;
        }

        try
        {
            if (model.Instances[styleLabel] is not IIfcSurfaceStyle surfaceStyle)
            {
                return false;
            }

            var texture = XbimTexture.Create(surfaceStyle);
            var colour = texture.ColourMap.Cast<object>().FirstOrDefault();
            if (colour == null)
            {
                return false;
            }

            return TryCreateMaterialFromColour(
                colour,
                $"style:{styleLabel}",
                MaterialInfo.CreateMaterialName(ifcTypeName, materialNames),
                "IfcSurfaceStyle",
                styleLabel,
                materialNames,
                out material);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateTypePaletteMaterial(string ifcTypeName, string[] materialNames, out MaterialInfo material)
    {
        material = default!;
        try
        {
            var colourMap = new XbimColourMap(StandardColourMaps.IfcProductTypeMap);
            var colour = colourMap[ifcTypeName];
            return TryCreateMaterialFromColour(
                colour,
                $"type:{ifcTypeName}",
                MaterialInfo.CreateMaterialName(ifcTypeName, materialNames),
                "XbimProductTypeColourMap",
                0,
                materialNames,
                out material);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateMaterialFromColour(
        object colour,
        string keyPrefix,
        string name,
        string source,
        int styleLabel,
        string[] materialNames,
        out MaterialInfo material)
    {
        material = default!;
        if (!TryReadDoubleProperty(colour, "Red", out var red) ||
            !TryReadDoubleProperty(colour, "Green", out var green) ||
            !TryReadDoubleProperty(colour, "Blue", out var blue))
        {
            return false;
        }

        if (!TryReadDoubleProperty(colour, "Alpha", out var alpha))
        {
            alpha = 1.0;
        }

        red = Clamp01(red);
        green = Clamp01(green);
        blue = Clamp01(blue);
        alpha = Clamp01(alpha);

        material = new MaterialInfo
        {
            Key = $"{keyPrefix}:{red:0.###}:{green:0.###}:{blue:0.###}:{alpha:0.###}",
            Name = string.IsNullOrWhiteSpace(name) ? "Material" : name,
            Red = red,
            Green = green,
            Blue = blue,
            Alpha = alpha,
            Source = source,
            StyleLabel = styleLabel,
            MaterialNames = materialNames
        };
        return true;
    }

    private static string[] ExtractMaterialNames(IIfcProduct product)
    {
        var names = new List<string>();
        var visited = new HashSet<int>();
        CollectMaterialNames(ReadMaterialSelect(product), names, visited, 0);
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static object? ReadMaterialSelect(IIfcProduct product)
    {
        var value = ReadProperty(product, "Material");
        if (value != null)
        {
            return value;
        }

        try
        {
            return typeof(IIfcObjectDefinition).GetProperty("Material")?.GetValue(product);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectMaterialNames(object? value, List<string> names, HashSet<int> visited, int depth)
    {
        if (value == null || depth > 8)
        {
            return;
        }

        if (value is string text)
        {
            AddName(names, text);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                CollectMaterialNames(item, names, visited, depth + 1);
            }

            return;
        }

        var entityLabel = ReadIntProperty(value, "EntityLabel");
        if (entityLabel > 0 && !visited.Add(entityLabel))
        {
            return;
        }

        var typeName = value.GetType().Name;
        if (typeName.Contains("Material", StringComparison.OrdinalIgnoreCase))
        {
            AddName(names, ReadStringProperty(value, "Name"));
        }

        foreach (var propertyName in MaterialChildPropertyNames)
        {
            CollectMaterialNames(ReadProperty(value, propertyName), names, visited, depth + 1);
        }
    }

    private static void AddName(List<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name.Trim());
        }
    }

    private static object? ReadProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                ?.GetValue(source);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadStringProperty(object source, string propertyName)
    {
        return ReadProperty(source, propertyName)?.ToString();
    }

    private static int ReadIntProperty(object source, string propertyName)
    {
        var value = ReadProperty(source, propertyName);
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
            _ => 0
        };
    }

    private static bool TryReadDoubleProperty(object source, string propertyName, out double value)
    {
        value = 0.0;
        var raw = ReadProperty(source, propertyName);
        if (raw == null)
        {
            return false;
        }

        try
        {
            value = System.Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Max(0.0, Math.Min(1.0, value));
    }

    private static string GetIfcTypeName(IModel model, short ifcTypeId)
    {
        try
        {
            return model.Metadata.ExpressType(ifcTypeId)?.Name ?? $"IfcType_{ifcTypeId}";
        }
        catch
        {
            return $"IfcType_{ifcTypeId}";
        }
    }

    private static float[] CreateNodeMatrixInMeters(XbimMatrix3D matrix, double oneMeter)
    {
        var value = XbimMatrix3D.Copy(matrix);
        value.OffsetX /= oneMeter;
        value.OffsetY /= oneMeter;
        value.OffsetZ /= oneMeter;
        return value.ToFloatArray();
    }

    private static MeshPrimitiveData? ReadMeshPrimitive(byte[] shapeData, double oneMeter)
    {
        using var stream = new MemoryStream(shapeData, writable: false);
        using var reader = new BinaryReader(stream);

        var triangulation = reader.ReadShapeTriangulation();
        triangulation.ToPointsWithNormalsAndIndices(out List<float[]> points, out List<int> indices);

        if (points.Count == 0 || indices.Count == 0)
        {
            return null;
        }

        var positions = new List<float>(points.Count * 3);
        var normals = new List<float>(points.Count * 3);
        var gltfIndices = new List<int>(indices.Count);

        foreach (var point in points)
        {
            positions.Add((float)(point[0] / oneMeter));
            positions.Add((float)(point[1] / oneMeter));
            positions.Add((float)(point[2] / oneMeter));

            normals.Add(point[3]);
            normals.Add(point[4]);
            normals.Add(point[5]);
        }

        gltfIndices.AddRange(indices);
        return new MeshPrimitiveData(positions, normals, gltfIndices);
    }

    private sealed class ConversionCounters
    {
        public int ProductCount { get; set; }

        public int ShapeInstanceCount { get; set; }

        public int PrimitiveCount { get; set; }

        public int TriangleCount { get; set; }

        public int VertexCount { get; set; }

        public int SkippedShapeCount { get; set; }
    }
}
