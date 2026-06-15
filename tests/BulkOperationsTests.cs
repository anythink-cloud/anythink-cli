using AnythinkCli.BulkOperations.Core;
using AnythinkCli.BulkOperations.Exporters;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.BulkOperations.Progress;
using AnythinkCli.BulkOperations.Readers;
using AnythinkCli.BulkOperations.Validators;
using FluentAssertions;
using System.Text;
using System.Text.Json.Nodes;

namespace AnythinkCli.Tests.BulkOperations;

#region CSV Reader

public class CsvDataReaderTests
{
    [Fact]
    public async Task ReadRecordsAsync_ParsesCsvWithHeaders()
    {
        var csv = "name,email,age\nJohn,john@example.com,30\nJane,jane@example.com,25";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
        records[0].Data["name"]!.ToString().Should().Be("John");
        records[0].Data["email"]!.ToString().Should().Be("john@example.com");
        records[1].Data["name"]!.ToString().Should().Be("Jane");
    }

    [Fact]
    public async Task ReadRecordsAsync_ParsesQuotedFields()
    {
        var csv = "name,city\n\"Doe, John\",\"New York\"";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(1);
        records[0].Data["name"]!.ToString().Should().Be("Doe, John");
        records[0].Data["city"]!.ToString().Should().Be("New York");
    }

    [Fact]
    public async Task ReadRecordsAsync_ParsesNumericValues()
    {
        var csv = "count,price,active\n42,19.99,true";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records[0].Data["count"]!.GetValue<long>().Should().Be(42);
        records[0].Data["price"]!.GetValue<double>().Should().Be(19.99);
        records[0].Data["active"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ReadRecordsAsync_CustomDelimiter()
    {
        var csv = "name;email\nJohn;john@example.com";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream, new CsvReaderOptions { Delimiter = ';' });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(1);
        records[0].Data["name"]!.ToString().Should().Be("John");
    }

    [Fact]
    public async Task ReadRecordsAsync_EmptyValues_ParsedAsNull()
    {
        var csv = "name,email\nJohn,\n,jane@test.com";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
        records[0].Data["email"].Should().BeNull();
        records[1].Data["name"].Should().BeNull();
    }

    [Fact]
    public async Task ReadRecordsAsync_EscapedQuotes()
    {
        // CSV: John,"She said ""hi"""
        // Expected parsed value: She said "hi"
        var csvLine = "name,bio\nJohn," + "\"She said \"\"hi\"\"\"";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvLine));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records[0].Data["bio"]!.ToString().Should().Contain("said");
        records[0].Data["bio"]!.ToString().Should().Contain("hi");
    }

    [Fact]
    public async Task ReadRecordsAsync_SkipsEmptyLines()
    {
        var csv = "name\nAlice\n\nBob\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
        records[0].Data["name"]!.ToString().Should().Be("Alice");
        records[1].Data["name"]!.ToString().Should().Be("Bob");
    }

    [Fact]
    public async Task ReadRecordsAsync_EmbeddedJsonParsed()
    {
        var csv = "name,config\nTest,\"{\"\"key\"\": \"\"value\"\"}\"";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        // The JSON should be parsed as a JsonNode
        records[0].Data["config"].Should().NotBeNull();
    }

    [Fact]
    public async Task ReadRecordsAsync_LineNumbersAreCorrect()
    {
        var csv = "name\nAlice\nBob\nCharlie";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records[0].LineNumber.Should().Be(2);
        records[1].LineNumber.Should().Be(3);
        records[2].LineNumber.Should().Be(4);
    }

    [Fact]
    public void GetMetadata_ReturnsCorrectInfo()
    {
        var csv = "name,email\nJohn,john@test.com";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = new CsvDataReader(stream);

        var metadata = reader.GetMetadata();
        metadata["format"].Should().Be("csv");
        metadata["has_headers"].Should().Be(true);
        ((string[])metadata["headers"]).Should().BeEquivalentTo(new[] { "name", "email" });
    }

    [Fact]
    public void Constructor_EmptyFile_Throws()
    {
        var stream = new MemoryStream(Array.Empty<byte>());
        var act = () => new CsvDataReader(stream);
        act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
    }
}

