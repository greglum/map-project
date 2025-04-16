using System.Text.Json.Serialization;
using Amazon.DynamoDBv2.DataModel;

namespace Ahupuaa.API.Models;

/// <summary>
/// Primary model representing an Ahupuaa land division in the Hawaiian land system.
/// Maps to items in the DynamoDB AhupuaaGIS table.
/// </summary>
[DynamoDBTable("AhupuaaGIS")]
public class AhupuaaItem
{
    /// <summary>
    /// Primary partition key in format "AHUPUAA#{id}"
    /// </summary>
    [DynamoDBHashKey("AhupuaaPK")]
    public string? AhupuaaPK { get; set; }

    /// <summary>
    /// Primary sort key in format "MOKUPUNI#{island}#MOKU#{district}"
    /// </summary>
    [DynamoDBRangeKey("HierarchySK")]
    public string? HierarchySK { get; set; }

    /// <summary>
    /// The name of the Ahupuaa (land division)
    /// </summary>
    [DynamoDBProperty("AhupuaaName")]
    public string? AhupuaaName { get; set; }

    /// <summary>
    /// The name of the Mokupuni (island)
    /// </summary>
    [DynamoDBProperty("MokupuniName")]
    public string? MokupuniName { get; set; }

    /// <summary>
    /// The name of the Moku (district)
    /// </summary>
    [DynamoDBProperty("MokuName")]
    public string? MokuName { get; set; }

    /// <summary>
    /// Geohash value for spatial indexing
    /// </summary>
    [DynamoDBProperty("Geohash")]
    public string? Geohash { get; set; }

    /// <summary>
    /// First 3 characters of geohash for bounding box queries
    /// </summary>
    [DynamoDBProperty("GeohashPrefix")]
    public string? GeohashPrefix { get; set; }

    /// <summary>
    /// Appropriate zoom level for map display
    /// </summary>
    [DynamoDBProperty("ZoomLevel")]
    public int ZoomLevel { get; set; }

    /// <summary>
    /// Minimum zoom level where this item should be displayed
    /// </summary>
    [DynamoDBProperty("MinZoom")]
    public int MinZoom { get; set; }

    /// <summary>
    /// Maximum zoom level where this item should be displayed
    /// </summary>
    [DynamoDBProperty("MaxZoom")]
    public int MaxZoom { get; set; }

    /// <summary>
    /// GeoJSON geometry type (e.g., "Polygon")
    /// </summary>
    [DynamoDBProperty("GeometryType")]
    public string? GeometryType { get; set; }

    /// <summary>
    /// Priority for map display (1-10, with 10 being highest)
    /// </summary>
    [DynamoDBProperty("DisplayPriority")]
    public int DisplayPriority { get; set; }

    /// <summary>
    /// Centroid of the geometry for map annotation placement
    /// </summary>
    [DynamoDBProperty("Centroid")]
    public GeoPoint? Centroid { get; set; }

    /// <summary>
    /// Geographic bounds of the feature
    /// </summary>
    [DynamoDBProperty("Bounds")]
    public GeoBounds? Bounds { get; set; }

    /// <summary>
    /// Minimum bounding rectangle as GeoJSON coordinates
    /// </summary>
    [DynamoDBProperty("MBR")]
    public string? MBR { get; set; }

    /// <summary>
    /// Point and associated information for map annotations
    /// </summary>
    [DynamoDBProperty("AnnotationPoint")]
    public string? AnnotationPoint { get; set; }

    /// <summary>
    /// Simplified GeoJSON representation for faster rendering
    /// </summary>
    [DynamoDBProperty("SimplifiedBoundaries")]
    public string? SimplifiedBoundaries { get; set; }

    /// <summary>
    /// Low detail GeoJSON representation for low zoom levels
    /// </summary>
    [DynamoDBProperty("LowDetailBoundaries")]
    public string? LowDetailBoundaries { get; set; }

    /// <summary>
    /// High detail GeoJSON representation for high zoom levels
    /// </summary>
    [DynamoDBProperty("HighDetailBoundaries")]
    public string? HighDetailBoundaries { get; set; }

    /// <summary>
    /// Complete GeoJSON geometry for detailed analysis
    /// </summary>
    [DynamoDBProperty("FullGeometry")]
    public string? FullGeometry { get; set; }

    /// <summary>
    /// Style properties for map rendering
    /// </summary>
    [DynamoDBProperty("StyleProperties")]
    public StyleProperties? StyleProperties { get; set; }

    /// <summary>
    /// iOS-specific rendering hints
    /// </summary>
    [DynamoDBProperty("iOSRenderingHints")]
    public IOSRenderingHints? IOSRenderingHints { get; set; }

    /// <summary>
    /// Original properties from the GeoJSON source
    /// </summary>
    [DynamoDBProperty("Properties")]
    public Dictionary<string, AttributeValue>? Properties { get; set; }

    /// <summary>
    /// Metadata about the item including version and update timestamp
    /// </summary>
    [DynamoDBProperty("Metadata")]
    public ItemMetadata? Metadata { get; set; }
}

/// <summary>
/// Represents a geographic point with latitude and longitude
/// </summary>
public class GeoPoint
{
    [JsonPropertyName("Lat")]
    public decimal Lat { get; set; }

    [JsonPropertyName("Lng")]
    public decimal Lng { get; set; }
}

/// <summary>
/// Represents geographic bounds with northeast and southwest corners
/// </summary>
public class GeoBounds
{
    [JsonPropertyName("Northeast")]
    public GeoPoint? Northeast { get; set; }

