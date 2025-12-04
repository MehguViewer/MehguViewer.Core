using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

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
}
