namespace MediaOrganizer.Transcoding;

/// <summary>
/// Converts a single video file to the configured target codec.
/// </summary>
public interface ITranscoder
{
    /// <summary>
    /// Transcodes <paramref name="filePath"/>. By default the original is replaced in place;
    /// when <c>KeepOriginal</c> is enabled a new file is written next to it instead.
    /// Returns true when a transcoded output file was produced.
    /// </summary>
    Task<bool> TranscodeAsync(string filePath, CancellationToken cancellationToken = default);
}
