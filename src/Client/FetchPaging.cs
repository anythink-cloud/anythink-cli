using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace AnythinkCli.Client;

internal static class FetchPaging
{
    public const int MaxPageSize = 100;

    public static int? QueryInt(string url, string key)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && int.TryParse(HttpUtility.ParseQueryString(uri.Query)[key], out var value) ? value : null;

    public static bool IsOversized(string url) => QueryInt(url, "pageSize") > MaxPageSize;

    public static string CapPageSize(string url) => IsOversized(url) ? WithQuery(url, "pageSize", MaxPageSize) : url;

    public static string WithQuery(string url, string key, int value)
    {
        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query[key] = value.ToString();
        return new UriBuilder(uri) { Query = query.ToString() }.Uri.AbsoluteUri;
    }

    public static bool HasNextPage(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject page
                && page["has_next_page"] is JsonValue more && more.TryGetValue<bool>(out var next) && next
                && page["items"] is JsonArray { Count: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
