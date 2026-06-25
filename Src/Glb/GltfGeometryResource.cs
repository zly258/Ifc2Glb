namespace Ifc2Glb;

internal sealed record GltfGeometryResource
{
    public required string Key { get; init; }

    public int PositionAccessor { get; init; }

    public int? NormalAccessor { get; init; }

    public int IndexAccessor { get; init; }

    public int VertexCount { get; init; }

    public int IndexCount { get; init; }
}
