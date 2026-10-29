using System.Buffers.Binary;

namespace StandDesk.Core;

public sealed record DeskReading(int ExtensionRaw, short SpeedRaw, DateTimeOffset At);

public static class DeskProtocol
{
    public static decimal ToCm(int extension, int baseRaw) => (extension + baseRaw) / 100m;
    public static int FromCm(decimal cm, int baseRaw) => checked((int)decimal.Round(cm * 100m, 0, MidpointRounding.AwayFromZero) - baseRaw);
    public static DeskReading Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4) throw new InvalidDataException("Invalid desk height packet.");
        return new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]), DateTimeOffset.UtcNow);
    }
    public static byte[] EncodeTarget(int raw)
    {
        if (raw is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(raw));
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)raw);
        return bytes;
    }
}
