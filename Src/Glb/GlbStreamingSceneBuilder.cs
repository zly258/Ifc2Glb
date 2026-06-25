using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace Ifc2Glb;

internal sealed class GlbStreamingSceneBuilder : IDisposable
{
    private readonly string tempDirectory;
    private readonly List<GltfBufferView> bufferViews = [];
    private readonly List<GltfAccessor> accessors = [];
    private readonly List<GltfMaterial> materials = [];
    private readonly List<GltfMesh> meshes = [];
    private readonly List<GltfNode> nodes = [];
    private readonly List<int> sceneNodes = [];
    private readonly Dictionary<string, int> materialMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GltfGeometryResource> geometryMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> meshMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly BinaryFileBuffer binary;
    private bool disposed;

    public GlbStreamingSceneBuilder(string tempDirectory)
    {
        this.tempDirectory = tempDirectory;
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, "ifc2glb-bin-" + Guid.NewGuid().ToString("N") + ".tmp");
        binary = new BinaryFileBuffer(tempPath);
    }

    public bool AddYUpRootTransform { get; init; } = true;

    public int UniqueGeometryCount => geometryMap.Count;

    public int MeshCount => meshes.Count;

    public int AddProductNode(ProductMetadata metadata)
    {
        var nodeIndex = nodes.Count;
        nodes.Add(new GltfNode
        {
            Name = CreateDisplayName(metadata),
            Extras = CreateExtras(metadata)
        });

        sceneNodes.Add(nodeIndex);
        return nodeIndex;
    }

    public int AddShapeNode(int productNodeIndex, ProductMetadata metadata, IReadOnlyList<float> matrix, int meshIndex, int shapeGeometryLabel)
    {
        if (productNodeIndex < 0 || productNodeIndex >= nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(productNodeIndex));
        }

        var extras = CreateExtras(metadata);
        extras["ShapeGeometryLabel"] = shapeGeometryLabel;

        var nodeIndex = nodes.Count;
        nodes.Add(new GltfNode
        {
            Name = CreateShapeName(metadata, shapeGeometryLabel),
            Mesh = meshIndex,
            Matrix = matrix.Select(v => (double)v).ToArray(),
            Extras = extras
        });

        nodes[productNodeIndex].AddChild(nodeIndex);
        return nodeIndex;
    }

    public int GetOrAddMaterial(MaterialInfo material)
    {
        var key = string.IsNullOrWhiteSpace(material.Key) ? material.Name : material.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "Default";
        }

        if (materialMap.TryGetValue(key, out var index))
        {
            return index;
        }

        index = materials.Count;
        materialMap.Add(key, index);
        materials.Add(new GltfMaterial
        {
            Name = string.IsNullOrWhiteSpace(material.Name) ? key : material.Name,
            PbrMetallicRoughness = new GltfPbrMetallicRoughness
            {
                BaseColorFactor = [Clamp01(material.Red), Clamp01(material.Green), Clamp01(material.Blue), Clamp01(material.Alpha)],
                MetallicFactor = 0.0,
                RoughnessFactor = 0.85
            },
            DoubleSided = true,
            AlphaMode = material.Alpha < 0.999 ? "BLEND" : null
        });
        return index;
    }

    public bool TryGetGeometry(string geometryKey, out GltfGeometryResource resource)
    {
        return geometryMap.TryGetValue(geometryKey, out resource!);
    }

    public GltfGeometryResource GetOrAddGeometry(string geometryKey, MeshPrimitiveData primitive)
    {
        if (geometryMap.TryGetValue(geometryKey, out var resource))
        {
            return resource;
        }

        var positionAccessor = AddFloatVec3Accessor(primitive.Positions, GltfBufferViewTarget.ArrayBuffer);
        int? normalAccessor = null;
        if (primitive.Normals.Count == primitive.Positions.Count && primitive.Normals.Count > 0)
        {
            normalAccessor = AddFloatVec3Accessor(primitive.Normals, GltfBufferViewTarget.ArrayBuffer);
        }

        var indexAccessor = AddIndexAccessor(primitive.Indices);
        resource = new GltfGeometryResource
        {
            Key = geometryKey,
            PositionAccessor = positionAccessor,
            NormalAccessor = normalAccessor,
            IndexAccessor = indexAccessor,
            VertexCount = primitive.Positions.Count / 3,
            IndexCount = primitive.Indices.Count
        };
        geometryMap.Add(geometryKey, resource);
        return resource;
    }

    public int GetOrAddMesh(string meshKey, string meshName, GltfGeometryResource geometry, int materialIndex)
    {
        if (meshMap.TryGetValue(meshKey, out var meshIndex))
        {
            return meshIndex;
        }

        var attributes = new Dictionary<string, int>
        {
            ["POSITION"] = geometry.PositionAccessor
        };
        if (geometry.NormalAccessor.HasValue)
        {
            attributes["NORMAL"] = geometry.NormalAccessor.Value;
        }

        meshIndex = meshes.Count;
        meshes.Add(new GltfMesh
        {
            Name = string.IsNullOrWhiteSpace(meshName) ? $"Mesh_{meshIndex}" : meshName,
            Primitives =
            [
                new GltfMeshPrimitive
                {
                    Attributes = attributes,
                    Indices = geometry.IndexAccessor,
                    Material = materialIndex,
                    Mode = 4
                }
            ]
        });
        meshMap.Add(meshKey, meshIndex);
        return meshIndex;
    }

    public void Save(string outputPath)
    {
        if (meshes.Count == 0)
        {
            throw new InvalidOperationException("No mesh geometry was exported. Check the IFC geometry or export filters.");
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        binary.FlushAndClose();

        var finalSceneNodes = sceneNodes.ToArray();
        if (AddYUpRootTransform)
        {
            var rootIndex = nodes.Count;
            nodes.Add(new GltfNode
            {
                Name = "Root_Z_UP_to_Y_UP",
                Matrix = [1.0, 0.0, 0.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0],
                Children = finalSceneNodes.ToList()
            });
            finalSceneNodes = [rootIndex];
        }

        var root = new GltfRoot
        {
            Asset = new GltfAsset
            {
                Version = "2.0",
                Generator = "ifc2glb-wpf"
            },
            Scene = 0,
            Scenes = [new GltfScene { Nodes = finalSceneNodes }],
            Nodes = nodes,
            Meshes = meshes,
            Materials = materials,
            Buffers = [new GltfBuffer { ByteLength = binary.Length }],
            BufferViews = bufferViews,
            Accessors = accessors
        };

        var jsonPath = Path.Combine(tempDirectory, "ifc2glb-json-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var jsonStream = new FileStream(jsonPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                JsonSerializer.Serialize(jsonStream, root, JsonOptions.Default);
                jsonStream.Flush(true);
            }

            var jsonLength = new FileInfo(jsonPath).Length;
            WriteGlb(outputPath, jsonPath, jsonLength, binary.Path, binary.Length);
        }
        finally
        {
            TryDelete(jsonPath);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        binary.Dispose();
    }

    private static Dictionary<string, object?> CreateExtras(ProductMetadata metadata)
    {
        return new Dictionary<string, object?>
        {
            ["GlobalId"] = metadata.GlobalId,
            ["EntityLabel"] = metadata.EntityLabel,
            ["IfcType"] = metadata.IfcType,
            ["Name"] = metadata.Name,
            ["ObjectType"] = metadata.ObjectType,
            ["StyleLabel"] = metadata.StyleLabel,
            ["MaterialName"] = metadata.MaterialName,
            ["MaterialNames"] = metadata.MaterialNames,
            ["MaterialSource"] = metadata.MaterialSource
        };
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Max(0.0, Math.Min(1.0, value));
    }

    private int AddFloatVec3Accessor(List<float> values, GltfBufferViewTarget target)
    {
        if (values.Count % 3 != 0)
        {
            throw new ArgumentException("VEC3 value count must be divisible by 3.", nameof(values));
        }

        var offset = binary.Align4();
        foreach (var value in values)
        {
            binary.WriteSingle(value);
        }
        var byteLength = binary.Length - offset;

        var bufferViewIndex = AddBufferView(offset, byteLength, target);
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };

        for (var i = 0; i < values.Count; i += 3)
        {
            for (var j = 0; j < 3; j++)
            {
                var value = values[i + j];
                if (value < min[j])
                {
                    min[j] = value;
                }

                if (value > max[j])
                {
                    max[j] = value;
                }
            }
        }

        var accessorIndex = accessors.Count;
        accessors.Add(new GltfAccessor
        {
            BufferView = bufferViewIndex,
            ByteOffset = 0,
            ComponentType = 5126,
            Count = values.Count / 3,
            Type = "VEC3",
            Min = min,
            Max = max
        });
        return accessorIndex;
    }

    private int AddIndexAccessor(List<int> indices)
    {
        if (indices.Count == 0)
        {
            throw new ArgumentException("Index list is empty.", nameof(indices));
        }

        var min = indices.Min();
        var max = indices.Max();
        if (min < 0)
        {
            throw new ArgumentException("Negative mesh index is not valid for glTF.", nameof(indices));
        }

        var useUInt = max > ushort.MaxValue;
        var offset = binary.Align4();
        foreach (var index in indices)
        {
            if (useUInt)
            {
                binary.WriteUInt32((uint)index);
            }
            else
            {
                binary.WriteUInt16((ushort)index);
            }
        }
        var byteLength = binary.Length - offset;

        var bufferViewIndex = AddBufferView(offset, byteLength, GltfBufferViewTarget.ElementArrayBuffer);
        var accessorIndex = accessors.Count;
        accessors.Add(new GltfAccessor
        {
            BufferView = bufferViewIndex,
            ByteOffset = 0,
            ComponentType = useUInt ? 5125 : 5123,
            Count = indices.Count,
            Type = "SCALAR",
            Min = [min],
            Max = [max]
        });
        return accessorIndex;
    }

    private int AddBufferView(long byteOffset, long byteLength, GltfBufferViewTarget target)
    {
        var index = bufferViews.Count;
        bufferViews.Add(new GltfBufferView
        {
            Buffer = 0,
            ByteOffset = byteOffset,
            ByteLength = byteLength,
            Target = (int)target
        });
        return index;
    }

    private static void WriteGlb(string outputPath, string jsonPath, long jsonLength, string binaryPath, long binaryLength)
    {
        var jsonPadding = PaddingLength(jsonLength);
        var binaryPadding = PaddingLength(binaryLength);
        var paddedJsonLength = jsonLength + jsonPadding;
        var paddedBinaryLength = binaryLength + binaryPadding;
        var totalLength = 12L + 8L + paddedJsonLength + 8L + paddedBinaryLength;

        if (totalLength > uint.MaxValue)
        {
            throw new InvalidOperationException("The generated GLB is larger than 4GB. GLB 2.0 uses 32-bit chunk lengths. Please split the IFC or add export filters.");
        }

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], (uint)totalLength);
        output.Write(header);

        Span<byte> jsonHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHeader[0..4], (uint)paddedJsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHeader[4..8], 0x4E4F534A);
        output.Write(jsonHeader);
        CopyFile(jsonPath, output);
        WritePadding(output, jsonPadding, 0x20);

        Span<byte> binaryHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binaryHeader[0..4], (uint)paddedBinaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(binaryHeader[4..8], 0x004E4942);
        output.Write(binaryHeader);
        CopyFile(binaryPath, output);
        WritePadding(output, binaryPadding, 0x00);
    }

    private static void CopyFile(string inputPath, Stream output)
    {
        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        input.CopyTo(output, 1024 * 1024);
    }

    private static int PaddingLength(long length)
    {
        return (int)((4 - length % 4) % 4);
    }

    private static void WritePadding(Stream stream, int count, byte value)
    {
        for (var i = 0; i < count; i++)
        {
            stream.WriteByte(value);
        }
    }

    private static string CreateDisplayName(ProductMetadata metadata)
    {
        var name = string.IsNullOrWhiteSpace(metadata.Name) ? metadata.IfcType : metadata.Name;
        return $"{name} #{metadata.EntityLabel}";
    }

    private static string CreateShapeName(ProductMetadata metadata, int shapeGeometryLabel)
    {
        var typeName = string.IsNullOrWhiteSpace(metadata.IfcType) ? "IfcProduct" : metadata.IfcType;
        return $"{typeName}_{metadata.EntityLabel}_Shape_{shapeGeometryLabel}";
    }

    private static string CreateSafeMeshName(ProductMetadata metadata, int shapeGeometryLabel)
    {
        var typeName = string.IsNullOrWhiteSpace(metadata.IfcType) ? "IfcProduct" : metadata.IfcType;
        return $"{typeName}_{shapeGeometryLabel}";
    }

    public static string CreateMeshName(ProductMetadata metadata, int shapeGeometryLabel)
    {
        return CreateSafeMeshName(metadata, shapeGeometryLabel);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
