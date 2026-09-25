namespace MediaOrganizer.Orchestration;

/// <summary>
/// Raised when a long-running job is requested while another one is still running.
/// The endpoints map this to HTTP 409.
/// </summary>
public class JobAlreadyRunningException : Exception
{
    public JobAlreadyRunningException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Serializes the long-running jobs (organize and transcode) so they never run at the same time.
/// Both jobs move and rewrite the same files; overlapping them could make one job move or delete
/// a file the other is still working on.
/// </summary>
public sealed class JobLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Acquires the lock without waiting. Returns a disposable that releases it, or <c>null</c>
    /// when another job already holds it.
    /// </summary>
    public IDisposable? TryAcquire()
        => _semaphore.Wait(0) ? new Releaser(_semaphore) : null;

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
