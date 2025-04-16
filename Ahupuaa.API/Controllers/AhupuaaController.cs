using Microsoft.AspNetCore.Mvc;
using Ahupuaa.API.Models;
using Ahupuaa.API.Services;
using Ahupuaa.API.Filters;

namespace Ahupuaa.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AhupuaaController : ControllerBase
{
    private readonly IAhupuaaService _ahupuaaService;
    private readonly ILogger<AhupuaaController> _logger;

    public AhupuaaController(
        IAhupuaaService ahupuaaService,
        ILogger<AhupuaaController> logger)
    {
        _ahupuaaService = ahupuaaService;
        _logger = logger;
    }


    /// <summary>
    /// Gets a list of all ahupuaa with their associated details
    /// </summary>
    /// <param name="island">Optional island (mokupuni) name to filter by</param>
    /// <param name="district">Optional district (moku) name to filter by</param>
    /// <returns>List of ahupuaa with their hierarchical details</returns>
    [HttpGet]
    [Route("api/ahupuaa/list")]
    [ProducesResponseType(typeof(List<AhupuaaListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllAhupuaa([FromQuery] string? island = null, [FromQuery] string? district = null)
    {
        try
        {
            var ahupuaaList = await _ahupuaaService.GetAllAhupuaaAsync(island, district);
            return Ok(ahupuaaList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ahupuaa list");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving ahupuaa list");
        }
    }


    /// <summary>
    /// Gets a specific Ahupuaa by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AhupuaaItem), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(string id)
    {
        try
        {
            var item = await _ahupuaaService.GetAhupuaaByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error retrieving Ahupuaa with ID {id}", id);
        }
    }

    /// <summary>
    /// Gets a list of all islands (Mokupuni)
    /// </summary>
    [HttpGet("islands")]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<IActionResult> GetIslands()
    {
        try
        {
            return Ok(await _ahupuaaService.GetIslandsAsync());
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error retrieving islands");
        }
    }

    /// <summary>
    /// Gets districts (Moku) for a specific island
    /// </summary>
    [HttpGet("islands/{islandName}/districts")]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<IActionResult> GetDistrictsByIsland(string islandName)
    {
        try
        {
            return Ok(await _ahupuaaService.GetDistrictsByIslandAsync(islandName));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error retrieving districts for island {islandName}", islandName);
        }
    }

    /// <summary>
    /// Queries Ahupuaa items using a geographic bounding box
    /// </summary>
    [HttpPost("query/boundingbox")]
    [ProducesResponseType(typeof(GeospatialQueryResponse), 200)]
    [GeospatialRequestValidationFilter]
    public async Task<IActionResult> QueryByBoundingBox(
        [FromBody] GeospatialQueryRequest request,
        [FromQuery] string? paginationToken = null)
    {
        try
        {
            return Ok(await _ahupuaaService.GetAhupuaaByBoundingBoxAsync(request, paginationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error executing bounding box query");
        }
    }

    /// <summary>
    /// Queries Ahupuaa items based on zoom level
    /// </summary>
    [HttpPost("query/zoomlevel")]
    [ProducesResponseType(typeof(GeospatialQueryResponse), 200)]
    [GeospatialRequestValidationFilter]
    public async Task<IActionResult> QueryByZoomLevel(
        [FromBody] GeospatialQueryRequest request,
        [FromQuery] string? paginationToken = null)
    {
        try
        {
            return Ok(await _ahupuaaService.GetAhupuaaByZoomLevelAsync(request, paginationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error executing zoom level query");
        }
    }

    /// <summary>
    /// Gets optimized map response data for a geographic bounding box
    /// </summary>
    [HttpPost("map/boundingbox")]
    [ProducesResponseType(typeof(List<AhupuaaMapResponse>), 200)]
    [GeospatialRequestValidationFilter]
    public async Task<IActionResult> GetMapDataByBoundingBox([FromBody] GeospatialQueryRequest request)
    {
        try
        {
            return Ok(await _ahupuaaService.GetMapResponseByBoundingBoxAsync(request));
        }
        catch (Exception ex)
        {
            return HandleException(ex, "Error retrieving map data for bounding box");
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(200)]
    public IActionResult HealthCheck() =>
        Ok(new { status = "healthy", timestamp = DateTime.UtcNow });

    private IActionResult HandleException(Exception ex, string errorMessage, object? additionalData = null)
    {
        _logger.LogError(ex, errorMessage, additionalData);
        return StatusCode(500, "An error occurred while processing your request.");
    }
}