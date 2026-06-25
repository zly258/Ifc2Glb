using System.Buffers.Binary;
using System.IO;

namespace Ifc2Glb;

internal sealed class BinaryFileBuffer : IDisposable
{
    private readonly FileStream stream;
    private bool closed;
    private long length;

    public BinaryFileBuffer(string path)
    {
        Path = path;
        stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
    }

    public string Path { get; }

    public long Length => closed ? length : stream.Length;

    public long Align4()
    {
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }

        return stream.Position;
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    public void FlushAndClose()
    {
        if (closed)
        {
            return;
        }

        length = stream.Length;
        stream.Flush(true);
        stream.Dispose();
        closed = true;
    }

    public void Dispose()
    {
        if (!closed)
        {
            stream.Dispose();
            closed = true;
        }

        TryDelete(Path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
