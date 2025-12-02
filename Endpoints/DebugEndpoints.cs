using MehguViewer.Core.Backend.Services;

namespace MehguViewer.Core.Backend.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/debug/seed", SeedDebugData);
    }

    private static async Task<IResult> SeedDebugData(IRepository repo)
    {
        await Task.CompletedTask;
        repo.SeedDebugData();
        return Results.Ok(new { message = "Debug data seeded." });
    }
}
