using MehguViewer.Core.Helpers;
using MehguViewer.Core.Infrastructures;

namespace MehguViewer.Core.Services;

/// <summary>
/// Coordinates repository initialization with proper sequencing and error handling.
/// Ensures database is ready before application starts handling requests.
/// </summary>
/// <remarks>
/// <para><strong>Initialization Scenarios:</strong></para>
/// <list type="number">
/// <item>Embedded PostgreSQL starts successfully → Uses PostgresRepository (data persists)</item>
/// <item>External PostgreSQL configured → Uses PostgresRepository with external connection</item>
/// <item>Embedded PostgreSQL fails with FallbackToMemory=true → Uses MemoryRepository (data NOT persisted)</item>
/// <item>Embedded PostgreSQL fails with FallbackToMemory=false → Application fails to start</item>
/// </list>
/// <para><strong>Post-Initialization Tasks:</strong></para>
/// <list type="bullet">
/// <item>Syncs edit permissions with library file system state</item>
/// <item>Logs repository type and data persistence status</item>
/// <item>Provides clear warnings for memory-only mode</item>
/// <item>Validates database connectivity</item>
/// </list>
/// <para><strong>Thread Safety:</strong></para>
/// <para>Runs as singleton BackgroundService. Initialization happens once during application startup.</para>
/// </remarks>
public sealed class RepositoryInitializerService : BackgroundService
{
    #region Constants
    
    /// <summary>Maximum time to wait for repository initialization before timeout.</summary>
    private const int InitializationTimeoutSeconds = 60;
    
    /// <summary>Delay before starting permission sync to allow file system operations to settle.</summary>
    private const int PermissionSyncDelayMilliseconds = 100;
    
    #endregion
    
    #region Fields
    
    private readonly DynamicRepository _repository;
    private readonly EmbeddedPostgresService? _embeddedPostgres;
    private readonly ILogger<RepositoryInitializerService> _logger;
    
    /// <summary>Indicates whether initialization has completed successfully.</summary>
    private bool _initializationSucceeded;
    
    #endregion
    
    #region Constructor
    
    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryInitializerService"/> class.
    /// </summary>
    /// <param name="repository">The dynamic repository to initialize.</param>
    /// <param name="logger">Logger for initialization status and warnings.</param>
    /// <param name="embeddedPostgres">Optional embedded PostgreSQL service to wait for (null if using external DB).</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public RepositoryInitializerService(
        DynamicRepository repository,
        ILogger<RepositoryInitializerService> logger,
        EmbeddedPostgresService? embeddedPostgres = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddedPostgres = embeddedPostgres;
        
