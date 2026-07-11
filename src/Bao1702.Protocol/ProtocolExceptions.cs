namespace Bao1702.Protocol;

/// <summary>
/// Exception thrown when a radio protocol operation fails.
/// </summary>
public class ProtocolException : Exception
{
    public ProtocolException(string message)
        : base(message)
    {
    }

    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a safety policy blocks a radio operation.
/// </summary>
public sealed class SafetyException : ProtocolException
{
    public SafetyException(string message)
        : base(message)
    {
    }

    public SafetyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
