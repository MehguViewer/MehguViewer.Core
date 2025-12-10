using System.Diagnostics;
using MehguViewer.Core.Shared;
using MehguViewer.Core.Services;
using MehguViewer.Core.Infrastructures;

namespace MehguViewer.Core.Workers;

/// <summary>
/// Background service that periodically validates taxonomy data across all series and units.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Automated quality assurance for content metadata.
/// Ensures all tags, authors, scanlators, and groups match configured taxonomy.
/// 
/// <para><strong>Schedule:</strong></para>
/// <list type="bullet">
///   <item>Initial delay: 5 minutes after application startup</item>
///   <item>Interval: Every 6 hours</item>
///   <item>Creates job for each validation run</item>
/// </list>
/// 
/// <para><strong>Validation Process:</strong></para>
/// <list type="number">
///   <item>Creates tracking job in JobService</item>
///   <item>Runs full validation via TaxonomyValidationService</item>
///   <item>Logs validation issues (series and units)</item>
///   <item>Updates job with results or error details</item>
/// </list>
/// 
/// <para><strong>Graceful Shutdown:</strong></para>
/// Responds to cancellation tokens during delays.
/// Will not interrupt in-progress validation.
/// 
/// <para><strong>Security Considerations:</strong></para>
/// <list type="bullet">
///   <item>Uses scoped services to prevent service lifetime issues</item>
///   <item>Validates service resolution before use</item>
///   <item>Limits error message length in job updates</item>
///   <item>Prevents resource exhaustion via bounded validation</item>
/// </list>
/// 
/// <para><strong>Performance:</strong></para>
/// Uses Stopwatch for precise duration tracking. Scoped services ensure proper disposal.
/// Validation runs are isolated to prevent interference between runs.
/// </remarks>
public sealed class TaxonomyValidationWorker : BackgroundService
{
    #region Constants

    /// <summary>Delay before the first validation run after application startup.</summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    
    /// <summary>Interval between validation runs.</summary>
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromHours(6);
    
    /// <summary>Job type identifier for taxonomy validation jobs.</summary>
    private const string JobType = "taxonomy-validation";
    
    /// <summary>Maximum length for error messages stored in jobs.</summary>
    private const int MaxErrorMessageLength = 500;
    
    /// <summary>Maximum number of individual issues to log (prevents log flooding).</summary>
    private const int MaxIssuesLogged = 50;

    #endregion

    #region Fields

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaxonomyValidationWorker> _logger;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the TaxonomyValidationWorker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped services.</param>
    /// <param name="logger">Logger for background worker operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider or logger is null.</exception>
    public TaxonomyValidationWorker(
        IServiceProvider serviceProvider,
        ILogger<TaxonomyValidationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogDebug("TaxonomyValidationWorker constructed with InitialDelay={InitialDelay}, ValidationInterval={ValidationInterval}",
            InitialDelay, ValidationInterval);
    }

    #endregion

    #region BackgroundService Overrides

