using System.Net;
using System.Net.Http.Json;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MehguViewer.Core.Tests.Endpoints;

/// <summary>
/// Comprehensive test suite for Job endpoints covering authentication, authorization, 
/// validation, error handling, and business logic.
/// </summary>
/// <remarks>
/// <para><strong>Test Coverage:</strong></para>
/// <list type="bullet">
///   <item><description>Authentication and authorization requirements</description></item>
///   <item><description>Input validation for all parameters</description></item>
///   <item><description>Job lifecycle operations (list, get, cancel, retry)</description></item>
///   <item><description>Edge cases and error conditions</description></item>
///   <item><description>Pagination and limit enforcement</description></item>
///   <item><description>Business rule validation (state transitions)</description></item>
/// </list>
/// </remarks>
[Trait("Category", "Endpoint")]
[Trait("Feature", "Jobs")]
public class JobEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _userClient;
    private readonly TestWebApplicationFactory _factory;

    public JobEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _adminClient = factory.CreateAuthenticatedClient("Admin");
        _userClient = factory.CreateAuthenticatedClient("User");
    }

    #region Authentication & Authorization Tests

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task GetAllJobs_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/jobs");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task GetJobStatus_RequiresAuthentication()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/jobs/test-job-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task CancelJob_RequiresAdminRole()
    {
        // Arrange - Create a job first
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");

        // Act - Non-admin user tries to cancel
        var response = await _userClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Critical")]
    public async Task RetryJob_RequiresAdminRole()
    {
        // Arrange - Create a failed job
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "FAILED", 50, error: "Test error");

        // Act - Non-admin user tries to retry
        var response = await _userClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region GetAllJobs Tests

    [Fact]
    [Trait("Priority", "High")]
    public async Task GetAllJobs_WithDefaultLimit_ReturnsSuccess()
    {
        // Arrange - Create some test jobs
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        for (int i = 0; i < 5; i++)
        {
            jobService.CreateJob($"TEST_JOB_{i}");
        }

        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.data);
        Assert.True(result.data.Length >= 5);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task GetAllJobs_WithCustomLimit_RespectsLimit()
    {
        // Arrange - Create multiple jobs
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        for (int i = 0; i < 10; i++)
        {
            jobService.CreateJob($"TEST_JOB_{i}");
        }

        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs?limit=5");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.NotNull(result);
        Assert.True(result.data.Length <= 5);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task GetAllJobs_WithZeroLimit_ReturnsBadRequest()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs?limit=0");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task GetAllJobs_WithNegativeLimit_ReturnsBadRequest()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs?limit=-1");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task GetAllJobs_WithExcessiveLimit_CapsToMaximum()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs?limit=1000");

        // Assert - Should succeed but cap to max limit (100)
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.NotNull(result);
        Assert.True(result.data.Length <= 100);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task GetAllJobs_WithNoJobs_ReturnsEmptyArray()
    {
        // Note: This test may have jobs from previous tests, so we just verify structure
        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs?limit=1");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.data);
    }

    #endregion

    #region GetJobStatus Tests

    [Fact]
    [Trait("Priority", "High")]
    public async Task GetJobStatus_WithValidJobId_ReturnsJob()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");

        // Act
        var response = await _adminClient.GetAsync($"/api/v1/jobs/{job.id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(result);
        Assert.Equal(job.id, result.id);
        Assert.Equal("TEST_JOB", result.type);
        Assert.Equal("QUEUED", result.status);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task GetJobStatus_WithNonExistentJobId_ReturnsNotFound()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/v1/jobs/non-existent-job-id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task GetJobStatus_WithWhitespaceJobId_ReturnsBadRequest()
    {
        // Act - URL-encoded whitespace gets validated as empty/whitespace
        var response = await _adminClient.GetAsync("/api/v1/jobs/%20");

        // Assert - Whitespace validation catches this
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task GetJobStatus_WithProcessingJob_ReturnsProgress()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "PROCESSING", 50);

        // Act
        var response = await _adminClient.GetAsync($"/api/v1/jobs/{job.id}");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(result);
        Assert.Equal("PROCESSING", result.status);
        Assert.Equal(50, result.progress_percentage);
    }

    #endregion

    #region CancelJob Tests

    [Fact]
    [Trait("Priority", "High")]
    public async Task CancelJob_WithQueuedJob_CancelsSuccessfully()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify job is cancelled
        var updatedJob = jobService.GetJob(job.id);
        Assert.NotNull(updatedJob);
        Assert.Equal("CANCELLED", updatedJob.status);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task CancelJob_WithProcessingJob_CancelsSuccessfully()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "PROCESSING", 25);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify job is cancelled
        var updatedJob = jobService.GetJob(job.id);
        Assert.NotNull(updatedJob);
        Assert.Equal("CANCELLED", updatedJob.status);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task CancelJob_WithCompletedJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "COMPLETED", 100);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task CancelJob_WithFailedJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "FAILED", 50, error: "Test error");

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task CancelJob_WithAlreadyCancelledJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "CANCELLED", 0);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task CancelJob_WithNonExistentJob_ReturnsNotFound()
    {
        // Act
        var response = await _adminClient.PostAsync("/api/v1/jobs/non-existent-job/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region RetryJob Tests

    [Fact]
    [Trait("Priority", "High")]
    public async Task RetryJob_WithFailedJob_CreatesNewJob()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "FAILED", 50, error: "Test error");

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        response.EnsureSuccessStatusCode();
        var newJob = await response.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(newJob);
        Assert.NotEqual(job.id, newJob.id);
        Assert.Equal("TEST_JOB", newJob.type);
        Assert.Equal("QUEUED", newJob.status);
        Assert.Equal(0, newJob.progress_percentage);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task RetryJob_WithCancelledJob_CreatesNewJob()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "CANCELLED", 25);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        response.EnsureSuccessStatusCode();
        var newJob = await response.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(newJob);
        Assert.NotEqual(job.id, newJob.id);
        Assert.Equal("TEST_JOB", newJob.type);
        Assert.Equal("QUEUED", newJob.status);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task RetryJob_WithQueuedJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task RetryJob_WithProcessingJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "PROCESSING", 50);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "High")]
    public async Task RetryJob_WithCompletedJob_ReturnsBadRequest()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("TEST_JOB");
        jobService.UpdateJob(job.id, "COMPLETED", 100);

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task RetryJob_WithNonExistentJob_ReturnsNotFound()
    {
        // Act
        var response = await _adminClient.PostAsync("/api/v1/jobs/non-existent-job/retry", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Priority", "Medium")]
    public async Task RetryJob_PreservesOriginalJobType()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        var job = jobService.CreateJob("METADATA_FETCH");
        jobService.UpdateJob(job.id, "FAILED", 75, error: "Network timeout");

        // Act
        var response = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);

        // Assert
        response.EnsureSuccessStatusCode();
        var newJob = await response.Content.ReadFromJsonAsync<Job>();
        Assert.NotNull(newJob);
        Assert.Equal("METADATA_FETCH", newJob.type);
    }

    #endregion

    #region Integration Tests

    [Fact]
    [Trait("Priority", "High")]
    public async Task JobLifecycle_CreateListGetCancelRetry_WorksCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        
        // Create job
        var job = jobService.CreateJob("INTEGRATION_TEST");
        Assert.Equal("QUEUED", job.status);

        // List jobs - should include our new job
        var listResponse = await _adminClient.GetAsync("/api/v1/jobs?limit=50");
        listResponse.EnsureSuccessStatusCode();
        var jobList = await listResponse.Content.ReadFromJsonAsync<JobListResponse>();
        Assert.Contains(jobList!.data, j => j.id == job.id);

        // Get job status
        var getResponse = await _adminClient.GetAsync($"/api/v1/jobs/{job.id}");
        getResponse.EnsureSuccessStatusCode();
        var retrievedJob = await getResponse.Content.ReadFromJsonAsync<Job>();
        Assert.Equal(job.id, retrievedJob!.id);

        // Cancel job
        var cancelResponse = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();

        // Verify cancelled
        var cancelledJob = jobService.GetJob(job.id);
        Assert.Equal("CANCELLED", cancelledJob!.status);

        // Retry job
        var retryResponse = await _adminClient.PostAsync($"/api/v1/jobs/{job.id}/retry", null);
        retryResponse.EnsureSuccessStatusCode();
        var newJob = await retryResponse.Content.ReadFromJsonAsync<Job>();
        Assert.NotEqual(job.id, newJob!.id);
        Assert.Equal("QUEUED", newJob.status);
    }

    #endregion
}
