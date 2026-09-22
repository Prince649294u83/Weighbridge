using System.Text;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Telemetry data chunk captured directly from the raw serial stream, representing
/// an unadulterated snapshot of the physical wire signals and hardware control pins.
/// </summary>
public sealed class DiagnosticDataChunk
{
    public byte[] RawBytes { get; }
    public string AsciiRepresentation { get; }
    public string HexRepresentation { get; }
    public bool CtsHolding { get; }
    public bool DsrHolding { get; }
    public bool CdHolding { get; }

    public DiagnosticDataChunk(byte[] bytes, int count, bool cts, bool dsr, bool cd)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (count < 0 || count > bytes.Length)
        {
            count = bytes.Length;
        }

        RawBytes = new byte[count];
        Array.Copy(bytes, RawBytes, count);
        CtsHolding = cts;
        DsrHolding = dsr;
        CdHolding = cd;

        var sbAscii = new StringBuilder(count * 2);
        var sbHex = new StringBuilder(count * 3);

        for (int i = 0; i < count; i++)
        {
            byte b = bytes[i];
            sbHex.Append(b.ToString("X2")).Append(' ');

            if (b == 0x02) sbAscii.Append("<STX>");
            else if (b == 0x03) sbAscii.Append("<ETX>");
            else if (b == 0x0D) sbAscii.Append("<CR>");
            else if (b == 0x0A) sbAscii.Append("<LF>\n");
            else if (b == 0x06) sbAscii.Append("<ACK>");
            else if (b == 0x15) sbAscii.Append("<NAK>");
            else if (b == 0x20) sbAscii.Append(' ');
            else if (b >= 32 && b <= 126) sbAscii.Append((char)b);
            else sbAscii.Append($"<0x{b:X2}>");
        }

        AsciiRepresentation = sbAscii.ToString();
        HexRepresentation = sbHex.ToString();
    }
}
