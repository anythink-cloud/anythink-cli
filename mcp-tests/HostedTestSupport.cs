using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

/// <summary>
/// Test-only OAuth issuer: a real RSA key plus a mocked discovery/JWKS handler, so hosted-mode auth
/// tests exercise the real JwtBearer validation path (signature, issuer, audience, lifetime) without
/// any network access.
/// </summary>
internal static class HostedTestSupport
{
    /// <summary>Registers the discovery document and JWKS for <paramref name="issuer"/> on <paramref name="mock"/>.</summary>
    public static void ConfigureIssuer(MockHttpMessageHandler mock, string issuer, RsaSecurityKey key)
    {
        mock.When(HttpMethod.Get, $"{issuer}/.well-known/openid-configuration")
            .Respond("application/json", $$"""
                {
                    "issuer": "{{issuer}}",
                    "authorization_endpoint": "{{issuer}}/oauth/v1/authorize",
                    "token_endpoint": "{{issuer}}/oauth/v1/token",
                    "jwks_uri": "{{issuer}}/.well-known/jwks.json",
                    "response_types_supported": ["code"],
                    "subject_types_supported": ["public"],
                    "id_token_signing_alg_values_supported": ["RS256"]
                }
                """);

        var rsaParams = key.Rsa?.ExportParameters(false) ?? key.Parameters;
        var n = Base64UrlEncoder.Encode(rsaParams.Modulus);
        var e = Base64UrlEncoder.Encode(rsaParams.Exponent);

        mock.When(HttpMethod.Get, $"{issuer}/.well-known/jwks.json")
            .Respond("application/json", $$"""
                {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{key.KeyId}}","n":"{{n}}","e":"{{e}}"}]}
                """);
    }

    /// <summary>Signs a test access token. Pass <paramref name="signingKey"/> to simulate a key the JWKS doesn't advertise.</summary>
    public static string CreateToken(
        RsaSecurityKey signingKey,
        string issuer,
        string audience,
        string? tid = "42",
        string? instanceUrl = "https://42.api.anythink.cloud",
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-1" };
        if (tid is not null) claims["tid"] = tid;
        if (instanceUrl is not null) claims["instance_url"] = instanceUrl;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
