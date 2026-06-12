using System.Buffers.Binary;
using System.Text;

namespace SteamEyaWinUI.Services;

internal sealed class SteamProtoWriter
{
    private readonly MemoryStream _stream = new();

    public static byte[] Build(Action<SteamProtoWriter> build)
    {
        var writer = new SteamProtoWriter();
        build(writer);
        return writer._stream.ToArray();
    }

    public void WriteInt32(int field, int value)
    {
        WriteTag(field, 0);
        WriteVarint((ulong)(uint)value);
    }

    public void WriteUInt32(int field, uint value)
    {
        WriteTag(field, 0);
        WriteVarint(value);
    }

    public void WriteUInt64(int field, ulong value)
    {
        WriteTag(field, 0);
        WriteVarint(value);
    }

    public void WriteFixed32(int field, uint value)
    {
        WriteTag(field, 5);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteFixed64(int field, ulong value)
    {
        WriteTag(field, 1);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteBool(int field, bool value)
    {
        WriteTag(field, 0);
        WriteVarint(value ? 1UL : 0UL);
    }

    public void WriteString(int field, string value)
    {
        WriteBytes(field, Encoding.UTF8.GetBytes(value));
    }

    public void WriteBytes(int field, byte[] value)
    {
        WriteTag(field, 2);
        WriteVarint((ulong)value.Length);
        _stream.Write(value);
    }

    private void WriteTag(int field, int wireType)
    {
        WriteVarint((ulong)((field << 3) | wireType));
    }

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        _stream.WriteByte((byte)value);
    }
}

internal sealed class SteamProtoReader
{
    private readonly byte[] _data;
    private int _offset;

    public SteamProtoReader(byte[] data)
    {
        _data = data;
    }

    public bool TryReadTag(out int field, out int wireType)
    {
        field = 0;
        wireType = 0;

        if (_offset >= _data.Length)
        {
            return false;
        }

        var tag = ReadVarint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 0x7);
        return true;
    }

    public ulong ReadVarint(int wireType)
    {
        EnsureWireType(wireType, 0);
        return ReadVarint();
    }

    public uint ReadFixed32(int wireType)
    {
        EnsureWireType(wireType, 5);
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset, 4));
        _offset += 4;
        return value;
    }

    public ulong ReadFixed64(int wireType)
    {
        EnsureWireType(wireType, 1);
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_offset, 8));
        _offset += 8;
        return value;
    }

    public bool ReadBool(int wireType)
    {
        return ReadVarint(wireType) != 0;
    }

    public string ReadString(int wireType)
    {
        return Encoding.UTF8.GetString(ReadLengthDelimited(wireType));
    }

    public byte[] ReadLengthDelimited(int wireType)
    {
        EnsureWireType(wireType, 2);
        var length = checked((int)ReadVarint());
        EnsureAvailable(length);
        var value = _data.AsSpan(_offset, length).ToArray();
        _offset += length;
        return value;
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0:
                ReadVarint();
                break;

            case 1:
                EnsureAvailable(8);
                _offset += 8;
                break;

            case 2:
                var length = checked((int)ReadVarint());
                EnsureAvailable(length);
                _offset += length;
                break;

            case 5:
                EnsureAvailable(4);
                _offset += 4;
                break;

            default:
                throw new InvalidOperationException($"不支持的 protobuf wire type：{wireType}");
        }
    }

    private ulong ReadVarint()
    {
        ulong value = 0;
        var shift = 0;

        while (shift < 64)
        {
            EnsureAvailable(1);
            var current = _data[_offset++];
            value |= (ulong)(current & 0x7F) << shift;

            if ((current & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new InvalidOperationException("protobuf varint 过长。");
    }

    private static void EnsureWireType(int actual, int expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"protobuf wire type 不匹配：实际 {actual}，期望 {expected}。");
        }
    }

    private void EnsureAvailable(int length)
    {
        if (length < 0 || _offset + length > _data.Length)
        {
            throw new InvalidOperationException("protobuf 数据不完整。");
        }
    }
}