#endregion

#region JSON Reader

public class JsonDataReaderTests
{
    [Fact]
    public async Task ReadRecordsAsync_ParsesJsonArray()
    {
        var json = "[{\"name\":\"Alice\",\"age\":28},{\"name\":\"Bob\",\"age\":32}]";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.Array });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
        records[0].Data["name"]!.ToString().Should().Be("Alice");
        records[1].Data["name"]!.ToString().Should().Be("Bob");
    }

    [Fact]
    public async Task ReadRecordsAsync_ParsesLineDelimited()
    {
        var json = "{\"name\":\"Alice\"}\n{\"name\":\"Bob\"}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.LineDelimited });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
        records[0].Data["name"]!.ToString().Should().Be("Alice");
    }

    [Fact]
    public async Task ReadRecordsAsync_LineDelimited_SkipsBlankLines()
    {
        var json = "{\"name\":\"Alice\"}\n\n{\"name\":\"Bob\"}\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.LineDelimited });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadRecordsAsync_LineDelimited_MalformedLine_ReturnsParseError()
    {
        var json = "{\"name\":\"Alice\"}\nnot-json\n{\"name\":\"Bob\"}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.LineDelimited });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(3);
        records[1].Metadata.Should().ContainKey("parse_error");
        records[1].Data.Count.Should().Be(0);
    }

    [Fact]
    public async Task ReadRecordsAsync_Array_NestedObjects()
    {
        var json = "[{\"name\":\"Alice\",\"address\":{\"city\":\"London\",\"country\":\"UK\"}}]";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.Array });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().HaveCount(1);
        records[0].Data["address"].Should().NotBeNull();
        records[0].Data["address"]!["city"]!.ToString().Should().Be("London");
    }

    [Fact]
    public async Task ReadRecordsAsync_Array_NonArrayRoot_Throws()
    {
        var json = "{\"name\":\"Alice\"}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.Array });

        var act = async () =>
        {
            await foreach (var _ in reader.ReadRecordsAsync()) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*array*");
    }

    [Fact]
    public async Task ReadRecordsAsync_Array_EmptyArray()
    {
        var json = "[]";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new JsonDataReader(stream, new JsonReaderOptions { Format = JsonFormat.Array });

        var records = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            records.Add(record);

        records.Should().BeEmpty();
    }
}

#endregion

#region CSV Exporter

public class CsvDataExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesCsvWithHeaders()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "John", ["email"] = "john@test.com" } },
            new() { LineNumber = 2, Data = new JsonObject { ["name"] = "Jane", ["email"] = "jane@test.com" } }
        };

        var output = await ExportToCsv(records, new[] { "name", "email" });
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("name,email");
        lines[1].Should().Be("John,john@test.com");
        lines[2].Should().Be("Jane,jane@test.com");
    }

    [Fact]
    public async Task ExportAsync_QuotesFieldsWithCommas()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "Doe, John", ["city"] = "New York" } }
        };

        var output = await ExportToCsv(records, new[] { "name", "city" });
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("\"Doe, John\",New York");
    }

    [Fact]
    public async Task ExportAsync_NoHeaders()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "John" } }
        };

        var stream = new MemoryStream();
        var exporter = new CsvDataExporter(stream, new[] { "name" }, new CsvExportOptions { IncludeHeaders = false });
        await exporter.ExportAsync(ToAsync(records));
        var output = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
        await exporter.DisposeAsync();

        output.Trim().Should().Be("John");
    }

    [Fact]
    public async Task ExportAsync_MissingFields_OutputEmpty()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "John" } }
        };

        var output = await ExportToCsv(records, new[] { "name", "email" });
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[1].Should().Be("John,");
    }

    [Fact]
    public async Task ExportAsync_EmptyRecordList()
    {
        var output = await ExportToCsv(new List<DataRecord>(), new[] { "name" });

        // Headers are only written when the first record arrives, so empty input = empty output
        output.Trim().Should().BeEmpty();
    }

    private static async Task<string> ExportToCsv(List<DataRecord> records, string[] headers, CsvExportOptions? options = null)
    {
        var stream = new MemoryStream();
        var exporter = new CsvDataExporter(stream, headers, options);
        await exporter.ExportAsync(ToAsync(records));
        var output = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
        await exporter.DisposeAsync();
        return output;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (var item in source) yield return item;
        await Task.CompletedTask;
    }
}

