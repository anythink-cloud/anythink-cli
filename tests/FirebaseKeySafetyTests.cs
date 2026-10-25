using System.Text.Json;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Firebase;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class FirebaseKeySafetyTests
{
    internal const string PrivateKey = "FAKE-KEY-MATERIAL-9f8e7d\nsecond-line-of-the-fake-key\n";
    internal const string KeyId = "0123456789abcdef0123456789abcdef01234567";

    internal static string KeyJson(string projectId = "demo-project") => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["type"] = "service_account",
        ["project_id"] = projectId,
        ["private_key_id"] = KeyId,
        ["private_key"] = PrivateKey,
        ["client_email"] = "importer@demo-project.iam.gserviceaccount.com"
    });

    private static FirebaseImporter Importer(FakeFirebaseSource source, FirebaseServiceAccountKey key) =>
        new(source, key.Redact);

    private static FakeFirebaseSource Leaky(FirebaseServiceAccountKey key, string leak)
    {
        var source = new FakeFirebaseSource
        {
            UsersFailure = new InvalidOperationException($"auth failed with {leak}"),
            ObjectsFailure = new InvalidOperationException($"storage failed with {leak}"),
            DocumentsFailure = new InvalidOperationException($"read failed with {leak}")
        };
        source.Collections["items"] = [FirebaseImporterTests.Doc("items/a", new() { ["x"] = 1L })];
        return source;
    }

    public static IEnumerable<object[]> Leaks() =>
    [
        [PrivateKey],
        [JsonSerializer.Serialize(PrivateKey).Trim('"')],
        [KeyId],
        [KeyJson()],
    ];

    // ── Rule: the key never appears in plans, warnings, errors or logs ──

    [Theory]
    [MemberData(nameof(Leaks))]
    public async Task FirebaseImport_KeyNeverAppearsInPlanOrErrors(string leak)
    {
        var key = FirebaseServiceAccountKey.Parse(KeyJson());
        var importer = Importer(Leaky(key, leak), key);

        var schema = await importer.FetchSchemaAsync(false, true, false);
        var records = () => importer.FetchRecordsAsync("items", 1, 10);

        var everything = string.Join('\n', schema.Warnings!.Concat(schema.Errors!));
        everything.Should().Contain("[redacted]").And.NotContain(leak.Trim()[..20]);
        (await records.Should().ThrowAsync<ImportException>()).Which.Message.Should().Contain("[redacted]").And.NotContain(leak.Trim()[..20]);
    }

    [Fact]
    public async Task FirebaseImport_KeyNeverAppearsInTheSavedPlan()
    {
        var key = FirebaseServiceAccountKey.Parse(KeyJson());
        var schema = await Importer(Leaky(key, PrivateKey), key).FetchSchemaAsync(false, true, false);

        var plan = ImportPlanner.Build(schema, TargetSnapshot.Empty, new PlanOptions(false, true, true, false),
            new PlanContext(new PlanSource("Firebase", "https://console.firebase.google.com/project/demo-project", ["schema"]),
                new PlanTarget("https://api.example.com", "1"), "run", DateTimeOffset.UnixEpoch));

        PlanJson.Serialise(plan).Should().NotContain("FAKE-KEY-MATERIAL").And.NotContain(KeyId);
    }

    [Fact]
    public void FirebaseImport_KeyIsNeverPrintedWhenTheObjectIsFormatted()
    {
        var key = FirebaseServiceAccountKey.Parse(KeyJson());

        key.ToString().Should().Be("service account key for demo-project");
        key.ToString().Should().NotContain("FAKE-KEY-MATERIAL");
    }

    [Theory]
    [InlineData("not json at all: FAKE-KEY-MATERIAL")]
    [InlineData("""{"type":"authorized_user","project_id":"p","client_email":"e","private_key":"FAKE-KEY-MATERIAL"}""")]
    [InlineData("""{"type":"service_account","project_id":"p","private_key":"FAKE-KEY-MATERIAL"}""")]
    public void FirebaseImport_AnInvalidKey_IsRefusedWithoutEchoingItsContents(string json)
    {
        var act = () => FirebaseServiceAccountKey.Parse(json);

        act.Should().Throw<ImportException>().Which.Message.Should().Contain("service account key").And.NotContain("FAKE-KEY-MATERIAL");
    }

    [Fact]
    public void FirebaseImport_AValidKey_ExposesOnlyItsProjectId()
    {
        FirebaseServiceAccountKey.Parse(KeyJson("my-app")).ProjectId.Should().Be("my-app");
    }
}
