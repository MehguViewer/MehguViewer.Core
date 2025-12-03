using MehguViewer.Shared.Models;

namespace MehguViewer.Core.Backend.Services;

public class IngestionWorker : BackgroundService
{
    private readonly JobService _jobService;
    private readonly ILogger<IngestionWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public IngestionWorker(JobService jobService, ILogger<IngestionWorker> logger, IServiceProvider serviceProvider)
    {
        _jobService = jobService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_jobService.TryDequeue(out var jobId) && jobId != null)
            {
                await ProcessJobAsync(jobId);
            }
            else
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(string jobId)
    {
        _logger.LogInformation("Processing Job {JobId}", jobId);
        _jobService.UpdateJob(jobId, "PROCESSING", 0);

        try
        {
            // Simulate processing
            for (int i = 0; i <= 100; i += 10)
            {
                await Task.Delay(100); // Simulate work
                _jobService.UpdateJob(jobId, "PROCESSING", i);
            }

            // In a real implementation, we would:
            // 1. Unzip the file (path stored in job metadata or separate store)
            // 2. Hash images
            // 3. Create assets
            // 4. Update Unit

            _jobService.UpdateJob(jobId, "COMPLETED", 100, "urn:mvn:unit:generated-id");
            _logger.LogInformation("Job {JobId} Completed", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} Failed", jobId);
            _jobService.UpdateJob(jobId, "FAILED", 0, null, ex.Message);
        }
    }
}
