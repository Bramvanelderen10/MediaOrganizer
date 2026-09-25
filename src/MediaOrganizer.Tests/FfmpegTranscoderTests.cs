using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;
using MediaOrganizer.Transcoding;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class FfmpegTranscoderTests
{
    private const string SourcePath = "/media/Movie/Movie.mkv";
    private const string TempPath = "/media/Movie/Movie.transcode.mkv";
    private const string KeptOutputPath = "/media/Movie/Movie.h264.mkv";

    private readonly Mock<ILogger<FfmpegTranscoder>> _loggerMock = new();
    private readonly Mock<IProcessRunner> _processRunnerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranscodeAsync_ReplacesOriginalOnSuccess()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.FileExists(TempPath)).Returns(true);
        SetupProcess(new ProcessResult(0, string.Empty, string.Empty));

        var result = await sut.TranscodeAsync(SourcePath, Ct);

        Assert.True(result);
        _fsMock.Verify(f => f.DeleteFile(SourcePath), Times.Once);
        _fsMock.Verify(f => f.MoveFile(TempPath, SourcePath), Times.Once);
    }

    [Fact]
    public async Task TranscodeAsync_UsesConfiguredHardwareEncoder()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.FileExists(TempPath)).Returns(true);
        var encoders = new List<string>();
        SetupProcess(new ProcessResult(0, string.Empty, string.Empty), encoders);

        await sut.TranscodeAsync(SourcePath, Ct);

        var arguments = CaptureArguments();
        Assert.Contains("h264_vaapi", arguments);
        Assert.Contains("/dev/dri/renderD128", arguments);
        Assert.Contains("format=nv12,hwupload", arguments);
    }

    [Fact]
    public async Task TranscodeAsync_FallsBackToSoftwareOnHardwareFailure()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.FileExists(TempPath)).Returns(true);
        var encoders = new List<string>();
        var results = new Queue<ProcessResult>(
        [
            new ProcessResult(1, string.Empty, "hardware failure"),
            new ProcessResult(0, string.Empty, string.Empty)
        ]);
        _processRunnerMock
            .Setup(p => p.RunAsync("ffmpeg", It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, TimeSpan, CancellationToken>((_, args, _, _) => encoders.Add(EncoderOf(args)))
            .ReturnsAsync(() => results.Dequeue());

        var result = await sut.TranscodeAsync(SourcePath, Ct);

        Assert.True(result);
        Assert.Equal(new[] { "h264_vaapi", "libx264" }, encoders);
    }

    [Fact]
    public async Task TranscodeAsync_PreservesOriginalWhenAllEncodersFail()
    {
        var sut = CreateSut();
        _fsMock.Setup(f => f.FileExists(TempPath)).Returns(true);
        SetupProcess(new ProcessResult(1, string.Empty, "boom"));

        var result = await sut.TranscodeAsync(SourcePath, Ct);

        Assert.False(result);
        _fsMock.Verify(f => f.DeleteFile(SourcePath), Times.Never);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TranscodeAsync_KeepsOriginalWritesSiblingOutput()
    {
        var sut = CreateSut(keepOriginal: true);
        _fsMock.Setup(f => f.FileExists(TempPath)).Returns(true);
        SetupProcess(new ProcessResult(0, string.Empty, string.Empty));

        var result = await sut.TranscodeAsync(SourcePath, Ct);

        Assert.True(result);
        _fsMock.Verify(f => f.MoveFile(TempPath, KeptOutputPath), Times.Once);
        _fsMock.Verify(f => f.DeleteFile(SourcePath), Times.Never);
    }

    [Fact]
    public async Task RunSelfTestAsync_ReportsDriverOnSuccess()
    {
        var sut = CreateSut();
        SetupProcess(new ProcessResult(
            0,
            string.Empty,
            "libva info: Trying to open /usr/lib/x86_64-linux-gnu/dri/iHD_drv_video.so\nlibva info: va_openDriver() returns 0"));

        var result = await sut.RunSelfTestAsync(Ct);

        Assert.True(result.Succeeded);
        Assert.True(result.IsHardwareEncoder);
        Assert.Equal("iHD", result.Driver);
        Assert.Contains("h264_vaapi", CaptureArguments());
        Assert.Contains("/dev/dri/renderD128", CaptureArguments());
    }

    [Fact]
    public async Task RunSelfTestAsync_ReportsFailureAndOutput()
    {
        var sut = CreateSut();
        SetupProcess(new ProcessResult(1, string.Empty, "Failed to initialise VAAPI connection: -1"));

        var result = await sut.RunSelfTestAsync(Ct);

        Assert.False(result.Succeeded);
        Assert.True(result.IsHardwareEncoder);
        Assert.Null(result.Driver);
        Assert.Contains("VAAPI", result.Output);
    }

    [Fact]
    public async Task RunSelfTestAsync_SuggestsI965DriverForVaDisplayFailure()
    {
        var sut = CreateSut();
        SetupProcess(new ProcessResult(
            1,
            string.Empty,
            "[AVHWDeviceContext @ 0x1] No VA display found for device /dev/dri/renderD128.\n"
            + "Device creation failed: -22.\n"
            + "Failed to set value '/dev/dri/renderD128' for option 'vaapi_device': Invalid argument"));

        var result = await sut.RunSelfTestAsync(Ct);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Hint);
        Assert.Contains("LIBVA_DRIVER_NAME=i965", result.Hint!);
    }

    [Fact]
    public async Task RunSelfTestAsync_NoHintWhenSuccessful()
    {
        var sut = CreateSut();
        SetupProcess(new ProcessResult(0, string.Empty, string.Empty));

        var result = await sut.RunSelfTestAsync(Ct);

        Assert.True(result.Succeeded);
        Assert.Null(result.Hint);
    }

    // ────────────── Helpers ──────────────

    private List<string>? _capturedArguments;

    private void SetupProcess(ProcessResult result, List<string>? encoders = null)
    {
        _processRunnerMock
            .Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, TimeSpan, CancellationToken>((_, args, _, _) =>
            {
                _capturedArguments = args.ToList();
                encoders?.Add(EncoderOf(args));
            })
            .ReturnsAsync(result);
    }

    private IReadOnlyList<string> CaptureArguments()
    {
        Assert.NotNull(_capturedArguments);
        return _capturedArguments;
    }

    private static string EncoderOf(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == "-c:v")
            {
                return arguments[index + 1];
            }
        }

        return string.Empty;
    }

    private FfmpegTranscoder CreateSut(bool keepOriginal = false)
    {
        var options = Options.Create(new MediaOrganizerOptions
        {
            Transcoding = new TranscodingOptions
            {
                Enabled = true,
                Encoder = "h264_vaapi",
                HardwareDevice = "/dev/dri/renderD128",
                Quality = 22,
                KeepOriginal = keepOriginal,
                AllowSoftwareFallback = true
            }
        });

        return new FfmpegTranscoder(_loggerMock.Object, _processRunnerMock.Object, _fsMock.Object, options);
    }
}