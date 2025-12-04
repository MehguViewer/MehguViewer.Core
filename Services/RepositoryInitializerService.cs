namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Initializes the DynamicRepository after EmbeddedPostgresService is ready.
/// This runs after all other hosted services have started.
/// 
/// The service supports the following scenarios:
/// 1. Embedded PostgreSQL starts successfully -> Uses PostgresRepository (data persists)
/// 2. External PostgreSQL configured -> Uses PostgresRepository with external connection
/// 3. Embedded PostgreSQL fails with FallbackToMemory=true -> Uses MemoryRepository (data NOT persisted)
/// 4. Embedded PostgreSQL fails with FallbackToMemory=false -> Application fails to start
/// </summary>
public class RepositoryInitializerService : BackgroundService
{
    private readonly DynamicRepository _repository;
    private readonly EmbeddedPostgresService? _embeddedPostgres;
    private readonly ILogger<RepositoryInitializerService> _logger;

    public RepositoryInitializerService(
        DynamicRepository repository,
        ILogger<RepositoryInitializerService> logger,
        EmbeddedPostgresService? embeddedPostgres = null)
    {
        _repository = repository;
        _embeddedPostgres = embeddedPostgres;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Initializing database repository...");
            
            if (_embeddedPostgres != null)
            {
                _logger.LogInformation("Waiting for embedded PostgreSQL service...");
                await _embeddedPostgres.WaitForStartupAsync();
                
                if (_embeddedPostgres.StartupFailed)
                {
                    if (_embeddedPostgres.FallbackToMemoryAllowed)
                    {
                        _logger.LogWarning("Embedded PostgreSQL failed to start. FallbackToMemory is enabled - using MemoryRepository.");
                    }
                    else
                    {
                        _logger.LogError("Embedded PostgreSQL failed to start and FallbackToMemory is disabled. Application may be unstable.");
                    }
                }
            }
            
            await _repository.InitializeAsync();
            
            if (_repository.IsInMemory)
            {
                _logger.LogWarning("⚠️ Repository initialized with MemoryRepository - DATA WILL NOT PERSIST!");
                _logger.LogWarning("⚠️ Any data created will be lost when the application restarts.");
            }
            else
            {
                _logger.LogInformation("✅ Repository initialized with PostgreSQL - data will be persisted.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize repository. Using MemoryRepository as fallback.");
        }
    }
}
