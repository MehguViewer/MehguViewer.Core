# MehguViewer.Core Tests

> **Comprehensive Test Suite for MehguViewer.Core**

This directory contains the complete test suite for MehguViewer.Core API, ensuring reliability, correctness, and compliance with the [MehguViewer.Proto](https://proto.mehguviewer.kazeo.xyz) specifications.

---

## 🚀 Overview

The test suite is built using **xUnit** with a focus on:
- **Integration Testing**: End-to-end API workflows
- **Unit Testing**: Isolated component behavior
- **Security Testing**: Authentication, authorization, and validation
- **Performance Testing**: Response times and resource usage

**Test Philosophy:**
- Tests should be fast, isolated, and deterministic
- Use in-memory repository for speed (no database dependencies)
- Follow Arrange-Act-Assert pattern consistently
- Comprehensive coverage of happy paths and edge cases

---

## 📁 Test Organization

Tests are organized into logical directories by category:

```
Tests/
├── Endpoints/                      # API endpoint tests (HTTP layer)
│   ├── ApiTests.cs                # Smoke tests for critical paths
│   ├── AuthEndpointTests.cs       # Authentication endpoints
│   ├── SeriesEndpointTests.cs     # Series CRUD operations
│   ├── SystemEndpointTests.cs     # System and taxonomy endpoints
│   └── UserEndpointTests.cs       # User management endpoints
│
├── Services/                       # Business logic tests
│   ├── AuthServiceTests.cs        # Authentication service
│   ├── FileBasedSeriesServiceTests.cs
│   ├── ImageProcessingTests.cs    # Image processing and variants
│   ├── JobServiceTests.cs         # Background job management
│   ├── LogsServiceTests.cs        # Logging service
│   ├── MetadataAggregationServiceTests.cs
│   ├── PasskeyServiceTests.cs     # WebAuthn/Passkey support
│   ├── TaxonomyValidationServiceTests.cs
│   └── TaxonomyValidationServiceEdgeCaseTests.cs
│
├── Infrastructures/                # Data access layer tests
│   ├── DynamicRepositoryTests.cs  # Repository selection logic
│   ├── EmbeddedPostgresServiceTests.cs
│   ├── MemoryRepositoryTests.cs   # In-memory repository
│   ├── PostgresRepositoryTests.cs # PostgreSQL implementation
│   └── RepositoryInitializerServiceTests.cs
│
├── Integrations/                   # Multi-component workflows
│   ├── IntegrationTests.cs        # General integration tests
│   ├── IntegrationTests_AuthCore.cs
│   └── UnitEndpointIntegrationTests.cs
│
├── Unit/                           # Isolated unit tests
│   ├── EditPermissionTests.cs     # Permission logic
│   ├── IngestionWorkerTests.cs    # Content ingestion
│   ├── LocalizedCoverTests.cs     # Localization handling
│   ├── ResultsExtensionsTests.cs  # Result helpers
│   ├── SecurityTests.cs           # Security validation
│   ├── SeriesUrnTests.cs          # URN parsing
│   ├── TaxonomyValidationWorkerTests.cs
│   ├── UnitMetadataTests.cs       # Unit metadata handling
│   └── UrnHelperTests.cs          # URN utilities
│
├── Shared/                         # Shared test utilities (if any)
├── TestWebApplicationFactory.cs    # Test host configuration
├── test.runsettings                # Test execution settings
├── Tests.csproj                    # Test project file
└── README.md                       # This file
```

---

## 📊 Test Categories by Directory

### Endpoints/ - API Endpoint Tests
**Purpose:** Test HTTP request/response handling, routing, and endpoint behavior.

| Test File | Coverage | Key Scenarios |
|-----------|----------|---------------|
| `ApiTests.cs` | Smoke tests | Quick health checks for critical endpoints |
| `AuthEndpointTests.cs` | Authentication | Login, register, token validation |
| `SeriesEndpointTests.cs` | Series CRUD | Create, read, update, delete, search, units |
| `SystemEndpointTests.cs` | System APIs | Well-known, instance, taxonomy |
| `UserEndpointTests.cs` | User management | Library, history, profile, preferences |

**Testing Focus:**
- HTTP status codes (200, 201, 400, 401, 403, 404, etc.)
- Request/response payload validation
- RFC 7807 Problem Details error responses
- URN-based routing and identifiers
- Authentication and authorization enforcement

---

### Services/ - Business Logic Tests
**Purpose:** Test service layer logic, business rules, and data transformations.

| Test File | Coverage | Key Scenarios |
|-----------|----------|---------------|
| `AuthServiceTests.cs` | Authentication | Password hashing, validation, token generation |
| `FileBasedSeriesServiceTests.cs` | Series storage | File-based JSON persistence |
| `ImageProcessingTests.cs` | Media processing | Image variants (THUMBNAIL, WEB, RAW) |
| `JobServiceTests.cs` | Background jobs | Job creation, status tracking, execution |
| `LogsServiceTests.cs` | Logging | Log storage, retrieval, filtering |
| `MetadataAggregationServiceTests.cs` | Metadata | Aggregation logic for series metadata |
| `PasskeyServiceTests.cs` | WebAuthn | Passkey registration, authentication |
| `TaxonomyValidationServiceTests.cs` | Taxonomy | Validation of authors, tags, etc. |
| `TaxonomyValidationServiceEdgeCaseTests.cs` | Edge cases | Boundary conditions, invalid inputs |

**Testing Focus:**
- Business rule enforcement
- Data validation and sanitization
- Service dependencies and mocking
- Error handling and edge cases
- Performance and resource usage

---

### Infrastructures/ - Data Access Tests
**Purpose:** Test repository implementations and data persistence.

| Test File | Coverage | Key Scenarios |
|-----------|----------|---------------|
| `DynamicRepositoryTests.cs` | Repository selection | Dynamic switching between implementations |
| `EmbeddedPostgresServiceTests.cs` | Database setup | Embedded PostgreSQL initialization |
| `MemoryRepositoryTests.cs` | In-memory storage | Fast, isolated test data |
| `PostgresRepositoryTests.cs` | PostgreSQL | Database operations, queries, transactions |
| `RepositoryInitializerServiceTests.cs` | Initialization | Schema setup, data seeding |

**Testing Focus:**
- CRUD operations (Create, Read, Update, Delete)
- Query performance and correctness
- Transaction handling and rollback
- Concurrency and data consistency
- Repository interface compliance

---

### Integrations/ - Integration Tests
**Purpose:** Test multi-component workflows and end-to-end scenarios.

| Test File | Coverage | Key Scenarios |
|-----------|----------|---------------|
| `IntegrationTests.cs` | General workflows | Multi-step user journeys |
| `IntegrationTests_AuthCore.cs` | Auth+Core integration | Authentication with Core API |
| `UnitEndpointIntegrationTests.cs` | Unit workflows | Create series, add units, upload files |

**Testing Focus:**
- End-to-end user workflows
- Component interaction and integration
- Data flow across layers
- Authentication and authorization flows
- Real-world usage scenarios

---

### Unit/ - Isolated Unit Tests
**Purpose:** Test individual functions, utilities, and helpers in isolation.

| Test File | Coverage | Key Scenarios |
|-----------|----------|---------------|
| `EditPermissionTests.cs` | Permission logic | Owner vs Admin vs Uploader permissions |
| `IngestionWorkerTests.cs` | Content ingestion | Worker logic, file processing |
| `LocalizedCoverTests.cs` | Localization | Cover image localization handling |
| `ResultsExtensionsTests.cs` | Result helpers | HTTP result creation utilities |
| `SecurityTests.cs` | Security validation | Input sanitization, validation rules |
| `SeriesUrnTests.cs` | URN parsing | Series URN format validation |
| `TaxonomyValidationWorkerTests.cs` | Taxonomy worker | Background validation processing |
| `UnitMetadataTests.cs` | Unit metadata | Metadata parsing and handling |
| `UrnHelperTests.cs` | URN utilities | URN creation, parsing, validation |

**Testing Focus:**
- Pure function behavior
- Input validation and edge cases
- Error handling and exceptions
- Helper and utility functions
- URN format compliance

---

## 🏷️ Test Traits & Filtering

Tests are organized using **xUnit traits** for flexible filtering and execution:

### Category Traits
| Trait | Purpose | When to Use |
|-------|---------|-------------|
| `Smoke` | Quick health checks | Critical functionality verification |
| `Unit` | Isolated unit tests | Testing individual components |
| `Integration` | Multi-component tests | End-to-end workflows |
| `Security` | Security-focused tests | Authentication, authorization, validation |

### Priority Traits
| Trait | Purpose | When to Use |
|-------|---------|-------------|
| `Critical` | Must pass for deployment | Breaking changes, core functionality |
| `High` | Important functionality | Major features, common workflows |
| `Normal` | Standard tests | Standard features, edge cases |

### Endpoint Traits
| Trait | Purpose | Endpoints Covered |
|-------|---------|-------------------|
| `System` | System endpoints | `/system/*`, `/.well-known/*` |
| `Series` | Series endpoints | `/series/*` |
| `User` | User endpoints | `/users/*`, `/library/*` |
| `Auth` | Authentication | `/auth/*` |

### Example Test with Traits
```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Priority", "Critical")]
[Trait("Endpoint", "Series")]
public async Task CreateSeriesAndAddUnit_ValidData_ReturnsSuccess()
{
    // Arrange
    var seriesPayload = new { title = "Test Series", media_type = "MANGA" };
    
    // Act
    var seriesResponse = await _client.PostAsJsonAsync("/api/v1/series", seriesPayload);
    var series = await seriesResponse.Content.ReadFromJsonAsync<Series>();
    
    var unitPayload = new { title = "Chapter 1", number = 1 };
    var unitResponse = await _client.PostAsJsonAsync($"/api/v1/series/{series.urn}/units", unitPayload);
    
    // Assert
    Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
}
```

---

## 🚀 Running Tests

### Quick Start

```bash
# Navigate to project root
cd MehguViewer.Core

# Run all tests
dotnet test Tests/Tests.csproj

# Run tests with detailed output
dotnet test Tests/Tests.csproj --verbosity normal

# Run tests and continue on failure
dotnet test Tests/Tests.csproj --no-build --logger "console;verbosity=minimal"
```

---

### Run by Category

```bash
# Run only smoke tests (fastest, for quick validation)
dotnet test Tests/Tests.csproj --filter "Category=Smoke"

# Run only unit tests (fast, isolated)
dotnet test Tests/Tests.csproj --filter "Category=Unit"

# Run only integration tests (slower, comprehensive)
dotnet test Tests/Tests.csproj --filter "Category=Integration"

# Run only security tests
dotnet test Tests/Tests.csproj --filter "Category=Security"
```

---

### Run by Priority

```bash
# Run only critical tests (must pass before deployment)
dotnet test Tests/Tests.csproj --filter "Priority=Critical"

# Run critical and high priority tests
dotnet test Tests/Tests.csproj --filter "Priority=Critical|Priority=High"

# Run all except normal priority
dotnet test Tests/Tests.csproj --filter "Priority!=Normal"
```

---

### Run by Endpoint

```bash
# Run only series endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=Series"

# Run only auth endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=Auth"

# Run only system endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=System"

# Run only user endpoint tests
dotnet test Tests/Tests.csproj --filter "Endpoint=User"
```

---

### Run by Test File

```bash
# Run tests from a specific file
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~SeriesEndpointTests"

# Run a specific test method
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~CreateSeries_ValidPayload_ReturnsCreated"
```

---

### Run with Coverage

```bash
# Generate code coverage report
dotnet test Tests/Tests.csproj \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=./coverage/

# Generate coverage with HTML report
dotnet test Tests/Tests.csproj \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=opencover \
    /p:CoverletOutput=./coverage/coverage.xml

# Generate coverage and open HTML report (requires ReportGenerator)
dotnet test Tests/Tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
reportgenerator -reports:./coverage/coverage.cobertura.xml -targetdir:./coverage/report
open ./coverage/report/index.html  # macOS
```

---

### Advanced Usage

```bash
# Run tests in parallel (faster execution)
dotnet test Tests/Tests.csproj --parallel

# Run tests with custom settings file
dotnet test Tests/Tests.csproj -s test.runsettings

# Run tests and collect diagnostics
dotnet test Tests/Tests.csproj --diag:log.txt

# Run tests with specific logger
dotnet test Tests/Tests.csproj --logger "trx;LogFileName=test-results.trx"

# Combine filters (Category=Integration AND Priority=Critical)
dotnet test Tests/Tests.csproj --filter "Category=Integration&Priority=Critical"

# Run all tests except integration tests
dotnet test Tests/Tests.csproj --filter "Category!=Integration"
```

---

## 🏗️ Test Infrastructure

### TestWebApplicationFactory

The `TestWebApplicationFactory.cs` class provides a configured test host for integration tests:

**Key Features:**
- **In-Memory Repository**: Uses `MemoryRepository` instead of PostgreSQL for speed
- **Background Service Removal**: Disables `EmbeddedPostgresService` and `IngestionWorker`
- **Test-Appropriate Configuration**: Overrides settings for test environment
- **Parallel Execution Support**: Enables running tests concurrently
- **Isolated Test Data**: Each test gets a clean state

**Usage Example:**
```csharp
public class SeriesEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SeriesEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSeries_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/series");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

**Configuration Overrides:**
```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        // Replace PostgreSQL with in-memory repository
        services.RemoveAll<IRepository>();
        services.AddSingleton<IRepository, MemoryRepository>();
        
        // Remove background services
        services.RemoveAll<EmbeddedPostgresService>();
        services.RemoveAll<IngestionWorker>();
    });
}
```

---

### Test Settings (test.runsettings)

The `test.runsettings` file configures test execution behavior:

**Configuration:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <MaxCpuCount>8</MaxCpuCount>          <!-- Parallel workers -->
    <ResultsDirectory>./TestResults</ResultsDirectory>
  </RunConfiguration>
  
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="Code Coverage" />
    </DataCollectors>
  </DataCollectionRunSettings>
  
  <xUnit>
    <MaxParallelThreads>8</MaxParallelThreads>
    <ParallelizeTestCollections>true</ParallelizeTestCollections>
  </xUnit>
</RunSettings>
```

