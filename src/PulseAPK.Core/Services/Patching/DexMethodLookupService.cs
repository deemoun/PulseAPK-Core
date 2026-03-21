using AlphaOmega.Debug;
using AlphaOmega.Debug.Dex;
using System.Reflection;
using PulseAPK.Core.Abstractions.Patching;

namespace PulseAPK.Core.Services.Patching;

public sealed class DexMethodLookupService : IDexMethodLookupService
{
    public bool ContainsMethodReference(byte[] dexData, string classDescriptor, string methodName, string signature)
    {
        ArgumentNullException.ThrowIfNull(dexData);

        using var stream = new MemoryStream(dexData, writable: false);
        using var streamLoader = new StreamLoader(stream);
        using var dexFile = new DexFile(streamLoader);

        var methods = GetObjectArray(dexFile, "MethodIdItems");
        if (methods is null || methods.Length == 0)
        {
            return false;
        }

        var strings = GetStringTable(dexFile);
        var types = GetTypeTable(dexFile, strings);
        var protos = GetProtoTable(dexFile, types);

        foreach (var method in methods)
        {
            var classIndex = GetIntMember(method, "ClassTypeIndex", "ClassIndex", "ClassIdx");
            var protoIndex = GetIntMember(method, "ProtoIndex", "ProtoIdx");
            var nameIndex = GetIntMember(method, "NameStringIndex", "NameIndex", "NameIdx");

            if (classIndex < 0 || protoIndex < 0 || nameIndex < 0 ||
                classIndex >= types.Length || protoIndex >= protos.Length || nameIndex >= strings.Length)
            {
                continue;
            }

            if (!string.Equals(types[classIndex], classDescriptor, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(strings[nameIndex], methodName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(protos[protoIndex], signature, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] GetStringTable(object dexFile)
    {
        var items = GetObjectArray(dexFile, "StringIdItems")
            ?? throw new InvalidOperationException("DexFile.StringIdItems is missing.");

        var strings = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            strings[i] = GetStringMember(items[i], "StringData", "Value", "Data", "Text") ?? string.Empty;
        }

        return strings;
    }

    private static string[] GetTypeTable(object dexFile, string[] strings)
    {
        var items = GetObjectArray(dexFile, "TypeIdItems")
            ?? throw new InvalidOperationException("DexFile.TypeIdItems is missing.");

        var types = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var descriptorIndex = GetIntMember(items[i], "DescriptorStringIndex", "DescriptorIndex", "DescriptorIdx", "StringIndex");
            if (descriptorIndex < 0 || descriptorIndex >= strings.Length)
            {
                throw new InvalidDataException($"Type descriptor index {descriptorIndex} is out of bounds.");
            }

            types[i] = strings[descriptorIndex];
        }

        return types;
    }

    private static string[] GetProtoTable(object dexFile, string[] types)
    {
        var items = GetObjectArray(dexFile, "ProtoIdItems")
            ?? throw new InvalidOperationException("DexFile.ProtoIdItems is missing.");

        var protos = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var returnTypeIndex = GetIntMember(items[i], "ReturnTypeIndex", "ReturnTypeIdx");
            if (returnTypeIndex < 0 || returnTypeIndex >= types.Length)
            {
                throw new InvalidDataException($"Proto return type index {returnTypeIndex} is out of bounds.");
            }

            var parameterTypeIndexes = GetIntEnumerableMember(items[i], "ParameterTypeIndexes", "ParametersTypeIndexes", "ParameterTypeIndices", "Parameters");
            var parameterDescriptors = parameterTypeIndexes.Select(index =>
            {
                if (index < 0 || index >= types.Length)
                {
                    throw new InvalidDataException($"Proto parameter type index {index} is out of bounds.");
                }

                return types[index];
            });

            protos[i] = $"({string.Concat(parameterDescriptors)}){types[returnTypeIndex]}";
        }

        return protos;
    }

    private static object[]? GetObjectArray(object source, string memberName)
    {
        var value = GetMemberValue(source, memberName);
        return value switch
        {
            null => null,
            object[] array => array,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToArray(),
            _ => throw new InvalidDataException($"Member '{memberName}' is not enumerable.")
        };
    }

    private static int[] GetIntEnumerableMember(object source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = GetMemberValue(source, candidate);
            if (value is null)
            {
                continue;
            }

            if (value is int[] ints)
            {
                return ints;
            }

            if (value is ushort[] ushorts)
            {
                return ushorts.Select(x => (int)x).ToArray();
            }

            if (value is IEnumerable<int> enumerableInts)
            {
                return enumerableInts.ToArray();
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Select(Convert.ToInt32).ToArray();
            }
        }

        return [];
    }

    private static int GetIntMember(object source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = GetMemberValue(source, candidate);
            if (value is null)
            {
                continue;
            }

            return Convert.ToInt32(value);
        }

        throw new InvalidDataException($"Unable to resolve any of [{string.Join(", ", candidates)}] on {source.GetType().FullName}.");
    }

    private static string? GetStringMember(object source, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = GetMemberValue(source, candidate);
            if (value is string stringValue)
            {
                return stringValue;
            }
        }

        return null;
    }

    private static object? GetMemberValue(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var type = source.GetType();
        var property = type.GetProperty(name, flags);
        if (property is not null)
        {
            return property.GetValue(source);
        }

        var field = type.GetField(name, flags);
        if (field is not null)
        {
            return field.GetValue(source);
        }

        return null;
    }
}
