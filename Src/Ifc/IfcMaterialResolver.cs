using System.Collections;
using System.Reflection;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace Ifc2Glb;

internal sealed class IfcMaterialResolver
{
    private static readonly string[] MaterialChildPropertyNames =
    [
        "Material",
        "Materials",
        "RelatingMaterial",
        "ForLayerSet",
        "ForProfileSet",
        "MaterialLayers",
        "MaterialProfiles",
        "MaterialConstituents",
        "AssociatedTo"
    ];

    private readonly IModel model;
    private readonly Dictionary<int, string[]> productMaterialNameCache = [];
    private readonly Dictionary<MaterialCacheKey, MaterialInfo> materialCache = [];

    public IfcMaterialResolver(IModel model)
    {
        this.model = model;
    }

    public MaterialInfo Resolve(IIfcProduct product, int styleLabel, string ifcTypeName)
    {
        var materialNames = GetMaterialNames(product);
        var key = new MaterialCacheKey(product.EntityLabel, styleLabel, ifcTypeName);
        if (materialCache.TryGetValue(key, out var material))
        {
            return material;
        }

        if (TryCreateSurfaceStyleMaterial(styleLabel, ifcTypeName, materialNames, out material) ||
            TryCreateTypePaletteMaterial(ifcTypeName, materialNames, out material))
        {
            materialCache[key] = material;
            return material;
        }

        material = MaterialInfo.CreateFallback(ifcTypeName, materialNames);
        materialCache[key] = material;
        return material;
    }

    private string[] GetMaterialNames(IIfcProduct product)
    {
        if (productMaterialNameCache.TryGetValue(product.EntityLabel, out var names))
        {
            return names;
        }

        names = ExtractMaterialNames(product);
        productMaterialNameCache[product.EntityLabel] = names;
        return names;
    }

