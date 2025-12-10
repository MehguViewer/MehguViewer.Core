# MehguViewer.Core.Shared

> **Shared Domain Models and DTOs for MehguViewer Ecosystem**

This project contains the shared data models, DTOs (Data Transfer Objects), and domain entities used across the MehguViewer.Core backend API, Blazor UI, and potentially external clients. It ensures type safety and consistency across the entire application.

---

## 🚀 Overview

MehguViewer.Core.Shared provides a centralized, strongly-typed definition of all domain models according to the [MehguViewer.Proto](https://proto.mehguviewer.kazeo.xyz) specifications. These models are shared between:

- **MehguViewer.Core** (ASP.NET Core backend)
- **MehguViewer.Core.UI** (Blazor WebAssembly frontend)
- **External API clients** (when using NSwag/OpenAPI code generation)

---

## 📁 Project Structure

```
MehguViewer.Core.Shared/
├── Domain.cs          # Core domain models (Series, Units, Media, etc.)
├── AdminModels.cs     # Admin-specific models (Users, Jobs, System)
├── Problem.cs         # RFC 7807 Problem Details for error handling
└── README.md          # This file
```

---

## 📦 Domain Models

### Domain.cs

Contains all public-facing domain entities and DTOs:

#### Node & System Models
- `NodeMetadata` - Core metadata about a MehguViewer node
- `NodeCapabilities` - Feature capabilities (search, streaming, download)
- `NodeMaintainer` - Maintainer contact information
- `NodeManifest` - Public manifest for federation and discovery
- `NodeFeatures` - Feature flags for client optimization
- `Taxonomy` - Complete taxonomy (authors, scanlators, groups, tags)

#### Series & Content Models
- `Series` - Complete series information with metadata
- `SeriesMetadata` - Series metadata (title, description, demographics)
- `SeriesListItem` - Lightweight series for list views
- `SeriesCreateRequest` / `SeriesUpdateRequest` - Mutation payloads
- `LocalizedVersion` - Localized series information
- `LinkedExternalId` - External platform links (MyAnimeList, AniList, etc.)

#### Unit Models
- `Unit` - Individual content unit (chapter, episode, volume)
- `UnitMetadata` - Unit-specific metadata (title, number, release date)
- `UnitListItem` - Lightweight unit for list views
- `UnitCreateRequest` / `UnitUpdateRequest` - Mutation payloads

#### Media & Asset Models
- `MediaItem` - Media asset reference (images, videos, files)
- `ImageVariant` - Image variant types (THUMBNAIL, WEB, RAW)
- `AssetMetadata` - Asset metadata (size, mime type, dimensions)

#### Taxonomy Models
- `Author` - Content creator information
- `Scanlator` - Translation/scanlation team
- `Group` - Content production group
- `Tag` - Genre, theme, or category tag
- `ContentWarning` - Content advisory information

#### User Models (Public)
- `UserLibrary` - User's library and collections
- `ReadingProgress` - User progress tracking
- `UserComment` - User comments on series/units
- `UserRating` - User ratings

#### Enumerations
- `MediaType` - MANGA, ANIME, NOVEL
- `PublicationStatus` - ONGOING, COMPLETED, HIATUS, CANCELLED
- `ReadingDirection` - LTR, RTL, TTB
- `UserRole` - ADMIN, UPLOADER, USER
- `ContentRating` - SAFE, QUESTIONABLE, EXPLICIT

---

### AdminModels.cs

Contains administrative and internal models:

#### User Management
- `User` - Complete user entity with authentication
- `UserCreateRequest` / `UserUpdateRequest` - User mutation payloads
- `PasswordChangeRequest` - Password update payload
- `TransferOwnershipRequest` - Ownership transfer

#### Background Jobs
- `Job` - Background job information
- `JobStatus` - Job execution status
- `JobType` - Job type enumeration

#### System Administration
- `SystemSettings` - Node configuration and settings
- `LogEntry` - System log entry
- `StorageInfo` - Storage statistics

#### Authentication (Internal)
- `LoginRequest` / `LoginResponse` - Authentication payloads
- `RegisterRequest` - User registration
- `PasskeyCreateRequest` / `PasskeyAuthRequest` - WebAuthn/Passkey support

---

### Problem.cs

Implements RFC 7807 Problem Details for standardized error responses:

```csharp
public record ProblemDetails(
    string type,           // URN error type (e.g., "urn:mvn:error:not-found")
    string title,          // Human-readable title
    int status,            // HTTP status code
    string detail,         // Detailed error message
    string? instance,      // Request path
    string? traceId,       // Correlation ID for tracing
    Dictionary<string, object>? extensions  // Additional context
);
```

**Standard Error Types:**
- `urn:mvn:error:not-found` - Resource not found (404)
- `urn:mvn:error:unauthorized` - Authentication required (401)
- `urn:mvn:error:forbidden` - Insufficient permissions (403)
- `urn:mvn:error:validation` - Validation failure (400)
- `urn:mvn:error:conflict` - Resource conflict (409)
- `urn:mvn:error:internal` - Internal server error (500)

---

## 🔑 Key Design Principles

### 1. URN-Based Identifiers
All resource identifiers use URN format:
```
urn:mvn:{type}:{uuid}

Examples:
- urn:mvn:series:abc-123
- urn:mvn:unit:def-456
- urn:mvn:user:ghi-789
```

### 2. Immutable Records
All models use C# `record` types for immutability and value semantics:
```csharp
public record Series(
    string urn,
    SeriesMetadata metadata,
    // ...
);
```

### 3. Validation Attributes
Models include validation attributes for automatic validation:
```csharp
public record SeriesCreateRequest(
    [Required(ErrorMessage = "Title is required")]
    [StringLength(500, ErrorMessage = "Title cannot exceed 500 characters")]
    string title,
    
    [Required(ErrorMessage = "Media type is required")]
    MediaType media_type
);
```

### 4. JSON Serialization
Models are optimized for JSON serialization with `System.Text.Json`:
```csharp
[JsonPropertyName("urn")]
public string Urn { get; init; }

[JsonConverter(typeof(JsonStringEnumConverter))]
public MediaType MediaType { get; init; }
```

### 5. Separation of Concerns
- **Domain models** represent complete entities
- **List items** provide lightweight projections
- **Request/Response DTOs** handle API payloads
- **Admin models** isolated from public API

---

## 🛠️ Usage Examples

### Backend (MehguViewer.Core)

```csharp
using MehguViewer.Core.Shared;

// Creating a series
var series = new Series(
    urn: "urn:mvn:series:abc-123",
    metadata: new SeriesMetadata(
        title: "Example Manga",
        description: "An example series",
        media_type: MediaType.MANGA
    ),
    owner_urn: "urn:mvn:user:owner-123",
    created_at: DateTime.UtcNow,
    updated_at: DateTime.UtcNow
);

// Returning error
return Results.Problem(new ProblemDetails(
    type: "urn:mvn:error:not-found",
    title: "Series Not Found",
    status: 404,
    detail: $"Series with URN {urn} does not exist",
    instance: context.Request.Path,
    traceId: context.TraceIdentifier
));
```

### Frontend (MehguViewer.Core.UI)

```csharp
@using MehguViewer.Core.Shared
@inject ApiService Api

@code {
    private List<SeriesListItem> seriesList = new();

    protected override async Task OnInitializedAsync()
    {
        var response = await Api.GetAsync<SeriesListResponse>("/api/v1/series");
        seriesList = response.series;
    }
}
```

### External Clients (Generated)

```typescript
// TypeScript client (generated from OpenAPI)
import { Series, SeriesCreateRequest, MediaType } from './generated-client';

const newSeries: SeriesCreateRequest = {
    title: "Example Manga",
    media_type: MediaType.MANGA,
    reading_direction: ReadingDirection.RTL
};

const response = await client.createSeries(newSeries);
```

---

## 📊 Model Categories

### Public API Models (Domain.cs)
Used in public-facing API endpoints:
- Series, Units, Media
- Taxonomy (Authors, Tags, etc.)
- Public user data (library, progress)
- Node metadata and federation

### Admin API Models (AdminModels.cs)
Restricted to authenticated admin users:
- User management
- System settings
- Background jobs
- Logs and diagnostics

### Error Models (Problem.cs)
Used across all endpoints for consistent error handling:
- RFC 7807 Problem Details
- URN-based error types
- Structured error responses

---

## 🔄 Versioning & Compatibility

This shared library follows semantic versioning:
- **Major version**: Breaking changes to models
- **Minor version**: New models or optional fields
- **Patch version**: Bug fixes, documentation

When updating models:
1. **Additive changes** (new optional fields) → Minor version bump
2. **Breaking changes** (removed/renamed fields) → Major version bump
3. **Always maintain backward compatibility** where possible

---

## 🧪 Testing

Models are tested indirectly through:
- **Backend integration tests** (serialization, validation)
- **Frontend component tests** (data binding, display)
- **API contract tests** (OpenAPI spec compliance)

---

## 📦 NuGet Package (Future)

This library is intended to be published as a standalone NuGet package:

```bash
dotnet pack MehguViewer.Core.Shared.csproj -c Release
```

External clients can then reference it:

```xml
<PackageReference Include="MehguViewer.Core.Shared" Version="1.0.0" />
```

---

## 🤝 Contributing

When adding or modifying models:

1. **Follow Proto specifications** from [MehguViewer.Proto](https://proto.mehguviewer.kazeo.xyz)
2. **Use URN identifiers** for all resource references
3. **Add validation attributes** for required/validated fields
4. **Include XML documentation** for all public models
5. **Use immutable records** instead of mutable classes
6. **Test serialization** with `System.Text.Json`
7. **Update this README** with new model descriptions

### Example Model Addition

```csharp
/// <summary>
/// Represents a content collection or shelf.
/// </summary>
/// <param name="urn">Unique URN identifier.</param>
/// <param name="name">Collection name.</param>
/// <param name="description">Collection description.</param>
/// <param name="series_urns">URNs of series in this collection.</param>
public record Collection(
    [Required(ErrorMessage = "URN is required")]
    string urn,
    
    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    string name,
    
    string? description,
    
    List<string> series_urns
);
```

---

## 📄 License

This project is part of MehguViewer.Core and shares the same license. See the root [LICENSE](../LICENSE) file.

---

<div align="center">
  <sub>Shared domain models for MehguViewer Ecosystem</sub>
  <br>
  <sub>MehguViewer.Core.Shared &copy; 2025</sub>
</div>
