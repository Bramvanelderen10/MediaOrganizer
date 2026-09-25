namespace MediaOrganizer.Configuration;

/// <summary>
/// Settings for the optional ffmpeg transcoding step that converts video streams to a codec the
/// host's hardware can play (for example HEVC to H.264 on older Intel GPUs that cannot decode HEVC).
/// When <see cref="Enabled"/> is false the feature is completely inert.
/// </summary>
public class TranscodingOptions
{
    /// <summary>Master switch. When false no probing or transcoding happens.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// ffmpeg video encoder. <c>h264_vaapi</c> (Intel/AMD via VA-API) and <c>h264_qsv</c>
    /// (Intel Quick Sync) use the GPU; <c>libx264</c> is the software fallback.
    /// </summary>
    public string Encoder { get; set; } = "h264_vaapi";

    /// <summary>Render node passed to the hardware encoder, e.g. <c>/dev/dri/renderD128</c>.</summary>
    public string HardwareDevice { get; set; } = "/dev/dri/renderD128";

    /// <summary>
    /// Quality target. Used as <c>-global_quality</c> for hardware encoders and <c>-crf</c> for
    /// <c>libx264</c>. Lower is better quality and larger files.
    /// </summary>
    public int Quality { get; set; } = 22;

    /// <summary>Encoder preset, only used by the software <c>libx264</c> encoder.</summary>
    public string Preset { get; set; } = "medium";

    /// <summary>
    /// Source video codecs that trigger a transcode. Files already using
    /// <see cref="TargetCodec"/> are always skipped. Leave this empty to transcode every file
    /// whose codec is not the target codec.
    /// </summary>
    public string[] OnlyCodecs { get; set; } = ["hevc", "h265", "mpeg2video", "vc1", "av1", "vp9"];

    /// <summary>Video codec the transcoded output must have. Used to skip already-compatible files.</summary>
    public string TargetCodec { get; set; } = "h264";

    /// <summary>Keep the original file next to the transcoded output instead of replacing it.</summary>
    public bool KeepOriginal { get; set; }

    /// <summary>
    /// When true and a hardware encoder fails, retry the file once with the <c>libx264</c>
    /// software encoder so a missing GPU still produces a playable result.
    /// </summary>
    public bool AllowSoftwareFallback { get; set; } = true;

    /// <summary>Maximum seconds a single ffmpeg/ffprobe call may run before it is killed.</summary>
    public int TimeoutSeconds { get; set; } = 3600;
}
