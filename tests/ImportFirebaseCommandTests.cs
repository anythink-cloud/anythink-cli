using System.Text.Json.Nodes;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Firebase;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ImportFirebaseCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("import-firebase-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string KeyFile()
    {
        var path = Path.Combine(_dir, "key.json");
        File.WriteAllText(path, FirebaseKeySafetyTests.KeyJson());
        return path;
    }

    private static ImportFirebaseCommand Command(Action<string, FirebaseImportOptions>? seen = null)
    {
        var source = new FakeFirebaseSource();
        source.Collections["orders"] = [FirebaseImporterTests.Doc("orders/o1", new() { ["total"] = 5L }, "items")];
        return new ImportFirebaseCommand
        {
            ImporterFactory = (json, options) => { seen?.Invoke(json, options); return new FirebaseImporter(source, text => text, options.SampleSize); }
        };
    }

    private static MockHttpMessageHandler EmptyProject()
    {
        var project = new MockHttpMessageHandler();
        project.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities", "[]");
        return project;
    }

    // ── Rule: the key comes from the flag, then the environment, and only ever from a file ──

    [Fact]
    public void Key_Flag_Wins_Over_The_Environment()
    {
        ImportFirebaseCommand.ReadKey(KeyFile(), "/nowhere.json").Should().Contain("service_account");
    }

    [Fact]
    public void Key_Falls_Back_To_The_Environment_Variable()
    {
        ImportFirebaseCommand.ReadKey(null, KeyFile()).Should().Contain("service_account");
    }

    [Fact]
    public void A_Missing_Key_Is_Refused_And_Names_Both_Ways_To_Supply_It()
    {
        var act = () => ImportFirebaseCommand.ReadKey(" ", "");

        act.Should().Throw<CliException>().WithMessage("*--key*GOOGLE_APPLICATION_CREDENTIALS*");
    }

    [Fact]
    public void An_Unreadable_Key_File_Is_Refused_Without_Saying_What_Was_In_It()
    {
        var act = () => ImportFirebaseCommand.ReadKey(_dir, null);

        act.Should().Throw<CliException>().WithMessage("*Couldn't read the key file*");
    }

    // ── Rule: a plan is read-only and reports what is not brought across ──

    [Fact]
    public async Task Plan_Flag_Saves_The_Plan_Lists_The_Unmapped_And_Changes_Nothing()
    {
        var path = Path.Combine(_dir, "plan.json");

        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => Command().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile(), Plan = path }));

        code.Should().Be(0, output);
        var plan = PlanJson.Deserialise(File.ReadAllText(path));
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Entity && i.TargetName == "orders" && i.Action == PlanAction.Create);
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.StartsWith("orders/items:"));
        File.ReadAllText(path).Should().NotContain("FAKE-KEY-MATERIAL");
    }

    [Fact]
    public async Task Json_With_Dry_Run_Prints_Only_The_Plan_As_Json()
    {
        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => Command().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile(), DryRun = true, Json = true }));

        code.Should().Be(0, output);
        JsonNode.Parse(output)!["items"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Options_Reach_The_Importer()
    {
        FirebaseImportOptions? received = null;

        await CommandRunner.RunAsync(EmptyProject(), () => Command((_, o) => received = o).ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile(), Bucket = "my-bucket", SampleSize = 7, DryRun = true }));

        received.Should().Be(new FirebaseImportOptions(7, "my-bucket"));
    }

    [Fact]
    public async Task A_Sample_Size_Below_One_Is_Refused()
    {
        var (code, _) = await CommandRunner.RunAsync(EmptyProject(), () => Command().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile(), SampleSize = 0, DryRun = true }));

        code.Should().Be(1);
    }

    [Fact]
    public async Task Force_Without_A_Plan_File_Is_Refused()
    {
        var (code, _) = await CommandRunner.RunAsync(EmptyProject(), () => Command().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile(), Force = true, DryRun = true }));

        code.Should().Be(1);
    }

    [Fact]
    public async Task A_Real_Import_Without_Yes_Is_Refused_When_Not_Interactive()
    {
        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => Command().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = KeyFile() }));

        code.Should().Be(1);
        output.Should().Contain("--yes");
    }

    [Fact]
    public async Task A_Key_That_Is_Not_A_Service_Account_Key_Fails_Without_Echoing_It()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, """{"type":"authorized_user","refresh_token":"FAKE-KEY-MATERIAL"}""");

        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => new ImportFirebaseCommand().ExecuteAsync(null!,
            new ImportFirebaseSettings { Key = path, DryRun = true }));

        code.Should().Be(1);
        output.Should().NotContain("FAKE-KEY-MATERIAL");
    }
}
