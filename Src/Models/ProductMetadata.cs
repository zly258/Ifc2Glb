namespace Ifc2Glb;

internal sealed record ProductMetadata
{
    public int EntityLabel { get; init; }

    public string GlobalId { get; init; } = string.Empty;

    public string IfcType { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string ObjectType { get; init; } = string.Empty;

    public int StyleLabel { get; init; }

    public string MaterialName { get; init; } = string.Empty;

    public string[] MaterialNames { get; init; } = [];

    public string MaterialSource { get; init; } = string.Empty;
}
