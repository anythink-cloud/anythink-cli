using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.BulkOperations.Progress;
using System.Collections.Concurrent;

namespace AnythinkCli.BulkOperations.Core;

/// <summary>
/// Core bulk processor with parallel processing and resilience
/// </summary>
public class BulkDataProcessor : IResilientBulkOperation
{
    private readonly SemaphoreSlim _semaphore;
    private readonly BulkOperationConfig _config;
    private readonly IBulkProgressReporter? _progressReporter;

    public BulkDataProcessor(BulkOperationConfig? config = null, IBulkProgressReporter? progressReporter = null)
    {
        _config = config ?? new BulkOperationConfig();
        _progressReporter = progressReporter;
        _semaphore = new SemaphoreSlim(_config.MaxConcurrency);
    }

    private DateTime _startTime;

    public async Task<BulkResult> ExecuteAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        BulkOperationConfig? config = null,
        IProgress<BulkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveConfig = config ?? _config;
        var result = new BulkResult { Success = true };
        _startTime = DateTime.UtcNow;

        var itemList = items.ToList();
        var totalItems = itemList.Count;

        _progressReporter?.StartOperation("Bulk Operation", totalItems);
        
        try
        {
            if (effectiveConfig.MaxConcurrency == 1)
            {
                await ProcessSequentiallyAsync(itemList, operation, result, effectiveConfig, cancellationToken);
            }
            else
            {
                await ProcessInParallelAsync(itemList, operation, result, effectiveConfig, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorDetails.Add(new BulkError
            {
                Message = $"Bulk operation failed: {ex.Message}",
                Exception = ex
            });
        }
        finally
        {
            result.Duration = DateTime.UtcNow - _startTime;
            result.Total = totalItems;
            result.Processed = totalItems - result.Errors;
            
            _progressReporter?.CompleteOperation(result.Success);
        }

        return result;
    }

    private async Task ProcessSequentiallyAsync<T>(
        List<T> items,
        Func<T, Task> operation,
        BulkResult result,
        BulkOperationConfig config,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var item = items[i];
            await ProcessItemWithRetryAsync(item, operation, result, config, cancellationToken);
            
            UpdateProgress(i + 1, items.Count, result);
        }
    }

    private async Task ProcessInParallelAsync<T>(
        List<T> items,
        Func<T, Task> operation,
        BulkResult result,
        BulkOperationConfig config,
        CancellationToken cancellationToken)
    {
        var tasks = items.Select(async (item, index) =>
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessItemWithRetryAsync(item, operation, result, config, cancellationToken);
                return index + 1; // Return processed count
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessItemWithRetryAsync<T>(
        T item,
        Func<T, Task> operation,
        BulkResult result,
        BulkOperationConfig config,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        Exception? lastException = null;

        while (attempts <= config.MaxRetries)
        {
            try
            {
                await operation(item);
                return; // Success
            }
            catch (Exception ex) when (attempts < config.MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                attempts++;
                
                if (config.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(config.RetryDelay, cancellationToken);
                }
            }
        }

        // All retries failed
        var error = new BulkError
        {
            Message = lastException?.Message ?? "Operation failed after retries",
            Exception = lastException,
            ErrorCode = "RETRY_EXHAUSTED"
        };

        result.ErrorDetails.Add(error);
        
        if (!config.ContinueOnError)
        {
            throw new InvalidOperationException("Bulk operation failed due to error and ContinueOnError=false", lastException);
        }
    }

    private void UpdateProgress(long processed, long total, BulkResult result)
    {
        var progress = new BulkProgress
        {
            Processed = processed,
            Total = total,
            Errors = result.Errors,
            Warnings = result.Warnings,
            Elapsed = DateTime.UtcNow - _startTime
        };

        _progressReporter?.Report(progress);
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

/// <summary>
/// Factory for creating bulk processors with appropriate configurations
/// </summary>
public static class BulkProcessorFactory
{
    public static BulkDataProcessor CreateForImport(
        BulkOperationConfig? config = null,
        IBulkProgressReporter? progressReporter = null)
    {
        var importConfig = config ?? new BulkOperationConfig
        {
            BatchSize = 100,
            MaxConcurrency = 5,
            ContinueOnError = true,
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromSeconds(2)
        };

        return new BulkDataProcessor(importConfig, progressReporter);
    }

    public static BulkDataProcessor CreateForExport(
        BulkOperationConfig? config = null,
        IBulkProgressReporter? progressReporter = null)
    {
        var exportConfig = config ?? new BulkOperationConfig
        {
            BatchSize = 500,
            MaxConcurrency = 2,
            ContinueOnError = true,
            MaxRetries = 1,
            RetryDelay = TimeSpan.FromSeconds(1)
        };

        return new BulkDataProcessor(exportConfig, progressReporter);
    }

    public static BulkDataProcessor CreateForTransformation(
        BulkOperationConfig? config = null,
        IBulkProgressReporter? progressReporter = null)
    {
        var transformConfig = config ?? new BulkOperationConfig
        {
            BatchSize = 50,
            MaxConcurrency = 8,
            ContinueOnError = false,
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromSeconds(1)
        };

        return new BulkDataProcessor(transformConfig, progressReporter);
    }
}