#endregion

#region JSON Exporter

public class JsonDataExporterTests
{
    [Fact]
    public async Task ExportAsync_LineDelimited()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "Alice" } },
            new() { LineNumber = 2, Data = new JsonObject { ["name"] = "Bob" } }
        };

        var output = await ExportToJson(records, new JsonExportOptions { Format = JsonFormat.LineDelimited });
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2);
        JsonNode.Parse(lines[0])!["name"]!.ToString().Should().Be("Alice");
        JsonNode.Parse(lines[1])!["name"]!.ToString().Should().Be("Bob");
    }

    [Fact]
    public async Task ExportAsync_ArrayFormat()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "Alice" } },
            new() { LineNumber = 2, Data = new JsonObject { ["name"] = "Bob" } }
        };

        var output = await ExportToJson(records, new JsonExportOptions { Format = JsonFormat.Array });

        var array = JsonNode.Parse(output) as JsonArray;
        array.Should().NotBeNull();
        array!.Count.Should().Be(2);
        array[0]!["name"]!.ToString().Should().Be("Alice");
        array[1]!["name"]!.ToString().Should().Be("Bob");
    }

    [Fact]
    public async Task ExportAsync_EmptyRecordList_ArrayFormat()
    {
        var output = await ExportToJson(new List<DataRecord>(), new JsonExportOptions { Format = JsonFormat.Array });
        output.Should().Be("[]");
    }

    [Fact]
    public async Task ExportAsync_EmptyRecordList_LineDelimited()
    {
        var output = await ExportToJson(new List<DataRecord>(), new JsonExportOptions { Format = JsonFormat.LineDelimited });
        output.Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_PreservesNestedObjects()
    {
        var records = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject
            {
                ["name"] = "Alice",
                ["address"] = new JsonObject { ["city"] = "London" }
            }}
        };

        var output = await ExportToJson(records, new JsonExportOptions { Format = JsonFormat.LineDelimited });
        var parsed = JsonNode.Parse(output.Trim())!;

        parsed["address"]!["city"]!.ToString().Should().Be("London");
    }

    private static async Task<string> ExportToJson(List<DataRecord> records, JsonExportOptions options)
    {
        var stream = new MemoryStream();
        var exporter = new JsonDataExporter(stream, options);
        await exporter.ExportAsync(ToAsync(records));
        var output = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
        await exporter.DisposeAsync();
        return output;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (var item in source) yield return item;
        await Task.CompletedTask;
    }
}

#endregion

#region Validator

