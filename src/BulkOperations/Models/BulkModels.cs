using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Models;

/// <summary>
/// Represents progress information for bulk operations
/// </summary>
public class BulkProgress
{
    public long Processed { get; set; }
    public long Total { get; set; }
    public long Errors { get; set; }
    public long Warnings { get; set; }
    public TimeSpan Elapsed { get; set; }
    public DateTime StartedAt { get; set; }
    public string? CurrentItem { get; set; }
    
    public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;
    
    public TimeSpan EstimatedTimeRemaining => 
        Processed > 0 ? TimeSpan.FromTicks((Elapsed.Ticks * (Total - Processed)) / Processed) : TimeSpan.Zero;
    
    public long Remaining => Total - Processed;
    public long ProcessedPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Processed / Elapsed.TotalSeconds) : 0;
}

/// <summary>
/// Represents the result of a bulk operation
/// </summary>
public class BulkResult
{
    public bool Success { get; set; }
    public long Processed { get; set; }
    public long Total { get; set; }
    public long Errors { get; set; }
    public long Warnings { get; set; }
    public TimeSpan Duration { get; set; }
    public List<BulkError> ErrorDetails { get; set; } = new();
    public List<BulkWarning> WarningDetails { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public double SuccessRate => Total > 0 ? (double)Processed / Total * 100 : 0;
}

/// <summary>
/// Represents an error that occurred during bulk operation
/// </summary>
public class BulkError
{
    public int LineNumber { get; set; }
    public string? ItemIdentifier { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorCode { get; set; }
    public JsonObject? ItemData { get; set; }
    public Exception? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a warning that occurred during bulk operation
/// </summary>
public class BulkWarning
{
    public int LineNumber { get; set; }
    public string? ItemIdentifier { get; set; }
    public string Message { get; set; } = "";
    public string? WarningCode { get; set; }
    public JsonObject? ItemData { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for bulk operations
/// </summary>
public class BulkOperationConfig
{
    public int BatchSize { get; set; } = 100;
    public int MaxConcurrency { get; set; } = 10;
    public bool ContinueOnError { get; set; } = true;
    public bool ValidateOnly { get; set; } = false;
    public bool ShowProgress { get; set; } = true;
    public TimeSpan? OperationTimeout { get; set; } = TimeSpan.FromHours(1);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public Dictionary<string, object> ValidationRules { get; set; } = new();
}

/// <summary>
/// Represents a data record to be processed
/// </summary>
public class DataRecord
{
    public int LineNumber { get; set; }
    public JsonObject Data { get; set; } = new();
    public string? Source { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
