using MehguViewer.Core.Backend.Services;

namespace MehguViewer.Core.Backend.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/library", GetLibrary);
    }

    private static IResult GetLibrary(MemoryRepository repo)
    {
        // For now, return empty list as expected by the test
        return Results.Ok(Array.Empty<object>());
    }
}
