using MehguViewer.Core.Backend.Services;
using MehguViewer.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/debug/seed", SeedDebugData);
    }

    private static async Task<IResult> SeedDebugData([FromServices] IRepository repo)
    {
        await Task.CompletedTask;
        repo.SeedDebugData();
        return Results.Ok(new DebugResponse("Debug data seeded."));
    }
}
