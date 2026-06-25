using System.IO;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;

namespace Ifc2Glb;

internal static class IfcMeshPrimitiveReader
{
    public static float[] CreateNodeMatrixInMeters(XbimMatrix3D matrix, double oneMeter)
    {
        var value = XbimMatrix3D.Copy(matrix);
        value.OffsetX /= oneMeter;
        value.OffsetY /= oneMeter;
        value.OffsetZ /= oneMeter;
        return value.ToFloatArray();
    }

    public static MeshPrimitiveData? ReadMeshPrimitive(byte[] shapeData, double oneMeter)
    {
        using var stream = new MemoryStream(shapeData, writable: false);
        using var reader = new BinaryReader(stream);

        var triangulation = reader.ReadShapeTriangulation();
        triangulation.ToPointsWithNormalsAndIndices(out List<float[]> points, out List<int> indices);

        if (points.Count == 0 || indices.Count == 0)
        {
            return null;
        }

        var positions = new List<float>(points.Count * 3);
        var normals = new List<float>(points.Count * 3);
        var gltfIndices = new List<int>(indices.Count);

        foreach (var point in points)
        {
            positions.Add((float)(point[0] / oneMeter));
            positions.Add((float)(point[1] / oneMeter));
            positions.Add((float)(point[2] / oneMeter));

            normals.Add(point[3]);
            normals.Add(point[4]);
            normals.Add(point[5]);
        }

        gltfIndices.AddRange(indices);
        return new MeshPrimitiveData(positions, normals, gltfIndices);
    }
}
