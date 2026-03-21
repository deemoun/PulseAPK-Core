using System.IO.Compression;
using System.Text.RegularExpressions;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class FinalDexInspectionService : IFinalDexInspectionService
{
    public async Task<(bool Found, string Diagnostics)> ContainsMethodReferenceAsync(string apkPath, string methodReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
        {
            return (false, $"APK path is missing or file does not exist: '{apkPath}'.");
        }

        if (!TryParseMethodReference(methodReference, out var classDescriptor, out var methodName, out var signature))
        {
            return (false, $"Method reference '{methodReference}' is invalid.");
        }

        using var stream = File.OpenRead(apkPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var dexEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("classes", StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase));

        var totalDexEntries = 0;
        foreach (var dexEntry in dexEntries)
        {
            totalDexEntries++;
            await using var dexStream = dexEntry.Open();
            using var buffer = new MemoryStream();
            await dexStream.CopyToAsync(buffer, cancellationToken);

            var dexData = buffer.ToArray();
            var (found, reason) = DexContainsMethodReference(dexData, classDescriptor, methodName, signature);
            if (found)
            {
                return (true, $"Found in '{dexEntry.FullName}' ({dexData.Length} bytes).");
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                return (false, $"Inspection failed on '{dexEntry.FullName}': {reason}");
            }
        }

        if (totalDexEntries == 0)
        {
            return (false, "No classes*.dex entries were found in the APK.");
        }

        return (false, $"Method tuple not found in any of the {totalDexEntries} dex entries.");
    }

    private static bool TryParseMethodReference(string methodReference, out string classDescriptor, out string methodName, out string signature)
    {
        classDescriptor = string.Empty;
        methodName = string.Empty;
        signature = string.Empty;

        if (string.IsNullOrWhiteSpace(methodReference))
        {
            return false;
        }

        var match = Regex.Match(methodReference, "^(L[^;]+;)->([^(]+)(\\(.*\\).+)$");
        if (!match.Success)
        {
            return false;
        }

        classDescriptor = match.Groups[1].Value;
        methodName = match.Groups[2].Value;
        signature = match.Groups[3].Value;

        return true;
    }

    private static (bool Found, string? Error) DexContainsMethodReference(byte[] dexData, string classDescriptor, string methodName, string signature)
    {
        if (dexData.Length < 0x70)
        {
            return (false, $"DEX payload too small ({dexData.Length} bytes).");
        }

        using var stream = new MemoryStream(dexData, writable: false);
        using var reader = new BinaryReader(stream);

        var stringIdsSize = ReadUInt32At(reader, 0x38);
        var stringIdsOff = ReadUInt32At(reader, 0x3C);
        var typeIdsSize = ReadUInt32At(reader, 0x40);
        var typeIdsOff = ReadUInt32At(reader, 0x44);
        var protoIdsSize = ReadUInt32At(reader, 0x4C);
        var protoIdsOff = ReadUInt32At(reader, 0x50);
        var methodIdsSize = ReadUInt32At(reader, 0x58);
        var methodIdsOff = ReadUInt32At(reader, 0x5C);

        if (!TryReadStringTable(dexData, reader, stringIdsSize, stringIdsOff, out var strings) ||
            !TryReadTypeTable(dexData, reader, typeIdsSize, typeIdsOff, strings, out var types) ||
            !TryReadProtoTable(dexData, reader, protoIdsSize, protoIdsOff, types, out var protos) ||
            !IsInBounds(dexData, methodIdsOff, methodIdsSize, 8))
        {
            return (false, "DEX header tables are out of bounds or malformed.");
        }

        for (var i = 0; i < methodIdsSize; i++)
        {
            var methodIdOff = (int)(methodIdsOff + (uint)(i * 8));
            var classIdx = ReadUInt16At(reader, methodIdOff);
            var protoIdx = ReadUInt16At(reader, methodIdOff + 2);
            var nameIdx = ReadUInt32At(reader, methodIdOff + 4);

            if (classIdx >= types.Length || protoIdx >= protos.Length || nameIdx >= strings.Length)
            {
                continue;
            }

            if (!string.Equals(types[classIdx], classDescriptor, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(strings[nameIdx], methodName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(protos[protoIdx], signature, StringComparison.Ordinal))
            {
                return (true, null);
            }
        }

        return (false, null);
    }

    private static bool TryReadStringTable(byte[] dexData, BinaryReader reader, uint stringIdsSize, uint stringIdsOff, out string[] strings)
    {
        strings = Array.Empty<string>();
        if (!IsInBounds(dexData, stringIdsOff, stringIdsSize, 4))
        {
            return false;
        }

        strings = new string[stringIdsSize];
        for (var i = 0; i < stringIdsSize; i++)
        {
            var stringIdOff = (int)(stringIdsOff + (uint)(i * 4));
            var stringDataOff = ReadUInt32At(reader, stringIdOff);
            if (!TryReadDexString(dexData, stringDataOff, out strings[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadTypeTable(byte[] dexData, BinaryReader reader, uint typeIdsSize, uint typeIdsOff, string[] strings, out string[] types)
    {
        types = Array.Empty<string>();
        if (!IsInBounds(dexData, typeIdsOff, typeIdsSize, 4))
        {
            return false;
        }

        types = new string[typeIdsSize];
        for (var i = 0; i < typeIdsSize; i++)
        {
            var typeIdOff = (int)(typeIdsOff + (uint)(i * 4));
            var descriptorIdx = ReadUInt32At(reader, typeIdOff);
            if (descriptorIdx >= strings.Length)
            {
                return false;
            }

            types[i] = strings[descriptorIdx];
        }

        return true;
    }

    private static bool TryReadProtoTable(byte[] dexData, BinaryReader reader, uint protoIdsSize, uint protoIdsOff, string[] types, out string[] protos)
    {
        protos = Array.Empty<string>();
        if (!IsInBounds(dexData, protoIdsOff, protoIdsSize, 12))
        {
            return false;
        }

        protos = new string[protoIdsSize];
        for (var i = 0; i < protoIdsSize; i++)
        {
            var protoOff = (int)(protoIdsOff + (uint)(i * 12));
            var returnTypeIdx = ReadUInt32At(reader, protoOff + 4);
            var parametersOff = ReadUInt32At(reader, protoOff + 8);
            if (returnTypeIdx >= types.Length)
            {
                return false;
            }

            if (!TryReadTypeList(dexData, reader, parametersOff, types, out var parameterTypes))
            {
                return false;
            }

            protos[i] = $"({string.Concat(parameterTypes)}){types[returnTypeIdx]}";
        }

        return true;
    }

    private static bool TryReadTypeList(byte[] dexData, BinaryReader reader, uint typeListOff, string[] types, out string[] parameterTypes)
    {
        parameterTypes = Array.Empty<string>();
        if (typeListOff == 0)
        {
            return true;
        }

        if (!IsInBounds(dexData, typeListOff, 1, 4))
        {
            return false;
        }

        var size = ReadUInt32At(reader, (int)typeListOff);
        if (!IsInBounds(dexData, typeListOff + 4, size, 2))
        {
            return false;
        }

        parameterTypes = new string[size];
        for (var i = 0; i < size; i++)
        {
            var typeIdx = ReadUInt16At(reader, (int)(typeListOff + 4 + (i * 2)));
            if (typeIdx >= types.Length)
            {
                return false;
            }

            parameterTypes[i] = types[typeIdx];
        }

        return true;
    }

    private static bool TryReadDexString(byte[] dexData, uint stringDataOff, out string value)
    {
        value = string.Empty;
        if (stringDataOff >= dexData.Length)
        {
            return false;
        }

        var index = (int)stringDataOff;
        if (!TryReadUleb128(dexData, ref index, out _))
        {
            return false;
        }

        var start = index;
        while (index < dexData.Length && dexData[index] != 0)
        {
            index++;
        }

        if (index >= dexData.Length)
        {
            return false;
        }

        value = System.Text.Encoding.UTF8.GetString(dexData, start, index - start);
        return true;
    }

    private static bool TryReadUleb128(byte[] data, ref int index, out uint value)
    {
        value = 0;
        var shift = 0;
        while (shift < 35)
        {
            if (index >= data.Length)
            {
                return false;
            }

            var b = data[index++];
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    private static uint ReadUInt32At(BinaryReader reader, int offset)
    {
        reader.BaseStream.Position = offset;
        return reader.ReadUInt32();
    }

    private static ushort ReadUInt16At(BinaryReader reader, int offset)
    {
        reader.BaseStream.Position = offset;
        return reader.ReadUInt16();
    }

    private static bool IsInBounds(byte[] data, uint offset, uint count, uint elementSize)
    {
        var length = (ulong)count * elementSize;
        return (ulong)offset + length <= (ulong)data.Length;
    }
}
