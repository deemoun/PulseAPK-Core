using System.IO.Compression;
using PulseAPK.Core.Services.Patching;

namespace PulseAPK.Tests.Services.Patching;

public sealed class FinalDexInspectionServiceTests
{
    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsTrue_WhenDexContainsExactMethodTuple()
    {
        var apkPath = CreateApkWithDexPayload(CreateDexPayload(
            strings:
            [
                "Lzed/rainxch/githubstore/MainActivity;",
                "V",
                "loadFridaGadget"
            ],
            typeDescriptorStringIndexes: [0, 1],
            protoDefinitions: [new ProtoDefinition(1, [])],
            methodDefinitions: [new MethodDefinition(0, 0, 2)]));

        var service = new FinalDexInspectionService();

        var (found, _) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.True(found);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WhenStringsExistButNotAsSameMethodIdTuple()
    {
        var apkPath = CreateApkWithDexPayload(CreateDexPayload(
            strings:
            [
                "Lzed/rainxch/githubstore/MainActivity;",
                "Lzed/rainxch/githubstore/OtherActivity;",
                "V",
                "I",
                "loadFridaGadget",
                "noop",
                "()V"
            ],
            typeDescriptorStringIndexes: [0, 1, 2, 3],
            protoDefinitions:
            [
                new ProtoDefinition(2, []),
                new ProtoDefinition(3, [])
            ],
            methodDefinitions:
            [
                new MethodDefinition(0, 1, 4),
                new MethodDefinition(1, 0, 5)
            ]));

        var service = new FinalDexInspectionService();

        var (found, _) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;->loadFridaGadget()V");

        Assert.False(found);
    }

    [Fact]
    public async Task ContainsMethodReferenceAsync_ReturnsFalse_WhenSignatureIsInvalid()
    {
        var apkPath = CreateApkWithDexPayload(CreateDexPayload(
            strings:
            [
                "Lzed/rainxch/githubstore/MainActivity;",
                "V",
                "loadFridaGadget"
            ],
            typeDescriptorStringIndexes: [0, 1],
            protoDefinitions: [new ProtoDefinition(1, [])],
            methodDefinitions: [new MethodDefinition(0, 0, 2)]));

        var service = new FinalDexInspectionService();

        var (found, _) = await service.ContainsMethodReferenceAsync(
            apkPath,
            "Lzed/rainxch/githubstore/MainActivity;loadFridaGadget()V");

        Assert.False(found);
    }

    private static string CreateApkWithDexPayload(byte[] dexPayload)
    {
        var apkPath = Path.Combine(Path.GetTempPath(), $"final-dex-inspection-{Guid.NewGuid():N}.apk");
        using var archive = ZipFile.Open(apkPath, ZipArchiveMode.Create);
        var dex = archive.CreateEntry("classes.dex", CompressionLevel.NoCompression);
        using var stream = dex.Open();
        stream.Write(dexPayload, 0, dexPayload.Length);
        return apkPath;
    }

    private static byte[] CreateDexPayload(
        string[] strings,
        int[] typeDescriptorStringIndexes,
        ProtoDefinition[] protoDefinitions,
        MethodDefinition[] methodDefinitions)
    {
        const int headerSize = 0x70;
        var bytes = new List<byte>(new byte[headerSize]);

        var stringIdsOff = bytes.Count;
        bytes.AddRange(new byte[strings.Length * 4]);

        var typeIdsOff = bytes.Count;
        foreach (var stringIndex in typeDescriptorStringIndexes)
        {
            bytes.AddRange(BitConverter.GetBytes((uint)stringIndex));
        }

        var protoIdsOff = bytes.Count;
        bytes.AddRange(new byte[protoDefinitions.Length * 12]);

        var methodIdsOff = bytes.Count;
        foreach (var method in methodDefinitions)
        {
            bytes.AddRange(BitConverter.GetBytes((ushort)method.ClassTypeIndex));
            bytes.AddRange(BitConverter.GetBytes((ushort)method.ProtoIndex));
            bytes.AddRange(BitConverter.GetBytes((uint)method.NameStringIndex));
        }

        var typeListOffsets = new uint[protoDefinitions.Length];
        for (var i = 0; i < protoDefinitions.Length; i++)
        {
            var parameters = protoDefinitions[i].ParameterTypeIndexes;
            if (parameters.Length == 0)
            {
                typeListOffsets[i] = 0;
                continue;
            }

            typeListOffsets[i] = (uint)bytes.Count;
            bytes.AddRange(BitConverter.GetBytes((uint)parameters.Length));
            foreach (var parameter in parameters)
            {
                bytes.AddRange(BitConverter.GetBytes((ushort)parameter));
            }

            if ((bytes.Count & 1) != 0)
            {
                bytes.Add(0);
            }
        }

        var stringDataOffsets = new uint[strings.Length];
        for (var i = 0; i < strings.Length; i++)
        {
            stringDataOffsets[i] = (uint)bytes.Count;
            WriteUleb128(bytes, (uint)strings[i].Length);
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(strings[i]));
            bytes.Add(0);
        }

        WriteUInt32(bytes, 0x38, (uint)strings.Length);
        WriteUInt32(bytes, 0x3C, (uint)stringIdsOff);
        WriteUInt32(bytes, 0x40, (uint)typeDescriptorStringIndexes.Length);
        WriteUInt32(bytes, 0x44, (uint)typeIdsOff);
        WriteUInt32(bytes, 0x4C, (uint)protoDefinitions.Length);
        WriteUInt32(bytes, 0x50, (uint)protoIdsOff);
        WriteUInt32(bytes, 0x58, (uint)methodDefinitions.Length);
        WriteUInt32(bytes, 0x5C, (uint)methodIdsOff);

        for (var i = 0; i < stringDataOffsets.Length; i++)
        {
            WriteUInt32(bytes, stringIdsOff + (i * 4), stringDataOffsets[i]);
        }

        for (var i = 0; i < protoDefinitions.Length; i++)
        {
            var protoBase = protoIdsOff + (i * 12);
            WriteUInt32(bytes, protoBase, 0);
            WriteUInt32(bytes, protoBase + 4, (uint)protoDefinitions[i].ReturnTypeIndex);
            WriteUInt32(bytes, protoBase + 8, typeListOffsets[i]);
        }

        return bytes.ToArray();
    }

    private static void WriteUInt32(List<byte> bytes, int offset, uint value)
    {
        var raw = BitConverter.GetBytes(value);
        bytes[offset] = raw[0];
        bytes[offset + 1] = raw[1];
        bytes[offset + 2] = raw[2];
        bytes[offset + 3] = raw[3];
    }

    private static void WriteUleb128(List<byte> bytes, uint value)
    {
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            bytes.Add(current);
        } while (value != 0);
    }

    private sealed record ProtoDefinition(int ReturnTypeIndex, int[] ParameterTypeIndexes);

    private sealed record MethodDefinition(int ClassTypeIndex, int ProtoIndex, int NameStringIndex);
}
