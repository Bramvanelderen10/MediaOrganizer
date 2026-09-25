namespace MediaOrganizer.Transcoding;

/// <summary>
/// Inspects media files and the local ffmpeg installation.
/// </summary>
public interface IVideoProbe
{
    /// <summary>
    /// Returns the codec name of the first video stream (for example <c>hevc</c> or <c>h264</c>),
    /// or <c>null</c> when it cannot be determined.
    /// </summary>
    Task<string?> GetVideoCodecAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Returns true when the given encoder is present in the local ffmpeg build.</summary>
    Task<bool> IsEncoderAvailableAsync(string encoder, CancellationToken cancellationToken = default);
}