public class BasicDataValidatorTests
{
    private static ValidationSchema TestSchema => new()
    {
        EntityName = "users",
        Fields = new Dictionary<string, FieldDefinition>
        {
            ["name"] = new() { Name = "name", Type = "string", Required = true, MinLength = 2, MaxLength = 50 },
            ["email"] = new() { Name = "email", Type = "string", Required = true, Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$" },
            ["age"] = new() { Name = "age", Type = "int", Required = false, MinValue = 0, MaxValue = 150 }
        }
    };

    [Fact]
    public async Task ValidateAsync_ValidRecord_ReturnsValid()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "John Doe", ["email"] = "john@example.com", ["age"] = 30 }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MissingRequired_ReturnsInvalid()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["age"] = 30 }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "name");
        result.Errors.Should().Contain(e => e.Field == "email");
    }

    [Fact]
    public async Task ValidateAsync_InvalidPattern_ReturnsError()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "John", ["email"] = "not-an-email" }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "email" && e.ValidationRule == "pattern");
    }

    [Fact]
    public async Task ValidateAsync_TooShort_ReturnsError()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "J", ["email"] = "j@t.co" }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "name" && e.ValidationRule == "minLength");
    }

    [Fact]
    public async Task ValidateAsync_ExceedsMaxValue_ReturnsError()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "John", ["email"] = "john@test.com", ["age"] = 200 }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "age" && e.ValidationRule == "maxValue");
    }

    [Fact]
    public async Task ValidateAsync_BelowMinValue_ReturnsError()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "John", ["email"] = "john@test.com", ["age"] = -5 }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "age" && e.ValidationRule == "minValue");
    }

    [Fact]
    public async Task ValidateAsync_AllowedValues()
    {
        var schema = new ValidationSchema
        {
            Fields = new Dictionary<string, FieldDefinition>
            {
                ["status"] = new() { Name = "status", Type = "string", AllowedValues = new List<string> { "active", "inactive" } }
            }
        };
        var validator = new BasicDataValidator(schema);

        var valid = await validator.ValidateAsync(new DataRecord { Data = new JsonObject { ["status"] = "active" } });
        valid.IsValid.Should().BeTrue();

        var invalid = await validator.ValidateAsync(new DataRecord { Data = new JsonObject { ["status"] = "deleted" } });
        invalid.IsValid.Should().BeFalse();
        invalid.Errors.Should().Contain(e => e.ValidationRule == "allowedValues");
    }

    [Fact]
    public async Task ValidateAsync_OptionalFieldNull_IsValid()
    {
        var validator = new BasicDataValidator(TestSchema);
        var record = new DataRecord
        {
            LineNumber = 1,
            Data = new JsonObject { ["name"] = "John", ["email"] = "john@test.com" }
        };

        var result = await validator.ValidateAsync(record);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBatchAsync_MixedRecords_ReportsCorrectCounts()
    {
        var validator = new BasicDataValidator(TestSchema);
        var records = new[]
        {
            new DataRecord { LineNumber = 1, Data = new JsonObject { ["name"] = "John", ["email"] = "john@test.com" } },
            new DataRecord { LineNumber = 2, Data = new JsonObject { ["name"] = "J", ["email"] = "invalid" } }
        };

        var result = await validator.ValidateBatchAsync(records);
        result.TotalRecords.Should().Be(2);
        result.ValidRecords.Should().Be(1);
        result.InvalidRecords.Should().Be(1);
        result.ValidRecordIndices.Should().Contain(0);
        result.InvalidRecordIndices.Should().Contain(1);
    }
}

#endregion

#region Bulk Processor

