namespace Anythink.ProjectImporters;

public static class UrlRedaction
{
    // Userinfo, query and fragment can carry credentials and are never needed: auth is the bearer token.
    public static string Redact(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "(invalid url)";
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }
            .Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
