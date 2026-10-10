using System.Text.Encodings.Web;
using System.Text.Json;

namespace AnythinkCli.Output;

public static class JsonOutput
{
    // Console output isn't HTML, so quotes, angle brackets and accented text stay readable instead of becoming \uXXXX.
    public static readonly JsonSerializerOptions PrettyRelaxed = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
