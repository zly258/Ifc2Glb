namespace Ifc2Glb;

internal readonly record struct RgbColor(double R, double G, double B);

internal static class ColorPalette
{
    public static RgbColor FromText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RgbColor(0.75, 0.75, 0.75);
        }

        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash *= 16777619u;
        }

        var hue = (hash % 360) / 360.0;
        return FromHsl(hue, 0.42, 0.68);
    }

    private static RgbColor FromHsl(double h, double s, double l)
    {
        var c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        var x = c * (1.0 - Math.Abs((h * 6.0) % 2.0 - 1.0));
        var m = l - c / 2.0;

        var sector = (int)Math.Floor(h * 6.0);
        var (r1, g1, b1) = sector switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return new RgbColor(r1 + m, g1 + m, b1 + m);
    }
}