**Usage:**
```bash
dotnet test Tests/Tests.csproj -s test.runsettings
```

---

### Test Utilities & Helpers

**Common Test Helpers:**
- `CreateAuthenticatedClient()` - Client with JWT token
- `CreateAdminClient()` - Client with admin role
- `CreateTestSeries()` - Generate test series data
- `CreateTestUser()` - Generate test user data
- `AssertProblemDetails()` - Validate RFC 7807 errors

**Example:**
```csharp
private async Task<HttpClient> CreateAuthenticatedClient(string role = "USER")
{
    var client = _factory.CreateClient();
    var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
    {
        username = "testuser",
        password = "password123"
    });
    
    var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", loginResult.token);
    
    return client;
}
```

---

## 📝 Writing New Tests

### Test Naming Convention

Follow the pattern: `MethodName_Scenario_ExpectedResult`

**Examples:**
```csharp
// Good naming
CreateSeries_ValidPayload_ReturnsCreated
CreateSeries_MissingTitle_ReturnsBadRequest
CreateSeries_UnauthorizedUser_ReturnsUnauthorized
GetSeries_NonExistentUrn_ReturnsNotFound
UpdateSeries_AsOwner_ReturnsOk
UpdateSeries_AsNonOwner_ReturnsForbidden

// Bad naming
TestCreateSeries
SeriesTest1
CreateSeriesTest
```

