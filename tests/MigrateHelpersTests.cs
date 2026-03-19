using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace AnythinkCli.Tests;

// ── MigrateCommand.RemapHref ──────────────────────────────────────────────────

public class RemapHrefTests
{
    private const string Src = "11111111";
    private const string Dst = "22222222";

    [Fact]
    public void Swaps_OrgId_In_Entity_Href()
    {
        var result = MigrateCommand.RemapHref($"/org/{Src}/entities/categories", Src, Dst);
        result.Should().Be($"/org/{Dst}/entities/categories");
    }

    [Fact]
    public void Swaps_OrgId_In_Nested_Path()
    {
        var result = MigrateCommand.RemapHref($"/org/{Src}/entities/products/items/5", Src, Dst);
        result.Should().Be($"/org/{Dst}/entities/products/items/5");
    }

    [Fact]
    public void Returns_Href_Unchanged_When_OrgId_Not_Present()
    {
        const string href = "/dashboard/settings";
        MigrateCommand.RemapHref(href, Src, Dst).Should().Be(href);
    }

    [Fact]
    public void Returns_Empty_String_Unchanged()
    {
        MigrateCommand.RemapHref("", Src, Dst).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/org/11111111/entities/foo", "/org/22222222/entities/foo")]
    [InlineData("/ORG/11111111/ENTITIES/foo", "/org/22222222/ENTITIES/foo")]
    public void Is_Case_Insensitive_On_Org_Segment(string input, string expected)
    {
        MigrateCommand.RemapHref(input, Src, Dst).Should().Be(expected);
    }

    [Fact]
    public void Does_Not_Corrupt_Href_When_Src_And_Dst_Are_Same()
    {
        var href = $"/org/{Src}/entities/orders";
        MigrateCommand.RemapHref(href, Src, Src).Should().Be(href);
    }
}

// ── MigrateCommand.StripEmbeddedFileRefs ─────────────────────────────────────

public class StripEmbeddedFileRefsTests
{
    // ── null / primitives pass through ────────────────────────────────────────

    [Fact]
    public void Null_Returns_Null()
        => MigrateCommand.StripEmbeddedFileRefs(null).Should().BeNull();

    [Fact]
    public void String_Value_Is_Unchanged()
    {
        var node   = JsonValue.Create("hello");
        var result = MigrateCommand.StripEmbeddedFileRefs(node);
        result!.GetValue<string>().Should().Be("hello");
    }

    [Fact]
    public void Integer_Value_Is_Unchanged()
    {
        var node   = JsonValue.Create(42);
        var result = MigrateCommand.StripEmbeddedFileRefs(node);
        result!.GetValue<int>().Should().Be(42);
    }

    // ── file objects are removed (return null) ────────────────────────────────

    [Theory]
    [InlineData("original_file_name")]
    [InlineData("file_name")]
    [InlineData("file_type")]
    public void Object_With_File_Key_Returns_Null(string fileKey)
    {
        var obj = new JsonObject { [fileKey] = "some-value", ["id"] = 5 };
        MigrateCommand.StripEmbeddedFileRefs(obj).Should().BeNull();
    }

    // ── safe objects are preserved ────────────────────────────────────────────

    [Fact]
    public void Object_Without_File_Keys_Is_Preserved()
    {
        var obj    = new JsonObject { ["name"] = "Alice", ["score"] = 100 };
        var result = MigrateCommand.StripEmbeddedFileRefs(obj) as JsonObject;
        result.Should().NotBeNull();
        result!["name"]!.GetValue<string>().Should().Be("Alice");
        result["score"]!.GetValue<int>().Should().Be(100);
    }

    // ── arrays: file items are removed, others kept ───────────────────────────

    [Fact]
    public void Array_Removes_File_Objects_And_Keeps_Others()
    {
        var arr = new JsonArray
        {
            new JsonObject { ["original_file_name"] = "clip.mp3", ["id"] = 1 },
            new JsonObject { ["text"] = "Option A" },
            new JsonObject { ["file_name"] = "img.png" },
            new JsonObject { ["text"] = "Option B" },
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(arr) as JsonArray;
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[0]!["text"]!.GetValue<string>().Should().Be("Option A");
        result[1]!["text"]!.GetValue<string>().Should().Be("Option B");
    }

    [Fact]
    public void Array_Of_Primitives_Is_Unchanged()
    {
        var arr    = new JsonArray { 1, 2, 3 };
        var result = MigrateCommand.StripEmbeddedFileRefs(arr) as JsonArray;
        result!.Count.Should().Be(3);
    }

    // ── nested structures ─────────────────────────────────────────────────────

    [Fact]
    public void Nested_File_Object_Inside_Options_Array_Is_Stripped()
    {
        // Mirrors the real mental_edge_questions#1 payload shape:
        // { "question_options": [ { "text": "Yes", "audio": { "file_name": "clip.mp3" } } ] }
        var payload = new JsonObject
        {
            ["question"] = "How do you feel?",
            ["question_options"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"]  = "Yes",
                    ["audio"] = new JsonObject { ["file_name"] = "clip.mp3", ["id"] = 7 }
                },
                new JsonObject
                {
                    ["text"]  = "No",
                    ["audio"] = new JsonObject { ["file_name"] = "clip2.mp3", ["id"] = 8 }
                }
            }
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(payload) as JsonObject;

        result!["question"]!.GetValue<string>().Should().Be("How do you feel?");
        var opts = result["question_options"] as JsonArray;
        opts!.Count.Should().Be(2);
        // audio sub-objects should have been stripped to null
        opts[0]!["audio"].Should().BeNull();
        opts[1]!["audio"].Should().BeNull();
        // text should still be there
        opts[0]!["text"]!.GetValue<string>().Should().Be("Yes");
    }

    [Fact]
    public void Deeply_Nested_File_Object_Is_Stripped()
    {
        var deep = new JsonObject
        {
            ["level1"] = new JsonObject
            {
                ["level2"] = new JsonObject
                {
                    ["file_type"] = "image/png",
                    ["id"]        = 99
                }
            }
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(deep) as JsonObject;
        var level1 = result!["level1"] as JsonObject;
        level1!["level2"].Should().BeNull();
    }
}

// ── MigrateCommand.CountItems ─────────────────────────────────────────────────

public class CountItemsTests
{
    private static MenuItemResponse Item(string name, params MenuItemResponse[] children) =>
        new(Id: 0, MenuId: 0, DisplayName: name, Icon: "", Href: "", ParentId: null, SortOrder: 0, Items: [.. children]);

    [Fact]
    public void Empty_List_Returns_Zero()
        => MigrateCommand.CountItems([]).Should().Be(0);

    [Fact]
    public void Flat_List_Returns_Count()
        => MigrateCommand.CountItems([Item("A"), Item("B"), Item("C")]).Should().Be(3);

    [Fact]
    public void Nested_Children_Are_Counted_Recursively()
    {
        var root = Item("Dashboard",
            Item("Overview"),
            Item("Reports", Item("Monthly"), Item("Annual")));

        MigrateCommand.CountItems([root]).Should().Be(5); // root + 4 descendants
    }
}