    /// <summary>
    /// Main execution loop for the background worker.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    /// <remarks>
    /// <para><strong>Execution Flow:</strong></para>
    /// <list type="number">
    ///   <item>Waits for initial delay (allows application to fully start)</item>
    ///   <item>Enters validation loop until cancellation requested</item>
    ///   <item>Each iteration: runs validation job, waits for interval</item>
    ///   <item>Gracefully exits on cancellation without aborting in-progress validation</item>
    /// </list>
    /// 
    /// <para><strong>Error Handling:</strong></para>
    /// Individual validation failures are caught and logged.
    /// Worker continues executing on subsequent cycles even after errors.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "TaxonomyValidationWorker started. Waiting {Minutes} minutes before first run",
            InitialDelay.TotalMinutes);

        try
        {
            // Wait for initial delay, respecting cancellation
            await Task.Delay(InitialDelay, stoppingToken);
            
            _logger.LogInformation("Initial delay completed, starting validation cycles");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TaxonomyValidationWorker cancelled during initial delay");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunValidationJobAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TaxonomyValidationWorker cancelled during validation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during taxonomy validation cycle");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogInformation(
                "Next taxonomy validation scheduled in {Hours} hours",
                ValidationInterval.TotalHours);
            
            try
            {
                await Task.Delay(ValidationInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TaxonomyValidationWorker cancelled during interval wait");
                break;
            }
        }
        
        _logger.LogInformation("TaxonomyValidationWorker execution loop ended");
    }

    /// <summary>
    /// Handles graceful shutdown of the background worker.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown timeout.</param>
    /// <remarks>
    /// Logs shutdown initiation. Does not abort in-progress validation jobs.
    /// Relies on cancellation token propagation through ExecuteAsync.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TaxonomyValidationWorker shutdown requested");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("TaxonomyValidationWorker stopped successfully");
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Runs a single validation job with tracking and error handling.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (not used during validation).</param>
    /// <remarks>
    /// <para><strong>Execution Steps:</strong></para>
    /// <list type="number">
    ///   <item>Creates scoped service instance (ensures fresh dependencies)</item>
    ///   <item>Validates service resolution (security check)</item>
    ///   <item>Creates job in JobService for tracking</item>
    ///   <item>Runs validation via TaxonomyValidationService</item>
    ///   <item>Logs results at appropriate levels (Info/Warning/Error)</item>
    ///   <item>Updates job with completion status and summary</item>
    /// </list>
    /// 
    /// <para><strong>Service Lifetime:</strong></para>
    /// Uses scoped services to ensure proper disposal and prevent memory leaks.
    /// Each validation run gets fresh repository and validation service instances.
    /// 
    /// <para><strong>Performance Tracking:</strong></para>
    /// Uses Stopwatch for precise duration measurement.
    /// Duration logged for performance monitoring and optimization.
    /// </remarks>
    private async Task RunValidationJobAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        
        // Validate service resolution (security & stability)
        var jobService = scope.ServiceProvider.GetService<JobService>();
        var validationService = scope.ServiceProvider.GetService<TaxonomyValidationService>();
        
        if (jobService == null)
        {
            _logger.LogError("Failed to resolve JobService from service provider");
            return;
        }
        
        if (validationService == null)
        {
            _logger.LogError("Failed to resolve TaxonomyValidationService from service provider");
            return;
        }

        var job = jobService.CreateJob(JobType);
        
        _logger.LogInformation("Starting taxonomy validation job {JobId}", job.id);
        
        try
        {
            jobService.UpdateJob(job.id, "PROCESSING", 0);
            
            _logger.LogDebug("Running full validation across all series and units");

            var report = await validationService.RunFullValidationAsync();
            
            stopwatch.Stop();

            var totalIssues = report.SeriesIssues.Length + report.UnitIssues.Length;
            
            _logger.LogInformation(
                "Taxonomy validation completed in {ElapsedMs}ms. Total issues: {TotalIssues}, Series issues: {SeriesIssues}, Unit issues: {UnitIssues}",
                stopwatch.ElapsedMilliseconds, totalIssues, report.SeriesIssues.Length, report.UnitIssues.Length);
            
            if (totalIssues == 0)
            {
                LogSuccessfulValidation(report);
                CompleteJobSuccessfully(jobService, job.id, report, totalIssues, stopwatch.Elapsed);
            }
            else
            {
                LogValidationIssues(report, totalIssues);
                CompleteJobWithIssues(jobService, job.id, report, totalIssues, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "Taxonomy validation job {JobId} failed after {ElapsedMs}ms", 
                job.id, stopwatch.ElapsedMilliseconds);
            
            // Truncate error message to prevent excessive storage
            var errorMessage = ex.Message.Length > MaxErrorMessageLength 
                ? ex.Message[..MaxErrorMessageLength] + "..." 
                : ex.Message;
            
            jobService.UpdateJob(
                job.id, 
                "FAILED", 
                0, 
                null, 
                $"Validation failed: {errorMessage}");
        }
    }

    /// <summary>
    /// Logs successful validation with no issues found.
    /// </summary>
    /// <param name="report">Validation report containing entity counts.</param>
    private void LogSuccessfulValidation(TaxonomyValidationReport report)
    {
        _logger.LogInformation(
            "Taxonomy validation successful - No issues found. Validated {TotalSeries} series and {TotalUnits} units",
            report.TotalSeries, report.TotalUnits);
    }

    /// <summary>
    /// Logs validation issues for series and units.
    /// </summary>
    /// <param name="report">Validation report containing all issues.</param>
    /// <param name="totalIssues">Total count of issues found.</param>
    /// <remarks>
    /// Limits individual issue logging to prevent log flooding.
    /// Uses MaxIssuesLogged constant to cap detailed issue output.
    /// </remarks>
    private void LogValidationIssues(TaxonomyValidationReport report, int totalIssues)
    {
        _logger.LogWarning(
            "Taxonomy validation found {TotalIssues} issues across {SeriesWithIssues} series and {UnitsWithIssues} units",
            totalIssues, report.SeriesIssues.Length, report.UnitIssues.Length);
        
        // Log series issues (limited to prevent log flooding)
        var seriesToLog = Math.Min(report.SeriesIssues.Length, MaxIssuesLogged);
        for (int i = 0; i < seriesToLog; i++)
        {
            var issue = report.SeriesIssues[i];
            _logger.LogWarning(
                "Series validation issue [{Index}/{Total}]: URN={Urn}, Title='{Title}', Issues={Issues}",
                i + 1, report.SeriesIssues.Length, issue.EntityUrn, issue.EntityTitle, string.Join(", ", issue.Issues));
        }
        
        if (report.SeriesIssues.Length > MaxIssuesLogged)
        {
            _logger.LogWarning(
                "Suppressed {SuppressedCount} additional series issue logs (total: {Total})",
                report.SeriesIssues.Length - MaxIssuesLogged, report.SeriesIssues.Length);
        }
        
        // Log unit issues (limited to prevent log flooding)
        var unitsToLog = Math.Min(report.UnitIssues.Length, MaxIssuesLogged);
        for (int i = 0; i < unitsToLog; i++)
        {
            var issue = report.UnitIssues[i];
            _logger.LogWarning(
                "Unit validation issue [{Index}/{Total}]: URN={Urn}, Title='{Title}', Issues={Issues}",
                i + 1, report.UnitIssues.Length, issue.EntityUrn, issue.EntityTitle, string.Join(", ", issue.Issues));
        }
        
        if (report.UnitIssues.Length > MaxIssuesLogged)
        {
            _logger.LogWarning(
                "Suppressed {SuppressedCount} additional unit issue logs (total: {Total})",
                report.UnitIssues.Length - MaxIssuesLogged, report.UnitIssues.Length);
        }
    }

    /// <summary>
    /// Completes job successfully when no issues are found.
    /// </summary>
    /// <param name="jobService">Job service instance.</param>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="report">Validation report.</param>
    /// <param name="totalIssues">Total issue count (should be 0).</param>
    /// <param name="duration">Validation duration.</param>
    private void CompleteJobSuccessfully(JobService jobService, string jobId, TaxonomyValidationReport report, int totalIssues, TimeSpan duration)
    {
        jobService.UpdateJob(
            jobId, 
            "COMPLETED", 
            100, 
            null, 
            $"Validation completed successfully in {duration.TotalSeconds:F2}s. No issues found. Checked {report.TotalSeries} series and {report.TotalUnits} units.");
    }

    /// <summary>
    /// Completes job with issue summary when validation problems are found.
    /// </summary>
    /// <param name="jobService">Job service instance.</param>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="report">Validation report.</param>
    /// <param name="totalIssues">Total issue count.</param>
    /// <param name="duration">Validation duration.</param>
    private void CompleteJobWithIssues(JobService jobService, string jobId, TaxonomyValidationReport report, int totalIssues, TimeSpan duration)
    {
        jobService.UpdateJob(
            jobId, 
            "COMPLETED", 
            100, 
            null, 
            $"Validation completed in {duration.TotalSeconds:F2}s. Found {totalIssues} issues across {report.SeriesIssues.Length} series and {report.UnitIssues.Length} units. Review logs for details.");
    }

    #endregion
}
