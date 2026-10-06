using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AnythinkCli.Tests;

/// <summary>
/// Spectre parses spinner text and message lines as markup, so a name like "Shop [beta]" throws
/// ("Could not find color or style 'beta'"). In a spinner that happens on a background thread and
/// takes the whole process down, hosted MCP server included.
/// </summary>
public class MarkupEscapingRuleTests
{
    // These two files are being rewritten by #67, which removes this exclusion when it rebases.
    private static readonly string[] SkippedUntilPr67 = ["ProjectsCommand.cs", "AccountsCommand.cs"];

    private static readonly HashSet<string> SinkNames =
    [
        "MarkupLine", "Markup", "Success", "Info", "Warn", "Confirm", "Title", "AddRow",
        "StartAsync", "Start", "AddTask", "TextPrompt", "Rule", "Panel"
    ];

    private static readonly HashSet<string> Escapers = ["Markup.Escape", "Renderer.Status"];

    // ── Rule: text bound for markup (spinner, message line, prompt, table cell) never carries a raw string ──

    [Fact]
    public void MarkupText_NeverInterpolatesARawString()
    {
        var violations = new List<string>();
        var inspected = new List<string>();

        foreach (var (tree, model) in CommandTrees())
        {
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (SinkOf(node, model) is not { } sink)
                    continue;

                inspected.Add(sink.Name);
                foreach (var argument in sink.Arguments.Where(a => model.GetTypeInfo(a.Expression).Type?.SpecialType == SpecialType.System_String))
                    if (!IsMarkupSafe(argument.Expression, model))
                        violations.Add($"{Path.GetFileName(tree.FilePath)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}  {sink.Name}({Regex.Replace(argument.Expression.ToString(), @"\s+", " ")})");
            }
        }

        inspected.Count(n => n == "StartAsync").Should().BeGreaterThan(40, "the scan must actually see the spinners");
        inspected.Count(n => n is "Success" or "Info" or "MarkupLine").Should().BeGreaterThan(100, "the scan must actually see the message lines");
        string.Join("\n", violations).Should().BeEmpty("wrap spinner text in Renderer.Status(...) and escape every other value with Markup.Escape(...)");
    }

    // ── Scanner ──────────────────────────────────────────────────────────────

    private record Sink(string Name, IEnumerable<ArgumentSyntax> Arguments);

    private static Sink? SinkOf(SyntaxNode node, SemanticModel model)
    {
        var symbol = node switch
        {
            InvocationExpressionSyntax call => model.GetSymbolInfo(call).Symbol,
            ObjectCreationExpressionSyntax created => model.GetSymbolInfo(created).Symbol,
            _ => null
        };
        var name = node switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member.Name.Identifier.Text,
            InvocationExpressionSyntax { Expression: SimpleNameSyntax simple } => simple.Identifier.Text,
            ObjectCreationExpressionSyntax { Type: GenericNameSyntax generic } => generic.Identifier.Text,
            ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax identifier } => identifier.Identifier.Text,
            _ => null
        };
        var arguments = (node as InvocationExpressionSyntax)?.ArgumentList.Arguments
            ?? (node as ObjectCreationExpressionSyntax)?.ArgumentList?.Arguments;

        if (name is null || arguments is null || !SinkNames.Contains(name))
            return null;

        var ns = symbol?.ContainingNamespace?.ToDisplayString();
        var type = symbol?.ContainingType?.Name;
        var isSpectre = ns == "Spectre.Console";
        var isRenderer = type == "Renderer";

        // Renderer.AddRow and the Renderer helpers that escape for the caller are not sinks.
        if (symbol is null || !(isSpectre || (isRenderer && name is "Success" or "Info" or "Warn")))
            return null;

        return new Sink(name, arguments);
    }

    private static bool IsMarkupSafe(ExpressionSyntax expression, SemanticModel model, int depth = 0)
    {
        if (depth > 5)
            return false;

        if (model.GetConstantValue(expression).HasValue || IsValueType(model.GetTypeInfo(expression).Type))
            return true;

        bool Safe(ExpressionSyntax e) => IsMarkupSafe(e, model, depth + 1);

        return expression switch
        {
            ParenthesizedExpressionSyntax p => Safe(p.Expression),
            InterpolatedStringExpressionSyntax s => s.Contents.OfType<InterpolationSyntax>().All(h => Safe(h.Expression)),
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) => Safe(b.Left) && Safe(b.Right),
            ConditionalExpressionSyntax c => Safe(c.WhenTrue) && Safe(c.WhenFalse),
            SwitchExpressionSyntax sw => sw.Arms.All(arm => Safe(arm.Expression)),
            InvocationExpressionSyntax call => Escapers.Contains(call.Expression.ToString()),
            MemberAccessExpressionSyntax { Name.Identifier.Text: "MarkupMessage" } => true,
            IdentifierNameSyntax name => model.GetSymbolInfo(name).Symbol is ILocalSymbol local
                && local.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>()
                    .All(d => d.Initializer is { } init && Safe(init.Value)),
            _ => false
        };
    }

    private static bool IsValueType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var inner] })
            type = inner;

        return type is not null
            && (type.TypeKind == TypeKind.Enum
                || type.SpecialType is SpecialType.System_Boolean or (>= SpecialType.System_SByte and <= SpecialType.System_Double)
                || type.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan");
    }

    private static IEnumerable<(SyntaxTree Tree, SemanticModel Model)> CommandTrees()
    {
        var sourceDir = Path.Combine(RepoRoot(), "src");
        var trees = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .Append(CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; " +
                "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;"))
            .ToList();

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) != "anythink.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create("commands", trees, references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        return trees
            .Where(t => t.FilePath.Length > 0
                     && Path.GetFileName(t.FilePath) != "Renderer.cs"
                     && !SkippedUntilPr67.Contains(Path.GetFileName(t.FilePath)))
            .Select(t => (t, compilation.GetSemanticModel(t)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AnythinkCli.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("AnythinkCli.sln not found above the test output.");
    }
}
