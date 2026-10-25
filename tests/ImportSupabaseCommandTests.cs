using System.Text.Json.Nodes;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Supabase;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ImportSupabaseCommandTests : IDisposable
{
    private const string Db = "postgresql://postgres:s3cr3t@db.example.com:5432/postgres";
    private readonly string _dir = Directory.CreateTempSubdirectory("import-supabase-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ImportSupabaseCommand Command(Action<string, string?, string?, SupabaseImportOptions>? seen = null)
    {
        var source = SupabaseImporterTests.Todos();
        source.Policies.Add(new SupabasePolicy("public", "todos", "Owners read", "PERMISSIVE", ["authenticated"], "SELECT", "true", null));
        return new ImportSupabaseCommand
        {
            ImporterFactory = (db, url, key, options) =>
            {
                seen?.Invoke(db, url, key, options);
                return new SupabaseImporter(source, new SupabaseSecrets(db, key), url, key, options);
            }
        };
    }

    private static MockHttpMessageHandler EmptyProject()
    {
        var project = new MockHttpMessageHandler();
        project.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities", "[]");
        return project;
    }

    private static Task<(int Code, string Output)> Run(ImportSupabaseCommand command, ImportSupabaseSettings settings) =>
        CommandRunner.RunAsync(EmptyProject(), () => command.ExecuteAsync(null!, settings));

    // ── Rule: the connection string and key come from the flag, then the environment, never a file or a tool call ──

    [Fact]
    public void Secret_Flag_Wins_Over_The_Environment()
    {
        ImportSupabaseCommand.ResolveSecret("flag", "env", false, "x", "ENV", "--x").Should().Be("flag");
    }

    [Fact]
    public void Secret_Falls_Back_To_The_Environment_Variable()
    {
        ImportSupabaseCommand.ResolveSecret(" ", "env", false, "x", "ENV", "--x").Should().Be("env");
    }

    [Fact]
    public void Secret_Is_Prompted_For_Only_When_Interactive()
    {
        ImportSupabaseCommand.ResolveSecret(null, null, true, "x", "ENV", "--x", () => "typed").Should().Be("typed");
        var act = () => ImportSupabaseCommand.ResolveSecret(null, null, false, "Postgres connection string", "SUPABASE_DB_URL", "--db");

        act.Should().Throw<CliException>().WithMessage("*SUPABASE_DB_URL*--db*");
    }

    [Fact]
    public void A_Blank_Prompt_Answer_Is_Refused()
    {
        var act = () => ImportSupabaseCommand.ResolveSecret(null, null, true, "x", "ENV", "--x", () => " ");

        act.Should().Throw<CliException>();
    }

    [Theory]
    [InlineData("https://abcd.supabase.co", true)]
    [InlineData("http://localhost:54321", true)]
    [InlineData("http://abcd.supabase.co", false)]
    [InlineData("ftp://abcd.supabase.co", false)]
    [InlineData("abcd.supabase.co", false)]
    public void The_Project_Url_Must_Be_Https_Or_Local(string url, bool accepted)
    {
        var act = () => ImportSupabaseCommand.ValidateUrl(url);

        if (accepted) act.Should().NotThrow(); else act.Should().Throw<CliException>();
    }

    // ── Rule: a plan is read-only and reports what is not brought across ──

    [Fact]
    public async Task Plan_Flag_Saves_The_Plan_Lists_The_Policies_And_Changes_Nothing()
    {
        var path = Path.Combine(_dir, "plan.json");

        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, Plan = path });

        code.Should().Be(0, output);
        var plan = PlanJson.Deserialise(File.ReadAllText(path));
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Entity && i.TargetName == "todos" && i.Action == PlanAction.Create);
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.StartsWith("row-level security policy \"Owners read\""));
        File.ReadAllText(path).Should().NotContain("s3cr3t");
    }

    [Fact]
    public async Task Json_With_Dry_Run_Prints_Only_The_Plan_As_Json()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, DryRun = true, Json = true });

        code.Should().Be(0, output);
        JsonNode.Parse(output)!["items"]!.AsArray().Should().NotBeEmpty();
        output.Should().NotContain("s3cr3t");
    }

    [Fact]
    public async Task The_Text_Dry_Run_Names_The_Policies_And_Never_The_Password()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, DryRun = true });

        code.Should().Be(0, output);
        output.Should().Contain("Owners read").And.Contain("Not brought across");
        output.Should().NotContain("s3cr3t");
    }

    [Fact]
    public async Task Options_Reach_The_Importer()
    {
        string? receivedDb = null, receivedUrl = null, receivedKey = null;
        SupabaseImportOptions? receivedOptions = null;
        var command = Command((db, url, key, options) => (receivedDb, receivedUrl, receivedKey, receivedOptions) = (db, url, key, options));

        await Run(command, new ImportSupabaseSettings { Db = Db, Url = "https://abcd.supabase.co", ServiceKey = "k", IncludeFiles = true, Schemas = "public,app", DryRun = true });

        (receivedDb, receivedUrl, receivedKey).Should().Be((Db, "https://abcd.supabase.co", "k"));
        receivedOptions!.Schemas.Should().Equal("public", "app");
    }

    [Fact]
    public async Task The_Service_Key_Is_Not_Read_Unless_Files_Are_Requested()
    {
        string? receivedKey = "unset";

        await Run(Command((_, _, key, _) => receivedKey = key), new ImportSupabaseSettings { Db = Db, ServiceKey = "k", DryRun = true });

        receivedKey.Should().BeNull();
    }

    [Fact]
    public async Task Files_Without_A_Project_Url_Are_Refused()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, IncludeFiles = true, ServiceKey = "k", DryRun = true });

        code.Should().Be(1);
        output.Should().Contain("--url");
    }

    [Fact]
    public async Task A_Plain_Http_Project_Url_Is_Refused_Before_Anything_Is_Read()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, Url = "http://abcd.supabase.co", DryRun = true });

        code.Should().Be(1);
        output.Should().Contain("http://");
    }

    [Fact]
    public async Task Force_Without_A_Plan_File_Is_Refused()
    {
        var (code, _) = await Run(Command(), new ImportSupabaseSettings { Db = Db, Force = true, DryRun = true });

        code.Should().Be(1);
    }

    [Fact]
    public async Task A_Real_Import_Without_Yes_Is_Refused_When_Not_Interactive()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db });

        code.Should().Be(1);
        output.Should().Contain("--yes");
    }

    [Fact]
    public async Task A_Supabase_System_Schema_Is_Refused()
    {
        var (code, output) = await Run(Command(), new ImportSupabaseSettings { Db = Db, Schemas = "auth", DryRun = true });

        code.Should().Be(1);
        output.Should().Contain("auth").And.Contain("never imported");
    }

    [Fact]
    public async Task A_Bad_Connection_String_Fails_Without_Echoing_It()
    {
        var (code, output) = await Run(new ImportSupabaseCommand(), new ImportSupabaseSettings { Db = "Host=;Password=s3cr3t;Bogus=1", DryRun = true });

        code.Should().Be(1);
        output.Should().Contain("connection string").And.NotContain("s3cr3t");
    }
}
