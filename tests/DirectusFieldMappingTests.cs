using AnythinkCli.Importers.Directus;
using FluentAssertions;

namespace AnythinkCli.Tests;

// ── DirectusFieldMapping ──────────────────────────────────────────────────────
// The mapping is db-type-aware: Anythink validates display_type against
// db_type server-side, so the importer must produce valid combinations. These
// tests pin the rules so we don't regress on a release of Directus or
// Anythink with new types.

public class DirectusFieldMappingTests
{
    // ── MapDatabaseType ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("string",     "varchar")]
    [InlineData("text",       "text")]
    [InlineData("integer",    "integer")]
    [InlineData("biginteger", "bigint")]
    [InlineData("float",      "decimal")]   // Anythink has no 'float' — use decimal
    [InlineData("decimal",    "decimal")]
    [InlineData("boolean",    "boolean")]
    [InlineData("datetime",   "timestamp")] // Anythink stores as 'timestamp'
    [InlineData("timestamp",  "timestamp")]
    [InlineData("date",       "date")]
    [InlineData("json",       "jsonb")]
    [InlineData("uuid",       "varchar")]
    [InlineData("geometry",   "geo")]
    public void Directus_Type_Maps_To_Anythink_Db_Type(string directus, string expected)
    {
        DirectusFieldMapping.MapDatabaseType(directus, sqlType: null)
            .Should().Be(expected);
    }

    [Fact]
    public void Unknown_Directus_Type_Falls_Back_To_Sql_Type_Hint()
    {
        DirectusFieldMapping.MapDatabaseType("custom-type", sqlType: "bigint")
            .Should().Be("bigint");
        DirectusFieldMapping.MapDatabaseType("custom-type", sqlType: "jsonb")
            .Should().Be("jsonb");
        DirectusFieldMapping.MapDatabaseType("custom-type", sqlType: null)
            .Should().Be("varchar");  // ultimate fallback
    }

    // ── MapDisplayType (db-type aware) ────────────────────────────────────────

    [Theory]
    [InlineData("input",                      "varchar", "input")]
    [InlineData("select-dropdown",            "varchar", "select")]
    [InlineData("select-radio",               "varchar", "select")]
    [InlineData("select-multiple-checkbox",   "varchar", "select")]
    [InlineData(null,                         "varchar", "input")]
    public void Varchar_Display_Maps_To_Input_Or_Select(string? iface, string db, string expected)
    {
        DirectusFieldMapping.MapDisplayType(iface, db).Should().Be(expected);
    }

    [Theory]
    [InlineData("input-rich-text-html", "text", "rich-text")]
    [InlineData("input-rich-text-md",   "text", "rich-text")]
    [InlineData("input-multiline",      "text", "textarea")]
    [InlineData("textarea",             "text", "textarea")]
    [InlineData(null,                   "text", "textarea")]
    public void Text_Display_Maps_To_Rich_Text_Or_Textarea(string? iface, string db, string expected)
    {
        DirectusFieldMapping.MapDisplayType(iface, db).Should().Be(expected);
    }

    [Theory]
    [InlineData("boolean",   "checkbox")]   // Anythink rejects 'boolean' display
    [InlineData("timestamp", "timestamp")]
    [InlineData("date",      "short-date")]
    [InlineData("integer",   "input")]
    [InlineData("bigint",    "input")]
    [InlineData("decimal",   "input")]
    [InlineData("jsonb",     "jsonb")]
    [InlineData("geo",       "geo")]
    [InlineData("file",      "file")]
    public void Other_Db_Types_Get_Their_Canonical_Display(string db, string expected)
    {
        DirectusFieldMapping.MapDisplayType(iface: null, db).Should().Be(expected);
    }
}
