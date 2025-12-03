using System.Text.Json.Serialization;

namespace MehguViewer.Shared.Models;

public record Problem(
    string type,
    string title,
    int status,
    string detail,
    string instance
);
