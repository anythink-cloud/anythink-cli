using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class RolesPermissionsTests
{
    private static readonly List<Permission> AllPerms =
    [
        new(1, "blog_posts:read", null, 10, true),
        new(2, "anythink_subscription_plans:read", null, null, true),
        new(3, "anythink_subscription_plans:read", null, 99, true),
    ];

    // ── System permissions (no entity row) resolve only to entity_id == null ─

    [Fact]
    public void FindPermission_NoEntity_MatchesOnlySystemPermission()
        => RolesPermissionsAddCommand.FindPermission(AllPerms, "anythink_subscription_plans:read", null)!
            .Id.Should().Be(2);

    [Fact]
    public void FindPermission_WithEntity_MatchesOnlyThatEntity()
        => RolesPermissionsAddCommand.FindPermission(AllPerms, "anythink_subscription_plans:read", 99)!
            .Id.Should().Be(3);

    [Fact]
    public void FindPermission_EntityPermissionName_NotReturnedAsSystem()
        => RolesPermissionsAddCommand.FindPermission(AllPerms, "blog_posts:read", null).Should().BeNull();

    // ── A missing entity is a system permission, not an error ───────────────

    [Fact]
    public async Task TryResolveEntityIdAsync_EntityNotFound_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("https://api.example.com/org/1/entities/anythink_subscription_plans")
               .Respond(HttpStatusCode.NotFound);
        var client = new AnythinkClient("1", "https://api.example.com", new HttpClient(handler));

        (await RolesPermissionsAddCommand.TryResolveEntityIdAsync(client, "anythink_subscription_plans"))
            .Should().BeNull();
    }

    [Fact]
    public async Task TryResolveEntityIdAsync_OtherErrors_Propagate()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("https://api.example.com/org/1/entities/blog_posts")
               .Respond(HttpStatusCode.Forbidden);
        var client = new AnythinkClient("1", "https://api.example.com", new HttpClient(handler));

        var act = () => RolesPermissionsAddCommand.TryResolveEntityIdAsync(client, "blog_posts");
        await act.Should().ThrowAsync<AnythinkException>();
    }
}
