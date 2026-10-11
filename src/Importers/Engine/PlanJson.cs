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
        var info = new FileInfo(path);
        if (info.LinkTarget is not null)
            throw new CliException($"{Spectre.Console.Markup.Escape(path)} is a symbolic link. Choose a regular file path.");
        if (info.Directory is { Exists: false })
            throw new CliException($"The folder for {Spectre.Console.Markup.Escape(path)} does not exist.");
        if (info.Exists && !force)
            throw AlreadyExists(path);
    }

    // Windows has no owner-only creation mode here; the folder's permissions apply.
    public static void Write(string path, ImportPlan plan, bool force)
    {
        EnsureWritable(path, force);
        var full = Path.GetFullPath(path);
        var temp = $"{full}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream))
                writer.Write(PlanJson.Serialise(plan));
            File.Move(temp, full, overwrite: force);
        }
        catch (IOException) when (!force && File.Exists(full))
        {
            throw AlreadyExists(path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static CliException AlreadyExists(string path) =>
        new($"{Spectre.Console.Markup.Escape(path)} already exists. Pass [bold #F97316]--force[/] to overwrite it, or choose another file.");
}
