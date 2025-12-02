using System.Collections.Concurrent;
using MehguViewer.Core.Backend.Models;

namespace MehguViewer.Core.Backend.Services;

public class JobService
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly ConcurrentQueue<string> _queue = new();

    public Job CreateJob(string type)
    {
        var job = new Job(
            Guid.NewGuid().ToString(),
            type,
            "QUEUED",
            0,
            null,
            null
        );
        _jobs.TryAdd(job.id, job);
        _queue.Enqueue(job.id);
        return job;
    }

    public Job? GetJob(string id)
    {
        return _jobs.TryGetValue(id, out var job) ? job : null;
    }

    public void UpdateJob(string id, string status, int progress, string? resultUrn = null, string? error = null)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            var updated = job with { 
                status = status, 
                progress_percentage = progress, 
                result_urn = resultUrn, 
                error_details = error 
            };
            _jobs.TryUpdate(id, updated, job);
        }
    }

    public bool TryDequeue(out string? jobId)
    {
        return _queue.TryDequeue(out jobId);
    }
}
