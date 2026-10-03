using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using System.Collections.Concurrent;

namespace AnythinkCli.BulkOperations.Progress;

/// <summary>
/// In-memory progress tracker for testing and programmatic access
/// </summary>
public class MemoryProgressTracker : IBulkProgressReporter
{
    private readonly ConcurrentQueue<BulkError> _errors = new();
    private readonly ConcurrentQueue<BulkWarning> _warnings = new();
    private readonly object _lock = new();
    private BulkProgress _currentProgress = new();
    private string _currentOperation = "";
    private long _estimatedTotal = 0;
    private DateTime _startTime = DateTime.UtcNow;

    public BulkProgress CurrentProgress => _currentProgress;
    public IReadOnlyList<BulkError> Errors => _errors.ToArray();
    public IReadOnlyList<BulkWarning> Warnings => _warnings.ToArray();
    public string CurrentOperation => _currentOperation;

    public void Report(BulkProgress progress)
    {
        lock (_lock)
        {
            _currentProgress = new BulkProgress
            {
                Processed = progress.Processed,
                Total = progress.Total,
                Errors = progress.Errors,
                Warnings = progress.Warnings,
                Elapsed = progress.Elapsed,
                StartedAt = progress.StartedAt,
                CurrentItem = progress.CurrentItem
            };
        }
    }

    public void StartOperation(string operationName, long estimatedTotal)
    {
        lock (_lock)
        {
            _currentOperation = operationName;
            _estimatedTotal = estimatedTotal;
            _startTime = DateTime.UtcNow;
            _currentProgress = new BulkProgress
            {
                StartedAt = _startTime
            };
        }
    }

    public void CompleteOperation(bool success)
    {
        lock (_lock)
        {
            _currentProgress.Elapsed = DateTime.UtcNow - _startTime;
        }
    }

    public void ReportError(BulkError error)
    {
        _errors.Enqueue(error);
        UpdateProgressCounts();
    }

    public void ReportWarning(BulkWarning warning)
    {
        _warnings.Enqueue(warning);
        UpdateProgressCounts();
    }

    private void UpdateProgressCounts()
    {
        lock (_lock)
        {
            _currentProgress.Errors = _errors.Count;
            _currentProgress.Warnings = _warnings.Count;
            _currentProgress.Elapsed = DateTime.UtcNow - _startTime;
        }
    }

    /// <summary>
    /// Gets a snapshot of the current state
    /// </summary>
    public ProgressSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new ProgressSnapshot
            {
                Operation = _currentOperation,
                Progress = _currentProgress,
                Errors = _errors.ToArray(),
                Warnings = _warnings.ToArray(),
                EstimatedTotal = _estimatedTotal,
                StartTime = _startTime
            };
        }
    }

    /// <summary>
    /// Clears all tracked data
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            while (_errors.TryDequeue(out _)) { }
            while (_warnings.TryDequeue(out _)) { }
            _currentProgress = new BulkProgress();
            _currentOperation = "";
            _estimatedTotal = 0;
        }
    }
}

/// <summary>
/// Immutable snapshot of progress state
/// </summary>
public class ProgressSnapshot
{
    public string Operation { get; set; } = "";
    public BulkProgress Progress { get; set; } = new();
    public BulkError[] Errors { get; set; } = Array.Empty<BulkError>();
    public BulkWarning[] Warnings { get; set; } = Array.Empty<BulkWarning>();
    public long EstimatedTotal { get; set; }
    public DateTime StartTime { get; set; }
}