    private bool TryCreateSurfaceStyleMaterial(int styleLabel, string ifcTypeName, string[] materialNames, out MaterialInfo material)
    {
        material = default!;
        if (styleLabel <= 0)
        {
            return false;
        }

        try
        {
            if (model.Instances[styleLabel] is not IIfcSurfaceStyle surfaceStyle)
            {
                return false;
            }

            if (!TryReadSurfaceStyleColour(surfaceStyle, out var red, out var green, out var blue, out var alpha))
            {
                return false;
            }

            return CreateMaterialFromRgba(
                red,
                green,
                blue,
                alpha,
                $"style:{styleLabel}",
                MaterialInfo.CreateMaterialName(ifcTypeName, materialNames),
                "IfcSurfaceStyle",
                styleLabel,
                materialNames,
                out material);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateTypePaletteMaterial(string ifcTypeName, string[] materialNames, out MaterialInfo material)
    {
        var color = ColorPalette.FromText(ifcTypeName);
        return CreateMaterialFromRgba(
            color.R,
            color.G,
            color.B,
            1.0,
            $"type:{ifcTypeName}",
            MaterialInfo.CreateMaterialName(ifcTypeName, materialNames),
            "DefaultIfcTypePalette",
            0,
            materialNames,
            out material);
    }

    private static bool TryReadSurfaceStyleColour(
        object surfaceStyle,
        out double red,
        out double green,
        out double blue,
        out double alpha)
    {
        red = 0.78;
        green = 0.78;
        blue = 0.78;
        alpha = 1.0;

        if (TryReadSurfaceStyleItems(surfaceStyle, out red, out green, out blue, out alpha))
        {
            return true;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!TryFindRgbObject(surfaceStyle, visited, 0, out var colourObject))
        {
            return false;
        }

        if (!TryReadColourObject(colourObject, out red, out green, out blue))
        {
            return false;
        }

        if (TryFindDoubleProperty(surfaceStyle, "Transparency", new HashSet<object>(ReferenceEqualityComparer.Instance), 0, out var transparency))
        {
            alpha = 1.0 - Clamp01(transparency);
        }

        return true;
    }

    private static bool TryReadSurfaceStyleItems(
        object surfaceStyle,
        out double red,
        out double green,
        out double blue,
        out double alpha)
    {
        red = 0.78;
        green = 0.78;
        blue = 0.78;
        alpha = 1.0;

        var styles = ReadProperty(surfaceStyle, "Styles");
        if (styles is not IEnumerable enumerable)
        {
            return false;
        }

        foreach (var styleItem in enumerable)
        {
            if (styleItem == null)
            {
                continue;
            }

            var colourCandidate = ReadProperty(styleItem, "SurfaceColour")
                ?? ReadProperty(styleItem, "DiffuseColour")
                ?? ReadProperty(styleItem, "ReflectionColour")
                ?? ReadProperty(styleItem, "TransmissionColour")
                ?? ReadProperty(styleItem, "SpecularColour");

            if (!TryReadColourObject(colourCandidate, out red, out green, out blue))
            {
                if (!TryFindRgbObject(styleItem, new HashSet<object>(ReferenceEqualityComparer.Instance), 0, out var nestedColour) ||
                    !TryReadColourObject(nestedColour, out red, out green, out blue))
                {
                    continue;
                }
            }

            if (TryReadDoubleProperty(styleItem, "Transparency", out var itemTransparency))
            {
                alpha = 1.0 - Clamp01(itemTransparency);
            }
            else if (TryFindDoubleProperty(surfaceStyle, "Transparency", new HashSet<object>(ReferenceEqualityComparer.Instance), 0, out var styleTransparency))
            {
                alpha = 1.0 - Clamp01(styleTransparency);
            }

            return true;
        }

        return false;
    }

    private static bool TryReadColourObject(object? colourObject, out double red, out double green, out double blue)
    {
        red = 0.78;
        green = 0.78;
        blue = 0.78;

        if (colourObject == null)
        {
            return false;
        }

        if (!TryReadDoubleProperty(colourObject, "Red", out red) ||
            !TryReadDoubleProperty(colourObject, "Green", out green) ||
            !TryReadDoubleProperty(colourObject, "Blue", out blue))
        {
            return false;
        }

        red = NormalizeRgbComponent(red);
        green = NormalizeRgbComponent(green);
        blue = NormalizeRgbComponent(blue);
        return true;
    }

    private static bool TryFindRgbObject(object? value, HashSet<object> visited, int depth, out object colourObject)
    {
        colourObject = default!;
        if (value == null || depth > 8)
        {
            return false;
        }

        if (value is string)
        {
            return false;
        }

        var type = value.GetType();
        if (!type.IsValueType && !visited.Add(value))
        {
            return false;
        }

        if (HasReadableDoubleProperty(value, "Red") &&
            HasReadableDoubleProperty(value, "Green") &&
            HasReadableDoubleProperty(value, "Blue"))
        {
            colourObject = value;
            return true;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (TryFindRgbObject(item, visited, depth + 1, out colourObject))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? child;
            try
            {
                child = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (TryFindRgbObject(child, visited, depth + 1, out colourObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindDoubleProperty(
        object? value,
        string propertyName,
        HashSet<object> visited,
        int depth,
        out double result)
    {
        result = 0.0;
        if (value == null || depth > 8)
        {
            return false;
        }

        if (value is string)
        {
            return false;
        }

        var type = value.GetType();
        if (!type.IsValueType && !visited.Add(value))
        {
            return false;
        }

        if (TryReadDoubleProperty(value, propertyName, out result))
        {
            return true;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (TryFindDoubleProperty(item, propertyName, visited, depth + 1, out result))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            object? child;
            try
            {
                child = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (TryFindDoubleProperty(child, propertyName, visited, depth + 1, out result))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReadableDoubleProperty(object source, string propertyName)
    {
        return TryReadDoubleProperty(source, propertyName, out _);
    }

    private static bool CreateMaterialFromRgba(
        double red,
        double green,
        double blue,
        double alpha,
        string keyPrefix,
        string name,
        string source,
        int styleLabel,
        string[] materialNames,
        out MaterialInfo material)
    {
        red = Clamp01(red);
        green = Clamp01(green);
        blue = Clamp01(blue);
        alpha = Clamp01(alpha);

        material = new MaterialInfo
        {
            Key = $"{keyPrefix}:{red:0.###}:{green:0.###}:{blue:0.###}:{alpha:0.###}",
            Name = string.IsNullOrWhiteSpace(name) ? "Material" : name,
            Red = red,
            Green = green,
            Blue = blue,
            Alpha = alpha,
            Source = source,
            StyleLabel = styleLabel,
            MaterialNames = materialNames
        };
        return true;
    }

    private static string[] ExtractMaterialNames(IIfcProduct product)
    {
        var names = new List<string>();
        var visited = new HashSet<int>();
        CollectMaterialNames(ReadMaterialSelect(product), names, visited, 0);
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static object? ReadMaterialSelect(IIfcProduct product)
    {
        var value = ReadProperty(product, "Material");
        if (value != null)
        {
            return value;
        }

        try
        {
            return typeof(IIfcObjectDefinition).GetProperty("Material")?.GetValue(product);
        }
        catch
        {
            return null;
        }
    }

    private static void CollectMaterialNames(object? value, List<string> names, HashSet<int> visited, int depth)
    {
        if (value == null || depth > 8)
        {
            return;
        }

        if (value is string text)
        {
            AddName(names, text);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                CollectMaterialNames(item, names, visited, depth + 1);
            }

            return;
        }

        var entityLabel = ReadIntProperty(value, "EntityLabel");
        if (entityLabel > 0 && !visited.Add(entityLabel))
        {
            return;
        }

        var typeName = value.GetType().Name;
        if (typeName.Contains("Material", StringComparison.OrdinalIgnoreCase))
        {
            AddName(names, ReadStringProperty(value, "Name"));
        }

        foreach (var propertyName in MaterialChildPropertyNames)
        {
            CollectMaterialNames(ReadProperty(value, propertyName), names, visited, depth + 1);
        }
    }

    private static void AddName(List<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name.Trim());
        }
    }

    private static object? ReadProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                ?.GetValue(source);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadStringProperty(object source, string propertyName)
    {
        return ReadProperty(source, propertyName)?.ToString();
    }

    private static int ReadIntProperty(object source, string propertyName)
    {
        var value = ReadProperty(source, propertyName);
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
            _ => 0
        };
    }

    private static bool TryReadDoubleProperty(object source, string propertyName, out double value)
    {
        value = 0.0;
        var raw = ReadProperty(source, propertyName);
        return TryConvertToDouble(raw, out value);
    }

    private static bool TryConvertToDouble(object? raw, out double value, int depth = 0)
    {
        value = 0.0;
        if (raw == null || depth > 4)
        {
            return false;
        }

        if (raw is double doubleValue)
        {
            value = doubleValue;
            return true;
        }

        if (raw is float floatValue)
        {
            value = floatValue;
            return true;
        }

        if (raw is decimal decimalValue)
        {
            value = (double)decimalValue;
            return true;
        }

        if (raw is string text)
        {
            return double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        if (raw is IConvertible convertible)
        {
            try
            {
                value = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // xBIM measure structs are often not directly convertible. Try Value below.
            }
        }

        var unwrapped = ReadProperty(raw, "Value")
            ?? ReadProperty(raw, "RawValue")
            ?? ReadProperty(raw, "NominalValue");

        if (unwrapped != null && !ReferenceEquals(unwrapped, raw))
        {
            return TryConvertToDouble(unwrapped, out value, depth + 1);
        }

        return false;
    }

    private static double NormalizeRgbComponent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0.78;
        }

        if (value > 1.0 && value <= 255.0)
        {
            value /= 255.0;
        }

        return Clamp01(value);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Max(0.0, Math.Min(1.0, value));
    }

    private readonly record struct MaterialCacheKey(int ProductLabel, int StyleLabel, string IfcTypeName);
}
