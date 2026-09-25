using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;
using MediaOrganizer.Orchestration;
using MediaOrganizer.Transcoding;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class TranscodeServiceTests
{
    private const string MoviePath = "/media/Movie/Movie.mkv";

    private readonly Mock<ILogger<TranscodeService>> _loggerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly Mock<IVideoProbe> _probeMock = new();
    private readonly Mock<ITranscoder> _transcoderMock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranscodeFilesAsync_SkipsFileAlreadyUsingTargetCodec()
    {
        var sut = CreateSut();
        SetupExistingFile(MoviePath);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("h264");

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.SkippedFiles);
        Assert.Equal(0, summary.TranscodedFiles);
        _transcoderMock.Verify(t => t.TranscodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeFilesAsync_SkipsCodecNotInOnlyList()
    {
        var sut = CreateSut();
        SetupExistingFile(MoviePath);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("mpeg4");

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.SkippedFiles);
        _transcoderMock.Verify(t => t.TranscodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeFilesAsync_TranscodesListedCodec()
    {
        var sut = CreateSut();
        SetupExistingFile(MoviePath);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("hevc");
        _transcoderMock.Setup(t => t.TranscodeAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.TotalFiles);
        Assert.Equal(1, summary.TranscodedFiles);
        Assert.Equal(0, summary.FailedFiles);
    }

    [Fact]
    public async Task TranscodeFilesAsync_EmptyOnlyCodecsTranscodesAnyNonTargetCodec()
    {
        var sut = CreateSut(onlyCodecs: []);
        SetupExistingFile(MoviePath);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("mpeg4");
        _transcoderMock.Setup(t => t.TranscodeAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.TranscodedFiles);
    }

    [Fact]
    public async Task TranscodeFilesAsync_CountsFailedTranscode()
    {
        var sut = CreateSut();
        SetupExistingFile(MoviePath);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("hevc");
        _transcoderMock.Setup(t => t.TranscodeAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.FailedFiles);
        Assert.Equal(0, summary.TranscodedFiles);
    }

    [Fact]
    public async Task TranscodeFilesAsync_CountsProbeFailureAndContinues()
    {
        var sut = CreateSut();
        SetupExistingFile(MoviePath);
        _probeMock
            .Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranscodeException("ffprobe is not installed"));

        var summary = await sut.TranscodeFilesAsync([MoviePath], Ct);

        Assert.Equal(1, summary.FailedFiles);
    }

    [Fact]
    public async Task TranscodeFilesAsync_SkipsEverythingWhenDisabled()
    {
        var sut = CreateSut(enabled: false);

        var summary = await sut.TranscodeFilesAsync([MoviePath, "/media/Other.mkv"], Ct);

        Assert.Equal(2, summary.SkippedFiles);
        Assert.Equal(0, summary.TranscodedFiles);
        _probeMock.Verify(p => p.GetVideoCodecAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeRequestAsync_ThrowsWhenDisabled()
    {
        var sut = CreateSut(enabled: false);

        await Assert.ThrowsAsync<TranscodeNotConfiguredException>(() => sut.TranscodeRequestAsync(null, Ct));
    }

    [Fact]
    public async Task TranscodeRequestAsync_ScansConfiguredFolderAndFiltersExtensions()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.DirectoryExists("/media")).Returns(true);
        _fsMock
            .Setup(f => f.EnumerateFiles("/media", "*", SearchOption.AllDirectories))
            .Returns(["/media/A.mkv", "/media/notes.txt"]);
        _fsMock.Setup(f => f.FileExists("/media/A.mkv")).Returns(true);
        _probeMock.Setup(p => p.GetVideoCodecAsync("/media/A.mkv", It.IsAny<CancellationToken>())).ReturnsAsync("hevc");
        _transcoderMock.Setup(t => t.TranscodeAsync("/media/A.mkv", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var summary = await sut.TranscodeRequestAsync(null, Ct);

        Assert.Equal(1, summary.TotalFiles);
        Assert.Equal(1, summary.TranscodedFiles);
    }

    [Fact]
    public async Task TranscodeRequestAsync_TranscodesOnlyGivenPaths()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.FileExists(MoviePath)).Returns(true);
        _probeMock.Setup(p => p.GetVideoCodecAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync("hevc");
        _transcoderMock.Setup(t => t.TranscodeAsync(MoviePath, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var summary = await sut.TranscodeRequestAsync([MoviePath], Ct);

        Assert.Equal(1, summary.TotalFiles);
        Assert.Equal(1, summary.TranscodedFiles);
        _fsMock.Verify(f => f.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeRequestAsync_RejectsPathOutsideMediaRoot()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<TranscodeValidationException>(
            () => sut.TranscodeRequestAsync(["/etc/passwd"], Ct));
    }

    [Fact]
    public async Task TranscodeRequestAsync_SkipsInProgressTempFiles()
    {
        var sut = CreateSut();
        var tempPath = TranscodeTempFiles.BuildTempPath(MoviePath);

        var summary = await sut.TranscodeRequestAsync([tempPath], Ct);

        Assert.Equal(1, summary.SkippedFiles);
        Assert.Equal(0, summary.TranscodedFiles);
        _transcoderMock.Verify(t => t.TranscodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeRequestAsync_ThrowsWhenAnotherJobIsRunning()
    {
        var jobLock = new JobLock();
        using var held = jobLock.TryAcquire();
        var sut = CreateSut(jobLock: jobLock);

        await Assert.ThrowsAsync<JobAlreadyRunningException>(() => sut.TranscodeRequestAsync(null, Ct));
    }

    [Fact]
    public async Task GetStatusAsync_ReportsHardwareAvailabilityWhenEnabled()
    {
        var sut = CreateSut();
        _probeMock
            .Setup(p => p.IsEncoderAvailableAsync("h264_vaapi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var status = await sut.GetStatusAsync(Ct);

        Assert.True(status.Enabled);
        Assert.True(status.HardwareAvailable);
        Assert.Equal("h264_vaapi", status.Encoder);
    }

    [Fact]
    public async Task GetStatusAsync_SkipsProbeWhenDisabled()
    {
        var sut = CreateSut(enabled: false);

        var status = await sut.GetStatusAsync(Ct);

        Assert.False(status.Enabled);
        Assert.False(status.HardwareAvailable);
        _probeMock.Verify(p => p.IsEncoderAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ────────────── Helpers ──────────────

    private void SetupExistingFile(string path)
        => _fsMock.Setup(f => f.FileExists(path)).Returns(true);

    private TranscodeService CreateSut(bool enabled = true, string[]? onlyCodecs = null, JobLock? jobLock = null)
    {
        var transcoding = new TranscodingOptions
        {
            Enabled = enabled,
            Encoder = "h264_vaapi",
            HardwareDevice = "/dev/dri/renderD128"
        };

        if (onlyCodecs is not null)
        {
            transcoding.OnlyCodecs = onlyCodecs;
        }

        var options = Options.Create(new MediaOrganizerOptions
        {
            SourceFolder = "/media",
            Transcoding = transcoding
        });

        return new TranscodeService(
            _loggerMock.Object,
            options,
            _fsMock.Object,
            _probeMock.Object,
            _transcoderMock.Object,
            jobLock ?? new JobLock());
    }
}