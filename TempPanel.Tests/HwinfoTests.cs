using System.Text;
using TempPanel.Sensors;
using Xunit;

namespace TempPanel.Tests;

/// <summary>
/// Valida el lector de memoria compartida de HWiNFO con un bloque sintético
/// (sin necesitar HWiNFO instalado). Cubre firma, validación y extracción.
/// </summary>
public sealed class HwinfoParserTests
{
    private static void WU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)(v & 0xFF);
        b[off + 1] = (byte)((v >> 8) & 0xFF);
        b[off + 2] = (byte)((v >> 16) & 0xFF);
        b[off + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static void WStr(byte[] b, int off, string s, int max)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        Array.Copy(bytes, 0, b, off, Math.Min(bytes.Length, max));
    }

    private static void WDbl(byte[] b, int off, double v)
    {
        Array.Copy(BitConverter.GetBytes(v), 0, b, off, 8);
    }

    // Bloque: 1 sensor CPU + lecturas (Package 65.5 °C, Core 0 61.2, Core 1 63.0, ventilador 2500 RPM).
    private static byte[] BuildValid()
    {
        const int sensSize = 264, readSize = 316;
        const int sensOff = 52, readOff = 52 + sensSize;
        var buf = new byte[readOff + 4 * readSize];
        buf[0] = (byte)'H'; buf[1] = (byte)'W'; buf[2] = (byte)'i'; buf[3] = (byte)'N';
        WU32(buf, 4, 1);      // versión
        WU32(buf, 8, 0);      // revisión
        WU32(buf, 28, (uint)sensOff);
        WU32(buf, 32, (uint)sensSize);
        WU32(buf, 36, 1);     // 1 sensor
        WU32(buf, 40, (uint)readOff);
        WU32(buf, 44, (uint)readSize);
        WU32(buf, 48, 4);     // 4 lecturas

        WU32(buf, sensOff, 1); // tipo CPU
        WU32(buf, sensOff + 4, 0);
        WStr(buf, sensOff + 8, "CPU [#0]: Intel Core i7-13620H", 128);

        void Reading(int i, int type, string label, string unit, double v)
        {
            int off = readOff + i * readSize;
            WU32(buf, off, (uint)type);
            WU32(buf, off + 4, 0); // sensor 0
            WU32(buf, off + 8, (uint)i);
            WStr(buf, off + 12, label, 128);
            WStr(buf, off + 268, unit, 16);
            WDbl(buf, off + 284, v);
            WDbl(buf, off + 292, v - 1);
            WDbl(buf, off + 300, v + 1);
            WDbl(buf, off + 308, v);
        }
        Reading(0, 1, "CPU Package", "°C", 65.5);
        Reading(1, 1, "Core 0", "°C", 61.2);
        Reading(2, 1, "Core 1", "°C", 63.0);
        Reading(3, 3, "CPU Fan", "RPM", 2500);
        return buf;
    }

    [Fact]
    public void Parses_Valid_Block()
    {
        Assert.True(HwinfoParser.TryParse(BuildValid(), out var rs));
        Assert.Equal(4, rs.Count);
        var pkg = rs.First(r => r.Label == "CPU Package");
        Assert.Equal(65.5, pkg.Value);
        Assert.Equal("°C", pkg.Unit); // "?C" normalizado (en ASCII ° llega como ?)
        Assert.Equal("CPU [#0]: Intel Core i7-13620H", pkg.SensorName);
        var fan = rs.First(r => r.Label == "CPU Fan");
        Assert.Equal(2500, fan.Value);
        Assert.Equal("RPM", fan.Unit);
    }

    [Fact]
    public void Rejects_Bad_Signature()
    {
        var buf = BuildValid();
        buf[0] = (byte)'X';
        Assert.False(HwinfoParser.TryParse(buf, out _));
    }

    [Fact]
    public void Rejects_Truncated_Buffer()
    {
        var buf = BuildValid();
        Assert.False(HwinfoParser.TryParse(buf[..100], out _));
        Assert.False(HwinfoParser.TryParse(Array.Empty<byte>(), out _));
    }

    [Fact]
    public void Rejects_Insane_Counts()
    {
        var buf = BuildValid();
        WU32(buf, 48, 999999);
        Assert.False(HwinfoParser.TryParse(buf, out _));
    }

    [Fact]
    public void Rejects_NonPrintable_Names()
    {
        var buf = BuildValid();
        buf[52 + 8] = 0x01; // ensucia el nombre del sensor
        Assert.False(HwinfoParser.TryParse(buf, out _));
    }
}
