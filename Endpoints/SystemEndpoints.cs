using MehguViewer.Core.Backend.Models;
using MehguViewer.Core.Backend.Services;

namespace MehguViewer.Core.Backend.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/mehgu-node", GetNodeMetadata);
        app.MapGet("/api/v1/instance", GetInstance);
        app.MapGet("/api/v1/taxonomy", GetTaxonomy);
        app.MapGet("/api/v1/system/config", GetSystemConfig);
        app.MapPut("/api/v1/system/config", UpdateSystemConfig);
    }

    private static IResult GetSystemConfig(MemoryRepository repo)
    {
        return Results.Ok(repo.GetSystemConfig());
    }

    private static IResult UpdateSystemConfig(SystemConfig config, MemoryRepository repo)
    {
        repo.UpdateSystemConfig(config);
        return Results.Ok(config);
    }

    private static IResult GetNodeMetadata()
    {
        return Results.Ok(new NodeMetadata(
            "1.0.0",
            "MehguViewer Core",
            "A MehguViewer Core Node",
            "https://auth.mehgu.example.com",
            new NodeCapabilities(true, true, true),
            new NodeMaintainer("Admin", "admin@example.com")
        ));
    }

    private static IResult GetInstance()
    {
        return Results.Ok(new NodeManifest(
            "urn:mvn:node:example",
            "MehguViewer Core",
            "A MehguViewer Core Node",
            "1.0.0",
            "MehguViewer.Core (NativeAOT)",
            "admin@example.com",
            false,
            new NodeFeatures(true, false),
            null
        ));
    }

    private static IResult GetTaxonomy()
    {
        return Results.Ok(new TaxonomyData(
            new[] { "Action", "Adventure", "Comedy", "Drama", "Fantasy", "Slice of Life" },
            new[] { "Gore", "Sexual Violence", "Nudity" },
            new[] { "Manga", "Manhwa", "Manhua", "Novel", "OEL" },
            new[] { "Official", "Fan Group A", "Fan Group B" }
        ));
    }
}
