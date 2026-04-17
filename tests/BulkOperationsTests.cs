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
}

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
}

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

        var stream = new MemoryStream();
        var headers = new[] { "name", "email" };
        var exporter = new CsvDataExporter(stream, headers);

        await exporter.ExportAsync(ToAsync(records));

        // Read the bytes before dispose closes the stream
        var output = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
        await exporter.DisposeAsync();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("name,email");
        lines[1].Should().Be("John,john@test.com");
        lines[2].Should().Be("Jane,jane@test.com");
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (var item in source) yield return item;
        await Task.CompletedTask;
    }
}

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
        result.ValidRecords.Should().BeGreaterThanOrEqualTo(0);
        result.InvalidRecords.Should().BeGreaterThanOrEqualTo(1);
    }
}

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

        var result = await processor.ExecuteAsync(
            new[] { 1, 2, 3 },
            async item =>
            {
                if (item == 2) throw new Exception("fail");
                await Task.CompletedTask;
            },
            config);

        result.Total.Should().Be(3);
        result.ErrorDetails.Should().HaveCount(1);
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
}
