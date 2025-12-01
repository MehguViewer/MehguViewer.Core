using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

app.MapGet("/api/v1/instance", () => new NodeManifest(
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

app.Run();

public record NodeManifest(
    string urn,
    string name,
    string description,
    string version,
    string software,
    string maintainer,
    bool registration_open,
    NodeFeatures features,
    string? image_cdn_url
);

public record NodeFeatures(
    bool ui_image_tiers_enabled,
    bool video_streaming_enabled
);

[JsonSerializable(typeof(NodeManifest))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