public class BulkDataProcessorTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesAllItems()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0 };
        var processor = new BulkDataProcessor(config);
        var processed = new List<int>();

        var result = await processor.ExecuteAsync(
            new[] { 1, 2, 3 },
            async item => { processed.Add(item); await Task.CompletedTask; },
            config);

        result.Success.Should().BeTrue();
        result.Total.Should().Be(3);
        processed.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task ExecuteAsync_ContinueOnError_ProcessesRemaining()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0, ContinueOnError = true };
        var processor = new BulkDataProcessor(config);
        var processed = new List<int>();

        var result = await processor.ExecuteAsync(
            new[] { 1, 2, 3 },
            async item =>
            {
                if (item == 2) throw new Exception("fail");
                processed.Add(item);
                await Task.CompletedTask;
            },
            config);

        result.Success.Should().BeFalse();
        result.Total.Should().Be(3);
        result.ErrorDetails.Should().NotBeEmpty();
        result.ErrorDetails[0].Message.Should().Contain("fail");
    }

    [Fact]
    public async Task ExecuteAsync_StopOnError_MarksFailure()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0, ContinueOnError = false };
        var processor = new BulkDataProcessor(config);

        var result = await processor.ExecuteAsync(
            new[] { 1, 2, 3 },
            async item =>
            {
                if (item == 2) throw new Exception("fail");
                await Task.CompletedTask;
            },
            config);

        result.Success.Should().BeFalse();
        result.ErrorDetails.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnFailure()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 2, RetryDelay = TimeSpan.Zero, ContinueOnError = true };
        var processor = new BulkDataProcessor(config);
        var attempts = 0;

        var result = await processor.ExecuteAsync(
            new[] { 1 },
            async _ =>
            {
                attempts++;
                if (attempts < 3) throw new Exception("transient");
                await Task.CompletedTask;
            },
            config);

        result.Success.Should().BeTrue();
        attempts.Should().Be(3); // 1 initial + 2 retries
        result.ErrorDetails.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RetryExhausted_RecordsError()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 2, RetryDelay = TimeSpan.Zero, ContinueOnError = true };
        var processor = new BulkDataProcessor(config);
        var attempts = 0;

        var result = await processor.ExecuteAsync(
            new[] { 1 },
            async _ =>
            {
                attempts++;
                await Task.CompletedTask;
                throw new Exception("permanent failure");
            },
            config);

        attempts.Should().BeGreaterThanOrEqualTo(2); // At least 1 initial + 1 retry
        result.Success.Should().BeFalse();
        result.ErrorDetails.Should().NotBeEmpty();
        result.ErrorDetails[0].Message.Should().Contain("permanent failure");
    }

    [Fact]
    public async Task ExecuteAsync_ParallelProcessing()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 5, MaxRetries = 0 };
        var processor = new BulkDataProcessor(config);
        var processed = new System.Collections.Concurrent.ConcurrentBag<int>();

        var result = await processor.ExecuteAsync(
            Enumerable.Range(1, 20).ToArray(),
            async item =>
            {
                await Task.Delay(10);
                processed.Add(item);
            },
            config);

        result.Success.Should().BeTrue();
        result.Total.Should().Be(20);
        processed.Should().HaveCount(20);
        processed.Should().BeEquivalentTo(Enumerable.Range(1, 20));
    }

    [Fact]
    public async Task ExecuteAsync_TracksDuration()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0 };
        var processor = new BulkDataProcessor(config);

        var result = await processor.ExecuteAsync(
            new[] { 1 },
            async _ => await Task.CompletedTask,
            config);

        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_Succeeds()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0 };
        var processor = new BulkDataProcessor(config);

        var result = await processor.ExecuteAsync(
            Array.Empty<int>(),
            async _ => await Task.CompletedTask,
            config);

        result.Success.Should().BeTrue();
        result.Total.Should().Be(0);
        result.Processed.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithProgressReporter_ReportsProgress()
    {
        var config = new BulkOperationConfig { MaxConcurrency = 1, MaxRetries = 0 };
        var tracker = new MemoryProgressTracker();
        var processor = new BulkDataProcessor(config, tracker);

        var result = await processor.ExecuteAsync(
            new[] { 1, 2, 3 },
            async _ => await Task.CompletedTask,
            config);

        result.Success.Should().BeTrue();
        tracker.CurrentOperation.Should().Be("Bulk Operation");
    }
}

#endregion

#region Progress Tracker

public class MemoryProgressTrackerTests
{
    [Fact]
    public void StartOperation_SetsState()
    {
        var tracker = new MemoryProgressTracker();
        tracker.StartOperation("Test Import", 100);

        tracker.CurrentOperation.Should().Be("Test Import");
    }

    [Fact]
    public void Report_UpdatesProgress()
    {
        var tracker = new MemoryProgressTracker();
        tracker.StartOperation("Test", 10);
        tracker.Report(new BulkProgress { Processed = 5, Total = 10 });

        tracker.CurrentProgress.Processed.Should().Be(5);
        tracker.CurrentProgress.Total.Should().Be(10);
    }

