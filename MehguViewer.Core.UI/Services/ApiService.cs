using System.Net.Http.Json;
using System.Text.Json;
using MehguViewer.Shared.Models;

namespace MehguViewer.Core.UI.Services;

/// <summary>
/// Comprehensive API service for all backend communication.
/// Provides typed methods for all API endpoints with proper error handling.
/// </summary>
public class ApiService
{
    private readonly HttpClient _http;

    public ApiService(HttpClient http)
    {
        _http = http;
    }

    #region Authentication

    /// <summary>
    /// Authenticate user and return login response with JWT token.
    /// </summary>
    public async Task<ApiResult<LoginResponse>> LoginAsync(string username, string password)
    {
        try
        {
            // Send plaintext password - server validates and verifies with BCrypt
            var request = new LoginRequest(username, password);
            var response = await _http.PostAsJsonAsync("api/v1/auth/login", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                return ApiResult<LoginResponse>.Success(result!);
            }
            
            return await HandleErrorResponse<LoginResponse>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<LoginResponse>.Failure($"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Register a new user.
    /// </summary>
    public async Task<ApiResult<LoginResponse>> RegisterAsync(string username, string password)
    {
        try
        {
            // Send plaintext password - server validates strength and hashes
            var request = new UserCreate(username, password, "User");
            var response = await _http.PostAsJsonAsync("api/v1/auth/register", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                return ApiResult<LoginResponse>.Success(result!);
            }
            
            return await HandleErrorResponse<LoginResponse>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<LoginResponse>.Failure($"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Register a new user with a specific role.
    /// </summary>
    public async Task<ApiResult<LoginResponse>> RegisterAsync(string username, string password, string role)
    {
        try
        {
            // Send plaintext password - server validates strength and hashes
            var request = new UserCreate(username, password, role);
            var response = await _http.PostAsJsonAsync("api/v1/auth/register", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                return ApiResult<LoginResponse>.Success(result!);
            }
            
            return await HandleErrorResponse<LoginResponse>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<LoginResponse>.Failure($"Network error: {ex.Message}");
        }
    }

    #endregion

    #region Series

    /// <summary>
    /// Get list of all series with optional filtering.
    /// </summary>
    public async Task<ApiResult<SeriesListResponse>> GetSeriesListAsync(string? query = null, string? type = null, int? limit = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={type}");
            if (limit.HasValue) queryParams.Add($"limit={limit}");

            var url = queryParams.Count > 0 ? $"api/v1/series?{string.Join("&", queryParams)}" : "api/v1/series";
            var result = await _http.GetFromJsonAsync<SeriesListResponse>(url);
            return ApiResult<SeriesListResponse>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<SeriesListResponse>.Failure($"Failed to load series: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a single series by ID.
    /// </summary>
    public async Task<ApiResult<Series>> GetSeriesAsync(string seriesId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<Series>($"api/v1/series/{seriesId}");
            return ApiResult<Series>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiResult<Series>.Failure("Series not found", 404);
        }
        catch (Exception ex)
        {
            return ApiResult<Series>.Failure($"Failed to load series: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a new series.
    /// </summary>
    public async Task<ApiResult<Series>> CreateSeriesAsync(SeriesCreate request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/series", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Series>();
                return ApiResult<Series>.Success(result!);
            }
            return await HandleErrorResponse<Series>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<Series>.Failure($"Failed to create series: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a series by ID.
    /// </summary>
    public async Task<ApiResult<bool>> DeleteSeriesAsync(string seriesId)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/v1/series/{seriesId}");
            if (response.IsSuccessStatusCode)
            {
                return ApiResult<bool>.Success(true);
            }
            return await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to delete series: {ex.Message}");
        }
    }

    /// <summary>
    /// Search series with query parameters.
    /// </summary>
    public async Task<ApiResult<SearchResults>> SearchSeriesAsync(string? query = null, string? type = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={type}");

            var url = queryParams.Count > 0 ? $"api/v1/search?{string.Join("&", queryParams)}" : "api/v1/search";
            var result = await _http.GetFromJsonAsync<SearchResults>(url);
            return ApiResult<SearchResults>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<SearchResults>.Failure($"Search failed: {ex.Message}");
        }
    }

    #endregion

    #region Units/Chapters

    /// <summary>
    /// Get units/chapters for a series.
    /// </summary>
    public async Task<ApiResult<UnitListResponse>> GetUnitsAsync(string seriesId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<UnitListResponse>($"api/v1/series/{seriesId}/units");
            return ApiResult<UnitListResponse>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<UnitListResponse>.Failure($"Failed to load chapters: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a new unit/chapter.
    /// </summary>
    public async Task<ApiResult<Unit>> CreateUnitAsync(string seriesId, UnitCreate request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/v1/series/{seriesId}/units", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Unit>();
                return ApiResult<Unit>.Success(result!);
            }
            return await HandleErrorResponse<Unit>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<Unit>.Failure($"Failed to create chapter: {ex.Message}");
        }
    }

    /// <summary>
    /// Get pages for a unit/chapter.
    /// </summary>
    public async Task<ApiResult<IEnumerable<Page>>> GetPagesAsync(string seriesId, string unitId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<IEnumerable<Page>>($"api/v1/series/{seriesId}/units/{unitId}/pages");
            return ApiResult<IEnumerable<Page>>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<IEnumerable<Page>>.Failure($"Failed to load pages: {ex.Message}");
        }
    }

    #endregion

    #region Users

    /// <summary>
    /// Get list of all users (admin only).
    /// </summary>
    public async Task<ApiResult<User[]>> GetUsersAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<User[]>("api/v1/users");
            return ApiResult<User[]>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<User[]>.Failure($"Failed to load users: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a new user (admin only).
    /// </summary>
    public async Task<ApiResult<User>> CreateUserAsync(string username, string password, string role)
    {
        try
        {
            // Send plaintext password - server validates strength and hashes
            var request = new UserCreate(username, password, role);
            var response = await _http.PostAsJsonAsync("api/v1/users", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<User>();
                return ApiResult<User>.Success(result!);
            }
            return await HandleErrorResponse<User>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<User>.Failure($"Failed to create user: {ex.Message}");
        }
    }

    /// <summary>
    /// Update a user (admin only).
    /// </summary>
    public async Task<ApiResult<User>> UpdateUserAsync(string userId, UserUpdate update)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync($"api/v1/users/{userId}", update);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<User>();
                return ApiResult<User>.Success(result!);
            }
            return await HandleErrorResponse<User>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<User>.Failure($"Failed to update user: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a user (admin only).
    /// </summary>
    public async Task<ApiResult<bool>> DeleteUserAsync(string userId)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/v1/users/{userId}");
            return response.IsSuccessStatusCode 
                ? ApiResult<bool>.Success(true) 
                : await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to delete user: {ex.Message}");
        }
    }

    #endregion

    #region Jobs

    /// <summary>
    /// Get all jobs with optional limit.
    /// </summary>
    public async Task<ApiResult<JobListResponse>> GetJobsAsync(int limit = 20)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<JobListResponse>($"api/v1/jobs?limit={limit}");
            return ApiResult<JobListResponse>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<JobListResponse>.Failure($"Failed to load jobs: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a single job by ID.
    /// </summary>
    public async Task<ApiResult<Job>> GetJobAsync(string jobId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<Job>($"api/v1/jobs/{jobId}");
            return ApiResult<Job>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<Job>.Failure($"Failed to load job: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancel a job.
    /// </summary>
    public async Task<ApiResult<bool>> CancelJobAsync(string jobId)
    {
        try
        {
            var response = await _http.PostAsync($"api/v1/jobs/{jobId}/cancel", null);
            return response.IsSuccessStatusCode 
                ? ApiResult<bool>.Success(true) 
                : await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to cancel job: {ex.Message}");
        }
    }

    /// <summary>
    /// Retry a failed job.
    /// </summary>
    public async Task<ApiResult<Job>> RetryJobAsync(string jobId)
    {
        try
        {
            var response = await _http.PostAsync($"api/v1/jobs/{jobId}/retry", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Job>();
                return ApiResult<Job>.Success(result!);
            }
            return await HandleErrorResponse<Job>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<Job>.Failure($"Failed to retry job: {ex.Message}");
        }
    }

    #endregion

    #region System

    /// <summary>
    /// Get system configuration.
    /// </summary>
    public async Task<ApiResult<SystemConfig>> GetSystemConfigAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<SystemConfig>("api/v1/admin/configuration");
            return ApiResult<SystemConfig>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<SystemConfig>.Failure($"Failed to load config: {ex.Message}");
        }
    }

    /// <summary>
    /// Alias for GetSystemConfigAsync for consistency.
    /// </summary>
    public Task<ApiResult<SystemConfig>> GetConfigurationAsync() => GetSystemConfigAsync();

    /// <summary>
    /// Update system configuration.
    /// </summary>
    public async Task<ApiResult<SystemConfig>> UpdateSystemConfigAsync(SystemConfig config)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync("api/v1/admin/configuration", config);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SystemConfig>();
                return ApiResult<SystemConfig>.Success(result!);
            }
            return await HandleErrorResponse<SystemConfig>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<SystemConfig>.Failure($"Failed to update config: {ex.Message}");
        }
    }

    /// <summary>
    /// Alias for UpdateSystemConfigAsync for consistency.
    /// </summary>
    public Task<ApiResult<SystemConfig>> UpdateConfigurationAsync(SystemConfig config) => UpdateSystemConfigAsync(config);

    /// <summary>
    /// Get system statistics.
    /// </summary>
    public async Task<ApiResult<SystemStats>> GetSystemStatsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<SystemStats>("api/v1/admin/stats");
            return ApiResult<SystemStats>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<SystemStats>.Failure($"Failed to load stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Get storage statistics and settings.
    /// </summary>
    public async Task<ApiResult<StorageStatsResponse>> GetStorageStatsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<StorageStatsResponse>("api/v1/admin/storage");
            return ApiResult<StorageStatsResponse>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<StorageStatsResponse>.Failure($"Failed to load storage stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Update storage settings.
    /// </summary>
    public async Task<ApiResult<StorageStatsResponse>> UpdateStorageSettingsAsync(StorageSettingsUpdate settings)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync("api/v1/admin/storage", settings);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StorageStatsResponse>();
                return ApiResult<StorageStatsResponse>.Success(result!);
            }
            return await HandleErrorResponse<StorageStatsResponse>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<StorageStatsResponse>.Failure($"Failed to update storage settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Get node metadata.
    /// </summary>
    public async Task<ApiResult<NodeMetadata>> GetNodeMetadataAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<NodeMetadata>(".well-known/mehgu-node");
            return ApiResult<NodeMetadata>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<NodeMetadata>.Failure($"Failed to load metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Update node metadata.
    /// </summary>
    public async Task<ApiResult<NodeMetadata>> UpdateNodeMetadataAsync(NodeMetadata metadata)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("api/v1/system/metadata", metadata);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<NodeMetadata>();
                return ApiResult<NodeMetadata>.Success(result!);
            }
            return await HandleErrorResponse<NodeMetadata>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<NodeMetadata>.Failure($"Failed to update metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Alias for UpdateNodeMetadataAsync for consistency.
    /// </summary>
    public Task<ApiResult<NodeMetadata>> UpdateMetadataAsync(NodeMetadata metadata) => UpdateNodeMetadataAsync(metadata);

    /// <summary>
    /// Check if setup is complete.
    /// </summary>
    public async Task<ApiResult<bool>> IsSetupCompleteAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<SetupStatusResponse>("api/v1/system/setup-status");
            return ApiResult<bool>.Success(result?.is_setup_complete ?? false);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to check setup status: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear the asset cache.
    /// </summary>
    public async Task<ApiResult<bool>> ClearCacheAsync()
    {
        try
        {
            var response = await _http.PostAsync("api/v1/admin/storage/clear-cache", null);
            return response.IsSuccessStatusCode 
                ? ApiResult<bool>.Success(true) 
                : await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to clear cache: {ex.Message}");
        }
    }

    #endregion

    #region Progress & Library

    /// <summary>
    /// Update reading progress for a series.
    /// </summary>
    public async Task<ApiResult<bool>> UpdateProgressAsync(string seriesId, ProgressUpdate progress)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"api/v1/series/{seriesId}/progress", progress);
            return response.IsSuccessStatusCode 
                ? ApiResult<bool>.Success(true) 
                : await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to update progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Get reading progress for a series.
    /// </summary>
    public async Task<ApiResult<ReadingProgress>> GetProgressAsync(string seriesId)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<ReadingProgress>($"api/v1/series/{seriesId}/progress");
            return ApiResult<ReadingProgress>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiResult<ReadingProgress>.Failure("No progress found", 404);
        }
        catch (Exception ex)
        {
            return ApiResult<ReadingProgress>.Failure($"Failed to get progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Get user's library.
    /// </summary>
    public async Task<ApiResult<IEnumerable<ReadingProgress>>> GetLibraryAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<IEnumerable<ReadingProgress>>("api/v1/me/library");
            return ApiResult<IEnumerable<ReadingProgress>>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<IEnumerable<ReadingProgress>>.Failure($"Failed to load library: {ex.Message}");
        }
    }

    /// <summary>
    /// Get user's reading history.
    /// </summary>
    public async Task<ApiResult<HistoryListResponse>> GetHistoryAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<HistoryListResponse>("api/v1/me/history");
            return ApiResult<HistoryListResponse>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<HistoryListResponse>.Failure($"Failed to load history: {ex.Message}");
        }
    }

    #endregion

    #region Collections

    /// <summary>
    /// Get user's collections.
    /// </summary>
    public async Task<ApiResult<Collection[]>> GetCollectionsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<Collection[]>("api/v1/collections");
            return ApiResult<Collection[]>.Success(result!);
        }
        catch (Exception ex)
        {
            return ApiResult<Collection[]>.Failure($"Failed to load collections: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a new collection.
    /// </summary>
    public async Task<ApiResult<Collection>> CreateCollectionAsync(string name)
    {
        try
        {
            var request = new CollectionCreate(name);
            var response = await _http.PostAsJsonAsync("api/v1/collections", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Collection>();
                return ApiResult<Collection>.Success(result!);
            }
            return await HandleErrorResponse<Collection>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<Collection>.Failure($"Failed to create collection: {ex.Message}");
        }
    }

    /// <summary>
    /// Add item to collection.
    /// </summary>
    public async Task<ApiResult<bool>> AddToCollectionAsync(string collectionId, string targetUrn)
    {
        try
        {
            var request = new CollectionItemAdd(targetUrn);
            var response = await _http.PostAsJsonAsync($"api/v1/collections/{collectionId}/items", request);
            return response.IsSuccessStatusCode 
                ? ApiResult<bool>.Success(true) 
                : await HandleErrorResponse<bool>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<bool>.Failure($"Failed to add to collection: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    private async Task<ApiResult<T>> HandleErrorResponse<T>(HttpResponseMessage response)
    {
        try
        {
            var problemJson = await response.Content.ReadAsStringAsync();
            var problem = JsonSerializer.Deserialize<Problem>(problemJson);
            return ApiResult<T>.Failure(problem?.title ?? "An error occurred", (int)response.StatusCode, problem);
        }
        catch
        {
            return ApiResult<T>.Failure($"Error: {response.StatusCode}", (int)response.StatusCode);
        }
    }

    #endregion
}

/// <summary>
/// Generic API result wrapper for consistent error handling.
/// </summary>
public class ApiResult<T>
{
    public bool IsSuccess { get; private set; }
    public T? Data { get; private set; }
    public string? Error { get; private set; }
    public int StatusCode { get; private set; }
    public Problem? Problem { get; private set; }

    private ApiResult() { }

    public static ApiResult<T> Success(T data)
    {
        return new ApiResult<T>
        {
            IsSuccess = true,
            Data = data,
            StatusCode = 200
        };
    }

    public static ApiResult<T> Failure(string error, int statusCode = 500, Problem? problem = null)
    {
        return new ApiResult<T>
        {
            IsSuccess = false,
            Error = error,
            StatusCode = statusCode,
            Problem = problem
        };
    }
}
