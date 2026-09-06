using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DominionWars.Adapters
{

/// <summary>
/// The single JSON boundary for v1.31 runtime DTOs. Process-internal DTO
/// property names remain PascalCase; the wire contract is lowerCamelCase.
/// </summary>
public static class RuntimeWireSerializer
{
    private static readonly JsonSerializerSettings WireSettings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Error,
        NullValueHandling = NullValueHandling.Include,
    };

    public static string Serialize<T>(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return JsonConvert.SerializeObject(value, WireSettings);
    }

    public static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON is required.", nameof(json));
        }

        return JsonConvert.DeserializeObject<T>(json, WireSettings)
            ?? throw new JsonSerializationException("The JSON payload was null.");
    }
}
}