    [Fact]
    public void ReportError_TracksErrors()
    {
        var tracker = new MemoryProgressTracker();
        tracker.ReportError(new BulkError { Message = "fail 1", LineNumber = 1 });
        tracker.ReportError(new BulkError { Message = "fail 2", LineNumber = 2 });

        tracker.Errors.Should().HaveCount(2);
        tracker.CurrentProgress.Errors.Should().Be(2);
    }

    [Fact]
    public void ReportWarning_TracksWarnings()
    {
        var tracker = new MemoryProgressTracker();
        tracker.ReportWarning(new BulkWarning { Message = "warn 1" });

        tracker.Warnings.Should().HaveCount(1);
        tracker.CurrentProgress.Warnings.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_ReturnsImmutableCopy()
    {
        var tracker = new MemoryProgressTracker();
        tracker.StartOperation("Test", 10);
        tracker.Report(new BulkProgress { Processed = 5, Total = 10 });
        tracker.ReportError(new BulkError { Message = "err" });

        var snapshot = tracker.GetSnapshot();
        snapshot.Operation.Should().Be("Test");
        snapshot.Progress.Processed.Should().Be(5);
        snapshot.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var tracker = new MemoryProgressTracker();
        tracker.StartOperation("Test", 10);
        tracker.Report(new BulkProgress { Processed = 5 });
        tracker.ReportError(new BulkError { Message = "err" });

        tracker.Reset();

        tracker.CurrentOperation.Should().BeEmpty();
        tracker.Errors.Should().BeEmpty();
        tracker.CurrentProgress.Processed.Should().Be(0);
    }
}

#endregion

#region Round-trip (CSV read → write)

public class CsvRoundTripTests
{
    [Fact]
    public async Task CsvExportThenImport_PreservesData()
    {
        var original = new List<DataRecord>
        {
            new() { LineNumber = 1, Data = new JsonObject { ["name"] = "Alice", ["score"] = "95" } },
            new() { LineNumber = 2, Data = new JsonObject { ["name"] = "Bob", ["score"] = "87" } }
        };

        // Export
        var stream = new MemoryStream();
        var headers = new[] { "name", "score" };
        var exporter = new CsvDataExporter(stream, headers);
        await exporter.ExportAsync(ToAsync(original));
        var csvBytes = stream.ToArray();
        await exporter.DisposeAsync();

        // Import
        var importStream = new MemoryStream(csvBytes);
        var reader = new CsvDataReader(importStream);
        var imported = new List<DataRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
            imported.Add(record);

        imported.Should().HaveCount(2);
        imported[0].Data["name"]!.ToString().Should().Be("Alice");
        imported[1].Data["name"]!.ToString().Should().Be("Bob");
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (var item in source) yield return item;
        await Task.CompletedTask;
    }
}

#endregion

#region Models

public class BulkProgressTests
{
    [Fact]
    public void PercentComplete_CalculatesCorrectly()
    {
        var progress = new BulkProgress { Processed = 25, Total = 100 };
        progress.PercentComplete.Should().Be(25.0);
    }

    [Fact]
    public void PercentComplete_ZeroTotal_ReturnsZero()
    {
        var progress = new BulkProgress { Processed = 0, Total = 0 };
        progress.PercentComplete.Should().Be(0);
    }

    [Fact]
    public void Remaining_CalculatesCorrectly()
    {
        var progress = new BulkProgress { Processed = 30, Total = 100 };
        progress.Remaining.Should().Be(70);
    }

    [Fact]
    public void ProcessedPerSecond_CalculatesRate()
    {
        var progress = new BulkProgress { Processed = 100, Elapsed = TimeSpan.FromSeconds(10) };
        progress.ProcessedPerSecond.Should().Be(10);
    }

    [Fact]
    public void SuccessRate_CalculatesCorrectly()
    {
        var result = new BulkResult { Processed = 90, Total = 100 };
        result.SuccessRate.Should().Be(90.0);
    }
}

#endregion
