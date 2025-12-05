using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Extension methods for creating RFC 7807 Problem Details responses.
/// All API errors should use these methods to ensure consistent error format.
/// </summary>
public static class ResultsExtensions
{
    public static IResult NotFound(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:not-found", "Not Found", 404, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 404, 
            contentType: "application/problem+json"
        );
    }

    public static IResult BadRequest(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:bad-request", "Bad Request", 400, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 400, 
            contentType: "application/problem+json"
        );
    }

    public static IResult Unauthorized(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:unauthorized", "Unauthorized", 401, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 401, 
            contentType: "application/problem+json"
        );
    }

    public static IResult Forbidden(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:forbidden", "Forbidden", 403, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 403, 
            contentType: "application/problem+json"
        );
    }

    public static IResult Conflict(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:conflict", "Conflict", 409, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 409, 
            contentType: "application/problem+json"
        );
    }

    public static IResult InternalServerError(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:internal-server-error", "Internal Server Error", 500, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 500, 
            contentType: "application/problem+json"
        );
    }

    public static IResult ValidationError(string detail, string instance)
    {
        return Results.Json(
            new Problem("urn:mvn:error:validation", "Validation Error", 422, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: 422, 
            contentType: "application/problem+json"
        );
    }
}
