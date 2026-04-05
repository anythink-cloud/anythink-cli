using AnythinkCli.BulkOperations.Readers;
using AnythinkCli.BulkOperations.Exporters;
using AnythinkCli.BulkOperations.Models;
using System.Text.Json.Nodes;

namespace AnythinkCli.Tests.BulkOperations;

/// <summary>
/// Test helper for bulk operations functionality
/// </summary>
public class BulkOperationsTests
{
    public static async Task TestCsvImport()
    {
        Console.WriteLine("🧪 Testing CSV Import...");
        
        // Create test CSV data
        var csvData = @"name,email,age,city
John Doe,john@example.com,30,New York
Jane Smith,jane@example.com,25,Los Angeles
Bob Johnson,bob@example.com,35,Chicago";

        // Write to temp file
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, csvData);

        try
        {
            // Test CSV reader
            await using var stream = File.OpenRead(tempFile);
            var reader = new CsvDataReader(stream);
            
            var records = new List<DataRecord>();
            await foreach (var record in reader.ReadRecordsAsync())
            {
                records.Add(record);
                Console.WriteLine($"📄 Record {record.LineNumber}: {record.Data["name"]} - {record.Data["email"]}");
            }

            Console.WriteLine($"✅ Successfully read {records.Count} records from CSV");
            
            // Show metadata
            var metadata = reader.GetMetadata();
            Console.WriteLine($"📊 Metadata: {string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    public static async Task TestJsonImport()
    {
        Console.WriteLine("🧪 Testing JSON Import...");
        
        // Create test JSON data
        var jsonData = @"[
  {""name"": ""Alice"", ""email"": ""alice@example.com"", ""age"": 28},
  {""name"": ""Bob"", ""email"": ""bob@example.com"", ""age"": 32},
  {""name"": ""Charlie"", ""email"": ""charlie@example.com"", ""age"": 24}
]";

        // Write to temp file
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, jsonData);

        try
        {
            // Test JSON reader
            await using var stream = File.OpenRead(tempFile);
            var reader = new JsonDataReader(stream);
            
            var records = new List<DataRecord>();
            await foreach (var record in reader.ReadRecordsAsync())
            {
                records.Add(record);
                Console.WriteLine($"📄 Record {record.LineNumber}: {record.Data["name"]} - {record.Data["email"]}");
            }

            Console.WriteLine($"✅ Successfully read {records.Count} records from JSON");
            
            // Show metadata
            var metadata = reader.GetMetadata();
            Console.WriteLine($"📊 Metadata: {string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    public static async Task TestCsvExport()
    {
        Console.WriteLine("🧪 Testing CSV Export...");
        
        // Create test records
        var records = new List<DataRecord>
        {
            new DataRecord
            {
                LineNumber = 1,
                Data = new JsonObject
                {
                    ["name"] = "John Doe",
                    ["email"] = "john@example.com",
                    ["age"] = 30
                }
            },
            new DataRecord
            {
                LineNumber = 2,
                Data = new JsonObject
                {
                    ["name"] = "Jane Smith",
                    ["email"] = "jane@example.com",
                    ["age"] = 25
                }
            }
        };

        var tempFile = Path.GetTempFileName();
        var headers = new[] { "name", "email", "age" };

        try
        {
            // Test CSV exporter
            await using var stream = File.Create(tempFile);
            var exporter = new CsvDataExporter(stream, headers);
            
            await exporter.ExportAsync(records.ToAsyncEnumerable());
            
            // Read back and verify
            var exportedContent = await File.ReadAllTextAsync(tempFile);
            Console.WriteLine("📄 Exported CSV content:");
            Console.WriteLine(exportedContent);
            
            Console.WriteLine($"✅ Successfully exported {records.Count} records to CSV");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    public static async Task TestJsonExport()
    {
        Console.WriteLine("🧪 Testing JSON Export...");
        
        // Create test records
        var records = new List<DataRecord>
        {
            new DataRecord
            {
                LineNumber = 1,
                Data = new JsonObject
                {
                    ["name"] = "John Doe",
                    ["email"] = "john@example.com",
                    ["age"] = 30
                }
            },
            new DataRecord
            {
                LineNumber = 2,
                Data = new JsonObject
                {
                    ["name"] = "Jane Smith",
                    ["email"] = "jane@example.com",
                    ["age"] = 25
                }
            }
        };

        var tempFile = Path.GetTempFileName();

        try
        {
            // Test JSON exporter
            await using var stream = File.Create(tempFile);
            var exporter = new JsonDataExporter(stream, new JsonExportOptions
            {
                Format = JsonFormat.LineDelimited,
                PrettyPrint = true
            });
            
            await exporter.ExportAsync(records.ToAsyncEnumerable());
            
            // Read back and verify
            var exportedContent = await File.ReadAllTextAsync(tempFile);
            Console.WriteLine("📄 Exported JSON content:");
            Console.WriteLine(exportedContent);
            
            Console.WriteLine($"✅ Successfully exported {records.Count} records to JSON");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    public static async Task TestValidation()
    {
        Console.WriteLine("🧪 Testing Data Validation...");
        
        // Create validation schema
        var schema = new ValidationSchema
        {
            EntityName = "users",
            Fields = new Dictionary<string, FieldDefinition>
            {
                ["name"] = new FieldDefinition
                {
                    Name = "name",
                    Type = "string",
                    Required = true,
                    MinLength = 2,
                    MaxLength = 50
                },
                ["email"] = new FieldDefinition
                {
                    Name = "email",
                    Type = "string",
                    Required = true,
                    Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
                },
                ["age"] = new FieldDefinition
                {
                    Name = "age",
                    Type = "int",
                    Required = false,
                    MinValue = 0,
                    MaxValue = 150
                }
            }
        };

        var validator = new BasicDataValidator(schema);

        // Test valid record
        var validRecord = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject
            {
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["age"] = 30
            }
        };

        var validResult = await validator.ValidateAsync(validRecord);
        Console.WriteLine($"✅ Valid record validation: {validResult.IsValid} (Errors: {validResult.Errors.Count})");

        // Test invalid record
        var invalidRecord = new DataRecord
        {
            LineNumber = 2,
            Data = new JsonObject
            {
                ["name"] = "A", // Too short
                ["email"] = "invalid-email", // Invalid format
                ["age"] = -5 // Negative age
            }
        };

        var invalidResult = await validator.ValidateAsync(invalidRecord);
        Console.WriteLine($"❌ Invalid record validation: {invalidResult.IsValid} (Errors: {invalidResult.Errors.Count})");
        
        foreach (var error in invalidResult.Errors)
        {
            Console.WriteLine($"   • {error.Field}: {error.Message}");
        }
    }

    public static async Task RunAllTests()
    {
        Console.WriteLine("🚀 Starting Bulk Operations Tests...\n");
        
        await TestCsvImport();
        Console.WriteLine();
        
        await TestJsonImport();
        Console.WriteLine();
        
        await TestCsvExport();
        Console.WriteLine();
        
        await TestJsonExport();
        Console.WriteLine();
        
        await TestValidation();
        Console.WriteLine();
        
        Console.WriteLine("🎉 All tests completed!");
    }
}

// Extension method for IEnumerable to IAsyncEnumerable
public static class EnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
    }
}
