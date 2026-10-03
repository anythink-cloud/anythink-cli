using AnythinkCli.Importers;
using FluentAssertions;

namespace AnythinkCli.Tests;

// ── ImportRunner.TopoSortCollections ──────────────────────────────────────────
// The data-import section depends on topological ordering by FK references so
// referenced rows always land before referrers — and so junction tables
// (which are pure FKs) come last after both endpoints exist.

public class ImportRunnerTopoSortTests
{
    private static ImportCollection Col(string name, params (string Field, string? FkTo)[] fields)
    {
        var specs = fields.Select(f => new ImportFieldSpec(
            Name: f.Field, DatabaseType: "integer", DisplayType: "input",
            Label: f.Field, IsRequired: false, IsUnique: false, IsIndexed: false,
            ForeignKeyCollection: f.FkTo)).ToList();
        return new ImportCollection(name, specs);
    }

    private static int IndexOf(List<ImportCollection> ordered, string name) =>
        ordered.FindIndex(c => c.Name == name);

    [Fact]
    public void Referenced_Collections_Come_Before_Referrers()
    {
        // articles → authors (m2o), articles → categories (m2o)
        var input = new List<ImportCollection>
        {
            Col("articles",   ("author",   "authors"),
                              ("category", "categories")),
            Col("authors"),
            Col("categories"),
        };

        var ordered = ImportRunner.TopoSortCollections(input);

        IndexOf(ordered, "authors")   .Should().BeLessThan(IndexOf(ordered, "articles"));
        IndexOf(ordered, "categories").Should().BeLessThan(IndexOf(ordered, "articles"));
    }

    [Fact]
    public void Chained_FKs_Are_Ordered_Transitively()
    {
        // comments → articles → authors, categories
        var input = new List<ImportCollection>
        {
            Col("comments",   ("article",  "articles")),
            Col("articles",   ("author",   "authors"),
                              ("category", "categories")),
            Col("authors"),
            Col("categories"),
        };

        var ordered = ImportRunner.TopoSortCollections(input);

        IndexOf(ordered, "authors")   .Should().BeLessThan(IndexOf(ordered, "articles"));
        IndexOf(ordered, "categories").Should().BeLessThan(IndexOf(ordered, "articles"));
        IndexOf(ordered, "articles")  .Should().BeLessThan(IndexOf(ordered, "comments"));
    }

    [Fact]
    public void Junction_Tables_Come_Last_After_Both_Endpoints()
    {
        // articles_tags (junction) references articles + tags
        var input = new List<ImportCollection>
        {
            new("articles_tags",
                new List<ImportFieldSpec>
                {
                    new("articles_id", "integer", "input", "articles id",
                        false, false, false, ForeignKeyCollection: "articles"),
                    new("tags_id",     "integer", "input", "tags id",
                        false, false, false, ForeignKeyCollection: "tags"),
                },
                IsJunction: true),
            Col("articles"),
            Col("tags"),
        };

        var ordered = ImportRunner.TopoSortCollections(input);

        IndexOf(ordered, "articles").Should().BeLessThan(IndexOf(ordered, "articles_tags"));
        IndexOf(ordered, "tags")    .Should().BeLessThan(IndexOf(ordered, "articles_tags"));
    }

    [Fact]
    public void Independent_Collections_Are_Ordered_Stably_By_Name()
    {
        var input = new List<ImportCollection>
        {
            Col("zoo"),
            Col("alpha"),
            Col("mango"),
        };

        var ordered = ImportRunner.TopoSortCollections(input);
        ordered.Select(c => c.Name).Should().Equal("alpha", "mango", "zoo");
    }

    [Fact]
    public void Cycle_Is_Tolerated_Not_Infinite_Loop()
    {
        // a → b, b → a — pathological cycle. Sort must terminate; the
        // runner handles cycle-breaking by dropping unresolvable FKs at
        // insert time, so order within the cycle just needs to be stable.
        var input = new List<ImportCollection>
        {
            Col("a", ("ref_b", "b")),
            Col("b", ("ref_a", "a")),
        };

        var ordered = ImportRunner.TopoSortCollections(input);
        ordered.Should().HaveCount(2);
        ordered.Select(c => c.Name).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void Fk_To_Unknown_Collection_Is_Ignored()
    {
        // Defensive: if Directus relations point at a collection outside
        // our import set (e.g. directus_files we strip elsewhere), the
        // sort shouldn't crash.
        var input = new List<ImportCollection>
        {
            Col("articles", ("cover_image", "directus_files")),
        };

        var ordered = ImportRunner.TopoSortCollections(input);
        ordered.Should().ContainSingle(c => c.Name == "articles");
    }
}