---

### Test Structure (Arrange-Act-Assert)

Always follow the AAA pattern with clear comments:

```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Priority", "High")]
[Trait("Endpoint", "Series")]
public async Task CreateSeries_ValidPayload_ReturnsCreated()
{
    // Arrange - Set up test data and preconditions
    var payload = new
    {
        title = "Test Manga Series",
        description = "A test series for integration testing",
        media_type = "MANGA",
        reading_direction = "RTL",
        publication_status = "ONGOING"
    };
    var client = await CreateAuthenticatedClient("UPLOADER");

    // Act - Perform the action being tested
    var response = await client.PostAsJsonAsync("/api/v1/series", payload);

    // Assert - Verify the expected outcome
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    
    var series = await response.Content.ReadFromJsonAsync<Series>();
    Assert.NotNull(series);
    Assert.Equal("Test Manga Series", series.metadata.title);
    Assert.StartsWith("urn:mvn:series:", series.urn);
}
```

---

### Test Trait Guidelines

| Test Type | Category | Priority | Endpoint (if applicable) |
|-----------|----------|----------|--------------------------|
| Quick health check | `Smoke` | `Critical` | Relevant endpoint |
| Isolated function test | `Unit` | `Normal` | N/A |
| Multi-component workflow | `Integration` | `High` | Relevant endpoint |
| Auth/validation test | `Security` | `Critical` | `Auth` or relevant |

