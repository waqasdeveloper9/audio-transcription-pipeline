using System.Collections.Concurrent;
using System.Threading.Channels;
using TranscriptionPipeline.Models;

namespace TranscriptionPipeline.Services;

public class TranscriptionJobWorker : IAsyncDisposable
{
    private readonly ITranscriptionService _service;
    private readonly Channel<TranscriptionJob> _queue;
    private readonly ConcurrentDictionary<Guid, TranscriptionJob> _jobs = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    public TranscriptionJobWorker(ITranscriptionService service)
    {
        _service = service;
        _queue = Channel.CreateUnbounded<TranscriptionJob>(new UnboundedChannelOptions { SingleReader = true });
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    // Enqueue job immediately and return traceable job ID (non-blocking)
    public Guid Enqueue(string filePath, TranscriptionOptions? options = null)
    {
        var job = new TranscriptionJob { FilePath = filePath, Options = options ?? new TranscriptionOptions() };
        _jobs[job.Id] = job;
        _queue.Writer.TryWrite(job);
        return job.Id;
    }

    // Retrieve current job status and result
    public TranscriptionJob? GetJob(Guid jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    // Background loop processing jobs sequentially without blocking caller
    private async Task ProcessQueueAsync()
    {
        var reader = _queue.Reader;

        while (await reader.WaitToReadAsync(_cts.Token))
        {
            while (reader.TryRead(out var job))
            {
                job.Status = JobStatus.Processing;

                try
                {
                    job.Result = await _service.TranscribeFileAsync(job.FilePath, job.Options, cancellationToken: _cts.Token);
                    job.Status = JobStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = ex.Message;
                    job.CompletedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.Complete();
        _cts.Cancel();
        await _workerTask;
        _cts.Dispose();
    }
}
