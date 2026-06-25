namespace Ifc2Glb;

internal sealed record MaterialInfo
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public double Red { get; init; } = 0.78;

    public double Green { get; init; } = 0.78;

    public double Blue { get; init; } = 0.78;

    public double Alpha { get; init; } = 1.0;

    public string Source { get; init; } = "Fallback";

    public int StyleLabel { get; init; }

    public string[] MaterialNames { get; init; } = [];

    public static MaterialInfo CreateFallback(string ifcType, string[] materialNames)
    {
        var color = ColorPalette.FromText(ifcType);
        return new MaterialInfo
        {
            Key = $"fallback:{ifcType}:{color.R:0.###}:{color.G:0.###}:{color.B:0.###}",
            Name = CreateMaterialName(ifcType, materialNames),
            Red = color.R,
            Green = color.G,
            Blue = color.B,
            Alpha = 1.0,
            Source = "FallbackTypePalette",
            StyleLabel = 0,
            MaterialNames = materialNames
        };
    }

    public static string CreateMaterialName(string ifcType, string[] materialNames)
    {
        var displayNames = materialNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToArray();

        if (displayNames.Length > 0)
        {
            return string.Join(" + ", displayNames);
        }

        return string.IsNullOrWhiteSpace(ifcType) ? "Default" : ifcType;
    }
}