**Example:**
```csharp
[Fact]
[Trait("Category", "Unit")]
[Trait("Priority", "Normal")]
public void UrnHelper_ParseSeriesUrn_ExtractsUuid()
{
    // Arrange
    var urn = "urn:mvn:series:abc-123-def-456";

    // Act
    var uuid = UrnHelper.ExtractUuid(urn);

    // Assert
    Assert.Equal("abc-123-def-456", uuid);
}
```

---

### Testing Best Practices

**DO:**
- ✅ Test one thing per test method
- ✅ Use descriptive test names
- ✅ Include both positive and negative test cases
- ✅ Test edge cases and boundary conditions
- ✅ Use appropriate assertions (`Assert.Equal`, `Assert.NotNull`, etc.)
- ✅ Clean up test data (when needed)
- ✅ Use `IClassFixture` for shared setup
- ✅ Mock external dependencies
- ✅ Test error responses (RFC 7807 format)
- ✅ Verify URN formats in responses

**DON'T:**
- ❌ Write tests that depend on execution order
- ❌ Use hard-coded values without explanation
- ❌ Test multiple scenarios in one test
- ❌ Ignore test failures
- ❌ Skip adding traits
- ❌ Leave commented-out code
- ❌ Use `Thread.Sleep()` (use async properly)
- ❌ Test implementation details

