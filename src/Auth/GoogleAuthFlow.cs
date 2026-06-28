using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AnythinkCli.Config;
using AnythinkCli.Models;

namespace AnythinkCli.Auth;

/// <summary>
/// Browser → loopback → JWT exchange for Google sign-in, shared by the CLI
/// ('login --google') and the MCP ('login_google' tool). Returns the platform
/// token; it does not save config or select an account — callers do that.
/// </summary>
public static class GoogleAuthFlow
{
    // Must match the http://localhost:<port>/callback redirect URIs registered
    // on the Google OAuth client. Multiple candidates so a busy port doesn't block.
    public static readonly int[] CallbackPorts = [8976, 8977, 8978];

    private static readonly byte[] SuccessPage = LoadSuccessPage();

    /// <param name="onAuthUrl">Invoked with the Google authorization URL (e.g. to print it as a fallback).</param>
    public static async Task<LoginResponse> RunAsync(PlatformConfig eff, Action<string>? onAuthUrl = null)
    {
        var (listener, port) = BindLoopbackListener();
        if (listener is null)
            throw new InvalidOperationException(
                $"Could not start the Google sign-in listener — ports {string.Join(", ", CallbackPorts)} are all in use.");

        try
        {
            var callbackUrl = $"http://localhost:{port}/callback";
            var baseUrl = $"{eff.MyAnythinkUrl.TrimEnd('/')}/org/{eff.MyAnythinkOrgId}/auth/v1/google";

            string authUrl;
            using (var http = new HttpClient())
            {
                var json = await http.GetStringAsync($"{baseUrl}/authorize?redirectUri={Uri.EscapeDataString(callbackUrl)}");
                authUrl = JsonDocument.Parse(json).RootElement.GetProperty("authorization_url").GetString()
                    ?? throw new InvalidOperationException("No authorization_url in response.");
            }

            if (!authUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to open a non-https authorization URL.");

            onAuthUrl?.Invoke(authUrl);
            OpenBrowser(authUrl);

            // Wait for the loopback callback (3 min). Ignore non-/callback paths.
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            HttpListenerContext ctx;
            while (true)
            {
                try { ctx = await listener.GetContextAsync().WaitAsync(cts.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("Timed out waiting for Google sign-in."); }
                if (string.Equals(ctx.Request.Url?.AbsolutePath, "/callback", StringComparison.Ordinal)) break;
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }

            var qs = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url?.Query ?? "");
            var code = qs["code"];
            var state = qs["state"];
            var error = qs["error"];

            // Respond to the browser before Stop() disposes the in-flight response.
            try
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = SuccessPage.Length;
                await ctx.Response.OutputStream.WriteAsync(SuccessPage);
                ctx.Response.Close();
            }
            catch { /* browser tab already closed — ignore */ }

            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException($"Google sign-in failed: {error ?? "unknown error"}");

            using (var http = new HttpClient())
            {
                var resp = await http.GetStringAsync(
                    $"{baseUrl}/callback?code={Uri.EscapeDataString(code)}" +
                    (state != null ? $"&state={Uri.EscapeDataString(state)}" : ""));
                var tokens = JsonSerializer.Deserialize<LoginResponse>(resp,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken))
                    throw new InvalidOperationException("No token received from server.");
                return tokens;
            }
        }
        finally { listener.Stop(); }
    }

    private static (HttpListener? listener, int port) BindLoopbackListener()
    {
        foreach (var port in CallbackPorts)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }
        return (null, 0);
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* user can open manually from the URL passed to onAuthUrl */ }
    }

    private static byte[] LoadSuccessPage()
    {
        var asm = typeof(GoogleAuthFlow).Assembly;
        var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("google-success.html"))!;
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
