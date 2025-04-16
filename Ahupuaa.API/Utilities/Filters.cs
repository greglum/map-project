using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Ahupuaa.API.Models;

namespace Ahupuaa.API.Filters
{
    /// <summary>
    /// Validates geospatial query requests to ensure they have required parameters
    /// based on the endpoint being called.
    /// </summary>
    public class GeospatialRequestValidationFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ActionArguments.TryGetValue("request", out var requestObj) && 
                requestObj is GeospatialQueryRequest request)
            {
                if (context.ActionDescriptor.DisplayName?.Contains("BoundingBox") == true)
                {
                    if (request == null || request.Northeast == null || request.Southwest == null)
                    {
                        context.Result = new BadRequestObjectResult("Northeast and Southwest coordinates are required");
                        return;
                    }
                }
                else if (context.ActionDescriptor.DisplayName?.Contains("ZoomLevel") == true)
                {
                    if (request == null || request.ZoomLevel <= 0)
                    {
                        context.Result = new BadRequestObjectResult("A valid zoom level is required");
                        return;
                    }
                }
            }
            base.OnActionExecuting(context);
        }
    }

    /// <summary>
    /// Adds appropriate cache control headers to API responses
    /// </summary>
    public class CacheControlFilter : ActionFilterAttribute
    {
        private readonly int _maxAgeSeconds;
        
        public CacheControlFilter(int maxAgeSeconds = 60)
        {
            _maxAgeSeconds = maxAgeSeconds;
        }
        
        public override void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.HttpContext.Response.Headers.ContainsKey("Cache-Control"))
            {
                return;
            }
            
            context.HttpContext.Response.Headers.Append(
                "Cache-Control", $"public, max-age={_maxAgeSeconds}");
            
            base.OnResultExecuting(context);
        }
    }
    
    /// <summary>
    /// Handles API exceptions and returns appropriate status codes
    /// </summary>
    public class ApiExceptionFilter : ExceptionFilterAttribute
    {
        public override void OnException(ExceptionContext context)
        {
            HandleException(context);
            base.OnException(context);
        }
        
        private void HandleException(ExceptionContext context)
        {
            if (context.Exception is ArgumentException)
            {
                context.Result = new BadRequestObjectResult(context.Exception.Message);
                context.ExceptionHandled = true;
            }
            else if (context.Exception is UnauthorizedAccessException)
            {
                context.Result = new UnauthorizedResult();
                context.ExceptionHandled = true;
            }
            else if (context.Exception is KeyNotFoundException)
            {
                context.Result = new NotFoundResult();
                context.ExceptionHandled = true;
            }
            else
            {
                context.Result = new ObjectResult("An unexpected error occurred")
                {
                    StatusCode = 500
                };
                context.ExceptionHandled = true;
            }
        }
    }
}