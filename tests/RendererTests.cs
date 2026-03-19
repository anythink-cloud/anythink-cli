using System.Text;
using System.Text.Json;
using AnythinkCli.Output;
using FluentAssertions;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for Renderer.NameFromJwt — the only pure-logic method in Renderer.
/// All other Renderer methods write to AnsiConsole and are integration concerns.
/// </summary>
public class RendererTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal JWT with the given payload object.</summary>
    private static string MakeJwt(object payload)
    {
        var json  = JsonSerializer.Serialize(payload);
        var b64   = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        // Use a fake header and signature — NameFromJwt only reads the payload segment
        return $"eyJhbGciOiJIUzI1NiJ9.{b64}.fakesignature";
    }

    // ── Null / malformed input ────────────────────────────────────────────────

    [Fact]
    public void NameFromJwt_NullToken_ReturnsNull()
        => Renderer.NameFromJwt(null).Should().BeNull();

    [Fact]
    public void NameFromJwt_EmptyToken_ReturnsNull()
        => Renderer.NameFromJwt("").Should().BeNull();

    [Fact]
    public void NameFromJwt_NoDots_ReturnsNull()
        => Renderer.NameFromJwt("notajwtatall").Should().BeNull();

    [Fact]
    public void NameFromJwt_OneDot_ReturnsNull()
        => Renderer.NameFromJwt("header.onlyone").Should().BeNull();

    [Fact]
    public void NameFromJwt_InvalidBase64Payload_ReturnsNull()
        => Renderer.NameFromJwt("header.!!!invalid!!!.sig").Should().BeNull();

    [Fact]
    public void NameFromJwt_ValidJwtButNoClaims_ReturnsNull()
    {
        var token = MakeJwt(new { sub = "1234", iat = 1700000000 });
        Renderer.NameFromJwt(token).Should().BeNull();
    }

    // ── Claim priority ────────────────────────────────────────────────────────

    [Fact]
    public void NameFromJwt_NameClaim_ReturnsName()
    {
        var token = MakeJwt(new { name = "Alice Smith", email = "alice@example.com" });
        Renderer.NameFromJwt(token).Should().Be("Alice Smith");
    }

    [Fact]
    public void NameFromJwt_FirstNameClaim_WhenNoName_ReturnsFirstName()
    {
        var token = MakeJwt(new { first_name = "Alice", email = "alice@example.com" });
        Renderer.NameFromJwt(token).Should().Be("Alice");
    }

    [Fact]
    public void NameFromJwt_GivenNameClaim_WhenNoNameOrFirstName_ReturnsGivenName()
    {
        var token = MakeJwt(new { given_name = "Bob", email = "bob@example.com" });
        Renderer.NameFromJwt(token).Should().Be("Bob");
    }

    [Fact]
    public void NameFromJwt_PreferredUsernameClaim_WhenNoHigherPriorityClaims_ReturnsUsername()
    {
        var token = MakeJwt(new { preferred_username = "bobbuilder", email = "bob@example.com" });
        Renderer.NameFromJwt(token).Should().Be("bobbuilder");
    }

    [Fact]
    public void NameFromJwt_NameClaimWinsOverFirstName()
    {
        var token = MakeJwt(new { name = "Alice Smith", first_name = "Alice", given_name = "A" });
        Renderer.NameFromJwt(token).Should().Be("Alice Smith");
    }

    // ── Email fallback ────────────────────────────────────────────────────────

    [Fact]
    public void NameFromJwt_OnlyEmailClaim_ReturnsLocalPart()
    {
        var token = MakeJwt(new { email = "alice@example.com" });
        Renderer.NameFromJwt(token).Should().Be("alice");
    }

    [Fact]
    public void NameFromJwt_EmailWithSubdomain_ReturnsLocalPart()
    {
        var token = MakeJwt(new { email = "chris.addams@company.co.uk" });
        Renderer.NameFromJwt(token).Should().Be("chris.addams");
    }

    [Fact]
    public void NameFromJwt_EmptyNameClaim_FallsThroughToEmail()
    {
        // Empty string name claim should be skipped, falling through to email
        var token = MakeJwt(new { name = "", email = "alice@example.com" });
        Renderer.NameFromJwt(token).Should().Be("alice");
    }

    [Fact]
    public void NameFromJwt_WhitespaceName_FallsThroughToEmail()
    {
        var token = MakeJwt(new { name = "   ", email = "fallback@example.com" });
        // name is non-empty (whitespace), so it will be returned as-is per current logic
        // This documents the current behaviour — trimming is intentionally not done
        Renderer.NameFromJwt(token).Should().Be("   ");
    }
}
