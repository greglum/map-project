// Use alias for AWS AttributeValue to avoid conflict with your model
using AWSAttributeValue = Amazon.DynamoDBv2.Model.AttributeValue;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Caching.Memory;
using Ahupuaa.API.Models;
using Ahupuaa.API.Utilities;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ahupuaa.API.Services;

public interface IAhupuaaService
{
    Task<List<AhupuaaListItem>> GetAllAhupuaaAsync(string? mokupuniName = null, string? mokuName = null);
    Task<AhupuaaItem?> GetAhupuaaByIdAsync(string id);
    Task<List<string?>?> GetIslandsAsync();
    Task<List<string?>?> GetDistrictsByIslandAsync(string islandName);
    Task<GeospatialQueryResponse> GetAhupuaaByBoundingBoxAsync(GeospatialQueryRequest request, string? paginationToken = null);
    Task<GeospatialQueryResponse> GetAhupuaaByZoomLevelAsync(GeospatialQueryRequest request, string? paginationToken = null);
    Task<List<AhupuaaMapResponse>> GetMapResponseByBoundingBoxAsync(GeospatialQueryRequest request);
}

public class AhupuaaService(
    IDynamoDBContext dynamoDbContext,
    IAmazonDynamoDB dynamoDbClient,
    IMemoryCache cache,
    ILogger<AhupuaaService> logger)
    : IAhupuaaService
{


    private const string TableName = "AhupuaaGIS";

    /// <summary>
    /// Gets a list of all ahupuaa with their associated moku and mokupuni
    /// </summary>
    /// <param name="mokupuniName">Optional filter by island name</param>
    /// <param name="mokuName">Optional filter by district name</param>
    /// <returns>List of ahupuaa with their hierarchical details</returns>
    public async Task<List<AhupuaaListItem>> GetAllAhupuaaAsync(string? mokupuniName = null, string? mokuName = null)
    {
        var cacheKey = $"ahupuaa_list_{mokupuniName ?? "all"}_{mokuName ?? "all"}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SetAbsoluteExpiration(GetCacheExpiration(TimeSpan.FromHours(6)));

            var scanRequest = new ScanRequest
            {
                TableName = TableName,
                ProjectionExpression = "AhupuaaPK, AhupuaaName, MokupuniName, MokuName, Centroid",
                Select = Select.SPECIFIC_ATTRIBUTES,
                ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
            };

            // Add filter if mokupuni or moku specified
            if (!string.IsNullOrEmpty(mokupuniName) || !string.IsNullOrEmpty(mokuName))
            {
                var filterExpressions = new List<string>();
                var expressionAttributeValues = new Dictionary<string, AWSAttributeValue>();

                if (!string.IsNullOrEmpty(mokupuniName))
                {
                    filterExpressions.Add("MokupuniName = :mokupuni");
                    expressionAttributeValues.Add(":mokupuni", new AWSAttributeValue { S = mokupuniName });
                }

                if (!string.IsNullOrEmpty(mokuName))
                {
                    filterExpressions.Add("MokuName = :moku");
                    expressionAttributeValues.Add(":moku", new AWSAttributeValue { S = mokuName });
                }

                scanRequest.FilterExpression = string.Join(" AND ", filterExpressions);
                scanRequest.ExpressionAttributeValues = expressionAttributeValues;
            }

            logger.LogInformation("Querying all ahupuaa from DynamoDB");
            var stopwatch = Stopwatch.StartNew();

            var results = new List<Dictionary<string, AWSAttributeValue>>();
            Dictionary<string, AWSAttributeValue>? lastEvaluatedKey = null;

            do
            {
                // Set the continuation token if we have one
                if (lastEvaluatedKey != null)
                {
                    scanRequest.ExclusiveStartKey = lastEvaluatedKey;
                }

                var response = await dynamoDbClient.ScanAsync(scanRequest);
                results.AddRange(response.Items);
                lastEvaluatedKey = response.LastEvaluatedKey;

                logger.LogInformation($"Retrieved {response.Items.Count} ahupuaa, consumed capacity: {response.ConsumedCapacity.CapacityUnits}");
            }
            while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

            stopwatch.Stop();
            logger.LogInformation($"Retrieved {results.Count} total ahupuaa in {stopwatch.ElapsedMilliseconds}ms");

            // Convert to AhupuaaListItem objects
            var ahupuaaList = results
                .Select(item => new AhupuaaListItem
                {
                    AhupuaaPK = item.TryGetValue("AhupuaaPK", out var pkValue) ? pkValue.S : null,
                    AhupuaaName = item.TryGetValue("AhupuaaName", out var nameValue) ? nameValue.S : null,
                    MokupuniName = item.TryGetValue("MokupuniName", out var islandValue) ? islandValue.S : null,
                    MokuName = item.TryGetValue("MokuName", out var districtValue) ? districtValue.S : null,
                    Centroid = item.TryGetValue("Centroid", out var centroidValue) && centroidValue.M != null
                        ? new GeoPoint
                        {
                            Lat = decimal.Parse(centroidValue.M["Lat"].N),
                            Lng = decimal.Parse(centroidValue.M["Lng"].N)
                        }
                        : null
                })
                .OrderBy(a => a.MokupuniName)
                .ThenBy(a => a.MokuName)
                .ThenBy(a => a.AhupuaaName)
                .ToList();

            return ahupuaaList;
        });
    }




    /// <summary>
    /// Retrieves a specific Ahupuaa by its ID
    /// </summary>
    public async Task<AhupuaaItem?> GetAhupuaaByIdAsync(string id)
    {
        var ahupuaaPk = KeyFormatHelper.FormatAhupuaaPK(id);

        // Try to get from cache first
        if (cache.TryGetValue(ahupuaaPk, out AhupuaaItem? cachedItem))
        {
            return cachedItem;
        }

        // If not in cache, query DynamoDB
        var item = await dynamoDbContext.LoadAsync<AhupuaaItem>(ahupuaaPk);

        // Cache the result with jitter to prevent cache stampedes
        if (item != null)
        {
            cache.Set(ahupuaaPk, item, GetCacheExpiration(TimeSpan.FromHours(1)));
        }

        return item;
    }

    /// <summary>
    /// Gets a list of all islands (Mokupuni)
    /// </summary>
    public async Task<List<string?>?> GetIslandsAsync()
    {
        return await cache.GetOrCreateAsync("islands", async entry =>
        {
            entry.SetAbsoluteExpiration(GetCacheExpiration(TimeSpan.FromDays(1)));

            var scanRequest = new ScanRequest
            {
                TableName = TableName,
                ProjectionExpression = "MokupuniName",
                Select = Select.SPECIFIC_ATTRIBUTES
            };

            var result = await dynamoDbClient.ScanAsync(scanRequest);

            var islands = result.Items
                .Select(item => item.TryGetValue("MokupuniName", out var value) ? value.S : null)
                .Where(i => !string.IsNullOrEmpty(i))
                .Distinct()
                .OrderBy(i => i)
                .ToList();

            return islands;
        });
    }

    /// <summary>
    /// Gets a list of districts (Moku) for a specific island
    /// </summary>
    public async Task<List<string?>?> GetDistrictsByIslandAsync(string islandName)
    {
        var cacheKey = $"districts_{islandName}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SetAbsoluteExpiration(GetCacheExpiration(TimeSpan.FromDays(1)));

            var queryRequest = new QueryRequest
            {
                TableName = TableName,
                IndexName = "MokupuniIndex",
                KeyConditionExpression = "MokupuniName = :islandName",
                ExpressionAttributeValues = new Dictionary<string, AWSAttributeValue>
                {
                    { ":islandName", new AWSAttributeValue { S = islandName } }
                },
                ProjectionExpression = "MokuName",
                Select = Select.SPECIFIC_ATTRIBUTES
            };

            var result = await dynamoDbClient.QueryAsync(queryRequest);

            var districts = result.Items
                .Select(item => item.TryGetValue("MokuName", out var value) ? value.S : null)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            return districts;
        });
    }

    /// <summary>
    /// Queries Ahupuaa items within a geographic bounding box
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<GeospatialQueryResponse> GetAhupuaaByBoundingBoxAsync(
        GeospatialQueryRequest request,
        string? paginationToken = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Calculate which geohash prefixes cover the bounding box
            if (request.Southwest != null && request.Northeast != null)
            {
                var geohashPrefixes = GeohashUtility.GetPrefixesInBoundingBox(
                    request.Southwest.Lat,
                    request.Southwest.Lng,
                    request.Northeast.Lat,
                    request.Northeast.Lng);

                logger.LogInformation($"Querying with geohash prefixes: {string.Join(", ", geohashPrefixes)}");

                // Convert pagination token if provided
                Dictionary<string, AWSAttributeValue>? exclusiveStartKey = null;
                if (!string.IsNullOrEmpty(paginationToken))
                {
                    exclusiveStartKey = PaginationHelper.GetConvertFromPaginationToken(paginationToken);
                }

                var items = new List<AhupuaaItem>();
                Dictionary<string, AWSAttributeValue>? lastEvaluatedKey = null;
                int totalScannedCount = 0;
                double? totalConsumedCapacity = 0;

                // Need to query each prefix separately
                foreach (var prefix in geohashPrefixes)
                {
                    if (items.Count >= request.Limit)
                    {
                        // We've reached the requested limit, no need to query more
                        break;
                    }

                    // Choose which detail level to return based on request
                    string detailField = GetDetailLevelInfo(request.DetailLevel).fieldName;

                    var queryRequest = new QueryRequest
                    {
                        TableName = TableName,
                        IndexName = "GeoBoundingBoxIndex",
                        KeyConditionExpression = "GeohashPrefix = :prefix",
                        ExpressionAttributeValues = new Dictionary<string, AWSAttributeValue>
                        {
                            { ":prefix", new AWSAttributeValue { S = prefix } }
                        },
                        Limit = request.Limit - items.Count,
                        ExclusiveStartKey = exclusiveStartKey,
                        ProjectionExpression = $"AhupuaaPK, HierarchySK, AhupuaaName, MokupuniName, MokuName, " +
                                               $"Centroid, {detailField}, MinZoom, MaxZoom, StyleProperties, AnnotationPoint",
                        ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
                    };

                    // Add filters for island and district if specified
                    if (!string.IsNullOrEmpty(request.MokupuniName) || !string.IsNullOrEmpty(request.MokuName))
                    {
                        var filterExpressions = new List<string>();

                        if (!string.IsNullOrEmpty(request.MokupuniName))
                        {
                            filterExpressions.Add("MokupuniName = :mokupuni");
                            queryRequest.ExpressionAttributeValues.Add(":mokupuni", new AWSAttributeValue { S = request.MokupuniName });
                        }

                        if (!string.IsNullOrEmpty(request.MokuName))
                        {
                            filterExpressions.Add("MokuName = :moku");
                            queryRequest.ExpressionAttributeValues.Add(":moku", new AWSAttributeValue { S = request.MokuName });
                        }

                        queryRequest.FilterExpression = string.Join(" AND ", filterExpressions);
                    }

                    var queryResult = await dynamoDbClient.QueryAsync(queryRequest);

                    // Add zoom level filtering in memory to leverage the GSI properly
                    var filteredItems = queryResult.Items
                        .Where(item =>
                            item.ContainsKey("MinZoom") &&
                            item.ContainsKey("MaxZoom") &&
                            int.Parse(item["MinZoom"].N) <= request.ZoomLevel &&
                            int.Parse(item["MaxZoom"].N) >= request.ZoomLevel)
                        .ToList();

                    // Add to running totals
                    var ahupuaaItems = filteredItems.Select(item => GetConvertToAhupuaaItem(item, detailField)).ToList();
                    items.AddRange(ahupuaaItems);

                    totalScannedCount += queryResult.ScannedCount ?? 0;
                    totalConsumedCapacity += queryResult.ConsumedCapacity.CapacityUnits!;

                    // Keep track of the last evaluated key from the last query
                    if (queryResult.LastEvaluatedKey is { Count: > 0 })
                    {
                        lastEvaluatedKey = queryResult.LastEvaluatedKey;
                    }
                }

                stopwatch.Stop();

                return new GeospatialQueryResponse
                {
                    Items = items,
                    LastEvaluatedKey = lastEvaluatedKey != null
                        ? PaginationHelper.GetConvertToPaginationToken(lastEvaluatedKey)
                        : null,
                    Count = items.Count,
                    Metadata = new QueryMetadata
                    {
                        ExecutionTime = stopwatch.ElapsedMilliseconds,
                        ScannedCount = totalScannedCount,
                        ConsumedCapacity = totalConsumedCapacity
                    }
                };
            }

            throw new InvalidOperationException("Northeast and Southwest coordinates must be provided");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing bounding box query");
            throw;
        }
    }

    /// <summary>
    /// Retrieves Ahupuaa items based on zoom level
    /// </summary>
    public async Task<GeospatialQueryResponse> GetAhupuaaByZoomLevelAsync(
        GeospatialQueryRequest request,
        string? paginationToken = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert pagination token if provided
            Dictionary<string, AWSAttributeValue>? exclusiveStartKey = null;
            if (!string.IsNullOrEmpty(paginationToken))
            {
                exclusiveStartKey = PaginationHelper.GetConvertFromPaginationToken(paginationToken);
            }

            // Choose which detail level to return based on request
            string detailField = GetDetailLevelInfo(request.DetailLevel).fieldName;

            var queryRequest = new QueryRequest
            {
                TableName = TableName,
                IndexName = "ZoomLevelIndex",
                KeyConditionExpression = "ZoomLevel = :zoomLevel",
                ExpressionAttributeValues = new Dictionary<string, AWSAttributeValue>
                {
                    { ":zoomLevel", new AWSAttributeValue { N = request.ZoomLevel.ToString() } },
                },
                Limit = request.Limit,
                ExclusiveStartKey = exclusiveStartKey,
                ProjectionExpression = $"AhupuaaPK, HierarchySK, AhupuaaName, MokupuniName, MokuName, " +
                                      $"Centroid, {detailField}, StyleProperties, AnnotationPoint",
                ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL
            };

            // Add filters for island and district if specified
            if (!string.IsNullOrEmpty(request.MokupuniName) || !string.IsNullOrEmpty(request.MokuName))
            {
                var filterExpressions = new List<string>();

                if (!string.IsNullOrEmpty(request.MokupuniName))
                {
                    filterExpressions.Add("MokupuniName = :mokupuni");
                    queryRequest.ExpressionAttributeValues.Add(":mokupuni", new AWSAttributeValue { S = request.MokupuniName });
                }

                if (!string.IsNullOrEmpty(request.MokuName))
                {
                    filterExpressions.Add("MokuName = :moku");
                    queryRequest.ExpressionAttributeValues.Add(":moku", new AWSAttributeValue { S = request.MokuName });
                }

                queryRequest.FilterExpression = string.Join(" AND ", filterExpressions);
            }

            var queryResult = await dynamoDbClient.QueryAsync(queryRequest);

            // Convert the items to AhupuaaItem objects
            var items = queryResult.Items
                .Select(item => GetConvertToAhupuaaItem(item, detailField))
                .ToList();

            stopwatch.Stop();

            return new GeospatialQueryResponse
            {
                Items = items,
                LastEvaluatedKey = queryResult.LastEvaluatedKey != null
                    ? PaginationHelper.GetConvertToPaginationToken(queryResult.LastEvaluatedKey)
                    : null,
                Count = items.Count,
                Metadata = new QueryMetadata
                {
                    ExecutionTime = stopwatch.ElapsedMilliseconds,
                    ScannedCount = queryResult.ScannedCount,
                    ConsumedCapacity = queryResult.ConsumedCapacity.CapacityUnits
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing zoom level query");
            throw;
        }
    }

    /// <summary>
    /// Gets optimized map responses for bounding box queries
    /// </summary>
    public async Task<List<AhupuaaMapResponse>> GetMapResponseByBoundingBoxAsync(GeospatialQueryRequest request)
    {
        var result = await GetAhupuaaByBoundingBoxAsync(request);

        return result.Items?.Select(item => new AhupuaaMapResponse
        {
            AhupuaaPK = item.AhupuaaPK,
            AhupuaaName = item.AhupuaaName,
            MokupuniName = item.MokupuniName,
            MokuName = item.MokuName,
            Centroid = item.Centroid,
            Boundaries = GetDetailLevelInfo(request.DetailLevel).accessor(item),
            Style = item.StyleProperties,
            Annotation = item.AnnotationPoint != null
                ? System.Text.Json.JsonSerializer.Deserialize<MapAnnotation>(item.AnnotationPoint)
                : null
        }).ToList() ?? new List<AhupuaaMapResponse>();
    }

    #region Helper Methods

    private (string fieldName, Func<AhupuaaItem, string?> accessor) GetDetailLevelInfo(string? detailLevel)
    {
        return detailLevel?.ToLower() switch
        {
            "low" => ("LowDetailBoundaries", item => item.LowDetailBoundaries),
            "full" => ("HighDetailBoundaries", item => item.HighDetailBoundaries),
            _ => ("SimplifiedBoundaries", item => item.SimplifiedBoundaries) // default is simplified
        };
    }

    private AhupuaaItem GetConvertToAhupuaaItem(Dictionary<string, AWSAttributeValue> itemAttributes, string detailField)
    {
        var item = new AhupuaaItem
        {
            AhupuaaPK = itemAttributes.TryGetValue("AhupuaaPK", out var attribute) ? attribute.S : null,
            HierarchySK = itemAttributes.TryGetValue("HierarchySK", out var itemAttribute) ? itemAttribute.S : null,
            AhupuaaName = itemAttributes.TryGetValue("AhupuaaName", out var attribute1) ? attribute1.S : null,
            MokupuniName = itemAttributes.TryGetValue("MokupuniName", out var itemAttribute1) ? itemAttribute1.S : null,
            MokuName = itemAttributes.TryGetValue("MokuName", out var attribute2) ? attribute2.S : null
        };

        // Add centroid if present
        if (itemAttributes.ContainsKey("Centroid") && itemAttributes["Centroid"].M != null)
        {
            var centroidMap = itemAttributes["Centroid"].M;
            item.Centroid = new GeoPoint
            {
                Lat = decimal.Parse(centroidMap["Lat"].N),
                Lng = decimal.Parse(centroidMap["Lng"].N)
            };
        }

        // Add the requested detail level boundaries more directly
        if (itemAttributes.TryGetValue(detailField, out var boundaryValue))
        {
            switch (detailField)
            {
                case "LowDetailBoundaries": item.LowDetailBoundaries = boundaryValue.S; break;
                case "SimplifiedBoundaries": item.SimplifiedBoundaries = boundaryValue.S; break;
                case "HighDetailBoundaries": item.HighDetailBoundaries = boundaryValue.S; break;
                case "FullGeometry": item.FullGeometry = boundaryValue.S; break;
            }
        }

        // Add StyleProperties if present
        if (itemAttributes.TryGetValue("StyleProperties", out var styleAttribute) && styleAttribute.M != null)
        {
            var styleMap = styleAttribute.M;
            item.StyleProperties = new StyleProperties
            {
                FillColor = styleMap.TryGetValue("FillColor", out var fillColor) ? fillColor.S : "#A3C1AD",
                BorderColor = styleMap.TryGetValue("BorderColor", out var borderColor) ? borderColor.S : "#2A6041",
                BorderWidth = styleMap.TryGetValue("BorderWidth", out var borderWidth) ? int.Parse(borderWidth.N) : 2
            };
        }

        // Add AnnotationPoint if present (simplified)
        if (itemAttributes.TryGetValue("AnnotationPoint", out var annotationPoint))
        {
            item.AnnotationPoint = annotationPoint.S;
        }

        return item;
    }

    /// <summary>
    /// Applies a random jitter to cache expiration to prevent cache stampedes
    /// </summary>
    private TimeSpan GetCacheExpiration(TimeSpan baseTime)
    {
        var jitter = new Random().Next(-10, 10);
        return baseTime.Add(TimeSpan.FromMinutes(jitter));
    }

    #endregion
}