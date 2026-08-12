using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace DominionWars.Engine.Localization
{

public sealed class Localization
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _entries;

    public Localization()
    {
        var parsed = ParseResources(Resources.Json);
        var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var entry in parsed)
        {
            entries[entry.Key] = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(entry.Value, StringComparer.OrdinalIgnoreCase));
        }

        _entries = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(entries);
    }

    public string Get(string key, string lang = "en")
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A localization key is required.", nameof(key));
        }

        return TryGet(key, lang, out var value) ? value : key;
    }

    public string GetOrThrow(string key, string lang = "en")
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A localization key is required.", nameof(key));
        }

        if (!TryGet(key, lang, out var value))
        {
            throw new KeyNotFoundException($"Localization key '{key}' is missing.");
        }

        return value;
    }

    private bool TryGet(string key, string lang, out string value)
    {
        value = string.Empty;
        if (!_entries.TryGetValue(key, out var translations))
        {
            return false;
        }

        var requested = string.IsNullOrWhiteSpace(lang) ? "en" : lang;
        if (translations.TryGetValue(requested, out value))
        {
            return true;
        }

        return !string.Equals(requested, "en", StringComparison.OrdinalIgnoreCase)
            && translations.TryGetValue("en", out value);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseResources(string json)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        const string pattern =
            @"""(?<key>[^""]+)""\s*:\s*\{\s*""en""\s*:\s*""(?<en>[^""]*)""\s*,\s*""zh""\s*:\s*""(?<zh>[^""]*)""\s*,\s*""jp""\s*:\s*""(?<jp>[^""]*)""\s*\}";
        foreach (Match match in Regex.Matches(json, pattern, RegexOptions.CultureInvariant))
        {
            result[match.Groups["key"].Value] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = match.Groups["en"].Value,
                ["zh"] = match.Groups["zh"].Value,
                ["jp"] = match.Groups["jp"].Value,
            };
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("Embedded localization resources are empty or malformed.");
        }

        return result;
    }
}
}
