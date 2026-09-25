namespace MediaOrganizer.Transcoding;

/// <summary>Result of a short synthetic encode used to validate the configured encoder.</summary>
public record TranscodeEncodeTest(
    bool Succeeded,
    bool IsHardwareEncoder,
    string? Driver,
    string? Output,
    /// <summary>Actionable hint when the test failed, e.g. how to fix a driver/device problem.</summary>
    string? Hint);

/// <summary>
/// Full transcoding self-test. Combines the "encoder is compiled into ffmpeg" check with a real
/// 2 second encode so callers can tell hardware acceleration apart from a software fallback.
/// </summary>
public record TranscodeSelfTest(
    bool Enabled,
    string Encoder,
    string HardwareDevice,
    /// <summary>The configured encoder is a hardware encoder (not libx264).</summary>
    bool IsHardwareEncoder,
    /// <summary>The configured encoder is listed by <c>ffmpeg -encoders</c>.</summary>
    bool EncoderAvailable,
    /// <summary>The short test encode completed with the configured encoder.</summary>
    bool EncodeSucceeded,
    /// <summary>True only when a hardware encoder was configured and the test encode succeeded.</summary>
    bool HardwareEncode,
    /// <summary>VA-API driver reported during the test (e.g. iHD or i965), when detectable.</summary>
    string? Driver,
    /// <summary>Trimmed ffmpeg output from the test encode, for diagnostics.</summary>
    string? Output,
    /// <summary>Actionable hint when the test failed.</summary>
    string? Hint);
