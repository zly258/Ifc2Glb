namespace Ifc2Glb;

internal sealed record ConversionProgress
{
    public int Percent { get; init; }

    public string Status { get; init; } = string.Empty;

    public string LogLine { get; init; } = string.Empty;

    public int ProductCount { get; init; } = -1;

    public int TriangleCount { get; init; } = -1;
}
