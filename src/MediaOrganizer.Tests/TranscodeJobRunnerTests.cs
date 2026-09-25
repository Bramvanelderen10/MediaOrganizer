using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;
using MediaOrganizer.Orchestration;
using MediaOrganizer.Transcoding;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class TranscodeJobRunnerTests
{
    private const string MoviePath = "/media/Movie/Movie.mkv";

    private readonly Mock<ILogger<TranscodeJobRunner>> _loggerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly Mock<IVideoProbe> _probeMock = new();
    private readonly Mock<ITranscoder> _transcoderMock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_RunsJobInBackgroundAndCompletes()
    {
        _fsMock.Setup(f => f.FileExists(MoviePath)).Returns(true);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("hevc");
        _transcoderMock.Setup(t => t.TranscodeAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var runner = new TranscodeJobRunner(_loggerMock.Object, CreateService(), new JobLock());

        var started = runner.Start("My movie", [MoviePath]);

        Assert.True(started.IsRunning);
        Assert.Equal(1, started.TotalFiles);
        Assert.Equal("My movie", started.Label);

        await WaitForCompletionAsync(runner);

        var final = runner.GetStatus();
        Assert.Equal(TranscodeJobState.Completed, final.State);
        Assert.False(final.IsRunning);
        Assert.Equal(1, final.TranscodedFiles);
        Assert.Null(final.Error);
    }

    [Fact]
    public void Start_ThrowsWhenAnotherJobHoldsTheLock()
    {
        var jobLock = new JobLock();
        using var held = jobLock.TryAcquire();
        var runner = new TranscodeJobRunner(_loggerMock.Object, CreateService(), jobLock);

        Assert.Throws<JobAlreadyRunningException>(() => runner.Start("x", [MoviePath]));
    }

    [Fact]
    public void Start_ThrowsWhenDisabled()
    {
        var runner = new TranscodeJobRunner(_loggerMock.Object, CreateService(enabled: false), new JobLock());

        Assert.Throws<TranscodeNotConfiguredException>(() => runner.Start("x", null));
    }

    [Fact]
    public async Task Start_ReleasesLockWhenJobFinishes()
    {
        _fsMock.Setup(f => f.FileExists(MoviePath)).Returns(true);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("h264");

        var jobLock = new JobLock();
        var runner = new TranscodeJobRunner(_loggerMock.Object, CreateService(), jobLock);

        runner.Start("x", [MoviePath]);
        await WaitForCompletionAsync(runner);

        // The lock must be free again so organize/transcode can run.
        using var next = jobLock.TryAcquire();
        Assert.NotNull(next);
    }

    private static async Task WaitForCompletionAsync(TranscodeJobRunner runner)
    {
        for (var attempt = 0; attempt < 200 && runner.GetStatus().IsRunning; attempt++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.False(runner.GetStatus().IsRunning);
    }

    private TranscodeService CreateService(bool enabled = true)
    {
        var options = Options.Create(new MediaOrganizerOptions
        {
            SourceFolder = "/media",
            Transcoding = new TranscodingOptions
            {
                Enabled = enabled,
                Encoder = "h264_vaapi",
                HardwareDevice = "/dev/dri/renderD128"
            }
        });

        return new TranscodeService(
            new Mock<ILogger<TranscodeService>>().Object,
            options,
            _fsMock.Object,
            _probeMock.Object,
            _transcoderMock.Object,
            new JobLock());
    }
}