using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

internal static class HostedTestSupport
{
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

    public static string CreateToken(
        RsaSecurityKey signingKey,
        string issuer,
        string audience,
        string? tid = "42",
        string? instanceUrl = "https://api.my.anythink.cloud",
        DateTime? expires = null,
        DateTime? notBefore = null,
        IReadOnlyDictionary<string, object>? extraClaims = null,
        string? sub = "user-1")
    {
        var claims = new Dictionary<string, object>();
        if (sub is not null) claims["sub"] = sub;
        foreach (var (name, value) in extraClaims ?? new Dictionary<string, object>()) claims[name] = value;
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
