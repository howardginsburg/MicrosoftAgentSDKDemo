using System.Text.Json;

namespace MicrosoftAgentSDKDemo.Storage;

/// <summary>
/// Helper class for working with Cosmos DB documents from CosmosDbPartitionedStorage.
/// Handles the common pattern of unwrapping nested document structures.
/// </summary>
public static class CosmosDbDocumentHelper
{
    /// <summary>
    /// Converts a storage value (Dictionary or JsonElement) to a JsonElement.
    /// Returns null if the value cannot be converted.
    /// </summary>
    public static JsonElement? ToJsonElement(object? value)
    {
        if (value is JsonElement jsonElement)
        {
            return jsonElement;
        }
        
        if (value is Dictionary<string, object> dict)
        {
            return JsonSerializer.SerializeToElement(dict);
        }
        
        return null;
    }

    /// <summary>
    /// Unwraps the nested "document" property if present.
    /// CosmosDbPartitionedStorage wraps all documents in this structure:
    /// { "id": "...", "realId": "...", "document": { /* actual data */ }, "partitionKey": "..." }
    /// </summary>
    public static JsonElement UnwrapDocument(JsonElement docElement)
    {
        if (docElement.ValueKind == JsonValueKind.Object && 
            docElement.TryGetProperty("document", out var nestedDoc))
        {
            return nestedDoc;
        }
        
        return docElement;
    }

    /// <summary>
    /// Converts a storage value to JsonElement and unwraps any nested document structure.
    /// Returns null if the value cannot be converted.
    /// </summary>
    public static JsonElement? ToUnwrappedJsonElement(object? value)
    {
        var element = ToJsonElement(value);
        if (element == null)
        {
            return null;
        }
        
        return UnwrapDocument(element.Value);
    }
}
