using AnythinkCli.Config;
using FluentAssertions;
using System.Text;

namespace AnythinkCli.Tests;

// ── ApiDefaults ───────────────────────────────────────────────────────────────

public class ApiDefaultsTests
{
    [Fact]
    public void MyAnythinkApiUrl_HasHttpsSchemeAndContainsAnythink()
    {
        ApiDefaults.MyAnythinkApiUrl.Should().StartWith("https://");
        ApiDefaults.MyAnythinkApiUrl.Should().Contain("anythink");
    }

    [Fact]
    public void BillingApiUrl_HasHttpsSchemeAndContainsAnythink()
    {
        ApiDefaults.BillingApiUrl.Should().StartWith("https://");
        ApiDefaults.BillingApiUrl.Should().Contain("anythink");
    }

    [Fact]
    public void MyAnythinkOrgId_IsNotEmpty()
    {
        ApiDefaults.MyAnythinkOrgId.Should().NotBeNullOrEmpty();
    }
}

// ── Profile.IsTokenExpired ────────────────────────────────────────────────────

public class ProfileTests
{
    [Fact]
    public void IsTokenExpired_WhenApiKeySet_ReturnsFalse()
    {
        var profile = new Profile { ApiKey = "ak_test123" };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenNoTokenOrKey_ReturnsTrue()
    {
        var profile = new Profile();
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_WhenNonJwtTokenAndNoStoredExpiry_ReturnsFalse()
    {
        // Non-JWT opaque token — can't be decoded, no stored expiry → not expired (fallback).
        var profile = new Profile { AccessToken = "opaque-api-token-not-a-jwt" };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenTokenSetAndFutureExpiry_ReturnsFalse()
    {
        var profile = new Profile
        {
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenTokenSetAndPastExpiry_ReturnsTrue()
    {
        var profile = new Profile
        {
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_ApiKeyIgnoresExpiredToken()
    {
        // API key auth should never be considered expired regardless of token state
        var profile = new Profile
        {
            ApiKey         = "ak_test123",
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    // ── JWT exp-claim based expiry ────────────────────────────────────────────

    /// <summary>Creates a minimal JWT with a real base64url-encoded payload.</summary>
    private static string MakeJwt(long unixExp)
    {
        var header  = B64Url("{\"alg\":\"HS256\"}");
        var payload = B64Url($"{{\"exp\":{unixExp},\"sub\":\"test-user\"}}");
        return $"{header}.{payload}.fakesignature";
    }

    private static string MakeJwtNoExp()
    {
        var header  = B64Url("{\"alg\":\"HS256\"}");
        var payload = B64Url("{\"sub\":\"test-user\"}");
        return $"{header}.{payload}.fakesignature";
    }

    private static string B64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static long UnixNow()    => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static long PastUnix()   => UnixNow() - 3600;
    private static long FutureUnix() => UnixNow() + 3600;

    [Fact]
    public void IsTokenExpired_JwtWithExpiredClaim_ReturnsTrue_EvenWithNoStoredExpiry()
    {
        var profile = new Profile { AccessToken = MakeJwt(PastUnix()) };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtWithFutureClaim_ReturnsFalse_EvenWithNoStoredExpiry()
    {
        var profile = new Profile { AccessToken = MakeJwt(FutureUnix()) };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtExpClaim_TakesPrecedenceOverStoredExpiry_WhenJwtExpired()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwt(PastUnix()),
            TokenExpiresAt = DateTime.UtcNow.AddHours(10)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtExpClaim_TakesPrecedenceOverStoredExpiry_WhenJwtValid()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwt(FutureUnix()),
            TokenExpiresAt = DateTime.UtcNow.AddHours(-10)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_FallsBackToStoredExpiry_WhenPast()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwtNoExp(),
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_FallsBackToStoredExpiry_WhenFuture()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwtNoExp(),
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_AndNoStoredExpiry_ReturnsFalse()
    {
        var profile = new Profile { AccessToken = MakeJwtNoExp() };
        profile.IsTokenExpired.Should().BeFalse();
    }
}
