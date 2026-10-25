using System.Text.Json;
using System.Text.Json.Serialization;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests that API model records serialise/deserialise correctly with the expected
/// snake_case property names — catching any mismatch between C# names and JSON wire format.
/// </summary>
public class ModelSerializationTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Entity ────────────────────────────────────────────────────────────────

    [Fact]
    public void Entity_Deserializes_FromSnakeCaseJson()
    {
        const string json = """
            {
                "id": 42,
                "name": "products",
                "table_name": "products",
                "enable_rls": true,
                "is_system": false,
                "is_junction": false,
                "is_public": true,
                "lock_new_records": false
            }
            """;

        var entity = JsonSerializer.Deserialize<Entity>(json, Opts)!;

        entity.Id.Should().Be(42);
        entity.Name.Should().Be("products");
        entity.TableName.Should().Be("products");
        entity.EnableRls.Should().BeTrue();
        entity.IsPublic.Should().BeTrue();
        entity.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void CreateEntityRequest_Serializes_WithSnakeCaseNames()
    {
        var req  = new CreateEntityRequest("orders", EnableRls: true, IsPublic: false);
        var json = JsonSerializer.Serialize(req, Opts);
        var doc  = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("name").GetString().Should().Be("orders");
        doc.GetProperty("enable_rls").GetBoolean().Should().BeTrue();
        doc.GetProperty("is_public").GetBoolean().Should().BeFalse();
    }

    // ── Field ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Field_Deserializes_Correctly()
    {
        const string json = """
            {
                "id": 101,
                "name": "email",
                "label": "Email Address",
                "database_type": "varchar",
                "display_type": "text",
                "is_required": true,
                "is_unique": true,
                "is_immutable": false,
                "is_searchable": true,
                "is_indexed": true,
                "default_value": null,
                "locked": false
            }
            """;

        var field = JsonSerializer.Deserialize<Field>(json, Opts)!;

        field.Id.Should().Be(101);
        field.Name.Should().Be("email");
        field.DatabaseType.Should().Be("varchar");
        field.IsRequired.Should().BeTrue();
        field.IsUnique.Should().BeTrue();
        field.DefaultValue.Should().BeNull();
    }

    // ── LoginResponse ─────────────────────────────────────────────────────────

    [Fact]
    public void LoginResponse_Deserializes_AccessToken()
    {
        const string json = """
            {
                "access_token": "eyJhbGci.test.token",
                "refresh_token": "refresh-xyz",
                "expires_in": 3600
            }
            """;

        var resp = JsonSerializer.Deserialize<LoginResponse>(json, Opts)!;

        resp.AccessToken.Should().Be("eyJhbGci.test.token");
        resp.RefreshToken.Should().Be("refresh-xyz");
        resp.ExpiresIn.Should().Be(3600);
    }

    [Fact]
    public void LoginResponse_HandlesNullOptionals()
    {
        const string json = """{"access_token":"tok","refresh_token":null}""";
        var resp = JsonSerializer.Deserialize<LoginResponse>(json, Opts)!;
        resp.RefreshToken.Should().BeNull();
        resp.ExpiresIn.Should().BeNull();
    }

    // ── Workflow ──────────────────────────────────────────────────────────────

    [Fact]
    public void Workflow_Deserializes_TriggersArray()
    {
        const string json = """
            {
                "id": 76,
                "name": "HN Research Pull",
                "description": "Pulls from HN Algolia",
                "group": "Research",
                "enabled": true,
                "editor_state": "{\"nodePositions\":{}}",
                "triggers": [
                    {
                        "id": 4, "workflow_id": 76, "type": "Event", "enabled": false,
                        "config_json": "{}",
                        "config": {
                            "event": "EntityUpdated", "event_entity": "posts", "manual_entities": null,
                            "cron_expression": null, "last_run_at": null, "next_run_at": null, "api_route": null,
                            "filter": { "field": "status", "op": "changed_to", "value": "published" }
                        }
                    }
                ]
            }
            """;

        var wf = JsonSerializer.Deserialize<Workflow>(json, Opts)!;

        wf.Id.Should().Be(76);
        wf.Enabled.Should().BeTrue();
        wf.Group.Should().Be("Research");
        wf.EditorState.Should().Contain("nodePositions");
        wf.Steps.Should().BeNull();
        var trigger = wf.Triggers.Should().ContainSingle().Subject;
        trigger.Type.Should().Be("Event");
        trigger.Enabled.Should().BeFalse();
        trigger.Config!.Event.Should().Be("EntityUpdated");
        trigger.Config.EventEntity.Should().Be("posts");
        trigger.Config.Filter!.Value.GetProperty("op").GetString().Should().Be("changed_to");
    }

    // ── PaginatedResult ───────────────────────────────────────────────────────

    [Fact]
    public void PaginatedResult_Deserializes_ItemsAndMeta()
    {
        const string json = """
            {
                "items": [{"id": 1}, {"id": 2}],
                "total_items": 50,
                "total_pages": 25,
                "has_next_page": true,
                "page": 2,
                "page_size": 2
            }
            """;

        var result = JsonSerializer.Deserialize<PaginatedResult<System.Text.Json.Nodes.JsonObject>>(json, Opts)!;

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(50);
        result.TotalPages.Should().Be(25);
        result.HasNextPage.Should().BeTrue();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public void PaginatedResult_Deserializes_WithoutOptionalFields()
    {
        const string json = """
            {
                "items": [{"id": 1}],
                "has_next_page": false,
                "page": 1,
                "page_size": 10
            }
            """;

        var result = JsonSerializer.Deserialize<PaginatedResult<System.Text.Json.Nodes.JsonObject>>(json, Opts)!;

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().BeNull();
        result.TotalPages.Should().BeNull();
        result.HasNextPage.Should().BeFalse();
    }

    // ── UpdateFieldRequest ───────────────────────────────────────────────────

    [Fact]
    public void UpdateFieldRequest_Serializes_WithSnakeCaseNames()
    {
        var req  = new UpdateFieldRequest("rich-text", Label: "Description", IsSearchable: true);
        var json = JsonSerializer.Serialize(req, Opts);
        var doc  = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("display_type").GetString().Should().Be("rich-text");
        doc.GetProperty("label").GetString().Should().Be("Description");
        doc.GetProperty("is_searchable").GetBoolean().Should().BeTrue();
        doc.GetProperty("is_required").GetBoolean().Should().BeFalse();
    }

    // ── WorkflowJob / WorkflowJobStep ────────────────────────────────────────

    [Fact]
    public void WorkflowJob_Deserializes_CurrentApiShape()
    {
        const string json = """
            {
                "id": 5,
                "workflow_id": 31,
                "status": "Failed",
                "payload": "{\"id\":1}",
                "current_executing_step_key": "send_email",
                "created_at": "2026-03-20T10:00:00Z",
                "updated_at": "2026-03-20T10:01:00Z",
                "error_message": "Step send_email failed",
                "steps": [
                    {
                        "id": 11,
                        "workflow_job_id": 5,
                        "workflow_step_id": 8,
                        "success": true,
                        "log": "Fetched 42 rows",
                        "data_json": "{}",
                        "error_message": null,
                        "workflow_step": { "id": 8, "key": "fetch_data", "name": "Fetch", "action": "ReadData" }
                    },
                    {
                        "id": 12,
                        "workflow_job_id": 5,
                        "workflow_step_id": 9,
                        "success": false,
                        "log": null,
                        "error_message": "SMTP timeout",
                        "workflow_step": { "id": 9, "key": "send_email", "name": "Send", "action": "SendAnEmail" }
                    }
                ]
            }
            """;

        var job = JsonSerializer.Deserialize<WorkflowJob>(json, Opts)!;

        job.Id.Should().Be(5);
        job.Status.Should().Be("Failed");
        job.CreatedAt.Should().Be("2026-03-20T10:00:00Z");
        job.CurrentExecutingStepKey.Should().Be("send_email");
        job.Steps.Should().HaveCount(2);
        job.Steps![0].Success.Should().BeTrue();
        job.Steps[0].WorkflowStep!.Key.Should().Be("fetch_data");
        job.Steps[0].Log.Should().Be("Fetched 42 rows");
        job.Steps[1].Success.Should().BeFalse();
        job.Steps[1].ErrorMessage.Should().Be("SMTP timeout");
    }

    // ── CreateUpdateWorkflowRequest ──────────────────────────────────────────

    [Fact]
    public void CreateUpdateWorkflowRequest_Serializes_TriggersArray_WithSnakeCaseNames()
    {
        var filter = JsonDocument.Parse("""{"field":"status","op":"eq","value":"live"}""").RootElement;
        var req = new CreateUpdateWorkflowRequest("New Name", "Updated desc", false,
            [new WorkflowTriggerRequest("Event", true,
                new WorkflowTriggerConfig(Event: "EntityCreated", EventEntity: "posts", Filter: filter))],
            Group: "Ops", EditorState: "{}");
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(req, Opts)).RootElement;

        doc.GetProperty("name").GetString().Should().Be("New Name");
        doc.GetProperty("description").GetString().Should().Be("Updated desc");
        doc.GetProperty("enabled").GetBoolean().Should().BeFalse();
        doc.GetProperty("group").GetString().Should().Be("Ops");
        doc.GetProperty("editor_state").GetString().Should().Be("{}");
        doc.TryGetProperty("trigger", out _).Should().BeFalse("the legacy single-trigger field is deprecated");
        doc.TryGetProperty("options", out _).Should().BeFalse("the legacy single-trigger field is deprecated");

        var config = doc.GetProperty("triggers")[0].GetProperty("config");
        config.GetProperty("event").GetString().Should().Be("EntityCreated");
        config.GetProperty("event_entity").GetString().Should().Be("posts");
        config.GetProperty("filter").GetProperty("value").GetString().Should().Be("live");
        config.TryGetProperty("cron_expression", out _).Should().BeFalse();
    }

    [Fact]
    public void WorkflowTriggerConfig_KeepsUnknownKeys_ThroughARoundTrip()
    {
        const string json = """{"cron_expression":"0 9 * * *","timezone":"Europe/London"}""";

        var config = JsonSerializer.Deserialize<WorkflowTriggerConfig>(json, Opts)!;
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(config, Opts)).RootElement;

        doc.GetProperty("cron_expression").GetString().Should().Be("0 9 * * *");
        doc.GetProperty("timezone").GetString().Should().Be("Europe/London");
    }

    // ── GoogleOAuthSettings ───────────────────────────────────────────────────

    [Fact]
    public void GoogleOAuthSettings_Deserializes_EnabledAndMaskedSecret()
    {
        const string json = """
            {
                "enabled": true,
                "client_id": "1234.apps.googleusercontent.com",
                "client_secret": "***masked***"
            }
            """;

        var settings = JsonSerializer.Deserialize<GoogleOAuthSettings>(json, Opts)!;

        settings.Enabled.Should().BeTrue();
        settings.ClientId.Should().Be("1234.apps.googleusercontent.com");
        settings.ClientSecret.Should().Be("***masked***");
    }

    [Fact]
    public void UpdateGoogleOAuthRequest_Serializes_WithSnakeCaseNames()
    {
        var req  = new UpdateGoogleOAuthRequest(true, "my-client-id", "my-secret");
        var json = JsonSerializer.Serialize(req, Opts);
        var doc  = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("enabled").GetBoolean().Should().BeTrue();
        doc.GetProperty("client_id").GetString().Should().Be("my-client-id");
        doc.GetProperty("client_secret").GetString().Should().Be("my-secret");
    }

    // ── StripeConnectStatus ───────────────────────────────────────────────────

    [Fact]
    public void StripeConnectStatus_Deserializes_AllFields()
    {
        const string json = """
            {
                "stripe_account_id": "acct_abc123",
                "onboarding_completed": true,
                "charges_enabled": true,
                "payouts_enabled": false,
                "details_submitted": true
            }
            """;

        var status = JsonSerializer.Deserialize<StripeConnectStatus>(json, Opts)!;

        status.StripeAccountId.Should().Be("acct_abc123");
        status.OnboardingCompleted.Should().BeTrue();
        status.ChargesEnabled.Should().BeTrue();
        status.PayoutsEnabled.Should().BeFalse();
    }

    // ── UserResponse ──────────────────────────────────────────────────────────

    [Fact]
    public void UserResponse_Deserializes_Correctly()
    {
        const string json = """
            {
                "id": 7,
                "first_name": "Alice",
                "last_name": "Smith",
                "email": "alice@example.com",
                "role_id": 3,
                "role_name": "Editor",
                "is_confirmed": true,
                "created_at": "2024-06-01T10:00:00Z"
            }
            """;

        var user = JsonSerializer.Deserialize<UserResponse>(json, Opts)!;

        user.Id.Should().Be(7);
        user.FirstName.Should().Be("Alice");
        user.Email.Should().Be("alice@example.com");
        user.RoleId.Should().Be(3);
        user.IsConfirmed.Should().BeTrue();
    }

    // ── FileResponse ──────────────────────────────────────────────────────────

    [Fact]
    public void FileResponse_Deserializes_FileSizeAndType()
    {
        const string json = """
            {
                "id": 5,
                "original_file_name": "logo.png",
                "file_name": "a1b2c3-logo.png",
                "file_type": "image/png",
                "file_size": 204800,
                "is_public": true,
                "created_at": "2024-01-15T08:30:00Z"
            }
            """;

        var file = JsonSerializer.Deserialize<FileResponse>(json, Opts)!;

        file.Id.Should().Be(5);
        file.OriginalFileName.Should().Be("logo.png");
        file.FileSize.Should().Be(204800);
        file.IsPublic.Should().BeTrue();
    }
}
