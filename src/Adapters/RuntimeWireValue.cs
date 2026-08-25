using System;
using System.Collections;
using System.Globalization;

namespace DominionWars.Adapters
{

/// <summary>Normalizes JSON-parser-specific object values at the 1.31 boundary.</summary>
internal static class RuntimeWireValue
{
    public static bool TryGetInt64(object value, out long number)
    {
        number = 0;
        if (value is string text)
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        if (TryGetJsonElement(value, out var elementType))
        {
            var kind = elementType.GetProperty("ValueKind").GetValue(value)?.ToString();
            if (kind == "Number") return InvokeInt64(value, elementType, "GetInt64", out number);
            if (kind == "String")
            {
                var textValue = elementType.GetMethod("GetString", Type.EmptyTypes)?.Invoke(value, null) as string;
                return long.TryParse(textValue, NumberStyles.None, CultureInfo.InvariantCulture, out number);
            }
            return false;
        }
        try
        {
            number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch { return false; }
    }

    public static bool TryGetString(object value, out string? text)
    {
        text = value as string;
        if (text is not null) return true;
        if (!TryGetJsonElement(value, out var elementType)) return false;
        var kind = elementType.GetProperty("ValueKind").GetValue(value)?.ToString();
        if (kind != "String") return false;
        text = elementType.GetMethod("GetString", Type.EmptyTypes)?.Invoke(value, null) as string;
        return text is not null;
    }

    public static bool TryEnumerate(object value, out IEnumerable values)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            values = enumerable;
            return true;
        }
        if (TryGetJsonElement(value, out var elementType) &&
            string.Equals(elementType.GetProperty("ValueKind").GetValue(value)?.ToString(), "Array", StringComparison.Ordinal))
        {
            values = (IEnumerable)elementType.GetMethod("EnumerateArray", Type.EmptyTypes)!.Invoke(value, null)!;
            return true;
        }
        values = Array.Empty<object>();
        return false;
    }

    public static object? Normalize(object? value)
    {
        if (value is null) return null;
        if (TryGetInt64(value, out var number)) return number;
        if (TryGetString(value, out var text)) return text;
        return value;
    }

    private static bool TryGetJsonElement(object value, out Type elementType)
    {
        elementType = value.GetType();
        return string.Equals(elementType.FullName, "System.Text.Json.JsonElement", StringComparison.Ordinal);
    }

    private static bool InvokeInt64(object value, Type type, string methodName, out long number)
    {
        number = 0;
        try
        {
            number = (long)type.GetMethod(methodName, Type.EmptyTypes)!.Invoke(value, null)!;
            return true;
        }
        catch { return false; }
    }
}
}
