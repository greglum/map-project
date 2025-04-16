using System;

namespace Ahupuaa.API.Utilities;

/// <summary>
/// Helper class for formatting and parsing DynamoDB keys for the Ahupuaa schema
/// </summary>
public static class KeyFormatHelper
{
    /// <summary>
    /// Formats an Ahupuaa ID into the partition key format
    /// </summary>
    public static string FormatAhupuaaPK(string id)
    {
        // Remove any AHUPUAA# prefix if already present
        if (id.StartsWith("AHUPUAA#"))
        {
            return id;
        }
        
        return $"AHUPUAA#{id}";
    }
    
    /// <summary>
    /// Formats the hierarchy sort key from mokupuni and moku names
    /// </summary>
    public static string FormatHierarchySK(string mokupuni, string moku)
    {
        // Clean and standardize names
        mokupuni = (mokupuni ?? "Unknown").Trim();
        moku = (moku ?? "Unknown").Trim();
        
        return $"MOKUPUNI#{mokupuni}#MOKU#{moku}";
    }
    
    /// <summary>
    /// Parses a hierarchy sort key into mokupuni and moku components
    /// </summary>
    public static (string Mokupuni, string Moku) ParseHierarchySK(string sk)
    {
        if (string.IsNullOrEmpty(sk))
        {
            return ("Unknown", "Unknown");
        }
        
        var parts = sk.Split('#');
        
        if (parts.Length < 4)
        {
            return ("Unknown", "Unknown");
        }
        
        return (parts[1], parts[3]);
    }
    
    /// <summary>
    /// Extracts the ID portion from an Ahupuaa partition key
    /// </summary>
    public static string? ExtractIdFromAhupuaaPK(string? pk)
    {
        if (string.IsNullOrEmpty(pk))
        {
            return null;
        }
        
        if (pk.StartsWith("AHUPUAA#"))
        {
            return pk.Substring(8);
        }
        
        return pk;
    }
}