---

### Common Test Patterns

#### Testing Error Responses (RFC 7807)
```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Priority", "High")]
public async Task GetSeries_NonExistentUrn_ReturnsNotFoundWithProblemDetails()
{
    // Arrange
    var fakeUrn = "urn:mvn:series:nonexistent-123";

    // Act
    var response = await _client.GetAsync($"/api/v1/series/{fakeUrn}");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.NotNull(problem);
    Assert.Equal("urn:mvn:error:not-found", problem.type);
    Assert.Equal(404, problem.status);
}
```

#### Testing Authentication
```csharp
[Fact]
[Trait("Category", "Security")]
[Trait("Priority", "Critical")]
public async Task CreateSeries_WithoutAuth_ReturnsUnauthorized()
{
    // Arrange
    var payload = new { title = "Test", media_type = "MANGA" };

    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/series", payload);

    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

#### Testing Authorization (Roles)
```csharp
[Fact]
[Trait("Category", "Security")]
[Trait("Priority", "Critical")]
public async Task DeleteSeries_AsNonOwner_ReturnsForbidden()
{
    // Arrange - Create series as one user
    var ownerClient = await CreateAuthenticatedClient("UPLOADER", "owner");
    var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/series", 
        new { title = "Test", media_type = "MANGA" });
    var series = await createResponse.Content.ReadFromJsonAsync<Series>();
    
    // Act - Try to delete as different user
    var otherClient = await CreateAuthenticatedClient("UPLOADER", "other");
    var deleteResponse = await otherClient.DeleteAsync($"/api/v1/series/{series.urn}");

    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
}
```

#### Testing URN Validation
```csharp
[Theory]
[InlineData("urn:mvn:series:abc-123", true)]
[InlineData("urn:mvn:unit:def-456", false)]
[InlineData("invalid-urn", false)]
[InlineData("", false)]
public void UrnHelper_IsSeriesUrn_ValidatesCorrectly(string urn, bool expected)
{
    // Act
    var result = UrnHelper.IsSeriesUrn(urn);

    // Assert
    Assert.Equal(expected, result);
}
```

---

### Adding Tests to Existing Files

1. **Find the appropriate test file** based on what you're testing
2. **Add your test method** following naming conventions
3. **Add appropriate traits** for filtering
4. **Follow the AAA pattern** with comments
5. **Run the test** to verify it works
6. **Run all related tests** to ensure no regressions

---

### Creating New Test Files

When creating a new test file:

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.Tests.Endpoints;

public class NewFeatureEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public NewFeatureEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "Critical")]
    [Trait("Endpoint", "NewFeature")]
    public async Task GetNewFeature_ReturnsOk()
    {
        // Arrange
        // ... setup

        // Act
        var response = await _client.GetAsync("/api/v1/new-feature");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

---

## 🔄 CI/CD Integration

Tests are automatically executed in CI/CD pipelines via **GitHub Actions**:

### Workflows

| Workflow | Trigger | Tests Run | Coverage | Duration |
|----------|---------|-----------|----------|----------|
| **CI** | Push, Pull Request | All tests | ✅ Yes | ~2-5 min |
| **Nightly** | Scheduled (daily) | Extended + PostgreSQL | ✅ Yes | ~10-15 min |
| **Pre-Release** | Tag push | Full suite + artifacts | ✅ Yes | ~5-10 min |

### CI Workflow (`.github/workflows/ci.yml`)
```yaml
name: CI
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Run tests
        run: dotnet test --no-build --verbosity normal /p:CollectCoverage=true
      
      - name: Upload coverage
        uses: codecov/codecov-action@v3
        with:
          files: ./coverage/coverage.cobertura.xml
