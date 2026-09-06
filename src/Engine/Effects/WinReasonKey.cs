using System;
using System.Globalization;
using System.Text;
using DominionWars.Engine.Model;

namespace DominionWars.Engine.Effects
{

/// <summary>Canonical runtime reason-key grammar and WIN_GAME fallback mapping.</summary>
public static class WinReasonKey
{
    public const string EncodedPrefix = "win.encoded.";

    public static string FromEffect(
        string? param,
        CardInstance? sourceCard,
        CardInstance? playedCard,
        CardInstance? attacker)
    {
        if (string.IsNullOrWhiteSpace(param))
        {
            return "win.special";
        }

        if (IsContractReasonKey(param) && !param.StartsWith(EncodedPrefix, StringComparison.Ordinal))
        {
            return param!;
        }

        // A reserved-prefix raw value must never be confused with an encoded
        // value, even when a source card is available.
        if (param.StartsWith(EncodedPrefix, StringComparison.Ordinal))
        {
            return EncodeRaw(param!);
        }

        var source = sourceCard ?? playedCard ?? attacker;
        var sourceId = source?.Definition.Id;
        var semanticKey = sourceId is null ? null : "win." + sourceId;
        if (IsContractReasonKey(semanticKey)
            && !semanticKey!.StartsWith(EncodedPrefix, StringComparison.Ordinal))
        {
            // Card ids are stable semantic identifiers. Do not copy localized
            // effect text into the runtime contract.
            return semanticKey;
        }

        return EncodeRaw(param!);
    }

    public static bool IsContractReasonKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isLowercaseLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (index == 0)
            {
                if (!isLowercaseLetter && !isDigit)
                {
                    return false;
                }

                continue;
            }

            if (!isLowercaseLetter && !isDigit && character is not '_' and not '.' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string EncodeRaw(string value)
    {
        // UTF-16 code-unit encoding is deterministic and collision-free for
        // .NET strings while remaining inside the ASCII contract grammar.
        var builder = new StringBuilder(EncodedPrefix);
        foreach (var character in value)
        {
            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
}
