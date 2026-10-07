using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class MenusTests
{
    private const string Org = "https://api.example.com/org/99999";

    private static AnythinkClient Client(MockHttpMessageHandler handler) =>
        new("99999", "https://api.example.com", new HttpClient(handler));

    private static MenuItemResponse Item(int id, int? parent, int sort, bool locked = false, params MenuItemResponse[] children) =>
        new(id, 1, $"Item {id}", "Icon", $"/path/{id}", parent, sort, [.. children], locked);

    private static MenuResponse Menu(params MenuItemResponse[] items) => new(1, "Admin", 0, [.. items]);

    // ── Rule: each operation sends the right method, path and body ────────────

    [Fact]
    public async Task UpdateMenuAsync_PutsTheNameAndRole_AndAcceptsNoContent()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{Org}/menus/92").WithContent("""{"name":"Staff","role_id":4}""").Respond(HttpStatusCode.NoContent);

        await Client(handler).UpdateMenuAsync(92, new CreateMenuRequest("Staff", 4));

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UpdateMenuItemAsync_PutsEveryFieldToTheItemsOwnPath()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{Org}/menus/92/items/299")
            .WithContent("""{"display_name":"Badges","icon":"Award","href":"/x","parent_id":168}""")
            .Respond(HttpStatusCode.NoContent);

        await Client(handler).UpdateMenuItemAsync(92, 299, new CreateMenuItemRequest("Badges", "Award", "/x", 168));

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task DeleteMenuItemAsync_DeletesThatItemInThatMenu()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Delete, $"{Org}/menus/92/items/299").Respond(HttpStatusCode.NoContent);

        await Client(handler).DeleteMenuItemAsync(92, 299);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ReorderMenuItemsAsync_PutsAnArrayOfPositions_AndOmitsAnEmptyParent()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{Org}/menus/92/items/reorder")
            .WithContent("""[{"item_id":300,"sort_order":0},{"item_id":301,"sort_order":1,"parent_id":299}]""")
            .Respond(HttpStatusCode.NoContent);

        await Client(handler).ReorderMenuItemsAsync(92,
            [new ReorderMenuItemRequest(300, 0, null), new ReorderMenuItemRequest(301, 1, 299)]);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ReorderMenusAsync_PutsAnArrayOfPositionsToTheCollectionPath()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{Org}/menus/reorder")
            .WithContent("""[{"menu_id":92,"sort_order":0},{"menu_id":90,"sort_order":1}]""")
            .Respond(HttpStatusCode.NoContent);

        await Client(handler).ReorderMenusAsync([new ReorderMenuRequest(92, 0), new ReorderMenuRequest(90, 1)]);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task AMenuTheApiRejects_SurfacesTheApisMessage()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{Org}/menus/92/items/10").Respond(HttpStatusCode.BadRequest, "text/plain", "Cannot update locked menu item");

        var act = () => Client(handler).UpdateMenuItemAsync(92, 10, new CreateMenuItemRequest("a", "b", "/c", 0));

        (await act.Should().ThrowAsync<AnythinkException>()).Which.Message.Should().Be("Cannot update locked menu item");
    }

    // ── Rule: a menu item is locked unless the API says otherwise ─────────────

    [Fact]
    public void MenuItemResponse_ReadsTheLockedFlag_AndDefaultsToUnlocked()
    {
        var locked = JsonSerializer.Deserialize<MenuItemResponse>(
            """{"id":1,"menu_id":1,"display_name":"Home","icon":"Home","href":"/","parent_id":null,"sort_order":0,"locked":true,"items":[]}""")!;
        var unflagged = JsonSerializer.Deserialize<MenuItemResponse>(
            """{"id":2,"menu_id":1,"display_name":"Home","icon":"Home","href":"/","parent_id":null,"sort_order":0,"items":[]}""")!;

        locked.Locked.Should().BeTrue();
        unflagged.Locked.Should().BeFalse();
    }

    // ── Rule: the tree is walked parents first, with each item's depth and siblings ──

    [Fact]
    public void Walk_VisitsParentsBeforeTheirChildren_WithDepthAndSiblings()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 0, false, Item(3, 2, 0))), Item(4, null, 1));

        var nodes = MenuTree.Walk(menu.Items).ToList();

        nodes.Select(n => n.Item.Id).Should().Equal(1, 2, 3, 4);
        nodes.Select(n => n.Depth).Should().Equal(0, 1, 2, 0);
        nodes[1].Siblings.Should().ContainSingle().Which.Id.Should().Be(2);
        nodes[3].Siblings.Select(s => s.Id).Should().Equal(1, 4);
    }

    // ── Rule: a reorder keeps the unnamed siblings after the named ones, in their current order ──

    [Fact]
    public void PlanItemOrder_PutsNamedItemsFirst_ThenTheRestInTheirCurrentOrder_SendingOnlyWhatMoves()
    {
        var menu = Menu(Item(1, null, 5), Item(2, null, 2), Item(3, null, 9), Item(4, null, 7));

        var (plan, error) = MenuTree.PlanItemOrder(menu, [3, 1]);

        error.Should().BeNull();
        plan!.Select(p => (p.ItemId, p.SortOrder)).Should().Equal((3, 0), (1, 1), (4, 3));
    }

    [Fact]
    public void PlanItemOrder_ForItemsAlreadyInThatOrder_PlansNothing()
    {
        var menu = Menu(Item(1, null, 0), Item(2, null, 1), Item(3, null, 2));

        var (plan, error) = MenuTree.PlanItemOrder(menu, [1, 2]);

        error.Should().BeNull();
        plan.Should().BeEmpty();
    }

    // ── Rule: locked items stay where they are and are never planned; movable items are numbered around them ──

    [Fact]
    public void PlanItemOrder_NumbersMovableItemsAfterALockedFirstItem()
    {
        var menu = Menu(Item(1, null, 0, true), Item(2, null, 1), Item(3, null, 2));

        var (plan, _) = MenuTree.PlanItemOrder(menu, [3]);

        plan!.Select(p => (p.ItemId, p.SortOrder)).Should().Equal((3, 1), (2, 2));
    }

    [Fact]
    public void PlanItemOrder_AroundALockedMiddleItem_KeepsItsSlotAndNumbersEachSideAroundIt()
    {
        var menu = Menu(Item(1, null, 0), Item(2, null, 5, true), Item(3, null, 9));

        var (plan, _) = MenuTree.PlanItemOrder(menu, [3]);

        plan!.Select(p => (p.ItemId, p.SortOrder)).Should().Equal((3, 4), (1, 6));
    }

    [Fact]
    public void PlanItemOrder_BeforeALockedLastItem_NumbersBelowIt()
    {
        var menu = Menu(Item(1, null, 0), Item(2, null, 1), Item(3, null, 2, true));

        var (plan, _) = MenuTree.PlanItemOrder(menu, [2]);

        plan!.Select(p => (p.ItemId, p.SortOrder)).Should().Equal((2, 0), (1, 1));
        plan.Should().NotContain(p => p.ItemId == 3);
    }

    [Fact]
    public void PlanItemOrder_WithNoRoomBetweenTwoLockedItems_IsRefusedRatherThanTied()
    {
        var menu = Menu(Item(1, null, 0, true), Item(2, null, 1), Item(3, null, 1), Item(4, null, 2, true));

        var (plan, error) = MenuTree.PlanItemOrder(menu, [3]);

        plan.Should().BeNull();
        error.Should().Be("There isn't room to move items between locked items 1 and 4.");
    }

    [Fact]
    public void PlanItemOrder_RefusesToMoveALockedItem()
    {
        var (plan, error) = MenuTree.PlanItemOrder(Menu(Item(1, null, 0, true)), [1]);

        plan.Should().BeNull();
        error.Should().Contain("locked");
    }

    // ── Rule: a joined list of ids reaching a markup line is escaped like any other text ──

    [Fact]
    public void MenusCommand_EscapesEveryJoinedListItPutsIntoAMarkupLine()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AnythinkCli.sln")))
            dir = dir.Parent;
        var source = File.ReadAllLines(Path.Combine(dir!.FullName, "src", "Commands", "MenusCommand.cs"));

        var unescaped = source.Where(line =>
            Regex.IsMatch(line, @"Renderer\.(Success|Info|Warn)\(") && line.Contains("string.Join(") && !line.Contains("Markup.Escape(string.Join("));

        unescaped.Should().BeEmpty();
    }

    // ── Rule: deleting a menu says what goes with it, in the question and in the result ──

    [Fact]
    public void DeleteImpact_CountsEveryNestedItem_AndSaysHowManyAreLockedBuiltInOnes()
    {
        var menu = Menu(Item(1, null, 0, true), Item(2, null, 1, false, Item(3, 2, 0), Item(4, 2, 1, true)));

        var impact = MenuTree.DeleteImpact(menu, [new(1, "Admin", 5, [])]);

        impact.ItemsNote.Should().Be(" and its 4 items, including 2 locked built-in items");
    }

    [Fact]
    public void DeleteImpact_ForAMenuWithNoLockedItems_DoesNotMentionLockedOnes()
    {
        var menu = Menu(Item(1, null, 0));

        MenuTree.DeleteImpact(menu, [new(1, "Admin", 5, [])]).ItemsNote.Should().Be(" and its 1 item");
    }

    [Fact]
    public void DeleteImpact_ForAnEmptyMenu_SaysNothingAboutItems()
    {
        MenuTree.DeleteImpact(Menu(), [new(1, "Admin", 5, [])]).ItemsNote.Should().BeEmpty();
    }

    [Fact]
    public void DeleteImpact_WhenItIsTheRolesOnlyMenu_SaysTheRoleIsLeftWithNone()
    {
        var impact = MenuTree.DeleteImpact(Menu(), [new(1, "Admin", 5, []), new(2, "Staff", 6, [])]);

        impact.RoleWillBeNote.Should().Be(" Role 5 will be left with no menu.");
        impact.RoleNowNote.Should().Be(" Role 5 now has no menu.");
    }

    [Fact]
    public void DeleteImpact_WhenTheRoleHasAnotherMenu_OrTheRoleIsUnknown_SaysNothingAboutTheRole()
    {
        MenuTree.DeleteImpact(Menu(), [new(1, "Admin", 5, []), new(2, "Other", 5, [])]).RoleWillBeNote.Should().BeEmpty();
        MenuTree.DeleteImpact(Menu(), []).RoleWillBeNote.Should().BeEmpty();
    }

    // ── Rule: a second menu for a role warns, because the role is only shown its first ──

    [Fact]
    public void SharedRoleWarning_NamesTheMenuThatAlreadyHasTheRole()
    {
        MenuResponse[] menus = [new(90, "Public", 3, []), new(91, "Staff", 4, [])];

        MenuTree.SharedRoleWarning(menus, 4).Should().Be("Role 4 already has a menu ('Staff', id: 91). A role is shown only its first menu, so this one may not appear.");
        MenuTree.SharedRoleWarning(menus, 9).Should().BeNull();
    }

    [Fact]
    public void SharedRoleWarning_IgnoresTheMenuBeingChanged()
    {
        MenuTree.SharedRoleWarning([new(91, "Staff", 4, [])], 4, exceptMenuId: 91).Should().BeNull();
    }

    // ── Rule: an item moved under a new parent goes after that parent's other children ──

    [Fact]
    public void PlaceLast_GoesAfterTheHighestSiblingNumber_EvenWhenALockedSiblingHoldsIt()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 4), Item(3, 1, 7, true)), Item(4, null, 1));

        var placed = MenuTree.PlaceLast(menu, MenuTree.Find(menu, 4)!.Item, 1);

        placed.Should().Be(new ReorderMenuItemRequest(4, 8, 1));
    }

    [Fact]
    public void PlaceLast_UnderAParentWithNoChildren_StartsAtZero()
    {
        var menu = Menu(Item(1, null, 0), Item(2, null, 1));

        MenuTree.PlaceLast(menu, MenuTree.Find(menu, 2)!.Item, 1).Should().Be(new ReorderMenuItemRequest(2, 0, 1));
    }

    [Fact]
    public void PlaceLast_AtTheTopLevel_ComesAfterTheOtherTopLevelItemsWithNoParent()
    {
        var menu = Menu(Item(1, null, 3), Item(2, null, 5, false, Item(3, 2, 0)));

        MenuTree.PlaceLast(menu, MenuTree.Find(menu, 3)!.Item, 0).Should().Be(new ReorderMenuItemRequest(3, 6, null));
    }

    [Fact]
    public void PlanItemOrder_KeepsEachItemsParent_SoReorderingNeverMovesAnItem()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 0), Item(3, 1, 1)));

        var (plan, _) = MenuTree.PlanItemOrder(menu, [3]);

        plan!.Select(p => (p.ItemId, p.ParentId)).Should().Equal((3, 1), (2, 1));
    }

    [Fact]
    public void PlanItemOrder_RefusesItemsFromDifferentParents()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 0)), Item(3, null, 1));

        var (plan, error) = MenuTree.PlanItemOrder(menu, [2, 3]);

        plan.Should().BeNull();
        error.Should().Contain("different parents");
    }

    [Fact]
    public void PlanItemOrder_RefusesAnItemThatIsNotInTheMenu()
    {
        var (plan, error) = MenuTree.PlanItemOrder(Menu(Item(1, null, 0)), [1, 9]);

        plan.Should().BeNull();
        error.Should().Be("Menu item 9 not found in menu 1.");
    }

    [Fact]
    public void PlanMenuOrder_PutsNamedMenusFirst_ThenTheRestInTheirCurrentOrder()
    {
        MenuResponse[] menus = [new(90, "A", 1, []), new(91, "B", 1, []), new(92, "C", 1, [])];

        var (plan, error) = MenuTree.PlanMenuOrder(menus, [92]);

        error.Should().BeNull();
        plan!.Select(p => (p.MenuId, p.SortOrder)).Should().Equal((92, 0), (90, 1), (91, 2));
    }

    [Fact]
    public void PlanMenuOrder_RefusesAMenuThatDoesNotExist()
    {
        var (plan, error) = MenuTree.PlanMenuOrder([new(90, "A", 1, [])], [7]);

        plan.Should().BeNull();
        error.Should().Be("Menu 7 not found.");
    }

    // ── Rule: an id list is whole positive numbers, each once ─────────────────

    [Theory]
    [InlineData("1,2,3", true)]
    [InlineData(" 4 , 5 ", true)]
    [InlineData("1,,2", true)]
    [InlineData("", false)]
    [InlineData(",", false)]
    [InlineData("1,a", false)]
    [InlineData("1.5", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("2,2", false)]
    [InlineData("99999999999", false)]
    public void TryParseIds_AcceptsOnlyDistinctPositiveWholeNumbers(string text, bool valid)
    {
        MenuTree.TryParseIds(text, "Item", out var ids, out var error).Should().Be(valid);

        (error is null).Should().Be(valid);
        if (valid) ids.Should().NotBeEmpty();
    }

    [Fact]
    public void TryParseIds_KeepsTheOrderGiven()
    {
        MenuTree.TryParseIds("301, 299,300", "Item", out var ids, out _).Should().BeTrue();

        ids.Should().Equal(301, 299, 300);
    }

    // ── Rule: a new parent can't be the item, a descendant of it, or something that doesn't exist ──

    [Fact]
    public void ParentProblem_AllowsTopLevelAnotherBranchOrASibling()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 0)), Item(3, null, 1));
        var two = MenuTree.Find(menu, 2)!.Item;

        MenuTree.ParentProblem(menu, two, 0).Should().BeNull();
        MenuTree.ParentProblem(menu, two, 3).Should().BeNull();
        MenuTree.ParentProblem(menu, two, 1).Should().BeNull();
    }

    [Fact]
    public void ParentProblem_RefusesTheItemItsDescendantsAndStrangers()
    {
        var menu = Menu(Item(1, null, 0, false, Item(2, 1, 0, false, Item(3, 2, 0))));
        var one = MenuTree.Find(menu, 1)!.Item;

        MenuTree.ParentProblem(menu, one, 1).Should().Contain("own parent");
        MenuTree.ParentProblem(menu, one, 3).Should().Contain("sits inside item 1");
        MenuTree.ParentProblem(menu, one, 8).Should().Contain("Parent menu item 8 not found in menu 1");
    }
}
