using AnythinkCli.BulkOperations.Models;

namespace AnythinkCli.BulkOperations.Models;

/// <summary>
/// Represents the result of a validation operation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents the result of a batch validation operation
/// </summary>
public class BatchValidationResult
{
    public bool IsValid { get; set; }
    public int TotalRecords { get; set; }
    public int ValidRecords { get; set; }
    public int InvalidRecords { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public List<int> ValidRecordIndices { get; set; } = new();
    public List<int> InvalidRecordIndices { get; set; } = new();
}

/// <summary>
/// Represents a validation error
/// </summary>
public class ValidationError
{
    public int LineNumber { get; set; }
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ErrorCode { get; set; }
    public object? AttemptedValue { get; set; }
    public string? ValidationRule { get; set; }
}

/// <summary>
/// Represents a validation warning
/// </summary>
public class ValidationWarning
{
    public int LineNumber { get; set; }
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
    public string? WarningCode { get; set; }
    public object? AttemptedValue { get; set; }
    public string? ValidationRule { get; set; }
}

/// <summary>
/// Represents the result of processing a batch
/// </summary>
public class BulkProcessResult
{
    public bool Success { get; set; }
    public int ProcessedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<BulkError> Errors { get; set; } = new();
    public List<BulkWarning> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents validation schema for data records
/// </summary>
public class ValidationSchema
{
    public string EntityName { get; set; } = "";
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();
    public Dictionary<string, object> Rules { get; set; } = new();
    public bool StrictMode { get; set; } = false;
}

/// <summary>
/// Represents field definition for validation
/// </summary>
public class FieldDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Required { get; set; } = false;
    public bool Unique { get; set; } = false;
    public object? DefaultValue { get; set; }
    public string? Pattern { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public List<string> AllowedValues { get; set; } = new();
    public Dictionary<string, object> CustomRules { get; set; } = new();
}
