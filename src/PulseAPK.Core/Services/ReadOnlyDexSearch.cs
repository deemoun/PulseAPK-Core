using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PulseAPK.Core.Services;

public sealed record DexEntrySearch(string Entry, int Matches, string? Error);

public static class ReadOnlyDexSearch
{
    public static Task<List<DexEntrySearch>> InspectAsync(string apk, string query, CancellationToken token) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(query)) throw new ArgumentException("Enter text to find.");
        using var archive = ZipFile.OpenRead(apk);
        var results = new List<DexEntrySearch>();
        foreach (var entry in archive.Entries.Where(e => e.Name == e.FullName &&
            System.Text.RegularExpressions.Regex.IsMatch(e.Name, @"^classes(?:[2-9]|[1-9][0-9]+)?\.dex$")))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (entry.Length is < 112 or > 64 * 1024 * 1024) throw new InvalidDataException("Unsupported entry size.");
                using var input = entry.Open();
                var bytes = new byte[(int)entry.Length];
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1) throw new InvalidDataException("Entry size mismatch.");
                results.Add(new(entry.Name, CountMatches(bytes, query, token), null));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException)
            {
                results.Add(new(entry.Name, 0, ex.Message));
            }
        }
        return results;
    }, token);

    // Inspect the string table only. DEX strings encode UTF-16 code units using modified UTF-8.
    private static int CountMatches(byte[] bytes, string query, CancellationToken token)
    {
        int Word(int offset) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
        InvalidDataException Invalid() => new("Invalid or unsupported DEX string table.");
        var version = Encoding.ASCII.GetString(bytes, 4, 3);
        if (!bytes.AsSpan(0, 4).SequenceEqual("dex\n"u8) || bytes[7] != 0 ||
            version is not ("035" or "037" or "038" or "039" or "040") ||
            Word(32) != bytes.Length || Word(36) != 112 || Word(40) != 0x12345678) throw Invalid();
        uint sum1 = 1, sum2 = 0;
        for (int start = 12; start < bytes.Length; start += 5552)
        {
            token.ThrowIfCancellationRequested();
            int end = Math.Min(bytes.Length, start + 5552);
            for (int i = start; i < end; i++) { sum1 += bytes[i]; sum2 += sum1; }
            sum1 %= 65521;
            sum2 %= 65521;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != ((sum2 << 16) | sum1))
            throw new InvalidDataException("DEX checksum mismatch.");
        int count = Word(56), table = Word(60);
        if (count != 0 && (table < 112 || (table & 3) != 0 || checked(table + count * 4) > bytes.Length)) throw Invalid();
        // Bound aggregate decoding work even if corrupt entries repeatedly reference the same data.
        long budget = bytes.Length;
        int matches = 0;
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            int pos = Word(table + i * 4);
            int Next() => pos < bytes.Length ? bytes[pos++] : throw Invalid();
            int Continuation() { int b = Next(); return (b & 192) == 128 ? b & 63 : throw Invalid(); }
            long length = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (shift > 28) throw Invalid();
                int b = Next();
                length |= (long)(b & 127) << shift;
                if (b < 128) break;
            }
            budget -= length + 1;
            if (budget < 0 || length > bytes.Length - pos) throw Invalid();
            text.Clear();
            for (int c = 0; c < length; c++)
            {
                if ((c & 4095) == 0) token.ThrowIfCancellationRequested();
                int b = Next();
                int value = b switch
                {
                    > 0 and < 128 => b,
                    >= 192 and <= 223 => ((b & 31) << 6) | Continuation(),
                    >= 224 and <= 239 => ((b & 15) << 12) | (Continuation() << 6) | Continuation(),
                    _ => throw Invalid()
                };
                if ((b >= 224 && value < 2048) || (b >= 192 && value is > 0 and < 128)) throw Invalid();
                text.Append((char)value);
            }
            if (Next() != 0) throw Invalid();
            if (text.ToString().Contains(query, StringComparison.Ordinal)) matches++;
        }
        return matches;
    }
}
