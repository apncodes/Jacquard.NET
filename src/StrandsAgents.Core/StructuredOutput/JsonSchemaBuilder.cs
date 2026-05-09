using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace StrandsAgents.Core;

/// <summary>
/// Generates a JSON schema string for a given CLR type using
/// <see cref="JsonSchemaExporter"/> (available in .NET 9+).
/// </summary>
internal static class JsonSchemaBuilder
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Returns a compact JSON schema string for <typeparamref name="T"/>.</summary>
    internal static string GetSchema<T>()
    {
        var schema = _options.GetJsonSchemaAsNode(typeof(T));
        return schema.ToJsonString();
    }
}
