namespace MediaOrganizer.Transcoding;

/// <summary>
/// Raised when an external transcoding process could not be started or failed unexpectedly.
/// </summary>
public class TranscodeException : Exception
{
    public TranscodeException(string message)
        : base(message)
    {
    }

    public TranscodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when transcoding was requested but is not enabled in configuration.
/// </summary>
public class TranscodeNotConfiguredException : TranscodeException
{
    public TranscodeNotConfiguredException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when a transcode request is malformed (missing/invalid folder).
/// </summary>
public class TranscodeValidationException : Exception
{
    public TranscodeValidationException(string message)
        : base(message)
    {
    }
}
