using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
// Use alias for AWS AttributeValue
using AWSAttributeValue = Amazon.DynamoDBv2.Model.AttributeValue;

namespace Ahupuaa.API.Utilities;

/// <summary>
/// Helper class for handling DynamoDB pagination tokens
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Converts a DynamoDB LastEvaluatedKey to a safe string token for API responses
    /// </summary>
    public static string? GetConvertToPaginationToken(Dictionary<string, AttributeValue>? lastEvaluatedKey)
    {
        if (lastEvaluatedKey == null || lastEvaluatedKey.Count == 0)
        {
            return null;
        }
        
        var tokenObject = new Dictionary<string, object>();
        
        foreach (var kvp in lastEvaluatedKey)
        {
            if (kvp.Value.S != null)
            {
                tokenObject[kvp.Key] = kvp.Value.S;
            }
            else if (kvp.Value.N != null)
            {
                tokenObject[kvp.Key] = kvp.Value.N;
            }
            else if (kvp.Value.B != null)
            {
                tokenObject[kvp.Key] = Convert.ToBase64String(kvp.Value.B.ToArray());
            }
            // Add other types as needed
        }
        
        var json = JsonSerializer.Serialize(tokenObject);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Converts a pagination token back to a DynamoDB LastEvaluatedKey
    /// </summary>
    public static Dictionary<string, AttributeValue>? GetConvertFromPaginationToken(string? paginationToken)
    {
        if (string.IsNullOrEmpty(paginationToken))
        {
            return null;
        }
        
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(paginationToken));
            var tokenObject = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            var lastEvaluatedKey = new Dictionary<string, AttributeValue>();

            if (tokenObject != null)
                foreach (var kvp in tokenObject)
                {
                    switch (kvp.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                            var stringValue = kvp.Value.GetString();

                            // Try to detect if this is actually a base64 encoded binary
                            try
                            {
                                if (stringValue != null)
                                {
                                    var bytes = Convert.FromBase64String(stringValue);
                                    if (bytes.Length > 0 && GetIsLikelyBinary(bytes))
                                    {
                                        lastEvaluatedKey[kvp.Key] = new AttributeValue { B = new MemoryStream(bytes) };
                                        continue;
                                    }
                                }
                            }
                            catch
                            {
                                // Not base64, treat as string
                            }

                            lastEvaluatedKey[kvp.Key] = new AttributeValue { S = stringValue };
                            break;

                        case JsonValueKind.Number:
                            lastEvaluatedKey[kvp.Key] = new AttributeValue { N = kvp.Value.ToString() };
                            break;

                        // Handle other types as needed
                    }
                }

            return lastEvaluatedKey;
        }
        catch
        {
            // If token is invalid, return null
            return null;
        }
    }
    
    /// <summary>
    /// Basic heuristic to detect if byte array is likely binary data rather than text
    /// </summary>
    private static bool GetIsLikelyBinary(byte[] bytes)
    {
        // Check a sample of bytes for non-text values
        int sampleSize = Math.Min(bytes.Length, 100);
        int nonTextCount = 0;
        
        for (int i = 0; i < sampleSize; i++)
        {
            byte b = bytes[i];
            if ((b < 32 || b > 126) && b != 9 && b != 10 && b != 13)
            {
                nonTextCount++;
            }
        }
        
        // If more than 10% of sampled bytes are non-text, consider it binary
        return nonTextCount > sampleSize * 0.1;
    }
}