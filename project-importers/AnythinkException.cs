namespace Anythink.ProjectImporters;

/// <summary>API error returned by the server (HTTP 4xx/5xx).</summary>
public class AnythinkException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; init; }

    public virtual string StatusOnlyMessage => StatusCode is >= 200 and < 300
        ? "The project API returned a response this command couldn't read."
        : $"The project API returned status {StatusCode}.";
}
