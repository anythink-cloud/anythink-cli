using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace AnythinkMcp;

public sealed class TokenExchangeOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string Audience { get; init; }
    public string? TokenEndpoint { get; init; }
}

public sealed class TokenExchangeException(bool rejected) : Exception("Token exchange failed.")
{
    public bool Rejected { get; } = rejected;
}

public interface ITokenExchanger
{
    Task<string> ExchangeAsync(string inboundToken, CancellationToken cancellationToken);
}

public sealed partial class HostedTokenExchanger : ITokenExchanger
{
    private const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private const int DefaultCacheLimit = 10_000;
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly string _issuer;
    private readonly TokenExchangeOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger? _logger;
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedToken>>> _inFlight = new();
    private readonly object _endpointLock = new();
    private Task<Uri>? _tokenEndpoint;

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

#pragma warning disable CS0618
    private sealed class ClockAdapter(TimeProvider clock) : ISystemClock
    {
        public DateTimeOffset UtcNow => clock.GetUtcNow();
    }
#pragma warning restore CS0618

    public HostedTokenExchanger(
        HttpClient http, string issuer, TokenExchangeOptions options,
        TimeProvider? clock = null, ILogger? logger = null, int cacheLimit = DefaultCacheLimit)
    {
        _http = http;
        _issuer = issuer.TrimEnd('/');
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheLimit, Clock = new ClockAdapter(_clock) });
    }

    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false };

    internal int CachedCount => _cache.Count;

    public async Task<string> ExchangeAsync(string inboundToken, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inboundToken)));

        if (_cache.TryGetValue(key, out CachedToken? hit) && hit!.ExpiresAt > _clock.GetUtcNow())
            return hit.AccessToken;

        var flight = _inFlight.GetOrAdd(key, _ => new Lazy<Task<CachedToken>>(() => ExchangeAndCacheAsync(key, inboundToken)));
        return (await flight.Value.WaitAsync(cancellationToken)).AccessToken;
    }

    private async Task<CachedToken> ExchangeAndCacheAsync(string key, string inboundToken)
    {
        try
        {
            var token = await RequestAsync(inboundToken);
            if (token.ExpiresAt > _clock.GetUtcNow())
                _cache.Set(key, token, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpiration = token.ExpiresAt });
            return token;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<CachedToken> RequestAsync(string inboundToken)
    {
        try
        {
            var endpoint = await GetTokenEndpointAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = GrantType,
                    ["subject_token"] = inboundToken,
                    ["subject_token_type"] = AccessTokenType,
                    ["audience"] = _options.Audience,
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                }),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var code = OAuthErrorCode(body);
                _logger?.LogWarning("Token exchange refused with status {Status}, error {Error}", (int)response.StatusCode, code);
                throw new TokenExchangeException(rejected: code == "invalid_grant");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (string.IsNullOrEmpty(accessToken)) throw new TokenExchangeException(rejected: false);

            var lifetime = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : DefaultLifetime;
            return new CachedToken(accessToken, _clock.GetUtcNow() + lifetime - ExpirySkew);
        }
        catch (TokenExchangeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Token exchange request failed: {ErrorType}", ex.GetType().Name);
            throw new TokenExchangeException(rejected: false);
        }
    }

    private static string OAuthErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return code is not null && ErrorCodePattern().IsMatch(code) ? code : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    [GeneratedRegex(@"^[a-z_]{1,64}\z")]
    private static partial Regex ErrorCodePattern();

    private async Task<Uri> GetTokenEndpointAsync()
    {
        Task<Uri> task;
        lock (_endpointLock) task = _tokenEndpoint ??= DiscoverTokenEndpointAsync();

        try
        {
            return await task;
        }
        catch
        {
            lock (_endpointLock)
            {
                if (ReferenceEquals(_tokenEndpoint, task)) _tokenEndpoint = null;
            }
            throw;
        }
    }

    private async Task<Uri> DiscoverTokenEndpointAsync()
    {
        if (_options.TokenEndpoint is not null)
            return Uri.TryCreate(_options.TokenEndpoint, UriKind.Absolute, out var configured) && configured.Scheme == Uri.UriSchemeHttps
                ? configured
                : throw new TokenExchangeException(rejected: false);

        var issuerUri = new Uri(_issuer);
        foreach (var path in new[] { "/.well-known/oauth-authorization-server", "/.well-known/openid-configuration" })
        {
            try
            {
                using var response = await _http.GetAsync(_issuer + path);
                if (!response.IsSuccessStatusCode) continue;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                var advertisedIssuer = root.TryGetProperty("issuer", out var iss) ? iss.GetString() : null;
                if (advertisedIssuer?.TrimEnd('/') != _issuer) continue;

                if (root.TryGetProperty("token_endpoint", out var te)
                    && Uri.TryCreate(te.GetString(), UriKind.Absolute, out var endpoint)
                    && endpoint.Scheme == Uri.UriSchemeHttps
                    && endpoint.GetLeftPart(UriPartial.Authority) == issuerUri.GetLeftPart(UriPartial.Authority))
                    return endpoint;
            }
            catch (Exception) { }
        }

        throw new TokenExchangeException(rejected: false);
    }
}
