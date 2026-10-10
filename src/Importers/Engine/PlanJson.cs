using System.Text.Json;
using System.Text.Json.Serialization;
using AnythinkCli.Client;

namespace AnythinkCli.Importers.Engine;

public static class PlanJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static string Serialise(ImportPlan plan) => JsonSerializer.Serialize(plan, Options);

    public static ImportPlan Deserialise(string json) =>
        JsonSerializer.Deserialize<ImportPlan>(json, Options)
        ?? throw new CliException("The plan file is empty.");
}

public static class PlanFile
{
    public static void EnsureWritable(string path, bool force)
    {
        if (File.Exists(path) && !force)
            throw new CliException($"{Spectre.Console.Markup.Escape(path)} already exists. Pass [bold #F97316]--force[/] to overwrite it, or choose another file.");
    }

    public static void Write(string path, ImportPlan plan, bool force)
    {
        EnsureWritable(path, force);
        var options = new FileStreamOptions
        {
            Mode = force ? FileMode.Create : FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var stream = new FileStream(path, options))
            using (var writer = new StreamWriter(stream))
                writer.Write(PlanJson.Serialise(plan));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException) when (!force && File.Exists(path))
        {
            throw new CliException($"{Spectre.Console.Markup.Escape(path)} already exists. Pass [bold #F97316]--force[/] to overwrite it, or choose another file.");
        }
    }
}
