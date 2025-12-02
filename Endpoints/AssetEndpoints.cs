using MehguViewer.Core.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace MehguViewer.Core.Backend.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        // Note: Auth is handled via Query String token in Program.cs middleware
        var group = app.MapGroup("/api/v1/assets").RequireAuthorization("MvnRead");

        group.MapGet("/{assetUrn}", GetAsset);
    }

    private static async Task<IResult> GetAsset(
        string assetUrn, 
        [FromQuery] string? variant, 
        IHttpClientFactory httpClientFactory,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(assetUrn)) 
            return Results.BadRequest("Asset URN is required.");

        // In a real implementation, we would:
        // 1. Validate the URN
        // 2. Check if the user has access to the series this asset belongs to
        // 3. Fetch the blob from storage (S3, local disk, etc.)
        // 4. Resize if variant is requested
        
        // For this prototype, we'll proxy a placeholder image to simulate "Proxy Mode"
        
        string size = "800x1200"; // Default WEB
        if (variant == "THUMBNAIL") size = "400x600";
        if (variant == "RAW") size = "1600x2400";

        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"https://placehold.co/{size}/png?text={assetUrn}");
        
        if (!response.IsSuccessStatusCode)
        {
            return Results.NotFound();
        }

        var stream = await response.Content.ReadAsStreamAsync();
        return Results.Stream(stream, "image/png");
    }
}
