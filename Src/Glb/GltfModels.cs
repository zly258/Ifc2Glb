using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ifc2Glb;

internal enum GltfBufferViewTarget
{
    ArrayBuffer = 34962,
    ElementArrayBuffer = 34963
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

internal sealed class GltfNode
{
    public required string Name { get; init; }

    public int? Mesh { get; init; }

    public double[]? Matrix { get; init; }

    public List<int>? Children { get; set; }

    public Dictionary<string, object?>? Extras { get; init; }

    public void AddChild(int childIndex)
    {
        Children ??= [];
        Children.Add(childIndex);
    }
}

internal sealed record GltfScene
{
    public required int[] Nodes { get; init; }
}