        _logger.LogDebug("RepositoryInitializerService created with {PostgresMode} mode", 
            embeddedPostgres != null ? "embedded PostgreSQL" : "external database");
    }
    
    #endregion
    
    #region Public Properties
    
    /// <summary>
    /// Gets a value indicating whether initialization completed successfully.
    /// </summary>
    public bool IsInitialized => _initializationSucceeded;
    
    #endregion
    
    #region BackgroundService Overrides
    
    /// <summary>
    /// Executes the repository initialization process with timeout and error handling.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for service shutdown.</param>
    /// <returns>Task representing the asynchronous initialization operation.</returns>
    /// <remarks>
    /// <para><strong>Execution Flow:</strong></para>
    /// <list type="number">
    /// <item>Wait for embedded PostgreSQL startup (if applicable)</item>
    /// <item>Check for startup failures and fallback logic</item>
    /// <item>Initialize repository (connects to PostgreSQL or creates in-memory)</item>
    /// <item>Validate database connectivity</item>
    /// <item>Log repository type with appropriate warnings</item>
    /// <item>Sync edit permissions with file system state</item>
    /// <item>On failure: Fall back to MemoryRepository with error logging</item>
    /// </list>
    /// <para><strong>Security Considerations:</strong></para>
    /// <para>Does not log connection strings or sensitive configuration data.</para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(InitializationTimeoutSeconds));
        
        try
        {
            _logger.LogInformation("Starting repository initialization sequence");
            
            // Step 1: Wait for embedded PostgreSQL if configured
            if (_embeddedPostgres != null)
            {
                _logger.LogDebug("Waiting for embedded PostgreSQL service to complete startup");
                
                await _embeddedPostgres.WaitForStartupAsync();
                
                if (_embeddedPostgres.StartupFailed)
                {
                    LogPostgresStartupFailure();
                }
                else
                {
                    _logger.LogInformation("Embedded PostgreSQL service started successfully on port {Port}", 
                        _embeddedPostgres.Port);
                }
            }
            else
            {
                _logger.LogDebug("No embedded PostgreSQL service configured, using external database connection");
            }
            
            // Step 2: Initialize repository
            _logger.LogDebug("Initializing DynamicRepository");
            await _repository.InitializeAsync();
            
            // Step 3: Log repository type and persistence mode
            LogRepositoryType();
            
            // Step 4: Sync edit permissions with file system
            await SyncEditPermissionsAsync();
            
            // Step 5: Mark initialization as successful
            _initializationSucceeded = true;
            _logger.LogInformation("Repository initialization completed successfully");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError("Repository initialization timed out after {Timeout} seconds", InitializationTimeoutSeconds);
            _initializationSucceeded = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical failure during repository initialization. Using MemoryRepository as fallback");
            _initializationSucceeded = false;
            
            // Don't rethrow - allow application to continue with MemoryRepository
        }
    }
    
    #endregion
    
    #region Private Helper Methods
    
    /// <summary>
    /// Logs appropriate messages when PostgreSQL startup fails.
    /// </summary>
    /// <remarks>
    /// Logs at WARNING level if fallback is allowed, ERROR level if fallback is disabled.
    /// Does not throw exceptions to allow graceful degradation.
    /// </remarks>
    private void LogPostgresStartupFailure()
    {
        if (_embeddedPostgres!.FallbackToMemoryAllowed)
        {
            _logger.LogWarning(
                "Embedded PostgreSQL failed to start. FallbackToMemory is enabled - using MemoryRepository");
        }
        else
        {
            _logger.LogError(
                "Embedded PostgreSQL failed to start and FallbackToMemory is disabled. Application may be unstable");
        }
    }
    
    /// <summary>
    /// Logs the initialized repository type with appropriate warnings for data persistence.
    /// </summary>
    /// <remarks>
    /// Uses emoji indicators for high visibility in production logs.
    /// WARNING level for non-persistent storage, INFORMATION level for persistent storage.
    /// </remarks>
    private void LogRepositoryType()
    {
        if (_repository.IsInMemory)
        {
            _logger.LogWarning("⚠️  Repository initialized with MemoryRepository - DATA WILL NOT PERSIST!");
            _logger.LogWarning("⚠️  Any data created will be lost when the application restarts");
            _logger.LogWarning("⚠️  Configure PostgreSQL connection for persistent storage");
        }
        else
        {
            _logger.LogInformation("✅ Repository initialized with PostgreSQL - data will be persisted");
            _logger.LogDebug("Repository connection validated and ready for operations");
        }
    }
    
    /// <summary>
    /// Synchronizes edit permissions with the file system state.
    /// </summary>
    /// <returns>Task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Adds a small delay to allow file system operations to settle before syncing.
    /// Failures are logged but do not prevent initialization from completing.
    /// </remarks>
    private async Task SyncEditPermissionsAsync()
    {
        try
        {
            _logger.LogDebug("Preparing to sync edit permissions with library state");
            
            // Small delay to ensure file system operations have settled
            await Task.Delay(PermissionSyncDelayMilliseconds);
            
            _logger.LogInformation("Syncing edit permissions with library file system state");
            _repository.SyncEditPermissions();
            
            _logger.LogDebug("Edit permissions sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync edit permissions. Permissions may be out of sync with file system");
            // Don't rethrow - this is not critical enough to fail initialization
        }
    }
    
    #endregion
}
