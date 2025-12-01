using MehguViewer.Core.Backend.Models;

namespace MehguViewer.Core.Backend.Services;

public static class ResultsExtensions
{
    public static IResult Problem(string type, string title, int status, string detail, string instance)
    {
        return Results.Json(
            new Problem(type, title, status, detail, instance), 
            AppJsonSerializerContext.Default.Problem,
            statusCode: status, 
            contentType: "application/problem+json"
        );
    }

    public static IResult NotFound(string detail, string instance)
    {
        return Problem("urn:mvn:error:not-found", "Not Found", 404, detail, instance);
    }

    public static IResult Unauthorized(string detail, string instance)
    {
        return Problem("urn:mvn:error:unauthorized", "Unauthorized", 401, detail, instance);
    }
    
    public static IResult Forbidden(string detail, string instance)
    {
        return Problem("urn:mvn:error:forbidden", "Forbidden", 403, detail, instance);
    }
}