    [JsonPropertyName("Southwest")]
    public GeoPoint? Southwest { get; set; }
}

/// <summary>
/// Style properties for map rendering
/// </summary>
public class StyleProperties
{
    [JsonPropertyName("FillColor")]
    public string FillColor { get; set; } = "#A3C1AD";

    [JsonPropertyName("BorderColor")]
    public string BorderColor { get; set; } = "#2A6041";

    [JsonPropertyName("BorderWidth")]
    public int BorderWidth { get; set; } = 2;
}

/// <summary>
/// iOS-specific rendering hints for better display on Apple devices
/// </summary>
public class IOSRenderingHints
{
    [JsonPropertyName("StrokeWidth")]
    public int StrokeWidth { get; set; } = 2;

    [JsonPropertyName("FillOpacity")]
    public decimal FillOpacity { get; set; } = 0.5m;

    [JsonPropertyName("StrokeOpacity")]
    public decimal StrokeOpacity { get; set; } = 0.8m;

    [JsonPropertyName("ZIndex")]
    public int ZIndex { get; set; } = 5;

    [JsonPropertyName("LineDashPattern")]
    public string LineDashPattern { get; set; } = "[0]";

    [JsonPropertyName("SelectedFillColor")]
    public string SelectedFillColor { get; set; } = "#C1E1AD";

    [JsonPropertyName("SelectedStrokeColor")]
    public string SelectedStrokeColor { get; set; } = "#205841";
}

/// <summary>
/// Metadata about the item version and updates
/// </summary>
public class ItemMetadata
{
    [JsonPropertyName("DataVersion")]
    public long DataVersion { get; set; }

    [JsonPropertyName("FeatureHash")]
    public string? FeatureHash { get; set; }

    [JsonPropertyName("LastUpdated")]
    public string? LastUpdated { get; set; }
}

/// <summary>
/// A generic DynamoDB AttributeValue class to handle various types
/// </summary>
public class AttributeValue
{
    [JsonPropertyName("S")]
    public string? StringValue { get; set; }

    [JsonPropertyName("N")]
    public string? NumberValue { get; set; }

    [JsonPropertyName("BOOL")]
    public bool? BoolValue { get; set; }

    [JsonPropertyName("NULL")]
    public bool? NullValue { get; set; }

    [JsonPropertyName("M")]
    public Dictionary<string, AttributeValue>? MapValue { get; set; }

    [JsonPropertyName("L")]
    public List<AttributeValue>? ListValue { get; set; }
}

/// <summary>
/// Parsed version of an annotation point
/// </summary>
public class MapAnnotation
{
    [JsonPropertyName("coordinate")]
    public double[]? Coordinate { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }
}

/// <summary>
/// Request parameters for geographic queries
/// </summary>
public class GeospatialQueryRequest
{
    /// <summary>
    /// Northeast corner of bounding box
    /// </summary>
    public GeoPoint? Northeast { get; set; }

    /// <summary>
    /// Southwest corner of bounding box
    /// </summary>
    public GeoPoint? Southwest { get; set; }

    /// <summary>
    /// Current zoom level of the map
    /// </summary>
    public int ZoomLevel { get; set; }

    /// <summary>
    /// Optional island filter
    /// </summary>
    public string? MokupuniName { get; set; }

    /// <summary>
    /// Optional district filter
    /// </summary>
    public string? MokuName { get; set; }

    /// <summary>
    /// Level of detail to return (full, simplified, low)
    /// </summary>
    public string? DetailLevel { get; set; } = "simplified";

    /// <summary>
    /// Limit of items to return
    /// </summary>
    public int Limit { get; set; } = 50;
}

/// <summary>
/// Response model for geospatial queries
/// </summary>
public class GeospatialQueryResponse
{
    /// <summary>
    /// List of Ahupuaa items matching the query
    /// </summary>
    public List<AhupuaaItem>? Items { get; set; }

    /// <summary>
    /// Token for pagination
    /// </summary>
    public string? LastEvaluatedKey { get; set; }

    /// <summary>
    /// Count of returned items
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Metadata about the query execution
    /// </summary>
    public QueryMetadata? Metadata { get; set; }
}

/// <summary>
/// Metadata about query execution
/// </summary>
public class QueryMetadata
{
    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTime { get; set; }

    /// <summary>
    /// DynamoDB capacity units consumed
    /// </summary>
    public double? ConsumedCapacity { get; set; }

    /// <summary>
    /// Number of items scanned
    /// </summary>
    public int? ScannedCount { get; set; }
}

public class AhupuaaMapResponse
{
    public string? AhupuaaPK { get; set; }
    public string? AhupuaaName { get; set; }
    public string? MokupuniName { get; set; }
    public string? MokuName { get; set; }
    public GeoPoint? Centroid { get; set; }
    public string? Boundaries { get; set; } // Will contain the appropriate detail level
    public StyleProperties? Style { get; set; }
    public MapAnnotation? Annotation { get; set; }
}

public class AhupuaaListItem
{
    [JsonPropertyName("id")]
    public string? AhupuaaPK { get; set; }

    [JsonPropertyName("name")]
    public string? AhupuaaName { get; set; }

    [JsonPropertyName("island")]
    public string? MokupuniName { get; set; }

    [JsonPropertyName("district")]
    public string? MokuName { get; set; }

    [JsonPropertyName("centroid")]
    public GeoPoint? Centroid { get; set; }
}