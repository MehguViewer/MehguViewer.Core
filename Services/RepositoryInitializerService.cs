namespace MehguViewer.Core.Backend.Services;

/// <summary>
/// Initializes the DynamicRepository after EmbeddedPostgresService is ready.
/// This runs after all other hosted services have started.
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
                    _logger.LogWarning("Embedded PostgreSQL failed to start. Using fallback.");
                }
            }
            
            await _repository.InitializeAsync();
            
            if (_repository.IsInMemory)
            {
                _logger.LogWarning("Repository initialized with MemoryRepository - data will not persist!");
            }
            else
            {
                _logger.LogInformation("Repository initialized with PostgreSQL - data will be persisted.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize repository. Using MemoryRepository as fallback.");
        }
    }
}
