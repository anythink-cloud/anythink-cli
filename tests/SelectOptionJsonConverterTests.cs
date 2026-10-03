using System.Text.Json;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class SelectOptionJsonConverterTests
{
    [Fact]
    public void Reads_a_string_value()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"Monday","value":"mon"}""")!;

        option.Should().Be(new SelectOption("Monday", "mon"));
        option.ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void Reads_a_numeric_value_as_text_and_remembers_its_kind()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"Monday","value":1}""")!;

        option.Label.Should().Be("Monday");
        option.Value.Should().Be("1");
        option.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public void Writes_a_numeric_value_back_as_a_number()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"Half","value":0.5}""")!;

        JsonSerializer.Serialize(option).Should().Be("""{"label":"Half","value":0.5}""");
    }

    [Fact]
    public void Writes_a_string_value_back_as_a_string()
    {
        JsonSerializer.Serialize(new SelectOption("Monday", "mon")).Should().Be("""{"label":"Monday","value":"mon"}""");
    }

    [Fact]
    public void Keeps_a_numeric_looking_string_as_a_string()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"Code","value":"007"}""")!;

        JsonSerializer.Serialize(option).Should().Be("""{"label":"Code","value":"007"}""");
    }

    [Fact]
    public void Reads_a_field_whose_options_mix_types()
    {
        const string json = """{"options":[{"label":"One","value":1},{"label":"Two","value":"two"}],"multiple":true}""";

        var options = JsonSerializer.Deserialize<SelectFieldOptions>(json)!;

        options.Options.Should().HaveCount(2);
        JsonSerializer.Serialize(options).Should().Be(json);
    }

    [Fact]
    public void Falls_back_to_the_label_when_value_is_missing()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"Only"}""")!;

        option.Value.Should().Be("Only");
        option.ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public void Treats_a_null_value_as_empty()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":"None","value":null}""")!;

        option.Value.Should().Be("");
        option.ValueKind.Should().Be(JsonValueKind.Null);
    }

    // Rule: options sent back by `fields update --multiple` must keep the exact JSON the server stored.
    [Theory]
    [InlineData("""{"label":"A","value":1e5}""")]
    [InlineData("""{"label":"A","value":123456789012345678901234567890}""")]
    [InlineData("""{"label":"A","value":1.5}""")]
    [InlineData("""{"label":"A","value":-3}""")]
    [InlineData("""{"label":"A","value":true}""")]
    [InlineData("""{"label":"A","value":false}""")]
    [InlineData("""{"label":"A","value":null}""")]
    [InlineData("""{"label":"A"}""")]
    [InlineData("""{"label":"A","value":"1"}""")]
    [InlineData("""{"label":"A","value":""}""")]
    public void Round_trips_the_value_exactly(string json)
    {
        var option = JsonSerializer.Deserialize<SelectOption>(json)!;

        JsonSerializer.Serialize(option).Should().Be(json);
    }

    [Fact]
    public void Writes_a_numeric_label_back_as_text()
    {
        var option = JsonSerializer.Deserialize<SelectOption>("""{"label":5,"value":5}""")!;

        option.Label.Should().Be("5");
        JsonSerializer.Serialize(option).Should().Be("""{"label":"5","value":5}""");
    }

    [Theory]
    [InlineData(JsonValueKind.Number, "abc")]
    [InlineData(JsonValueKind.Number, "")]
    [InlineData(JsonValueKind.Number, "true")]
    [InlineData(JsonValueKind.True, "1")]
    public void Writes_a_string_when_the_text_is_not_json_of_the_recorded_kind(JsonValueKind kind, string text)
    {
        var option = new SelectOption("A", text) { ValueKind = kind };

        JsonSerializer.Serialize(option).Should().Be($$"""{"label":"A","value":"{{text}}"}""");
    }
}
