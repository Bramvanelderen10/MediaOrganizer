using MediaOrganizer.Orchestration;

using Xunit;

namespace MediaOrganizer.Tests;

public class JobLockTests
{
    [Fact]
    public void TryAcquire_ReturnsNullWhileHeld_AndSucceedsAfterRelease()
    {
        var jobLock = new JobLock();

        var first = jobLock.TryAcquire();
        Assert.NotNull(first);

        Assert.Null(jobLock.TryAcquire());

        first!.Dispose();

        var second = jobLock.TryAcquire();
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public void DisposeTwice_ReleasesOnlyOnePermit()
    {
        var jobLock = new JobLock();

        var handle = jobLock.TryAcquire();
        handle!.Dispose();
        handle.Dispose();

        // If the second Dispose had released again, two permits would be free and the
        // second acquire below would also succeed.
        var second = jobLock.TryAcquire();
        Assert.NotNull(second);

        Assert.Null(jobLock.TryAcquire());
        second!.Dispose();
    }
}