using Xbim.Ifc4.Interfaces;

namespace Ifc2Glb;

internal static class IfcExportFilter
{
    public static bool ShouldExport(IIfcProduct product, ConversionOptions options)
    {
        if (!options.IncludeSpaces && product is IIfcSpace)
        {
            return false;
        }

        if (!options.IncludeOpenings && product is IIfcFeatureElement)
        {
            return false;
        }

        return true;
    }
}
