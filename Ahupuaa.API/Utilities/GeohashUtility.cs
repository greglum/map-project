using System;
using System.Collections.Generic;

namespace Ahupuaa.API.Utilities;

/// <summary>
/// Utility class for geohash operations
/// </summary>
public static class GeohashUtility
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";
    
    /// <summary>
    /// Gets a list of geohash prefixes that cover a bounding box.
    /// This implements a simplified algorithm that returns the geohash prefixes
    /// that cover the four corners of the bounding box.
    /// </summary>
    public static List<string> GetPrefixesInBoundingBox(
        decimal swLat, decimal swLng, 
        decimal neLat, decimal neLng,
        int precision = 3)
    {
        // We need to cover the four corners of the bounding box
        var hashSW = EncodeGeohash(swLat, swLng, precision);
        var hashNE = EncodeGeohash(neLat, neLng, precision);
        var hashNW = EncodeGeohash(neLat, swLng, precision);
        var hashSE = EncodeGeohash(swLat, neLng, precision);
        
        // Use a HashSet to avoid duplicates
        var prefixes = new HashSet<string> { hashSW, hashNE, hashNW, hashSE };
        
        // Add center point for more accurate coverage
        var centerLat = (swLat + neLat) / 2;
        var centerLng = (swLng + neLng) / 2;
        prefixes.Add(EncodeGeohash(centerLat, centerLng, precision));
        
        // Add mid-points of each edge for better coverage
        var midNorthLat = neLat;
        var midNorthLng = (swLng + neLng) / 2;
        prefixes.Add(EncodeGeohash(midNorthLat, midNorthLng, precision));
        
        var midSouthLat = swLat;
        var midSouthLng = (swLng + neLng) / 2;
        prefixes.Add(EncodeGeohash(midSouthLat, midSouthLng, precision));
        
        var midEastLat = (swLat + neLat) / 2;
        var midEastLng = neLng;
        prefixes.Add(EncodeGeohash(midEastLat, midEastLng, precision));
        
        var midWestLat = (swLat + neLat) / 2;
        var midWestLng = swLng;
        prefixes.Add(EncodeGeohash(midWestLat, midWestLng, precision));
        
        return new List<string>(prefixes);
    }
    
    /// <summary>
    /// Encodes a coordinate to a geohash with the specified precision
    /// </summary>
    public static string EncodeGeohash(decimal lat, decimal lng, int precision = 12)
    {
        // Convert to double for the calculation
        double latitude = (double)lat;
        double longitude = (double)lng;
        
        bool isEven = true;
        int bit = 0;
        int ch = 0;
        string geohash = "";
        
        double[] latRange = new double[2] { -90.0, 90.0 };
        double[] lngRange = new double[2] { -180.0, 180.0 };
        
        while (geohash.Length < precision)
        {
            double mid;
            
            if (isEven)
            {
                mid = (lngRange[0] + lngRange[1]) / 2;
                if (longitude > mid)
                {
                    ch |= (1 << (4 - bit));
                    lngRange[0] = mid;
                }
                else
                {
                    lngRange[1] = mid;
                }
            }
            else
            {
                mid = (latRange[0] + latRange[1]) / 2;
                if (latitude > mid)
                {
                    ch |= (1 << (4 - bit));
                    latRange[0] = mid;
                }
                else
                {
                    latRange[1] = mid;
                }
            }
            
            isEven = !isEven;
            
            if (bit < 4)
            {
                bit++;
            }
            else
            {
                geohash += Base32[ch];
                bit = 0;
                ch = 0;
            }
        }
        
        return geohash;
    }
    
    /// <summary>
    /// Decodes a geohash to a latitude/longitude point
    /// </summary>
    public static (decimal Lat, decimal Lng) DecodeGeohash(string geohash)
    {
        double[] latRange = new double[2] { -90.0, 90.0 };
        double[] lngRange = new double[2] { -180.0, 180.0 };
        bool isEven = true;
        
        foreach (char c in geohash)
        {
            int cd = Base32.IndexOf(c);
            
            for (int i = 0; i < 5; i++)
            {
                int mask = 1 << (4 - i);
                
                if (isEven)
                {
                    if ((cd & mask) != 0)
                    {
                        lngRange[0] = (lngRange[0] + lngRange[1]) / 2;
                    }
                    else
                    {
                        lngRange[1] = (lngRange[0] + lngRange[1]) / 2;
                    }
                }
                else
                {
                    if ((cd & mask) != 0)
                    {
                        latRange[0] = (latRange[0] + latRange[1]) / 2;
                    }
                    else
                    {
                        latRange[1] = (latRange[0] + latRange[1]) / 2;
                    }
                }
                
                isEven = !isEven;
            }
        }
        
        double lat = (latRange[0] + latRange[1]) / 2;
        double lng = (lngRange[0] + lngRange[1]) / 2;
        
        return ((decimal)lat, (decimal)lng);
    }
}