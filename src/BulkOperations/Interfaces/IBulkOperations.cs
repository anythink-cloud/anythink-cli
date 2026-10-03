using AnythinkCli.BulkOperations.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Interfaces;

/// <summary>
/// Interface for streaming data readers
/// </summary>
public interface IDataReader : IAsyncDisposable
{
    /// <summary>
    /// Reads data records asynchronously from the source
    /// </summary>
    IAsyncEnumerable<DataRecord> ReadRecordsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the total estimated number of records (if available)
    /// </summary>
    long? EstimatedTotalRecords { get; }
    
    /// <summary>
    /// Gets metadata about the data source
    /// </summary>
    Dictionary<string, object> GetMetadata();
}

/// <summary>
/// Interface for data validators
/// </summary>
public interface IDataValidator
{
    /// <summary>
    /// Validates a single data record
    /// </summary>
    Task<ValidationResult> ValidateAsync(DataRecord record, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates multiple records in batch
    /// </summary>
    Task<BatchValidationResult> ValidateBatchAsync(IEnumerable<DataRecord> records, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the validation schema
    /// </summary>
    ValidationSchema GetSchema();
}

/// <summary>
/// Interface for bulk data processors
/// </summary>
public interface IBulkProcessor
{
    /// <summary>
    /// Processes a batch of data records
    /// </summary>
    Task<BulkProcessResult> ProcessBatchAsync(IEnumerable<DataRecord> records, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the recommended batch size for this processor
    /// </summary>
    int RecommendedBatchSize { get; }
    
    /// <summary>
    /// Prepares the processor for operation
    /// </summary>
    Task PrepareAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cleans up resources after operation
    /// </summary>
    Task CleanupAsync();
}

/// <summary>
/// Interface for progress reporting
/// </summary>
public interface IBulkProgressReporter : IProgress<BulkProgress>
{
    /// <summary>
    /// Starts a new operation
    /// </summary>
    void StartOperation(string operationName, long estimatedTotal);
    
    /// <summary>
    /// Completes the current operation
    /// </summary>
    void CompleteOperation(bool success);
    
    /// <summary>
    /// Reports an error for a specific item
    /// </summary>
    void ReportError(BulkError error);
    
    /// <summary>
    /// Reports a warning for a specific item
    /// </summary>
    void ReportWarning(BulkWarning warning);
}

/// <summary>
/// Interface for data exporters
/// </summary>
public interface IDataExporter : IAsyncDisposable
{
    /// <summary>
    /// Exports data records asynchronously
    /// </summary>
    Task ExportAsync(IAsyncEnumerable<DataRecord> records, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the export format
    /// </summary>
    string GetFormat();
    
    /// <summary>
    /// Gets metadata about the export
    /// </summary>
    Dictionary<string, object> GetMetadata();
}

/// <summary>
/// Interface for resilient bulk operations with retry logic
/// </summary>
public interface IResilientBulkOperation
{
    /// <summary>
    /// Executes the operation with retry logic and circuit breaking
    /// </summary>
    Task<BulkResult> ExecuteAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        BulkOperationConfig config,
        IProgress<BulkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
