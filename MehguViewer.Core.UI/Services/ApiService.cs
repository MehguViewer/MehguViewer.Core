using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MehguViewer.Core.Shared;

namespace MehguViewer.Core.UI.Services;

/// <summary>
/// Comprehensive API service for all backend communication.
/// Provides typed methods for all API endpoints with proper error handling, logging, and security.
/// </summary>
/// <remarks>
/// This service handles:
/// - Authentication and authorization
/// - Series and content management
/// - User management and progress tracking
/// - System configuration and monitoring
/// - Social features (comments, votes, collections)
/// All methods return ApiResult{T} for consistent error handling.
/// </remarks>
public class ApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<ApiService> _logger;

    /// <summary>
    /// Initializes a new instance of the ApiService with HTTP client and logger.
    /// </summary>
    /// <param name="http">Configured HTTP client for backend communication.</param>
    /// <param name="logger">Logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when http or logger is null.</exception>
    public ApiService(HttpClient http, ILogger<ApiService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogDebug("ApiService initialized with base address: {BaseAddress}", _http.BaseAddress);
    }

    #region Authentication

    /// <summary>
    /// Authenticates a user with username and password credentials.
    /// </summary>
    /// <param name="username">The username for authentication. Cannot be null or empty.</param>
    /// <param name="password">The plaintext password. Server performs BCrypt validation.</param>
    /// <returns>ApiResult containing LoginResponse with JWT token on success.</returns>
    /// <remarks>
    /// Security: Password is sent as plaintext over HTTPS. Server validates and hashes with BCrypt.
    /// The returned JWT token should be stored securely and used for subsequent requests.
    /// </remarks>
    public async Task<ApiResult<LoginResponse>> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("Login attempt with empty username");
            return ApiResult<LoginResponse>.Failure("Username cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Login attempt with empty password for user: {Username}", username);
            return ApiResult<LoginResponse>.Failure("Password cannot be empty", 400);
        }

        _logger.LogInformation("Attempting login for user: {Username}", username);
        
        try
        {
            // Send plaintext password - server validates and verifies with BCrypt
            var request = new LoginRequest(username, password);
            var response = await _http.PostAsJsonAsync("api/v1/auth/login", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                _logger.LogInformation("User {Username} logged in successfully", username);
                return ApiResult<LoginResponse>.Success(result!);
            }
            
            _logger.LogWarning("Login failed for user {Username} with status code: {StatusCode}", 
                username, response.StatusCode);
            return await HandleErrorResponse<LoginResponse>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during login for user: {Username}", username);
            return ApiResult<LoginResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for user: {Username}", username);
            return ApiResult<LoginResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers a new user account with default "User" role.
    /// </summary>
    /// <param name="username">Desired username. Must meet server validation requirements.</param>
    /// <param name="password">Plaintext password. Server validates strength and hashes with BCrypt.</param>
    /// <returns>ApiResult containing LoginResponse with JWT token for immediate login on success.</returns>
    /// <remarks>
    /// Security: Password strength is validated server-side. 
    /// Successful registration automatically logs the user in.
    /// </remarks>
    public async Task<ApiResult<LoginResponse>> RegisterAsync(string username, string password)
    {
        return await RegisterAsync(username, password, "User");
    }

    /// <summary>
    /// Registers a new user account with specified role.
    /// </summary>
    /// <param name="username">Desired username. Must meet server validation requirements.</param>
    /// <param name="password">Plaintext password. Server validates strength and hashes with BCrypt.</param>
    /// <param name="role">User role (e.g., "User", "Admin"). Server validates role permissions.</param>
    /// <returns>ApiResult containing LoginResponse with JWT token for immediate login on success.</returns>
    /// <remarks>
    /// Security: Role assignment may require elevated privileges depending on server configuration.
    /// Password strength is validated server-side before account creation.
    /// </remarks>
    public async Task<ApiResult<LoginResponse>> RegisterAsync(string username, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("Registration attempt with empty username");
            return ApiResult<LoginResponse>.Failure("Username cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Registration attempt with empty password for username: {Username}", username);
            return ApiResult<LoginResponse>.Failure("Password cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            _logger.LogWarning("Registration attempt with empty role for username: {Username}", username);
            return ApiResult<LoginResponse>.Failure("Role cannot be empty", 400);
        }

        _logger.LogInformation("Attempting registration for user: {Username} with role: {Role}", username, role);
        
        try
        {
            // Send plaintext password - server validates strength and hashes
            var request = new UserCreate(username, password, role);
            var response = await _http.PostAsJsonAsync("api/v1/auth/register", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                _logger.LogInformation("User {Username} registered successfully with role: {Role}", username, role);
                return ApiResult<LoginResponse>.Success(result!);
            }
            
            _logger.LogWarning("Registration failed for user {Username} with status code: {StatusCode}", 
                username, response.StatusCode);
            return await HandleErrorResponse<LoginResponse>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during registration for user: {Username}", username);
            return ApiResult<LoginResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration for user: {Username}", username);
            return ApiResult<LoginResponse>.Failure($"Unexpected error: {ex.Message}");
        }
    }

    #endregion

    #region Series

    /// <summary>
    /// Retrieves a paginated list of series with optional filtering.
    /// </summary>
    /// <param name="query">Optional search query to filter series by title or metadata.</param>
    /// <param name="type">Optional content type filter (e.g., "manga", "novel").</param>
    /// <param name="limit">Optional maximum number of results to return.</param>
    /// <returns>ApiResult containing SeriesListResponse with matching series.</returns>
    /// <remarks>
    /// Performance: Use limit parameter to control response size and improve load times.
    /// Caching: Consider caching results for frequently accessed queries.
    /// </remarks>
    public async Task<ApiResult<SeriesListResponse>> GetSeriesListAsync(string? query = null, string? type = null, int? limit = null)
    {
        _logger.LogDebug("Fetching series list. Query: {Query}, Type: {Type}, Limit: {Limit}", 
            query ?? "none", type ?? "none", limit?.ToString() ?? "none");
        
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={type}");
            if (limit.HasValue && limit > 0) queryParams.Add($"limit={limit}");

            var url = queryParams.Count > 0 ? $"api/v1/series?{string.Join("&", queryParams)}" : "api/v1/series";
            
            _logger.LogTrace("Requesting series from URL: {Url}", url);
            var result = await _http.GetFromJsonAsync<SeriesListResponse>(url);
            
            _logger.LogInformation("Successfully fetched {Count} series", result?.data?.Length ?? 0);
            return ApiResult<SeriesListResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching series list");
            return ApiResult<SeriesListResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching series list");
            return ApiResult<SeriesListResponse>.Failure($"Failed to load series: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves detailed information for a specific series.
    /// </summary>
    /// <param name="seriesId">Unique identifier of the series.</param>
    /// <returns>ApiResult containing Series details on success.</returns>
    /// <exception cref="ArgumentException">Thrown when seriesId is null or empty.</exception>
    public async Task<ApiResult<Series>> GetSeriesAsync(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("GetSeriesAsync called with empty seriesId");
            return ApiResult<Series>.Failure("Series ID cannot be empty", 400);
        }

        _logger.LogDebug("Fetching series details for ID: {SeriesId}", seriesId);
        
        try
        {
            var result = await _http.GetFromJsonAsync<Series>($"api/v1/series/{seriesId}");
            _logger.LogInformation("Successfully fetched series: {SeriesId}", seriesId);
            return ApiResult<Series>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Series not found: {SeriesId}", seriesId);
            return ApiResult<Series>.Failure("Series not found", 404);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching series: {SeriesId}", seriesId);
            return ApiResult<Series>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching series: {SeriesId}", seriesId);
            return ApiResult<Series>.Failure($"Failed to load series: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new series with the provided metadata.
    /// </summary>
    /// <param name="request">Series creation request containing title, type, and metadata.</param>
    /// <returns>ApiResult containing newly created Series on success.</returns>
    /// <remarks>
    /// Security: Requires appropriate authorization. User must have content creation permissions.
    /// Validation: Server validates all metadata fields before creation.
    /// </remarks>
    public async Task<ApiResult<Series>> CreateSeriesAsync(SeriesCreate request)
    {
        if (request == null)
        {
            _logger.LogWarning("CreateSeriesAsync called with null request");
            return ApiResult<Series>.Failure("Series creation request cannot be null", 400);
        }

        _logger.LogInformation("Creating new series: {Title}", request.title ?? "untitled");
        
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/series", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Series>();
                _logger.LogInformation("Successfully created series with ID: {SeriesId}", result?.id);
                return ApiResult<Series>.Success(result!);
            }
            
            _logger.LogWarning("Series creation failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<Series>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during series creation");
            return ApiResult<Series>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during series creation");
            return ApiResult<Series>.Failure($"Failed to create series: {ex.Message}");
        }
    }

    /// <summary>
    /// Permanently deletes a series and all associated content.
    /// </summary>
    /// <param name="seriesId">Unique identifier of the series to delete.</param>
    /// <returns>ApiResult with true on successful deletion.</returns>
    /// <remarks>
    /// Security: Requires admin or content owner permissions.
    /// Warning: This operation is irreversible and deletes all units/chapters and pages.
    /// </remarks>
    public async Task<ApiResult<bool>> DeleteSeriesAsync(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("DeleteSeriesAsync called with empty seriesId");
            return ApiResult<bool>.Failure("Series ID cannot be empty", 400);
        }

        _logger.LogWarning("Attempting to delete series: {SeriesId}", seriesId);
        
        try
        {
            var response = await _http.DeleteAsync($"api/v1/series/{seriesId}");
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully deleted series: {SeriesId}", seriesId);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogError("Failed to delete series {SeriesId} with status: {StatusCode}", 
                seriesId, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during series deletion: {SeriesId}", seriesId);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during series deletion: {SeriesId}", seriesId);
            return ApiResult<bool>.Failure($"Failed to delete series: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches for series using advanced query parameters.
    /// </summary>
    /// <param name="query">Search query text.</param>
    /// <param name="type">Optional content type filter.</param>
    /// <returns>ApiResult containing SearchResults with matching series.</returns>
    /// <remarks>
    /// Performance: Search is optimized with server-side indexing.
    /// Use specific queries to improve search accuracy and speed.
    /// </remarks>
    public async Task<ApiResult<SearchResults>> SearchSeriesAsync(string? query = null, string? type = null)
    {
        _logger.LogDebug("Searching series. Query: {Query}, Type: {Type}", 
            query ?? "none", type ?? "none");
        
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={type}");

            var url = queryParams.Count > 0 ? $"api/v1/search?{string.Join("&", queryParams)}" : "api/v1/search";
            
            _logger.LogTrace("Searching with URL: {Url}", url);
            var result = await _http.GetFromJsonAsync<SearchResults>(url);
            
            _logger.LogInformation("Search returned {Count} results", result?.data?.Length ?? 0);
            return ApiResult<SearchResults>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during search");
            return ApiResult<SearchResults>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during search");
            return ApiResult<SearchResults>.Failure($"Search failed: {ex.Message}");
        }
    }

    #endregion

    #region Units/Chapters

    /// <summary>
    /// Retrieves all units/chapters for a specific series.
    /// </summary>
    /// <param name="seriesId">Unique identifier of the parent series.</param>
    /// <returns>ApiResult containing UnitListResponse with all chapters/units.</returns>
    public async Task<ApiResult<UnitListResponse>> GetUnitsAsync(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("GetUnitsAsync called with empty seriesId");
            return ApiResult<UnitListResponse>.Failure("Series ID cannot be empty", 400);
        }

        _logger.LogDebug("Fetching units for series: {SeriesId}", seriesId);
        
        try
        {
            var result = await _http.GetFromJsonAsync<UnitListResponse>($"api/v1/series/{seriesId}/units");
            _logger.LogInformation("Successfully fetched {Count} units for series: {SeriesId}", 
                result?.data?.Length ?? 0, seriesId);
            return ApiResult<UnitListResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching units for series: {SeriesId}", seriesId);
            return ApiResult<UnitListResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching units for series: {SeriesId}", seriesId);
            return ApiResult<UnitListResponse>.Failure($"Failed to load chapters: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new unit/chapter within a series.
    /// </summary>
    /// <param name="seriesId">Parent series identifier.</param>
    /// <param name="request">Unit creation request with title and metadata.</param>
    /// <returns>ApiResult containing newly created Unit on success.</returns>
    /// <remarks>
    /// Security: Requires content creation permissions for the specified series.
    /// </remarks>
    public async Task<ApiResult<Unit>> CreateUnitAsync(string seriesId, UnitCreate request)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("CreateUnitAsync called with empty seriesId");
            return ApiResult<Unit>.Failure("Series ID cannot be empty", 400);
        }

        if (request == null)
        {
            _logger.LogWarning("CreateUnitAsync called with null request for series: {SeriesId}", seriesId);
            return ApiResult<Unit>.Failure("Unit creation request cannot be null", 400);
        }

        _logger.LogInformation("Creating unit for series: {SeriesId}, Title: {Title}", 
            seriesId, request.title ?? "untitled");
        
        try
        {
            var response = await _http.PostAsJsonAsync($"api/v1/series/{seriesId}/units", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Unit>();
                _logger.LogInformation("Successfully created unit with ID: {UnitId} for series: {SeriesId}", 
                    result?.id, seriesId);
                return ApiResult<Unit>.Success(result!);
            }
            
            _logger.LogWarning("Unit creation failed for series {SeriesId} with status: {StatusCode}", 
                seriesId, response.StatusCode);
            return await HandleErrorResponse<Unit>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during unit creation for series: {SeriesId}", seriesId);
            return ApiResult<Unit>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during unit creation for series: {SeriesId}", seriesId);
            return ApiResult<Unit>.Failure($"Failed to create chapter: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves all pages for a specific unit/chapter.
    /// </summary>
    /// <param name="seriesId">Parent series identifier.</param>
    /// <param name="unitId">Unit/chapter identifier.</param>
    /// <returns>ApiResult containing collection of Page objects.</returns>
    /// <remarks>
    /// Performance: Pages may be large. Consider pagination for better performance.
    /// </remarks>
    public async Task<ApiResult<IEnumerable<Page>>> GetPagesAsync(string seriesId, string unitId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("GetPagesAsync called with empty seriesId");
            return ApiResult<IEnumerable<Page>>.Failure("Series ID cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(unitId))
        {
            _logger.LogWarning("GetPagesAsync called with empty unitId for series: {SeriesId}", seriesId);
            return ApiResult<IEnumerable<Page>>.Failure("Unit ID cannot be empty", 400);
        }

        _logger.LogDebug("Fetching pages for series: {SeriesId}, unit: {UnitId}", seriesId, unitId);
        
        try
        {
            var result = await _http.GetFromJsonAsync<IEnumerable<Page>>($"api/v1/series/{seriesId}/units/{unitId}/pages");
            _logger.LogInformation("Successfully fetched {Count} pages for series: {SeriesId}, unit: {UnitId}", 
                result?.Count() ?? 0, seriesId, unitId);
            return ApiResult<IEnumerable<Page>>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching pages for series: {SeriesId}, unit: {UnitId}", 
                seriesId, unitId);
            return ApiResult<IEnumerable<Page>>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching pages for series: {SeriesId}, unit: {UnitId}", 
                seriesId, unitId);
            return ApiResult<IEnumerable<Page>>.Failure($"Failed to load pages: {ex.Message}");
        }
    }

    #endregion

    #region Users

    /// <summary>
    /// Retrieves list of all registered users.
    /// </summary>
    /// <returns>ApiResult containing array of User objects.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Requires administrator privileges.
    /// Privacy: Contains sensitive user information. Handle with care.
    /// </remarks>
    public async Task<ApiResult<User[]>> GetUsersAsync()
    {
        _logger.LogDebug("Fetching users list");
        
        try
        {
            var result = await _http.GetFromJsonAsync<User[]>("api/v1/users");
            _logger.LogInformation("Successfully fetched {Count} users", result?.Length ?? 0);
            return ApiResult<User[]>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Unauthorized attempt to fetch users list");
            return ApiResult<User[]>.Failure("Access denied: Admin privileges required", 403);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching users");
            return ApiResult<User[]>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching users");
            return ApiResult<User[]>.Failure($"Failed to load users: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new user account with specified credentials and role.
    /// </summary>
    /// <param name="username">Unique username for the new account.</param>
    /// <param name="password">Plaintext password. Server validates and hashes with BCrypt.</param>
    /// <param name="role">User role assignment (e.g., "User", "Admin").</param>
    /// <returns>ApiResult containing created User object on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Password is validated and hashed server-side.
    /// Validation: Username uniqueness and password strength are enforced.
    /// </remarks>
    public async Task<ApiResult<User>> CreateUserAsync(string username, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("CreateUserAsync called with empty username");
            return ApiResult<User>.Failure("Username cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("CreateUserAsync called with empty password for username: {Username}", username);
            return ApiResult<User>.Failure("Password cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            _logger.LogWarning("CreateUserAsync called with empty role for username: {Username}", username);
            return ApiResult<User>.Failure("Role cannot be empty", 400);
        }

        _logger.LogInformation("Creating user: {Username} with role: {Role}", username, role);
        
        try
        {
            // Send plaintext password - server validates strength and hashes
            var request = new UserCreate(username, password, role);
            var response = await _http.PostAsJsonAsync("api/v1/users", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<User>();
                _logger.LogInformation("Successfully created user: {Username} with ID: {UserId}", 
                    username, result?.id);
                return ApiResult<User>.Success(result!);
            }
            
            _logger.LogWarning("User creation failed for {Username} with status: {StatusCode}", 
                username, response.StatusCode);
            return await HandleErrorResponse<User>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during user creation: {Username}", username);
            return ApiResult<User>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during user creation: {Username}", username);
            return ApiResult<User>.Failure($"Failed to create user: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing user's information.
    /// </summary>
    /// <param name="userId">Unique identifier of the user to update.</param>
    /// <param name="update">UserUpdate object containing fields to modify.</param>
    /// <returns>ApiResult containing updated User object on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Partial updates supported.
    /// </remarks>
    public async Task<ApiResult<User>> UpdateUserAsync(string userId, UserUpdate update)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("UpdateUserAsync called with empty userId");
            return ApiResult<User>.Failure("User ID cannot be empty", 400);
        }

        if (update == null)
        {
            _logger.LogWarning("UpdateUserAsync called with null update for user: {UserId}", userId);
            return ApiResult<User>.Failure("Update request cannot be null", 400);
        }

        _logger.LogInformation("Updating user: {UserId}", userId);
        
        try
        {
            var response = await _http.PatchAsJsonAsync($"api/v1/users/{userId}", update);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<User>();
                _logger.LogInformation("Successfully updated user: {UserId}", userId);
                return ApiResult<User>.Success(result!);
            }
            
            _logger.LogWarning("User update failed for {UserId} with status: {StatusCode}", 
                userId, response.StatusCode);
            return await HandleErrorResponse<User>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during user update: {UserId}", userId);
            return ApiResult<User>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during user update: {UserId}", userId);
            return ApiResult<User>.Failure($"Failed to update user: {ex.Message}");
        }
    }

    /// <summary>
    /// Permanently deletes a user account and associated data.
    /// </summary>
    /// <param name="userId">Unique identifier of the user to delete.</param>
    /// <returns>ApiResult with true on successful deletion.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint.
    /// Warning: Irreversible operation. Deletes all user data including progress and collections.
    /// </remarks>
    public async Task<ApiResult<bool>> DeleteUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("DeleteUserAsync called with empty userId");
            return ApiResult<bool>.Failure("User ID cannot be empty", 400);
        }

        _logger.LogWarning("Attempting to delete user: {UserId}", userId);
        
        try
        {
            var response = await _http.DeleteAsync($"api/v1/users/{userId}");
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully deleted user: {UserId}", userId);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogError("User deletion failed for {UserId} with status: {StatusCode}", 
                userId, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during user deletion: {UserId}", userId);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during user deletion: {UserId}", userId);
            return ApiResult<bool>.Failure($"Failed to delete user: {ex.Message}");
        }
    }

    #endregion

    #region Jobs

    /// <summary>
    /// Retrieves a list of background jobs with optional result limiting.
    /// </summary>
    /// <param name="limit">Maximum number of jobs to return. Default is 20.</param>
    /// <returns>ApiResult containing JobListResponse with job details.</returns>
    /// <remarks>
    /// Performance: Use limit parameter to control response size.
    /// Jobs are ordered by creation date (newest first).
    /// </remarks>
    public async Task<ApiResult<JobListResponse>> GetJobsAsync(int limit = 20)
    {
        if (limit <= 0)
        {
            _logger.LogWarning("GetJobsAsync called with invalid limit: {Limit}", limit);
            return ApiResult<JobListResponse>.Failure("Limit must be greater than 0", 400);
        }

        _logger.LogDebug("Fetching jobs list with limit: {Limit}", limit);
        
        try
        {
            var result = await _http.GetFromJsonAsync<JobListResponse>($"api/v1/jobs?limit={limit}");
            _logger.LogInformation("Successfully fetched {Count} jobs", result?.data?.Length ?? 0);
            return ApiResult<JobListResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching jobs");
            return ApiResult<JobListResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching jobs");
            return ApiResult<JobListResponse>.Failure($"Failed to load jobs: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves detailed information for a specific job.
    /// </summary>
    /// <param name="jobId">Unique identifier of the job.</param>
    /// <returns>ApiResult containing Job details including status and progress.</returns>
    public async Task<ApiResult<Job>> GetJobAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("GetJobAsync called with empty jobId");
            return ApiResult<Job>.Failure("Job ID cannot be empty", 400);
        }

        _logger.LogDebug("Fetching job details for ID: {JobId}", jobId);
        
        try
        {
            var result = await _http.GetFromJsonAsync<Job>($"api/v1/jobs/{jobId}");
            _logger.LogInformation("Successfully fetched job: {JobId}, Status: {Status}", 
                jobId, result?.status ?? "unknown");
            return ApiResult<Job>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Job not found: {JobId}", jobId);
            return ApiResult<Job>.Failure("Job not found", 404);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching job: {JobId}", jobId);
            return ApiResult<Job>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching job: {JobId}", jobId);
            return ApiResult<Job>.Failure($"Failed to load job: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to cancel a running or queued job.
    /// </summary>
    /// <param name="jobId">Unique identifier of the job to cancel.</param>
    /// <returns>ApiResult with true if cancellation was successful.</returns>
    /// <remarks>
    /// Note: Already completed or failed jobs cannot be cancelled.
    /// Cancellation may not be immediate for some job types.
    /// </remarks>
    public async Task<ApiResult<bool>> CancelJobAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("CancelJobAsync called with empty jobId");
            return ApiResult<bool>.Failure("Job ID cannot be empty", 400);
        }

        _logger.LogInformation("Attempting to cancel job: {JobId}", jobId);
        
        try
        {
            var response = await _http.PostAsync($"api/v1/jobs/{jobId}/cancel", null);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully cancelled job: {JobId}", jobId);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogWarning("Job cancellation failed for {JobId} with status: {StatusCode}", 
                jobId, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during job cancellation: {JobId}", jobId);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during job cancellation: {JobId}", jobId);
            return ApiResult<bool>.Failure($"Failed to cancel job: {ex.Message}");
        }
    }

    /// <summary>
    /// Retries a failed job by creating a new job instance.
    /// </summary>
    /// <param name="jobId">Unique identifier of the failed job to retry.</param>
    /// <returns>ApiResult containing new Job object created for the retry.</returns>
    /// <remarks>
    /// Only failed jobs can be retried. Creates a new job with same parameters.
    /// </remarks>
    public async Task<ApiResult<Job>> RetryJobAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("RetryJobAsync called with empty jobId");
            return ApiResult<Job>.Failure("Job ID cannot be empty", 400);
        }

        _logger.LogInformation("Attempting to retry job: {JobId}", jobId);
        
        try
        {
            var response = await _http.PostAsync($"api/v1/jobs/{jobId}/retry", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Job>();
                _logger.LogInformation("Successfully retried job: {JobId}, New Job ID: {NewJobId}", 
                    jobId, result?.id);
                return ApiResult<Job>.Success(result!);
            }
            
            _logger.LogWarning("Job retry failed for {JobId} with status: {StatusCode}", 
                jobId, response.StatusCode);
            return await HandleErrorResponse<Job>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during job retry: {JobId}", jobId);
            return ApiResult<Job>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during job retry: {JobId}", jobId);
            return ApiResult<Job>.Failure($"Failed to retry job: {ex.Message}");
        }
    }

    #endregion

    #region System

    /// <summary>
    /// Retrieves current system configuration settings.
    /// </summary>
    /// <returns>ApiResult containing SystemConfig with all configuration parameters.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint.
    /// Contains sensitive system settings.
    /// </remarks>
    public async Task<ApiResult<SystemConfig>> GetSystemConfigAsync()
    {
        _logger.LogDebug("Fetching system configuration");
        
        try
        {
            var result = await _http.GetFromJsonAsync<SystemConfig>("api/v1/admin/configuration");
            _logger.LogInformation("Successfully fetched system configuration");
            return ApiResult<SystemConfig>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching system configuration");
            return ApiResult<SystemConfig>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching system configuration");
            return ApiResult<SystemConfig>.Failure($"Failed to load config: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates system configuration with new settings.
    /// </summary>
    /// <param name="config">SystemConfig object containing updated configuration values.</param>
    /// <returns>ApiResult containing updated SystemConfig on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Changes affect entire system.
    /// Validation: Server validates all configuration values before applying.
    /// </remarks>
    public async Task<ApiResult<SystemConfig>> UpdateSystemConfigAsync(SystemConfig config)
    {
        if (config == null)
        {
            _logger.LogWarning("UpdateSystemConfigAsync called with null config");
            return ApiResult<SystemConfig>.Failure("Configuration cannot be null", 400);
        }

        _logger.LogInformation("Updating system configuration");
        
        try
        {
            var response = await _http.PatchAsJsonAsync("api/v1/admin/configuration", config);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SystemConfig>();
                _logger.LogInformation("Successfully updated system configuration");
                return ApiResult<SystemConfig>.Success(result!);
            }
            
            _logger.LogWarning("System configuration update failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<SystemConfig>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during system configuration update");
            return ApiResult<SystemConfig>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during system configuration update");
            return ApiResult<SystemConfig>.Failure($"Failed to update config: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves system statistics and performance metrics.
    /// </summary>
    /// <returns>ApiResult containing SystemStats with usage and performance data.</returns>
    /// <remarks>
    /// Performance: Cached for short duration to reduce overhead.
    /// </remarks>
    public async Task<ApiResult<SystemStats>> GetSystemStatsAsync()
    {
        _logger.LogDebug("Fetching system statistics");
        
        try
        {
            var result = await _http.GetFromJsonAsync<SystemStats>("api/v1/admin/stats");
            _logger.LogInformation("Successfully fetched system statistics");
            return ApiResult<SystemStats>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching system statistics");
            return ApiResult<SystemStats>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching system statistics");
            return ApiResult<SystemStats>.Failure($"Failed to load stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves storage statistics including disk usage and cache size.
    /// </summary>
    /// <returns>ApiResult containing StorageStatsResponse with storage metrics.</returns>
    public async Task<ApiResult<StorageStatsResponse>> GetStorageStatsAsync()
    {
        _logger.LogDebug("Fetching storage statistics");
        
        try
        {
            var result = await _http.GetFromJsonAsync<StorageStatsResponse>("api/v1/admin/storage");
            _logger.LogInformation("Successfully fetched storage statistics");
            return ApiResult<StorageStatsResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching storage statistics");
            return ApiResult<StorageStatsResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching storage statistics");
            return ApiResult<StorageStatsResponse>.Failure($"Failed to load storage stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates storage-related settings such as cache limits and retention policies.
    /// </summary>
    /// <param name="settings">StorageSettingsUpdate object with new storage settings.</param>
    /// <returns>ApiResult containing updated StorageStatsResponse on success.</returns>
    public async Task<ApiResult<StorageStatsResponse>> UpdateStorageSettingsAsync(StorageSettingsUpdate settings)
    {
        if (settings == null)
        {
            _logger.LogWarning("UpdateStorageSettingsAsync called with null settings");
            return ApiResult<StorageStatsResponse>.Failure("Storage settings cannot be null", 400);
        }

        _logger.LogInformation("Updating storage settings");
        
        try
        {
            var response = await _http.PatchAsJsonAsync("api/v1/admin/storage", settings);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<StorageStatsResponse>();
                _logger.LogInformation("Successfully updated storage settings");
                return ApiResult<StorageStatsResponse>.Success(result!);
            }
            
            _logger.LogWarning("Storage settings update failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<StorageStatsResponse>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during storage settings update");
            return ApiResult<StorageStatsResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during storage settings update");
            return ApiResult<StorageStatsResponse>.Failure($"Failed to update storage settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves node metadata for federation and discovery.
    /// </summary>
    /// <returns>ApiResult containing NodeMetadata with node information.</returns>
    /// <remarks>
    /// Public endpoint accessible without authentication.
    /// Used for federation protocol and node discovery.
    /// </remarks>
    public async Task<ApiResult<NodeMetadata>> GetNodeMetadataAsync()
    {
        _logger.LogDebug("Fetching node metadata");
        
        try
        {
            var result = await _http.GetFromJsonAsync<NodeMetadata>(".well-known/mehgu-node");
            _logger.LogInformation("Successfully fetched node metadata");
            return ApiResult<NodeMetadata>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching node metadata");
            return ApiResult<NodeMetadata>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching node metadata");
            return ApiResult<NodeMetadata>.Failure($"Failed to load metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates node metadata information.
    /// </summary>
    /// <param name="metadata">NodeMetadata object with updated node information.</param>
    /// <returns>ApiResult containing updated NodeMetadata on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Affects node discovery and federation.
    /// </remarks>
    public async Task<ApiResult<NodeMetadata>> UpdateNodeMetadataAsync(NodeMetadata metadata)
    {
        if (metadata == null)
        {
            _logger.LogWarning("UpdateNodeMetadataAsync called with null metadata");
            return ApiResult<NodeMetadata>.Failure("Metadata cannot be null", 400);
        }

        _logger.LogInformation("Updating node metadata");
        
        try
        {
            var response = await _http.PutAsJsonAsync("api/v1/system/metadata", metadata);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<NodeMetadata>();
                _logger.LogInformation("Successfully updated node metadata");
                return ApiResult<NodeMetadata>.Success(result!);
            }
            
            _logger.LogWarning("Node metadata update failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<NodeMetadata>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during node metadata update");
            return ApiResult<NodeMetadata>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during node metadata update");
            return ApiResult<NodeMetadata>.Failure($"Failed to update metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates node metadata information (alias for UpdateNodeMetadataAsync).
    /// </summary>
    /// <param name="metadata">NodeMetadata object with updated node information.</param>
    /// <returns>ApiResult containing updated NodeMetadata on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Affects node discovery and federation.
    /// This is an alias method for backward compatibility.
    /// </remarks>
    public async Task<ApiResult<NodeMetadata>> UpdateMetadataAsync(NodeMetadata metadata)
    {
        return await UpdateNodeMetadataAsync(metadata);
    }

    /// <summary>
    /// Retrieves the current system configuration.
    /// </summary>
    /// <returns>ApiResult containing SystemConfig with current configuration.</returns>
    /// <remarks>
    /// Requires authentication. Returns configuration settings including setup status.
    /// </remarks>
    public async Task<ApiResult<SystemConfig>> GetConfigurationAsync()
    {
        _logger.LogDebug("Fetching system configuration");
        
        try
        {
            var result = await _http.GetFromJsonAsync<SystemConfig>("api/v1/system/config");
            _logger.LogInformation("Successfully fetched system configuration");
            return ApiResult<SystemConfig>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching system configuration");
            return ApiResult<SystemConfig>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching system configuration");
            return ApiResult<SystemConfig>.Failure($"Failed to load configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the system configuration.
    /// </summary>
    /// <param name="config">SystemConfig object with updated configuration.</param>
    /// <returns>ApiResult containing updated SystemConfig on success.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint. Completely replaces the current configuration.
    /// </remarks>
    public async Task<ApiResult<SystemConfig>> UpdateConfigurationAsync(SystemConfig config)
    {
        if (config == null)
        {
            _logger.LogWarning("UpdateConfigurationAsync called with null config");
            return ApiResult<SystemConfig>.Failure("Configuration cannot be null", 400);
        }

        _logger.LogInformation("Updating system configuration");
        
        try
        {
            var response = await _http.PutAsJsonAsync("api/v1/system/config", config);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SystemConfig>();
                _logger.LogInformation("Successfully updated system configuration");
                return ApiResult<SystemConfig>.Success(result!);
            }
            
            _logger.LogWarning("System configuration update failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<SystemConfig>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during system configuration update");
            return ApiResult<SystemConfig>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during system configuration update");
            return ApiResult<SystemConfig>.Failure($"Failed to update configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether initial system setup has been completed.
    /// </summary>
    /// <returns>ApiResult with true if setup is complete, false otherwise.</returns>
    /// <remarks>
    /// Used during initial installation and first-run wizard.
    /// </remarks>
    public async Task<ApiResult<bool>> IsSetupCompleteAsync()
    {
        _logger.LogDebug("Checking setup status");
        
        try
        {
            var result = await _http.GetFromJsonAsync<SetupStatusResponse>("api/v1/system/setup-status");
            var isComplete = result?.is_setup_complete ?? false;
            _logger.LogInformation("Setup status: {Status}", isComplete ? "Complete" : "Incomplete");
            return ApiResult<bool>.Success(isComplete);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while checking setup status");
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while checking setup status");
            return ApiResult<bool>.Failure($"Failed to check setup status: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all cached assets to free up disk space.
    /// </summary>
    /// <returns>ApiResult with true on successful cache clear.</returns>
    /// <remarks>
    /// Security: Admin-only endpoint.
    /// Warning: This will cause temporary performance degradation as cache rebuilds.
    /// </remarks>
    public async Task<ApiResult<bool>> ClearCacheAsync()
    {
        _logger.LogWarning("Attempting to clear asset cache");
        
        try
        {
            var response = await _http.PostAsync("api/v1/admin/storage/clear-cache", null);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully cleared asset cache");
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogError("Cache clear failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during cache clear");
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during cache clear");
            return ApiResult<bool>.Failure($"Failed to clear cache: {ex.Message}");
        }
    }

    #endregion

    #region Progress & Library

    /// <summary>
    /// Updates reading progress for a specific series.
    /// </summary>
    /// <param name="seriesId">Unique identifier of the series.</param>
    /// <param name="progress">ProgressUpdate object containing current reading position.</param>
    /// <returns>ApiResult with true on successful update.</returns>
    /// <remarks>
    /// Tracks user's current chapter/page position for continue reading features.
    /// Progress is user-specific and persisted across sessions.
    /// </remarks>
    public async Task<ApiResult<bool>> UpdateProgressAsync(string seriesId, ProgressUpdate progress)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("UpdateProgressAsync called with empty seriesId");
            return ApiResult<bool>.Failure("Series ID cannot be empty", 400);
        }

        if (progress == null)
        {
            _logger.LogWarning("UpdateProgressAsync called with null progress for series: {SeriesId}", seriesId);
            return ApiResult<bool>.Failure("Progress update cannot be null", 400);
        }

        _logger.LogDebug("Updating progress for series: {SeriesId}", seriesId);
        
        try
        {
            var response = await _http.PutAsJsonAsync($"api/v1/series/{seriesId}/progress", progress);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully updated progress for series: {SeriesId}", seriesId);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogWarning("Progress update failed for series {SeriesId} with status: {StatusCode}", 
                seriesId, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during progress update for series: {SeriesId}", seriesId);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during progress update for series: {SeriesId}", seriesId);
            return ApiResult<bool>.Failure($"Failed to update progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves reading progress for a specific series.
    /// </summary>
    /// <param name="seriesId">Unique identifier of the series.</param>
    /// <returns>ApiResult containing ReadingProgress with current position.</returns>
    public async Task<ApiResult<ReadingProgress>> GetProgressAsync(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            _logger.LogWarning("GetProgressAsync called with empty seriesId");
            return ApiResult<ReadingProgress>.Failure("Series ID cannot be empty", 400);
        }

        _logger.LogDebug("Fetching progress for series: {SeriesId}", seriesId);
        
        try
        {
            var result = await _http.GetFromJsonAsync<ReadingProgress>($"api/v1/series/{seriesId}/progress");
            _logger.LogInformation("Successfully fetched progress for series: {SeriesId}", seriesId);
            return ApiResult<ReadingProgress>.Success(result!);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("No progress found for series: {SeriesId}", seriesId);
            return ApiResult<ReadingProgress>.Failure("No progress found", 404);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching progress for series: {SeriesId}", seriesId);
            return ApiResult<ReadingProgress>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching progress for series: {SeriesId}", seriesId);
            return ApiResult<ReadingProgress>.Failure($"Failed to get progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves user's complete library with all series in progress.
    /// </summary>
    /// <returns>ApiResult containing collection of ReadingProgress for all library items.</returns>
    /// <remarks>
    /// Library includes all series the user has started reading.
    /// Results are ordered by last read timestamp.
    /// </remarks>
    public async Task<ApiResult<IEnumerable<ReadingProgress>>> GetLibraryAsync()
    {
        _logger.LogDebug("Fetching user library");
        
        try
        {
            var result = await _http.GetFromJsonAsync<IEnumerable<ReadingProgress>>("api/v1/me/library");
            _logger.LogInformation("Successfully fetched library with {Count} items", result?.Count() ?? 0);
            return ApiResult<IEnumerable<ReadingProgress>>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching library");
            return ApiResult<IEnumerable<ReadingProgress>>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching library");
            return ApiResult<IEnumerable<ReadingProgress>>.Failure($"Failed to load library: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves user's reading history with recent activity.
    /// </summary>
    /// <returns>ApiResult containing HistoryListResponse with recent reads.</returns>
    /// <remarks>
    /// History shows chronological reading activity.
    /// May include completed and discontinued series.
    /// </remarks>
    public async Task<ApiResult<HistoryListResponse>> GetHistoryAsync()
    {
        _logger.LogDebug("Fetching user reading history");
        
        try
        {
            var result = await _http.GetFromJsonAsync<HistoryListResponse>("api/v1/me/history");
            _logger.LogInformation("Successfully fetched reading history with {Count} items", 
                result?.data?.Length ?? 0);
            return ApiResult<HistoryListResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching history");
            return ApiResult<HistoryListResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching history");
            return ApiResult<HistoryListResponse>.Failure($"Failed to load history: {ex.Message}");
        }
    }

    #endregion

    #region Collections

    /// <summary>
    /// Retrieves all collections owned by the current user.
    /// </summary>
    /// <returns>ApiResult containing array of Collection objects.</returns>
    /// <remarks>
    /// Collections allow users to organize series into custom groups.
    /// </remarks>
    public async Task<ApiResult<Collection[]>> GetCollectionsAsync()
    {
        _logger.LogDebug("Fetching user collections");
        
        try
        {
            var result = await _http.GetFromJsonAsync<Collection[]>("api/v1/collections");
            _logger.LogInformation("Successfully fetched {Count} collections", result?.Length ?? 0);
            return ApiResult<Collection[]>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching collections");
            return ApiResult<Collection[]>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching collections");
            return ApiResult<Collection[]>.Failure($"Failed to load collections: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new collection with the specified name.
    /// </summary>
    /// <param name="name">Display name for the new collection.</param>
    /// <returns>ApiResult containing newly created Collection object.</returns>
    public async Task<ApiResult<Collection>> CreateCollectionAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("CreateCollectionAsync called with empty name");
            return ApiResult<Collection>.Failure("Collection name cannot be empty", 400);
        }

        _logger.LogInformation("Creating collection: {Name}", name);
        
        try
        {
            var request = new CollectionCreate(name);
            var response = await _http.PostAsJsonAsync("api/v1/collections", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Collection>();
                _logger.LogInformation("Successfully created collection: {Name} with ID: {CollectionId}", 
                    name, result?.id);
                return ApiResult<Collection>.Success(result!);
            }
            
            _logger.LogWarning("Collection creation failed for {Name} with status: {StatusCode}", 
                name, response.StatusCode);
            return await HandleErrorResponse<Collection>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during collection creation: {Name}", name);
            return ApiResult<Collection>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during collection creation: {Name}", name);
            return ApiResult<Collection>.Failure($"Failed to create collection: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a series or other content to a collection.
    /// </summary>
    /// <param name="collectionId">Unique identifier of the collection.</param>
    /// <param name="targetUrn">URN of the content to add (e.g., series URN).</param>
    /// <returns>ApiResult with true on successful addition.</returns>
    public async Task<ApiResult<bool>> AddToCollectionAsync(string collectionId, string targetUrn)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            _logger.LogWarning("AddToCollectionAsync called with empty collectionId");
            return ApiResult<bool>.Failure("Collection ID cannot be empty", 400);
        }

        if (string.IsNullOrWhiteSpace(targetUrn))
        {
            _logger.LogWarning("AddToCollectionAsync called with empty targetUrn for collection: {CollectionId}", 
                collectionId);
            return ApiResult<bool>.Failure("Target URN cannot be empty", 400);
        }

        _logger.LogInformation("Adding {TargetUrn} to collection: {CollectionId}", targetUrn, collectionId);
        
        try
        {
            var request = new CollectionItemAdd(targetUrn);
            var response = await _http.PostAsJsonAsync($"api/v1/collections/{collectionId}/items", request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully added {TargetUrn} to collection: {CollectionId}", 
                    targetUrn, collectionId);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogWarning("Failed to add {TargetUrn} to collection {CollectionId} with status: {StatusCode}", 
                targetUrn, collectionId, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error adding {TargetUrn} to collection: {CollectionId}", 
                targetUrn, collectionId);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error adding {TargetUrn} to collection: {CollectionId}", 
                targetUrn, collectionId);
            return ApiResult<bool>.Failure($"Failed to add to collection: {ex.Message}");
        }
    }

    #endregion

    #region Social & Discovery

    /// <summary>
    /// Retrieves taxonomy data including available tags, genres, and content types.
    /// </summary>
    /// <returns>ApiResult containing TaxonomyData with all classification options.</returns>
    /// <remarks>\n    /// Taxonomy data is used for content categorization and filtering.
    /// Performance: Results are typically cached on client side.
    /// </remarks>
    public async Task<ApiResult<TaxonomyData>> GetTaxonomyAsync()
    {
        _logger.LogDebug("Fetching taxonomy data");
        
        try
        {
            var result = await _http.GetFromJsonAsync<TaxonomyData>("api/v1/taxonomy");
            _logger.LogInformation("Successfully fetched taxonomy data");
            return ApiResult<TaxonomyData>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching taxonomy");
            return ApiResult<TaxonomyData>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching taxonomy");
            return ApiResult<TaxonomyData>.Failure($"Failed to load taxonomy: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves comments for a specific target (series, chapter, or parent comment).
    /// </summary>
    /// <param name="targetUrn">URN of the target to fetch comments for.</param>
    /// <param name="depth">Depth of nested replies to fetch (default: 1).</param>
    /// <param name="cursor">Optional pagination cursor for loading more comments.</param>
    /// <returns>ApiResult containing CommentListResponse with comments and pagination info.</returns>
    /// <remarks>
    /// Performance: Use depth=1 for initial load, then load nested replies on demand.
    /// Pagination: Use cursor for loading additional comments.
    /// </remarks>
    public async Task<ApiResult<CommentListResponse>> GetCommentsAsync(string targetUrn, int depth = 1, string? cursor = null)
    {
        if (string.IsNullOrWhiteSpace(targetUrn))
        {
            _logger.LogWarning("GetCommentsAsync called with empty targetUrn");
            return ApiResult<CommentListResponse>.Failure("Target URN cannot be empty", 400);
        }

        if (depth < 0 || depth > 10)
        {
            _logger.LogWarning("GetCommentsAsync called with invalid depth: {Depth}", depth);
            return ApiResult<CommentListResponse>.Failure("Depth must be between 0 and 10", 400);
        }

        _logger.LogDebug("Fetching comments for target: {TargetUrn}, depth: {Depth}", targetUrn, depth);
        
        try
        {
            var queryParams = new List<string>
            {
                $"target_urn={Uri.EscapeDataString(targetUrn)}",
                $"depth={depth}"
            };
            if (!string.IsNullOrEmpty(cursor)) queryParams.Add($"cursor={cursor}");

            var url = $"api/v1/comments?{string.Join("&", queryParams)}";
            var result = await _http.GetFromJsonAsync<CommentListResponse>(url);
            
            _logger.LogInformation("Successfully fetched {Count} comments for target: {TargetUrn}", 
                result?.data?.Length ?? 0, targetUrn);
            return ApiResult<CommentListResponse>.Success(result!);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while fetching comments for target: {TargetUrn}", targetUrn);
            return ApiResult<CommentListResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching comments for target: {TargetUrn}", targetUrn);
            return ApiResult<CommentListResponse>.Failure($"Failed to load comments: {ex.Message}");
        }
    }

    /// <summary>
    /// Posts a new comment on a target (series, chapter, or another comment).
    /// </summary>
    /// <param name="request">CommentCreate object with comment content and target information.</param>
    /// <returns>ApiResult containing newly created Comment on success.</returns>
    /// <remarks>
    /// Security: Requires authentication. Comments are attributed to the authenticated user.
    /// Validation: Server validates content for prohibited content and length limits.
    /// </remarks>
    public async Task<ApiResult<Comment>> PostCommentAsync(CommentCreate request)
    {
        if (request == null)
        {
            _logger.LogWarning("PostCommentAsync called with null request");
            return ApiResult<Comment>.Failure("Comment request cannot be null", 400);
        }

        _logger.LogInformation("Posting comment on target: {TargetUrn}", request.target_urn ?? "unknown");
        
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/comments", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Comment>();
                _logger.LogInformation("Successfully posted comment with ID: {CommentId}", result?.id);
                return ApiResult<Comment>.Success(result!);
            }
            
            _logger.LogWarning("Comment posting failed with status: {StatusCode}", response.StatusCode);
            return await HandleErrorResponse<Comment>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during comment posting");
            return ApiResult<Comment>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during comment posting");
            return ApiResult<Comment>.Failure($"Failed to post comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Casts a vote (upvote/downvote) on a target.
    /// </summary>
    /// <param name="vote">Vote object containing target URN and vote value.</param>
    /// <returns>ApiResult with true on successful vote.</returns>
    /// <remarks>
    /// Voting: Users can change their vote or remove it by voting again.
    /// Validation: Server enforces one vote per user per target.
    /// </remarks>
    public async Task<ApiResult<bool>> CastVoteAsync(Vote vote)
    {
        if (vote == null)
        {
            _logger.LogWarning("CastVoteAsync called with null vote");
            return ApiResult<bool>.Failure("Vote cannot be null", 400);
        }

        _logger.LogInformation("Casting vote on target: {TargetType}/{TargetId}", vote.target_type ?? "unknown", vote.target_id ?? "unknown");
        
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/votes", vote);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully cast vote on target: {TargetType}/{TargetId}", vote.target_type, vote.target_id);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogWarning("Vote casting failed for target {TargetType}/{TargetId} with status: {StatusCode}", 
                vote.target_type, vote.target_id, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during vote casting on target: {TargetType}/{TargetId}", vote.target_type, vote.target_id);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during vote casting on target: {TargetType}/{TargetId}", vote.target_type, vote.target_id);
            return ApiResult<bool>.Failure($"Failed to cast vote: {ex.Message}");
        }
    }

    /// <summary>
    /// Submits a content or user report for moderation review.
    /// </summary>
    /// <param name="report">Report object with details about the issue being reported.</param>
    /// <returns>ApiResult with true on successful report submission.</returns>
    /// <remarks>
    /// Security: Reports are reviewed by moderators. Abuse of reporting may result in penalties.
    /// Privacy: Reporter identity is logged for accountability.
    /// </remarks>
    public async Task<ApiResult<bool>> SubmitReportAsync(Report report)
    {
        if (report == null)
        {
            _logger.LogWarning("SubmitReportAsync called with null report");
            return ApiResult<bool>.Failure("Report cannot be null", 400);
        }

        _logger.LogWarning("Submitting report for target: {TargetUrn}, reason: {Reason}", 
            report.target_urn ?? "unknown", report.reason ?? "unknown");
        
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/reports", report);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully submitted report for target: {TargetUrn}", report.target_urn);
                return ApiResult<bool>.Success(true);
            }
            
            _logger.LogError("Report submission failed for target {TargetUrn} with status: {StatusCode}", 
                report.target_urn, response.StatusCode);
            return await HandleErrorResponse<bool>(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during report submission for target: {TargetUrn}", report.target_urn);
            return ApiResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during report submission for target: {TargetUrn}", report.target_urn);
            return ApiResult<bool>.Failure($"Failed to submit report: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Handles error responses from API calls by parsing Problem Details.
    /// </summary>
    /// <typeparam name="T">Expected result type.</typeparam>
    /// <param name="response">HTTP response message containing the error.</param>
    /// <returns>ApiResult with failure state and parsed error information.</returns>
    /// <remarks>
    /// Follows RFC 7807 Problem Details for HTTP APIs specification.
    /// Gracefully handles malformed error responses.
    /// </remarks>
    private async Task<ApiResult<T>> HandleErrorResponse<T>(HttpResponseMessage response)
    {
        _logger.LogDebug("Handling error response with status code: {StatusCode}", response.StatusCode);
        
        try
        {
            var problemJson = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            };
            var problem = JsonSerializer.Deserialize<Problem>(problemJson, options);
            
            if (problem != null && !string.IsNullOrEmpty(problem.title))
            {
                _logger.LogWarning("API error: {Title} - {Detail}", problem.title, problem.detail ?? "No details");
                return ApiResult<T>.Failure(problem.title, (int)response.StatusCode, problem);
            }
            
            // Fallback if Problem deserialization fails
            _logger.LogWarning("Received error without valid Problem Details. Status: {StatusCode}", response.StatusCode);
            return ApiResult<T>.Failure($"Error: {response.StatusCode}", (int)response.StatusCode);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse error response as Problem Details");
            return ApiResult<T>.Failure($"Error: {response.StatusCode}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while handling error response");
            return ApiResult<T>.Failure($"Error: {response.StatusCode}", (int)response.StatusCode);
        }
    }

    #endregion
}

/// <summary>
/// Generic API result wrapper providing consistent error handling across all API calls.
/// </summary>
/// <typeparam name="T">Type of data contained in successful results.</typeparam>
/// <remarks>
/// This wrapper eliminates the need for try-catch blocks in consuming code.
/// Use IsSuccess to check result status, then access Data or Error accordingly.
/// StatusCode provides HTTP status for detailed error handling.
/// Problem property contains RFC 7807 Problem Details when available.
/// </remarks>
public class ApiResult<T>
{
    /// <summary>
    /// Indicates whether the API call succeeded.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Contains the result data when IsSuccess is true, otherwise null.
    /// </summary>
    public T? Data { get; private set; }

    /// <summary>
    /// Contains error message when IsSuccess is false, otherwise null.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// HTTP status code from the response (200 for success, 4xx/5xx for errors).
    /// </summary>
    public int StatusCode { get; private set; }

    /// <summary>
    /// RFC 7807 Problem Details object when available, otherwise null.
    /// </summary>
    public Problem? Problem { get; private set; }

    /// <summary>
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private ApiResult() { }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The successful result data.</param>
    /// <returns>ApiResult in success state with the provided data.</returns>
    public static ApiResult<T> Success(T data)
    {
        return new ApiResult<T>
        {
            IsSuccess = true,
            Data = data,
            StatusCode = 200
        };
    }

    /// <summary>
    /// Creates a failed result with error information.
    /// </summary>
    /// <param name="error">Error message describing the failure.</param>
    /// <param name="statusCode">HTTP status code (default: 500).</param>
    /// <param name="problem">Optional RFC 7807 Problem Details object.</param>
    /// <returns>ApiResult in failure state with error details.</returns>
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
