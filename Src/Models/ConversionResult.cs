namespace Ifc2Glb;

internal sealed record ConversionResult
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public int ProductCount { get; init; }

    public int ShapeInstanceCount { get; init; }

    public int PrimitiveCount { get; init; }

    public int UniqueGeometryCount { get; init; }

    public int ReusedGeometryCount { get; init; }

    public int MeshCount { get; init; }

    public int TriangleCount { get; init; }

    public int VertexCount { get; init; }

    public int SkippedShapeCount { get; init; }
}
