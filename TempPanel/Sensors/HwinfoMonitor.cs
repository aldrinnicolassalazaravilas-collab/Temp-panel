using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace TempPanel.Sensors;

/// <summary>
/// Lectura OPCIONAL de HWiNFO64 vía su memoria compartida pública (SDK SM2).
/// Solo se usa si el usuario ejecuta HWiNFO64 con "Shared Memory Support".
/// Sin HWiNFO en ejecución no hace nada (sin errores, sin dependencias).
/// El bloque se valida de forma estricta antes de confiar en ningún valor.
/// </summary>
public sealed class HwinfoReading
{
    public int SensorType { get; set; }
    public string SensorName { get; set; } = "";
    public int ReadingType { get; set; }
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
}

public static class HwinfoParser
{
    public const string MapName = "Global\\HWiNFO_SENS_SM2";

    // Tipos del SDK de HWiNFO (se usan como pista; la fusión valida además
    // por nombre de sensor, etiqueta y unidad en texto).
    public const int SensorCpu = 1;
    public const int SensorGpu = 4;
    public const int ReadingTemp = 1;
    public const int ReadingFan = 3;

    private const int HeaderSize = 52;
    private const int MaxElements = 20000;

    public static bool TryParse(byte[] buf, out List<HwinfoReading> readings)
    {
        readings = new List<HwinfoReading>();
        try
        {
            if (buf == null || buf.Length < HeaderSize) return false;
            // Firma ASCII "HWiN" al inicio (magia del SDK).
            if (buf[0] != (byte)'H' || buf[1] != (byte)'W' ||
                buf[2] != (byte)'i' || buf[3] != (byte)'N') return false;

            uint version = ReadU32(buf, 4);
            uint revision = ReadU32(buf, 8);
            if (version == 0 || version > 16 || revision > 100000) return false;

            uint sensOff = ReadU32(buf, 28);
            uint sensSize = ReadU32(buf, 32);
            uint sensCount = ReadU32(buf, 36);
            uint readOff = ReadU32(buf, 40);
            uint readSize = ReadU32(buf, 44);
            uint readCount = ReadU32(buf, 48);

            if (sensCount > MaxElements || readCount > MaxElements) return false;
            // Tamaños mínimos para los campos que leemos:
            // sensor: id+inst+nombre(128) = 136; lectura: cabecera+etiquetas+unidad+valor = 316.
            if (sensSize < 136 || readSize < 316) return false;
            if (sensOff < HeaderSize || readOff < HeaderSize) return false;
            if ((ulong)sensOff + (ulong)sensCount * sensSize > (ulong)buf.Length) return false;
            if ((ulong)readOff + (ulong)readCount * readSize > (ulong)buf.Length) return false;
            if (sensCount == 0 || readCount == 0) return false;

            var sensorNames = new string[sensCount];
            var sensorTypes = new int[sensCount];
            for (int i = 0; i < sensCount; i++)
            {
                int off = checked((int)sensOff + i * (int)sensSize);
                // Para no leer basura: el nombre debe ser texto imprimible.
                string name = ReadString(buf, off + 8, 128);
                if (!IsPrintable(name)) return false;
                sensorNames[i] = name.Trim();
                sensorTypes[i] = ReadI32(buf, off); // dwSensorID = tipo en el SDK
            }

            for (int i = 0; i < readCount; i++)
            {
                int off = checked((int)readOff + i * (int)readSize);
                int type = ReadI32(buf, off);
                uint sIdx = ReadU32(buf, off + 4);
                if (sIdx >= sensCount) continue;
                string label = ReadString(buf, off + 12, 128);
                string unit = ReadString(buf, off + 268, 16);
                if (!IsPrintable(label) || !IsPrintable(unit)) continue;
                double value = ReadF64(buf, off + 284);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                readings.Add(new HwinfoReading
                {
                    SensorType = sensorTypes[sIdx],
                    SensorName = sensorNames[sIdx],
                    ReadingType = type,
                    Label = label.Trim(),
                    Unit = NormalizeUnit(unit.Trim()),
                    Value = value,
                    Min = ReadF64(buf, off + 292),
                    Max = ReadF64(buf, off + 300),
                    Avg = ReadF64(buf, off + 308)
                });
            }
            return readings.Count > 0;
        }
        catch
        {
            readings = new List<HwinfoReading>();
            return false;
        }
    }

    private static string NormalizeUnit(string u)
    {
        // El SDK usa CHAR ANSI: "°C" puede llegar como "?C" al decodificar ASCII.
        if (u == "?C") return "°C";
        if (u == "?F") return "°F";
        return u;
    }

    private static uint ReadU32(byte[] b, int off) =>
        (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));

    private static int ReadI32(byte[] b, int off) => unchecked((int)ReadU32(b, off));

    private static double ReadF64(byte[] b, int off)
    {
        if (off + 8 > b.Length) return double.NaN;
        return BitConverter.ToDouble(b, off);
    }

    private static string ReadString(byte[] b, int off, int max)
    {
        if (off < 0 || off + max > b.Length) return "";
        int len = 0;
        while (len < max && b[off + len] != 0) len++;
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            char c = (char)b[off + i]; // ASCII 0-127
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsPrintable(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (char c in s)
        {
            if (c < 32 || c > 126) return false;
        }
        return true;
    }
}

public static class HwinfoMonitor
{
    /// <summary>
    /// Intenta leer el bloque compartido. false = HWiNFO64 no está en ejecución
    /// (o sin "Shared Memory Support"): no es un error.
    /// </summary>
    public static bool TryRead(out List<HwinfoReading> readings, out int sensorCount, out string? note)
    {
        readings = new List<HwinfoReading>();
        sensorCount = 0;
        note = null;
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(HwinfoParser.MapName, MemoryMappedFileRights.Read);
            using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            // Tamaño acotado (el bloque real ronda decenas de KB).
            const int maxBytes = 4 * 1024 * 1024;
            int len = (int)Math.Min(stream.Length, maxBytes);
            if (len < 52) return false;
            var buf = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = stream.Read(buf, read, len - read);
                if (n == 0) break;
                read += n;
            }
            if (read < 52) return false;
            if (read < len) Array.Resize(ref buf, read);
            if (!HwinfoParser.TryParse(buf, out readings)) return false;
            sensorCount = readings.Select(r => r.SensorName).Distinct().Count();
            note = $"HWiNFO64 detectado ({sensorCount} sensores, {readings.Count} lecturas). " +
                   "Sus valores complementan a los de LibreHardwareMonitor.";
            return true;
        }
        catch (FileNotFoundException)
        {
            note = "HWiNFO64 no está en ejecución. Para temperaturas sin desactivar protecciones, " +
                   "instala HWiNFO64 (gratuito) y activa «Shared Memory Support».";
            return false;
        }
        catch
        {
            return false;
        }
    }
}
