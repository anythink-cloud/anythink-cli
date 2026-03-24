using System.Text.Json;
using AnythinkMcp.Tools;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class EntityToolsTests : McpTestBase
{
    private const string OrgId = "12345";
    private const string ApiUrl = "https://12345.api.anythink.cloud";

    [Fact]
    public async Task ListEntities_Excludes_TableName()
    {
        SetupProjectProfile(orgId: OrgId, apiUrl: ApiUrl);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/{OrgId}/entities")
            .Respond("application/json", """
                [
                    {"name":"products","table_name":"mg_products","is_public":false,
                     "enable_rls":false,"is_system":false,"is_junction":false}
                ]
            """);

        var factory = CreateFactory(mock);
        var tools = new EntityTools(factory);

        var result = await tools.ListEntities();

        result.Should().Contain("products");
        result.Should().NotContain("table_name");
        result.Should().NotContain("TableName");
        result.Should().NotContain("mg_products");
    }

    [Fact]
    public async Task ListEntities_Returns_All_Projected_Fields()
    {
        SetupProjectProfile(orgId: OrgId, apiUrl: ApiUrl);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/{OrgId}/entities")
            .Respond("application/json", """
                [
                    {"name":"artists","table_name":"mg_artists","is_public":true,
                     "enable_rls":true,"is_system":false,"is_junction":true}
                ]
            """);

        var factory = CreateFactory(mock);
        var tools = new EntityTools(factory);

        var result = await tools.ListEntities();
        var doc = JsonDocument.Parse(result);
        var entity = doc.RootElement[0];

        entity.GetProperty("Name").GetString().Should().Be("artists");
        entity.GetProperty("IsPublic").GetBoolean().Should().BeTrue();
        entity.GetProperty("EnableRls").GetBoolean().Should().BeTrue();
        entity.GetProperty("IsSystem").GetBoolean().Should().BeFalse();
        entity.GetProperty("IsJunction").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ListEntities_Empty_Returns_Empty_Array()
    {
        SetupProjectProfile(orgId: OrgId, apiUrl: ApiUrl);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/{OrgId}/entities")
            .Respond("application/json", "[]");

        var factory = CreateFactory(mock);
        var tools = new EntityTools(factory);

        var result = await tools.ListEntities();

        result.Should().Be("[]");
    }

    [Fact]
    public async Task GetEntity_Returns_Full_Entity()
    {
        SetupProjectProfile(orgId: OrgId, apiUrl: ApiUrl);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/{OrgId}/entities/products")
            .Respond("application/json", """
                {"name":"products","table_name":"mg_products","is_public":false,
                 "enable_rls":false,"is_system":false,"is_junction":false,
                 "fields":[{"id":1,"name":"title","db_type":"varchar"}]}
            """);

        var factory = CreateFactory(mock);
        var tools = new EntityTools(factory);

        var result = await tools.GetEntity("products");

        // GetEntity returns full entity including table_name
        result.Should().Contain("products");
        result.Should().Contain("title");
    }
}
