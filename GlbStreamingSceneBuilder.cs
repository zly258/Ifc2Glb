using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ifc2Glb;

internal sealed class GlbStreamingSceneBuilder : IDisposable
{
    private readonly List<GltfBufferView> bufferViews = [];
    private readonly List<GltfAccessor> accessors = [];
    private readonly List<GltfMaterial> materials = [];
    private readonly List<GltfMesh> meshes = [];
    private readonly List<GltfNode> nodes = [];
    private readonly List<int> sceneNodes = [];
    private readonly Dictionary<string, int> materialMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> nodeMeshMap = [];
    private readonly BinaryFileBuffer binary;
    private bool disposed;

    public GlbStreamingSceneBuilder(string tempDirectory)
    {
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, "ifc2glb-" + Guid.NewGuid().ToString("N") + ".bin");
        binary = new BinaryFileBuffer(tempPath);
    }

    public bool AddYUpRootTransform { get; init; } = true;

    public int AddProductNode(ProductMetadata metadata, IReadOnlyList<float> matrix)
    {
        var meshIndex = meshes.Count;
        meshes.Add(new GltfMesh
        {
            Name = CreateSafeName(metadata.IfcType, metadata.EntityLabel),
            Primitives = []
        });

        var nodeIndex = nodes.Count;
        nodes.Add(new GltfNode
        {
            Name = CreateDisplayName(metadata),
            Mesh = meshIndex,
            Matrix = matrix.Select(v => (double)v).ToArray(),
            Extras = new Dictionary<string, object?>
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
            }
        });

        nodeMeshMap[nodeIndex] = meshIndex;
        sceneNodes.Add(nodeIndex);
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

    public void AddPrimitiveToNode(int nodeIndex, MeshPrimitiveData primitive, int materialIndex)
    {
        if (!nodeMeshMap.TryGetValue(nodeIndex, out var meshIndex))
        {
            throw new InvalidOperationException($"Node {nodeIndex} does not have a mesh.");
        }

        var positionAccessor = AddFloatVec3Accessor(primitive.Positions, GltfBufferViewTarget.ArrayBuffer);
        var normalAccessor = AddFloatVec3Accessor(primitive.Normals, GltfBufferViewTarget.ArrayBuffer);
        var indexAccessor = AddIndexAccessor(primitive.Indices);

        meshes[meshIndex].Primitives.Add(new GltfMeshPrimitive
        {
            Attributes = new Dictionary<string, int>
            {
                ["POSITION"] = positionAccessor,
                ["NORMAL"] = normalAccessor
            },
            Indices = indexAccessor,
            Material = materialIndex,
            Mode = 4
        });
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

        var finalNodes = nodes.ToList();
        var finalSceneNodes = sceneNodes.ToArray();

        if (AddYUpRootTransform)
        {
            var rootIndex = finalNodes.Count;
            finalNodes.Add(new GltfNode
            {
                Name = "Root_Z_UP_to_Y_UP",
                Matrix = [1.0, 0.0, 0.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0],
                Children = finalSceneNodes
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
            Nodes = finalNodes,
            Meshes = meshes,
            Materials = materials,
            Buffers = [new GltfBuffer { ByteLength = binary.Length }],
            BufferViews = bufferViews,
            Accessors = accessors
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(root, JsonOptions.Default);
        WriteGlb(outputPath, json, binary.Path, binary.Length);
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

    private static void WriteGlb(string outputPath, byte[] jsonBytes, string binaryPath, long binaryLength)
    {
        var jsonPadding = PaddingLength(jsonBytes.Length);
        var binaryPadding = PaddingLength(binaryLength);
        var paddedJsonLength = jsonBytes.Length + jsonPadding;
        var paddedBinaryLength = binaryLength + binaryPadding;
        var totalLength = 12L + 8L + paddedJsonLength + 8L + paddedBinaryLength;

        if (totalLength > uint.MaxValue)
        {
            throw new InvalidOperationException("The generated GLB is larger than 4GB. GLB 2.0 uses 32-bit chunk lengths. Please split the IFC or add export filters.");
        }

        using var output = File.Create(outputPath);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], (uint)totalLength);
        output.Write(header);

        Span<byte> jsonHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHeader[0..4], (uint)paddedJsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(jsonHeader[4..8], 0x4E4F534A);
        output.Write(jsonHeader);
        output.Write(jsonBytes);
        WritePadding(output, jsonPadding, 0x20);

        Span<byte> binaryHeader = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(binaryHeader[0..4], (uint)paddedBinaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(binaryHeader[4..8], 0x004E4942);
        output.Write(binaryHeader);

        using (var input = File.OpenRead(binaryPath))
        {
            input.CopyTo(output, 1024 * 1024);
        }
        WritePadding(output, binaryPadding, 0x00);
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

    private static string CreateSafeName(string ifcType, int entityLabel)
    {
        if (string.IsNullOrWhiteSpace(ifcType))
        {
            ifcType = "IfcProduct";
        }

        return $"{ifcType}_{entityLabel}";
    }
}

internal enum GltfBufferViewTarget
{
    ArrayBuffer = 34962,
    ElementArrayBuffer = 34963
}

internal sealed class BinaryFileBuffer : IDisposable
{
    private readonly FileStream stream;
    private bool closed;
    private long length;

    public BinaryFileBuffer(string path)
    {
        Path = path;
        stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    }

    public string Path { get; }

    public long Length => closed ? length : stream.Length;

    public long Align4()
    {
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }
        return stream.Position;
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void FlushAndClose()
    {
        if (closed)
        {
            return;
        }

        length = stream.Length;
        stream.Flush(true);
        stream.Dispose();
        closed = true;
    }

    public void Dispose()
    {
        if (!closed)
        {
            stream.Dispose();
            closed = true;
        }

        try
        {
            File.Delete(Path);
        }
        catch
        {
        }
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

internal sealed record GltfRoot
{
    public required GltfAsset Asset { get; init; }

    public required List<GltfBuffer> Buffers { get; init; }

    public required List<GltfBufferView> BufferViews { get; init; }

    public required List<GltfAccessor> Accessors { get; init; }

    public required List<GltfMaterial> Materials { get; init; }

    public required List<GltfMesh> Meshes { get; init; }

    public required List<GltfNode> Nodes { get; init; }

    public required GltfScene[] Scenes { get; init; }

    public int Scene { get; init; }
}

internal sealed record GltfAsset
{
    public required string Version { get; init; }

    public required string Generator { get; init; }
}

internal sealed record GltfBuffer
{
    public long ByteLength { get; init; }
}

internal sealed record GltfBufferView
{
    public int Buffer { get; init; }

    public long ByteOffset { get; init; }

    public long ByteLength { get; init; }

    public int Target { get; init; }
}

internal sealed record GltfAccessor
{
    public int BufferView { get; init; }

    public int ByteOffset { get; init; }

    public int ComponentType { get; init; }

    public int Count { get; init; }

    public required string Type { get; init; }

    public double[]? Min { get; init; }

    public double[]? Max { get; init; }
}

internal sealed record GltfMaterial
{
    public required string Name { get; init; }

    public required GltfPbrMetallicRoughness PbrMetallicRoughness { get; init; }

    public bool DoubleSided { get; init; }

    public string? AlphaMode { get; init; }
}

internal sealed record GltfPbrMetallicRoughness
{
    public required double[] BaseColorFactor { get; init; }

    public double MetallicFactor { get; init; }

    public double RoughnessFactor { get; init; }
}

internal sealed record GltfMesh
{
    public required string Name { get; init; }

    public required List<GltfMeshPrimitive> Primitives { get; init; }
}

internal sealed record GltfMeshPrimitive
{
    public required Dictionary<string, int> Attributes { get; init; }

    public int Indices { get; init; }

    public int Material { get; init; }

    public int Mode { get; init; }
}

internal sealed record GltfNode
{
    public required string Name { get; init; }

    public int? Mesh { get; init; }

    public double[]? Matrix { get; init; }

    public int[]? Children { get; init; }

    public Dictionary<string, object?>? Extras { get; init; }
}

internal sealed record GltfScene
{
    public required int[] Nodes { get; init; }
}