```

### Test Failure Policy

**CI must pass before merging:**
- All `Priority=Critical` tests must pass
- Code coverage must not decrease
- No new compiler warnings

**Allowed to merge with review:**
- `Priority=Normal` test failures (with issue created)
- Flaky test failures (after rerun)

---

## 📊 Code Coverage

### Current Coverage Targets
- **Overall**: ≥ 80%
- **Critical paths**: 100% (auth, series CRUD, URN handling)
- **Edge cases**: ≥ 70%
- **UI components**: ≥ 60%

### Viewing Coverage Reports

```bash
# Generate coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Generate HTML report (requires reportgenerator)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:./coverage/coverage.cobertura.xml -targetdir:./coverage/report

# Open report
open ./coverage/report/index.html  # macOS
xdg-open ./coverage/report/index.html  # Linux
start ./coverage/report/index.html  # Windows
```

### Coverage by Component

| Component | Target | Current |
|-----------|--------|---------|
| Endpoints | 90% | ~85% |
| Services | 85% | ~80% |
| Infrastructure | 80% | ~75% |
| Middlewares | 90% | ~88% |
| Helpers/Utilities | 95% | ~92% |

---

## 🐛 Debugging Tests

### Running Tests in Debug Mode

**Visual Studio Code:**
1. Open test file
2. Click "Debug Test" above test method
3. Set breakpoints as needed
4. Inspect variables and step through code

**Command Line:**
```bash
# Run with detailed diagnostics
dotnet test --diag:log.txt --verbosity diagnostic

# Run specific test in debug mode
dotnet test --filter "FullyQualifiedName~YourTestName" --logger "console;verbosity=detailed"
```

### Common Issues & Solutions

**Issue: Tests fail locally but pass in CI**
- Check test isolation (shared state?)
- Verify local environment matches CI
- Check for timing-dependent tests

**Issue: Flaky tests (intermittent failures)**
- Add `[Fact(Skip = "Flaky - investigating")]` temporarily
- Investigate race conditions
- Check for external dependencies

**Issue: Tests timeout**
- Increase timeout: `[Fact(Timeout = 10000)]` (10 seconds)
- Check for infinite loops or deadlocks
- Profile test performance

---

## 📚 Additional Resources

### Documentation
- [xUnit Documentation](https://xunit.net/)
- [ASP.NET Core Testing](https://docs.microsoft.com/aspnet/core/test/)
- [MehguViewer.Proto Specifications](https://proto.mehguviewer.kazeo.xyz)

### Tools
- [Coverlet](https://github.com/coverlet-coverage/coverlet) - Code coverage
- [ReportGenerator](https://github.com/danielpalme/ReportGenerator) - Coverage reports
- [xUnit.net](https://xunit.net/) - Testing framework

### Related Documentation
- [Main README](../README.md) - Project overview
- [UI Documentation](../MehguViewer.Core.UI/README.md) - UI testing
- [Shared Models](../MehguViewer.Core.Shared/README.md) - Domain models

---

## 🤝 Contributing to Tests

When contributing tests:

1. **Write tests for new features** before or during implementation
2. **Maintain existing test coverage** when refactoring
3. **Add regression tests** when fixing bugs
4. **Update documentation** when adding new test categories
5. **Follow naming conventions** consistently
6. **Use appropriate traits** for filtering
7. **Keep tests fast** (< 100ms per test ideal)
8. **Ensure tests are isolated** and can run in parallel

---

<div align="center">
  <sub>Comprehensive testing for reliable software</sub>
  <br>
  <sub>MehguViewer.Core Tests &copy; 2025</sub>
</div>
