using AnythinkCli.Importers.Directus;
using FluentAssertions;
using System.Text.Json;

namespace AnythinkCli.Tests;

// ── DirectusTemplateAdapter ───────────────────────────────────────────────────
// Verifies Directus mustache expressions get rewritten into Anythink's
// $anythink.* template syntax during flow import — so migrated workflows
// actually substitute values at run time rather than printing literal
// placeholders.

public class DirectusTemplateAdapterTests
{
    private static readonly HashSet<string> NoSteps = new(StringComparer.OrdinalIgnoreCase);

    // ── $trigger.payload.X ────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_Trigger_Payload_Field()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "hello {{ $trigger.payload.title }}", previousStepKey: null, knownStepKeys: NoSteps);

        adapted.Should().Be("hello {{ $anythink.trigger.data.title }}");
        unresolved.Should().BeFalse();
    }

    [Fact]
    public void Rewrites_Trigger_Body_Field_Like_Payload()
    {
        // Webhook trigger uses $trigger.body; map to the same Anythink path
        // because Anythink presents the inbound data uniformly via $anythink.trigger.data.
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{ $trigger.body.email }}", null, NoSteps);

        adapted.Should().Be("{{ $anythink.trigger.data.email }}");
    }

    [Fact]
    public void Rewrites_Trigger_Id()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{ $trigger.id }}", null, NoSteps);

        adapted.Should().Be("{{ $anythink.trigger.id }}");
    }

    [Fact]
    public void Rewrites_Bare_Trigger()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{ $trigger }}", null, NoSteps);

        adapted.Should().Be("{{ $anythink.trigger }}");
    }

    // ── $last.X ──────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_Last_To_Predecessor_Step()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "{{ $last.title }}", previousStepKey: "fetch_payload", knownStepKeys: NoSteps);

        adapted.Should().Be("{{ $anythink.steps.fetch_payload.title }}");
        unresolved.Should().BeFalse();
    }

    [Fact]
    public void Last_Without_Predecessor_Is_Neutered()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "{{ $last.title }}", previousStepKey: null, knownStepKeys: NoSteps);

        // Mustache opener is broken so Anythink's server-side templater
        // can't resolve it accidentally. Visible to the user for review.
        adapted.Should().Be("{ { $last.title }} ");
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Last_Bare_Maps_To_Steps_Predecessor()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{ $last }}", "fetch_payload", NoSteps);

        adapted.Should().Be("{{ $anythink.steps.fetch_payload }}");
    }

    // ── Step-key references ──────────────────────────────────────────────────

    [Fact]
    public void Rewrites_Direct_Step_Key_Reference()
    {
        var steps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fetch_payload" };
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "{{ $fetch_payload.title }}", null, steps);

        adapted.Should().Be("{{ $anythink.steps.fetch_payload.title }}");
        unresolved.Should().BeFalse();
    }

    [Fact]
    public void Unknown_Step_Key_Is_Neutered_And_Flagged()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "{{ $unknown_step.title }}", null, NoSteps);

        adapted.Should().Be("{ { $unknown_step.title }} ");
        unresolved.Should().BeTrue();
    }

    // ── Unsupported expressions ──────────────────────────────────────────────

    [Theory]
    [InlineData("{{ $env.SOME_VAR }}",          "{ { $env.SOME_VAR }} ")]
    [InlineData("{{ $accountability.user }}",   "{ { $accountability.user }} ")]
    public void Unsupported_Heads_Are_Neutered_And_Flagged(string template, string expected)
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(template, null, NoSteps);

        adapted.Should().Be(expected);
        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Hostile_Anythink_Secret_Reference_Is_Neutered_Not_Passed_Through()
    {
        // Security: a compromised Directus admin must not be able to embed
        // `{{ $anythink.secrets.X }}` and have Anythink's server-side
        // templater resolve it at run time. The adapter must break the
        // mustache so it can't be matched by `\{\{([^}]+)\}\}`.
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "leak: {{ $anythink.secrets.STRIPE_KEY }}", null, NoSteps);

        adapted.Should().NotMatch("*{{*}}*");
        adapted.Should().Contain("$anythink.secrets.STRIPE_KEY");  // visible for review
        unresolved.Should().BeTrue();
    }

    // ── No-op cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Text_Without_Mustache_Is_Unchanged()
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.Adapt(
            "Plain text with no templates", null, NoSteps);

        adapted.Should().Be("Plain text with no templates");
        unresolved.Should().BeFalse();
    }

    [Fact]
    public void Multiple_Templates_Rewrite_Independently()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{ $trigger.payload.slug }} by {{ $trigger.payload.author }}",
            null, NoSteps);

        adapted.Should().Be(
            "{{ $anythink.trigger.data.slug }} by {{ $anythink.trigger.data.author }}");
    }

    [Fact]
    public void Whitespace_Inside_Braces_Is_Tolerated()
    {
        var (adapted, _) = DirectusTemplateAdapter.Adapt(
            "{{   $trigger.payload.title   }}", null, NoSteps);

        adapted.Should().Be("{{ $anythink.trigger.data.title }}");
    }

    // ── JsonElement walker ────────────────────────────────────────────────────

    [Fact]
    public void Adapts_All_String_Values_In_Json_Object()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            script = "console.log(\"{{ $trigger.payload.title }}\");",
            url    = "https://api.example.com/{{ $trigger.payload.slug }}"
        });

        var (adapted, unresolved) = DirectusTemplateAdapter.AdaptElement(input, null, NoSteps);

        unresolved.Should().BeFalse();
        adapted.GetProperty("script").GetString()
            .Should().Be("console.log(\"{{ $anythink.trigger.data.title }}\");");
        adapted.GetProperty("url").GetString()
            .Should().Be("https://api.example.com/{{ $anythink.trigger.data.slug }}");
    }

    [Fact]
    public void Walker_Recurses_Into_Nested_Objects_And_Arrays()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            headers = new { authorization = "Bearer {{ $trigger.payload.token }}" },
            tags    = new[] { "{{ $trigger.payload.tag }}" }
        });

        var (adapted, _) = DirectusTemplateAdapter.AdaptElement(input, null, NoSteps);

        adapted.GetProperty("headers").GetProperty("authorization").GetString()
            .Should().Be("Bearer {{ $anythink.trigger.data.token }}");
        adapted.GetProperty("tags")[0].GetString()
            .Should().Be("{{ $anythink.trigger.data.tag }}");
    }

    [Fact]
    public void Walker_Sets_Unresolved_When_Any_Inner_String_Cant_Translate()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            ok  = "{{ $trigger.payload.title }}",
            bad = "{{ $env.SECRET }}"
        });

        var (_, unresolved) = DirectusTemplateAdapter.AdaptElement(input, null, NoSteps);

        unresolved.Should().BeTrue();
    }

    [Fact]
    public void Walker_Preserves_Non_String_Primitive_Values()
    {
        var input = JsonSerializer.SerializeToElement(new
        {
            view_count  = 42,
            is_featured = true,
            tag         = "{{ $trigger.payload.tag }}"
        });

        var (adapted, _) = DirectusTemplateAdapter.AdaptElement(input, null, NoSteps);

        adapted.GetProperty("view_count").GetInt32().Should().Be(42);
        adapted.GetProperty("is_featured").GetBoolean().Should().BeTrue();
        adapted.GetProperty("tag").GetString().Should().Be("{{ $anythink.trigger.data.tag }}");
    }
}
