using System.Text.Json.Serialization;

namespace MehguViewer.Core.Backend.Models;

public record Problem(
    string type,
    string title,
    int status,
    string detail,
    string instance
);
