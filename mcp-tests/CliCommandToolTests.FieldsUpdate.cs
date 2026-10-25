using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public partial class CliCommandToolTests
{
    private const string GenresName = """
        {"id":9,"name":"name","label":"Name","database_type":"varchar","display_type":"select","description":"Genre name",
         "is_required":true,"is_name_field":false,"is_unique":true,"is_immutable":true,"is_searchable":true,
         "publicly_searchable":true,"is_indexed":true,"default_value":"rock","sort_order":3,"width":50,
         "options":{"select":{"options":[{"label":"Rock","value":"rock"}],"multiple":false},"input":{"max_length":40},"read_only":true}}
        """;

    private static async Task<JsonElement> UpdateGenresName(object args, string field = GenresName)
    {
        JsonElement sent = default;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/42/entities/genres/fields").Respond("application/json", $"[{field}]");
        mock.When(HttpMethod.Put, $"{ApiUrl}/org/42/entities/genres/fields/9").Respond(async req =>
        {
            sent = JsonDocument.Parse(await req.Content!.ReadAsStringAsync()).RootElement.Clone();
            return new HttpResponseMessage { Content = new StringContent(field) };
        });

        var result = await Tool("fields_update").RunAsync(Args(args), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        return sent;
    }

    private static void ShouldKeepGenresNameSettings(JsonElement sent)
    {
        sent.GetProperty("label").GetString().Should().Be("Name");
        sent.GetProperty("display_type").GetString().Should().Be("select");
        sent.GetProperty("description").GetString().Should().Be("Genre name");
        sent.GetProperty("default_value").GetString().Should().Be("rock");
        foreach (var flag in new[] { "is_required", "is_unique", "is_immutable", "is_searchable", "publicly_searchable", "is_indexed" })
            sent.GetProperty(flag).GetBoolean().Should().BeTrue(flag);
        sent.GetProperty("sort_order").GetDecimal().Should().Be(3);
        sent.GetProperty("width").GetInt32().Should().Be(50);
        sent.GetProperty("options").GetProperty("input").GetProperty("max_length").GetInt32().Should().Be(40);
        sent.GetProperty("options").GetProperty("read_only").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FieldsUpdate_WithNameField_SetsItAndKeepsOtherSettings()
    {
        var sent = await UpdateGenresName(new { entity = "genres", field_name = "name", name_field = true });

        sent.GetProperty("is_name_field").GetBoolean().Should().BeTrue();
        sent.GetProperty("options").GetProperty("select").GetProperty("options")[0].GetProperty("value").GetString().Should().Be("rock");
        ShouldKeepGenresNameSettings(sent);
    }

    [Fact]
    public async Task FieldsUpdate_WithNoNameField_ClearsItAndKeepsOtherSettings()
    {
        var sent = await UpdateGenresName(new { entity = "genres", field_name = "name", no_name_field = true },
            GenresName.Replace("\"is_name_field\":false", "\"is_name_field\":true"));

        sent.GetProperty("is_name_field").GetBoolean().Should().BeFalse();
        ShouldKeepGenresNameSettings(sent);
    }

    [Fact]
    public async Task FieldsUpdate_WithoutNameField_KeepsTheNameFieldAndEveryOtherSetting()
    {
        var sent = await UpdateGenresName(new { entity = "genres", field_name = "name", required = true },
            GenresName.Replace("\"is_name_field\":false", "\"is_name_field\":true"));

        sent.GetProperty("is_name_field").GetBoolean().Should().BeTrue();
        ShouldKeepGenresNameSettings(sent);
    }

    [Fact]
    public async Task FieldsUpdate_WithNewSelectOptions_KeepsTheFieldsOtherOptions()
    {
        var sent = await UpdateGenresName(new { entity = "genres", field_name = "name", options = "Jazz=jazz", multiple = true });

        var select = sent.GetProperty("options").GetProperty("select");
        select.GetProperty("options")[0].GetProperty("value").GetString().Should().Be("jazz");
        select.GetProperty("multiple").GetBoolean().Should().BeTrue();
        ShouldKeepGenresNameSettings(sent);
    }
}
