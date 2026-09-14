using System.Globalization;
using System.Text;

namespace RunawayExplorer.Core.Formats;

/// <summary>
/// Fallback viewer for entries no decoder claims: renders a classic hex/ASCII dump for inspection.
/// </summary>
public static class RawFormat
{
    /// <summary>Only the first 128 KiB are ever rendered as text.</summary>
    public const int MaxDumpBytes = 128 * 1024;

    /// <summary>
    /// Produces a hex dump of (at most) the first 128 KiB of <paramref name="data"/>. Each line has the form
    /// <c>XXXX:XXXX | b0 b1 ... b15 | c0c1...c15</c>: the offset split into two 4-hex-digit groups, each
    /// byte as two hex digits, and the character column showing ASCII for 33..127 and '.' otherwise.
    /// </summary>
    public static string ToHexDump(ReadOnlySpan<byte> data)
    {
        int length = Math.Min(data.Length, MaxDumpBytes);
        var sb = new StringBuilder(length / 16 * 80 + 128);

        for (int offset = 0; offset < length; offset += 16)
        {
            int lineLength = Math.Min(16, length - offset);

            uint offsetValue = (uint)offset;
            sb.Append(((offsetValue >> 16) & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture));
            sb.Append(':');
            sb.Append((offsetValue & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture));
            sb.Append(" | ");

            for (int i = 0; i < 16; i++)
            {
                if (i < lineLength)
                    sb.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture));
                else
                    sb.Append("  ");
                sb.Append(' ');
            }

            sb.Append("| ");

            for (int i = 0; i < lineLength; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 33 and <= 127 ? (char)b : '.');
            }

            sb.Append('\n');
        }

        if (data.Length > length)
            sb.Append(CultureInfo.InvariantCulture, $"\n... {data.Length - length:N0} more byte(s) not shown.\n");

        return sb.ToString();
    }
}
