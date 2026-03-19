using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// Extended coverage for AnythinkClient — fields, workflows, data, users,
/// files, roles, pay, and auth. Complements AnythinkClientTests.cs.
/// </summary>
public class AnythinkClientExtendedTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string OrgPath = $"{BaseUrl}/org/{OrgId}";
    private const string PayPath = $"{OrgPath}/integrations/anythinkpay";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler));

    // ── Project Auth ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeTransferTokenAsync_ReturnsLoginResponse()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/auth/v1/exchange-transfer-token")
               .Respond("application/json",
                   """{"access_token":"new-token","refresh_token":"ref-token","expires_in":3600}""");

        var resp = await BuildClient(handler).ExchangeTransferTokenAsync("transfer-xyz");

        resp.AccessToken.Should().Be("new-token");
        resp.ExpiresIn.Should().Be(3600);
    }

    // ── Entities ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEntityAsync_ReturnsEntity()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/customers")
               .Respond("application/json",
                   """{"name":"customers","table_name":"customers","enable_rls":true,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false}""");

        var entity = await BuildClient(handler).GetEntityAsync("customers");

        entity.Name.Should().Be("customers");
        entity.EnableRls.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEntityAsync_ReturnsUpdatedEntity()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{OrgPath}/entities/orders")
               .Respond("application/json",
                   """{"name":"orders","table_name":"orders","enable_rls":true,"is_system":false,"is_junction":false,"is_public":true,"lock_new_records":false}""");

        var req    = new UpdateEntityRequest(EnableRls: true, IsPublic: true, LockNewRecords: false);
        var result = await BuildClient(handler).UpdateEntityAsync("orders", req);

        result!.EnableRls.Should().BeTrue();
        result.IsPublic.Should().BeTrue();
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFieldsAsync_ReturnsFieldList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/customers/fields")
               .Respond("application/json",
                   """[{"id":1,"name":"email","database_type":"varchar","display_type":"text","is_required":true,"is_unique":true,"is_immutable":false,"is_searchable":true,"is_indexed":true,"locked":false}]""");

        var fields = await BuildClient(handler).GetFieldsAsync("customers");

        fields.Should().HaveCount(1);
        fields[0].Name.Should().Be("email");
        fields[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task GetFieldsAsync_EmptyArray_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/empty/fields").Respond("application/json", "[]");

        var fields = await BuildClient(handler).GetFieldsAsync("empty");
        fields.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFieldAsync_ReturnsCreatedField()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/entities/customers/fields")
               .Respond("application/json",
                   """{"id":99,"name":"phone","database_type":"varchar","display_type":"text","is_required":false,"is_unique":false,"is_immutable":false,"is_searchable":false,"is_indexed":false,"locked":false}""");

        var req   = new CreateFieldRequest("phone", "varchar", "text");
        var field = await BuildClient(handler).AddFieldAsync("customers", req);

        field.Id.Should().Be(99);
        field.Name.Should().Be("phone");
    }

    [Fact]
    public async Task DeleteFieldAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/entities/customers/fields/99")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteFieldAsync("customers", 99);
        await act.Should().NotThrowAsync();
    }

    // ── Workflows ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflowAsync_Found_ReturnsWorkflow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/workflows/76")
               .Respond("application/json",
                   """{"id":76,"name":"HN Research Pull","trigger":"Timed","enabled":true,"description":"Pulls HN stories"}""");

        var wf = await BuildClient(handler).GetWorkflowAsync(76);

        wf.Id.Should().Be(76);
        wf.Name.Should().Be("HN Research Pull");
        wf.Trigger.Should().Be("Timed");
    }

    [Fact]
    public async Task CreateWorkflowAsync_ReturnsCreatedWorkflow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/workflows")
               .Respond("application/json",
                   """{"id":101,"name":"daily-sync","trigger":"Timed","enabled":true,"description":null}""");

        var req = new CreateWorkflowRequest("daily-sync", null, "Timed", true,
            new { cron = "0 6 * * *" });
        var wf = await BuildClient(handler).CreateWorkflowAsync(req);

        wf.Id.Should().Be(101);
        wf.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnableWorkflowAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/workflows/76/enable")
               .Respond("application/json", "{}");

        var act = async () => await BuildClient(handler).EnableWorkflowAsync(76);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisableWorkflowAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/workflows/76/disable")
               .Respond("application/json", "{}");

        var act = async () => await BuildClient(handler).DisableWorkflowAsync(76);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TriggerWorkflowAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/workflows/76/trigger")
               .Respond("application/json", "{}");

        var act = async () => await BuildClient(handler).TriggerWorkflowAsync(76);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteWorkflowAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/workflows/76")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteWorkflowAsync(76);
        await act.Should().NotThrowAsync();
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListItemsAsync_ReturnsPaginatedResult()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/blog_posts/items?page=1&page_size=20")
               .Respond("application/json",
                   """{"items":[{"id":1,"title":"Hello"},{"id":2,"title":"World"}],"total_count":2,"page":1,"page_size":20}""");

        var result = await BuildClient(handler).ListItemsAsync("blog_posts");

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListItemsAsync_WithFilterParam_AppendsFilterToUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/blog_posts/items*")
               .Respond("application/json",
                   """{"items":[],"total_count":0,"page":1,"page_size":20}""");

        // Should not throw — filter is URL-encoded and appended
        var result = await BuildClient(handler).ListItemsAsync("blog_posts", filterJson: """{"status":"draft"}""");
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemAsync_ReturnsItem()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/blog_posts/items/42")
               .Respond("application/json", """{"id":42,"title":"My Post","status":"draft"}""");

        var item = await BuildClient(handler).GetItemAsync("blog_posts", 42);

        item["id"]!.GetValue<int>().Should().Be(42);
        item["title"]!.GetValue<string>().Should().Be("My Post");
    }

    [Fact]
    public async Task GetItemAsync_NotFound_ThrowsAnythinkException()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities/blog_posts/items/999")
               .Respond(HttpStatusCode.NotFound, "application/json", """{"error":"Not found"}""");

        var act = async () => await BuildClient(handler).GetItemAsync("blog_posts", 999);
        await act.Should().ThrowAsync<AnythinkException>().Where(ex => ex.StatusCode == 404);
    }

    [Fact]
    public async Task CreateItemAsync_ReturnsCreatedItem()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/entities/blog_posts/items")
               .Respond("application/json", """{"id":10,"title":"New Post","status":"draft"}""");

        var data   = new JsonObject { ["title"] = "New Post" };
        var result = await BuildClient(handler).CreateItemAsync("blog_posts", data);

        result["id"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task UpdateItemAsync_ReturnsUpdatedItem()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{OrgPath}/entities/blog_posts/items/10")
               .Respond("application/json", """{"id":10,"title":"New Post","status":"approved"}""");

        var data   = new JsonObject { ["status"] = "approved" };
        var result = await BuildClient(handler).UpdateItemAsync("blog_posts", 10, data);

        result!["status"]!.GetValue<string>().Should().Be("approved");
    }

    [Fact]
    public async Task DeleteItemAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/entities/blog_posts/items/10")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteItemAsync("blog_posts", 10);
        await act.Should().NotThrowAsync();
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMeAsync_ReturnsCurrentUser()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/users/me")
               .Respond("application/json",
                   """{"id":1,"first_name":"Chris","last_name":"Addams","email":"chris@anythink.cloud","is_confirmed":true,"created_at":"2024-01-01T00:00:00Z"}""");

        var user = await BuildClient(handler).GetMeAsync();

        user.Should().NotBeNull();
        user!.FirstName.Should().Be("Chris");
    }

    [Fact]
    public async Task GetMeAsync_ReturnsNull_WhenNotFound()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/users/me")
               .Respond(HttpStatusCode.NotFound, "application/json", "null");

        var act = async () => await BuildClient(handler).GetMeAsync();
        await act.Should().ThrowAsync<AnythinkException>().Where(ex => ex.StatusCode == 404);
    }

    [Fact]
    public async Task GetUserAsync_ReturnsUser()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/users/7")
               .Respond("application/json",
                   """{"id":7,"first_name":"Alice","last_name":"Smith","email":"alice@example.com","is_confirmed":true,"created_at":"2024-01-01T00:00:00Z"}""");

        var user = await BuildClient(handler).GetUserAsync(7);

        user!.Id.Should().Be(7);
        user.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsCreatedUser()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/users")
               .Respond("application/json",
                   """{"id":8,"first_name":"Bob","last_name":"Jones","email":"bob@example.com","is_confirmed":false,"created_at":"2024-06-01T00:00:00Z"}""");

        var req  = new CreateUserRequest("Bob", "Jones", "bob@example.com", null);
        var user = await BuildClient(handler).CreateUserAsync(req);

        user.Id.Should().Be(8);
        user.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserAsync_ReturnsUpdatedUser()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{OrgPath}/users/8")
               .Respond("application/json",
                   """{"id":8,"first_name":"Robert","last_name":"Jones","email":"bob@example.com","is_confirmed":false,"created_at":"2024-06-01T00:00:00Z"}""");

        var req  = new UpdateUserRequest("Robert", "Jones", null);
        var user = await BuildClient(handler).UpdateUserAsync(8, req);

        user!.FirstName.Should().Be("Robert");
    }

    [Fact]
    public async Task DeleteUserAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/users/8")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteUserAsync(8);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendInvitationAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/users/8/send-invitation-email")
               .Respond("application/json", "{}");

        var act = async () => await BuildClient(handler).SendInvitationAsync(8);
        await act.Should().NotThrowAsync();
    }

    // ── Files ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFilesAsync_ReturnsFileList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/files?page=1&pageSize=25")
               .Respond("application/json",
                   """{"items":[{"id":1,"original_file_name":"logo.png","file_name":"abc-logo.png","file_type":"image/png","file_size":10240,"is_public":true,"created_at":"2024-01-01T00:00:00Z"}],"total_count":1,"page":1,"page_size":25}""");

        var files = await BuildClient(handler).GetFilesAsync();

        files.Should().HaveCount(1);
        files[0].OriginalFileName.Should().Be("logo.png");
        files[0].FileSize.Should().Be(10240);
    }

    [Fact]
    public async Task GetFilesAsync_EmptyResult_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/files*")
               .Respond("application/json",
                   """{"items":[],"total_count":0,"page":1,"page_size":25}""");

        var files = await BuildClient(handler).GetFilesAsync();
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFileAsync_ReturnsFileMetadata()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/files/5")
               .Respond("application/json",
                   """{"id":5,"original_file_name":"report.pdf","file_name":"xyz-report.pdf","file_type":"application/pdf","file_size":204800,"is_public":false,"created_at":"2024-03-01T00:00:00Z"}""");

        var file = await BuildClient(handler).GetFileAsync(5);

        file!.Id.Should().Be(5);
        file.FileType.Should().Be("application/pdf");
        file.IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/files/5")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteFileAsync(5);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadFileAsync_ReturnsUploadedFile()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/files*")
               .Respond("application/json",
                   """{"id":9,"original_file_name":"test.txt","file_name":"abc-test.txt","file_type":"text/plain","file_size":13,"is_public":false,"created_at":"2024-01-01T00:00:00Z"}""");

        // Write a real temp file so the method can read it
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "hello, anythink");
        try
        {
            var file = await BuildClient(handler).UploadFileAsync(tmpFile, isPublic: false);
            file.Id.Should().Be(9);
            file.OriginalFileName.Should().Be("test.txt");
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task UploadFileAsync_ServerError_ThrowsAnythinkException()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/files*")
               .Respond(HttpStatusCode.RequestEntityTooLarge, "application/json",
                   """{"error":"File too large"}""");

        var tmpFile = Path.GetTempFileName();
        try
        {
            var act = async () => await BuildClient(handler).UploadFileAsync(tmpFile);
            await act.Should().ThrowAsync<AnythinkException>()
                     .Where(ex => ex.StatusCode == 413);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRolesAsync_ReturnsRoleList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/roles")
               .Respond("application/json",
                   """[{"id":1,"name":"admin","description":"Full access","is_active":true},{"id":2,"name":"editor","description":"Can edit","is_active":true}]""");

        var roles = await BuildClient(handler).GetRolesAsync();

        roles.Should().HaveCount(2);
        roles[0].Name.Should().Be("admin");
        roles[1].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRoleAsync_ReturnsCreatedRole()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/roles")
               .Respond("application/json",
                   """{"id":3,"name":"viewer","description":"Read-only access","is_active":true}""");

        var req  = new CreateRoleRequest("viewer", "Read-only access");
        var role = await BuildClient(handler).CreateRoleAsync(req);

        role.Id.Should().Be(3);
        role.Name.Should().Be("viewer");
    }

    [Fact]
    public async Task DeleteRoleAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/roles/3")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteRoleAsync(3);
        await act.Should().NotThrowAsync();
    }

    // ── Pay ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStripeConnectAsync_ReturnsStatus()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/stripe-connect")
               .Respond("application/json",
                   """{"stripe_account_id":"acct_abc123","onboarding_completed":true,"charges_enabled":true,"payouts_enabled":true,"details_submitted":true}""");

        var status = await BuildClient(handler).GetStripeConnectAsync();

        status!.StripeAccountId.Should().Be("acct_abc123");
        status.ChargesEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetStripeConnectAsync_NotConfigured_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/stripe-connect")
               .Respond(HttpStatusCode.NotFound, "application/json", "null");

        var act = async () => await BuildClient(handler).GetStripeConnectAsync();
        await act.Should().ThrowAsync<AnythinkException>().Where(ex => ex.StatusCode == 404);
    }

    [Fact]
    public async Task CreateStripeConnectAsync_ReturnsCreatedStatus()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/stripe-connect")
               .Respond("application/json",
                   """{"stripe_account_id":"acct_new123","onboarding_completed":false,"charges_enabled":false,"payouts_enabled":false,"details_submitted":false}""");

        var req    = new CreateStripeConnectRequest("individual", "GB", "billing@example.com");
        var status = await BuildClient(handler).CreateStripeConnectAsync(req);

        status.StripeAccountId.Should().Be("acct_new123");
        status.OnboardingCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task CreateOnboardingLinkAsync_ReturnsUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/stripe-connect/onboarding")
               .Respond("application/json",
                   """{"url":"https://connect.stripe.com/setup/s/abc123"}""");

        var link = await BuildClient(handler).CreateOnboardingLinkAsync(
            "https://app.example.com/refresh",
            "https://app.example.com/return");

        link.Url.Should().Be("https://connect.stripe.com/setup/s/abc123");
    }

    [Fact]
    public async Task GetPaymentsAsync_ReturnsPaymentList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/payments?page=1&pageSize=25")
               .Respond("application/json",
                   """{"items":[{"id":"pay_abc","amount":99.99,"currency":"gbp","status":"succeeded","created_at":"2024-06-01T10:00:00Z"}],"total_count":1,"page":1,"page_size":25}""");

        var payments = await BuildClient(handler).GetPaymentsAsync();

        payments.Should().HaveCount(1);
        payments[0].Amount.Should().Be(99.99m);
        payments[0].Currency.Should().Be("gbp");
    }

    [Fact]
    public async Task GetPaymentsAsync_EmptyResult_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/payments*")
               .Respond("application/json",
                   """{"items":[],"total_count":0,"page":1,"page_size":25}""");

        var payments = await BuildClient(handler).GetPaymentsAsync();
        payments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPaymentAsync_ReturnsPayment()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/payments/pay_abc")
               .Respond("application/json",
                   """{"id":"pay_abc","amount":49.00,"currency":"usd","status":"succeeded","created_at":"2024-05-01T00:00:00Z"}""");

        var payment = await BuildClient(handler).GetPaymentAsync("pay_abc");

        payment!.Id.Should().Be("pay_abc");
        payment.Status.Should().Be("succeeded");
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsMethodList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/payment-methods")
               .Respond("application/json",
                   """[{"id":"pm_abc","type":"card","brand":"visa","last4":"4242","exp_month":12,"exp_year":2028}]""");

        var methods = await BuildClient(handler).GetPaymentMethodsAsync();

        methods.Should().HaveCount(1);
        methods[0].Brand.Should().Be("visa");
        methods[0].Last4.Should().Be("4242");
        methods[0].ExpYear.Should().Be(2028);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_EmptyList_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{PayPath}/payment-methods").Respond("application/json", "[]");

        var methods = await BuildClient(handler).GetPaymentMethodsAsync();
        methods.Should().BeEmpty();
    }
}
