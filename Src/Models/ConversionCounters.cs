namespace Ifc2Glb;

internal sealed class ConversionCounters
{
    public int ProductCount { get; set; }

    public int ShapeInstanceCount { get; set; }

    public int PrimitiveCount { get; set; }

    public int UniqueGeometryCount { get; set; }

    public int ReusedGeometryCount { get; set; }

    public int MeshCount { get; set; }

    public int TriangleCount { get; set; }

    public int VertexCount { get; set; }

    public int SkippedShapeCount { get; set; }
}
