using Xbim.Common;

namespace Ifc2Glb;

internal static class IfcTypeNameResolver
{
    public static string GetIfcTypeName(IModel model, short ifcTypeId, Dictionary<short, string> cache)
    {
        if (cache.TryGetValue(ifcTypeId, out var value))
        {
            return value;
        }

        try
        {
            value = model.Metadata.ExpressType(ifcTypeId)?.Name ?? $"IfcType_{ifcTypeId}";
        }
        catch
        {
            value = $"IfcType_{ifcTypeId}";
        }

        cache[ifcTypeId] = value;
        return value;
    }
}
