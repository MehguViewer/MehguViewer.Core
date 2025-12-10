using System.ComponentModel.DataAnnotations;
using MehguViewer.Core.Shared;
using Xunit;

namespace Tests.Shared;

/// <summary>
/// Unit tests for RFC 7807 Problem Details implementation.
/// Ensures compliance with RFC 7807 specification and correct error formatting.
/// </summary>
public class ProblemTests
{
    #region Constructor Tests

    [Fact]
    public void Problem_ValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var problem = new Problem(
            type: "urn:mvn:error:not-found",
            title: "Not Found",
            status: 404,
            detail: "The requested resource was not found",
            instance: "/api/series/123"
        );

        // Assert
        Assert.Equal("urn:mvn:error:not-found", problem.type);
        Assert.Equal("Not Found", problem.title);
        Assert.Equal(404, problem.status);
        Assert.Equal("The requested resource was not found", problem.detail);
        Assert.Equal("/api/series/123", problem.instance);
    }

    [Fact]
    public void Problem_NullDetail_IsAllowed()
    {
        // Arrange & Act
        var problem = new Problem(
            type: "urn:mvn:error:bad-request",
            title: "Bad Request",
            status: 400,
            detail: null,
            instance: "/api/test"
        );

        // Assert
        Assert.Null(problem.detail);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Problem_ValidProblem_PassesValidation()
    {
        // Arrange
        var problem = new Problem(
            type: "urn:mvn:error:unauthorized",
            title: "Unauthorized",
            status: 401,
            detail: "Authentication required",
            instance: "/api/protected"
        );

        // Act
        var results = ValidateModel(problem);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(99)]   // Below minimum
    [InlineData(600)]  // Above maximum
    public void Problem_InvalidStatusCode_FailsValidation(int status)
    {
        // Arrange
        var problem = new Problem(
            type: "urn:mvn:error:test",
            title: "Test",
            status: status,
            detail: "Test detail",
            instance: "/test"
        );

        // Act
        var results = ValidateModel(problem);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("HTTP status must be between 100 and 599"));
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void Factory_BadRequest_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.BadRequest(
            "Invalid input data",
            "/api/series"
        );

        // Assert
        Assert.Equal("urn:mvn:error:bad-request", problem.type);
        Assert.Equal("Bad Request", problem.title);
        Assert.Equal(400, problem.status);
        Assert.Equal("Invalid input data", problem.detail);
        Assert.Equal("/api/series", problem.instance);
    }

    [Fact]
    public void Factory_Unauthorized_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.Unauthorized(
            "Missing authentication token",
            "/api/protected"
        );

        // Assert
        Assert.Equal("urn:mvn:error:unauthorized", problem.type);
        Assert.Equal("Unauthorized", problem.title);
        Assert.Equal(401, problem.status);
        Assert.Equal("Missing authentication token", problem.detail);
        Assert.Equal("/api/protected", problem.instance);
    }

    [Fact]
    public void Factory_Forbidden_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.Forbidden(
            "Insufficient permissions",
            "/api/admin"
        );

        // Assert
        Assert.Equal("urn:mvn:error:forbidden", problem.type);
        Assert.Equal("Forbidden", problem.title);
        Assert.Equal(403, problem.status);
        Assert.Equal("Insufficient permissions", problem.detail);
        Assert.Equal("/api/admin", problem.instance);
    }

    [Fact]
    public void Factory_NotFound_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.NotFound(
            "Series not found",
            "/api/series/urn:mvn:series:123"
        );

        // Assert
        Assert.Equal("urn:mvn:error:not-found", problem.type);
        Assert.Equal("Not Found", problem.title);
        Assert.Equal(404, problem.status);
        Assert.Equal("Series not found", problem.detail);
        Assert.Equal("/api/series/urn:mvn:series:123", problem.instance);
    }

    [Fact]
    public void Factory_Conflict_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.Conflict(
            "Resource already exists",
            "/api/users"
        );

        // Assert
        Assert.Equal("urn:mvn:error:conflict", problem.type);
        Assert.Equal("Conflict", problem.title);
        Assert.Equal(409, problem.status);
        Assert.Equal("Resource already exists", problem.detail);
        Assert.Equal("/api/users", problem.instance);
    }

    [Fact]
    public void Factory_UnprocessableEntity_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.UnprocessableEntity(
            "Validation failed",
            "/api/series"
        );

        // Assert
        Assert.Equal("urn:mvn:error:unprocessable-entity", problem.type);
        Assert.Equal("Unprocessable Entity", problem.title);
        Assert.Equal(422, problem.status);
        Assert.Equal("Validation failed", problem.detail);
        Assert.Equal("/api/series", problem.instance);
    }

    [Fact]
    public void Factory_TooManyRequests_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.TooManyRequests(
            "Rate limit exceeded",
            "/api/search"
        );

        // Assert
        Assert.Equal("urn:mvn:error:too-many-requests", problem.type);
        Assert.Equal("Too Many Requests", problem.title);
        Assert.Equal(429, problem.status);
        Assert.Equal("Rate limit exceeded", problem.detail);
        Assert.Equal("/api/search", problem.instance);
    }

    [Fact]
    public void Factory_InternalServerError_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.InternalServerError(
            "An unexpected error occurred",
            "/api/process"
        );

        // Assert
        Assert.Equal("urn:mvn:error:internal-server-error", problem.type);
        Assert.Equal("Internal Server Error", problem.title);
        Assert.Equal(500, problem.status);
        Assert.Equal("An unexpected error occurred", problem.detail);
        Assert.Equal("/api/process", problem.instance);
    }

    [Fact]
    public void Factory_ServiceUnavailable_CreatesCorrectProblem()
    {
        // Arrange & Act
        var problem = Problem.Factory.ServiceUnavailable(
            "Service temporarily unavailable",
            "/api/health"
        );

        // Assert
        Assert.Equal("urn:mvn:error:service-unavailable", problem.type);
        Assert.Equal("Service Unavailable", problem.title);
        Assert.Equal(503, problem.status);
        Assert.Equal("Service temporarily unavailable", problem.detail);
        Assert.Equal("/api/health", problem.instance);
    }

    #endregion

    #region RFC 7807 Compliance Tests

    [Fact]
    public void Problem_RFC7807_TypeIsURI()
    {
        // Arrange & Act
        var problem = Problem.Factory.NotFound("Test", "/test");

        // Assert
        Assert.StartsWith("urn:", problem.type);
    }

    [Fact]
    public void Problem_RFC7807_StatusMatchesHttpStatus()
    {
        // Arrange
        var testCases = new[]
        {
            (Problem.Factory.BadRequest("", "/"), 400),
            (Problem.Factory.Unauthorized("", "/"), 401),
            (Problem.Factory.Forbidden("", "/"), 403),
            (Problem.Factory.NotFound("", "/"), 404),
            (Problem.Factory.Conflict("", "/"), 409),
            (Problem.Factory.UnprocessableEntity("", "/"), 422),
            (Problem.Factory.TooManyRequests("", "/"), 429),
            (Problem.Factory.InternalServerError("", "/"), 500),
            (Problem.Factory.ServiceUnavailable("", "/"), 503)
        };

        // Act & Assert
        foreach (var (problem, expectedStatus) in testCases)
        {
            Assert.Equal(expectedStatus, problem.status);
        }
    }

    [Fact]
    public void Problem_RFC7807_TitleIsHumanReadable()
    {
        // Arrange & Act
        var problem = Problem.Factory.NotFound("Series not found", "/api/series/123");

        // Assert
        Assert.NotEmpty(problem.title);
        Assert.DoesNotContain("urn:", problem.title);
        Assert.False(string.IsNullOrWhiteSpace(problem.title));
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void Problem_EqualProblems_AreEqual()
    {
        // Arrange
        var problem1 = new Problem("urn:test", "Test", 400, "Detail", "/test");
        var problem2 = new Problem("urn:test", "Test", 400, "Detail", "/test");

        // Act & Assert
        Assert.Equal(problem1, problem2);
        Assert.True(problem1 == problem2);
    }

    [Fact]
    public void Problem_DifferentProblems_AreNotEqual()
    {
        // Arrange
        var problem1 = new Problem("urn:test", "Test", 400, "Detail", "/test");
        var problem2 = new Problem("urn:test", "Test", 404, "Detail", "/test");

        // Act & Assert
        Assert.NotEqual(problem1, problem2);
        Assert.False(problem1 == problem2);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates a model using DataAnnotations validation.
    /// </summary>
    private static List<ValidationResult> ValidateModel(object model)
    {
        var context = new ValidationContext(model, null, null);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    #endregion
}
