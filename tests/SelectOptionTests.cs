using System.Text.Json;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class SelectOptionTests
{
    [Theory]
    [InlineData("""{"label":"Monday","value":"mon"}""")]
    [InlineData("""{"label":"Monday","value":1}""")]
    [InlineData("""{"label":"Half","value":0.5}""")]
    [InlineData("""{"label":"Yes","value":true}""")]
    [InlineData("""{"label":"Code","value":"007"}""")]
    public void SelectOption_Value_RoundTripsWithItsOriginalJsonType(string json)
    {
        var option = JsonSerializer.Deserialize<SelectOption>(json)!;

        JsonSerializer.Serialize(option).Should().Be(json);
    }

    [Fact]
    public void SelectFieldOptions_WithMixedValueTypes_RoundTrip()
    {
        const string json = """{"options":[{"label":"One","value":1},{"label":"Two","value":"two"}],"multiple":true}""";

        var options = JsonSerializer.Deserialize<SelectFieldOptions>(json)!;

        options.Options.Should().HaveCount(2);
        JsonSerializer.Serialize(options).Should().Be(json);
    }

    [Fact]
    public void SelectOptionsParsedFromTheCommandLine_AreSentAsStrings()
    {
        var options = SelectOptionsParser.Parse("1=One,two");

        JsonSerializer.Serialize(options).Should().Be("""[{"label":"1","value":"One"},{"label":"two","value":"two"}]""");
    }
}
