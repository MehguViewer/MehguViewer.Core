namespace MehguViewer.Core.Backend.Services;

public static class UrnHelper
{
    public static string CreateSeriesUrn() => $"urn:mvn:series:{Guid.NewGuid()}";
    public static string CreateUserUrn() => $"urn:mvn:user:{Guid.NewGuid()}";
    public static string CreateAssetUrn() => $"urn:mvn:asset:{Guid.NewGuid()}";
    public static string CreateCommentUrn() => $"urn:mvn:comment:{Guid.NewGuid()}";
    public static string CreateErrorUrn(string code) => $"urn:mvn:error:{code}";

    public static UrnParts Parse(string urn)
    {
        if (string.IsNullOrEmpty(urn)) throw new ArgumentException("URN cannot be empty");

        var parts = urn.Split(':');
        
        // Basic validation
        if (parts.Length < 3 || parts[0] != "urn")
        {
             throw new ArgumentException($"Invalid URN format: {urn}");
        }

        // Handle Federation URNs: urn:src:<source>:<id>
        if (parts[1] == "src")
        {
             if (parts.Length < 4) throw new ArgumentException($"Invalid Source URN: {urn}");
             // Join the rest in case ID contains colons
             return new UrnParts("src", parts[2], string.Join(":", parts.Skip(3)));
        }

        // Handle MehguViewer URNs: urn:mvn:<type>:<id>
        if (parts[1] == "mvn")
        {
             if (parts.Length < 4) throw new ArgumentException($"Invalid MehguViewer URN: {urn}");
             return new UrnParts("mvn", parts[2], string.Join(":", parts.Skip(3)));
        }

        throw new ArgumentException($"Unknown URN namespace: {parts[1]}");
    }
}

public record UrnParts(string Namespace, string Type, string Id);
