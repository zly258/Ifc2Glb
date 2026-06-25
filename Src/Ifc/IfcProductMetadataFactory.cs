using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;

namespace Ifc2Glb;

internal static class IfcProductMetadataFactory
{
    public static ProductMetadata Build(IIfcProduct product, XbimShapeInstance shapeInstance, string ifcTypeName, MaterialInfo materialInfo)
    {
        return new ProductMetadata
        {
            EntityLabel = product.EntityLabel,
            GlobalId = product.GlobalId.ToString() ?? string.Empty,
            IfcType = ifcTypeName,
            Name = product.Name?.ToString() ?? string.Empty,
            ObjectType = product.ObjectType?.ToString() ?? string.Empty,
            StyleLabel = shapeInstance.StyleLabel,
            MaterialName = materialInfo.Name,
            MaterialNames = materialInfo.MaterialNames,
            MaterialSource = materialInfo.Source
        };
    }
}
