# MehguViewer.Core Tests

This directory contains the test suite for MehguViewer.Core API.

## Test Organization

Tests are organized by category and purpose:

### Test Files

| File | Purpose | Category |
|------|---------|----------|
| `ApiTests.cs` | Quick smoke tests for critical endpoints | Smoke |
| `SystemEndpointTests.cs` | System-related endpoints (well-known, instance, taxonomy) | Unit |
| `SeriesEndpointTests.cs` | Series CRUD operations, search, units | Unit |
| `UserEndpointTests.cs` | User-related endpoints (library, history) | Unit |
| `AuthEndpointTests.cs` | Authentication endpoints (login, register) | Unit |
| `AuthServiceTests.cs` | AuthService unit tests (password hashing, validation) | Unit |
| `UrnHelperTests.cs` | URN helper utility tests | Unit |
| `SecurityTests.cs` | Security headers and response validation | Unit |
| `IntegrationTests.cs` | Multi-endpoint workflow tests | Integration |

### Test Categories (Traits)

Tests are organized using xUnit traits for filtering:

- **Category**:
  - `Smoke` - Quick health checks for critical functionality
  - `Unit` - Isolated unit tests
  - `Integration` - Tests spanning multiple endpoints
  - `Security` - Security-focused tests

- **Priority**:
  - `Critical` - Must pass for deployment
  - `High` - Important functionality
  - `Normal` - Standard tests

- **Endpoint**:
  - `System` - System endpoints
  - `Series` - Series endpoints
  - `User` - User endpoints
  - `Auth` - Authentication endpoints

## Running Tests

### Run All Tests

```bash
dotnet test Tests/Tests.csproj
```

### Run by Category

```bash
# Run only smoke tests
dotnet test Tests/Tests.csproj --filter "Category=Smoke"

# Run only unit tests
dotnet test Tests/Tests.csproj --filter "Category=Unit"

# Run only integration tests
dotnet test Tests/Tests.csproj --filter "Category=Integration"
```

### Run by Priority

```bash
# Run only critical tests
dotnet test Tests/Tests.csproj --filter "Priority=Critical"
```

### Run by Endpoint

```bash
# Run only series endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=Series"

# Run only auth endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=Auth"
```

### Run with Coverage

```bash
dotnet test Tests/Tests.csproj \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=./coverage/
```

## Test Infrastructure

### TestWebApplicationFactory

Located in `TestWebApplicationFactory.cs`, this class configures the test host:

- Uses in-memory `MemoryRepository` instead of PostgreSQL
- Removes background services (EmbeddedPostgresService, IngestionWorker)
- Configures test-appropriate settings
- Enables parallel test execution

### Test Settings

The `test.runsettings` file configures:
- Parallel execution (8 workers)
- Data collection settings
- Coverage exclusions

Usage:
```bash
dotnet test Tests/Tests.csproj -s test.runsettings
```

## Adding New Tests

1. **Choose the appropriate file** based on what you're testing
2. **Add the right traits** for categorization:
   ```csharp
   [Trait("Category", "Unit")]
   [Trait("Endpoint", "YourEndpoint")]
   [Trait("Priority", "Normal")]
   ```
3. **Follow naming conventions**: `MethodName_Scenario_ExpectedResult`
4. **Use Arrange-Act-Assert pattern** with comments

### Example Test

```csharp
[Fact]
[Trait("Priority", "High")]
public async Task CreateSeries_ValidPayload_ReturnsCreated()
{
    // Arrange
    var payload = new
    {
        title = "Test Series",
        media_type = "MANGA",
        reading_direction = "RTL"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

    // Assert
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}
```

## CI/CD Integration

Tests are automatically run in CI via GitHub Actions:

- **On Push/PR**: Full test suite with coverage
- **Nightly**: Extended tests including integration tests with PostgreSQL
- **Pre-Release**: Full test suite with artifact generation

See `.github/workflows/` for workflow configurations.
