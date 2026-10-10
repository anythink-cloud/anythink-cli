using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AnythinkCli.Tests;

public class MarkupEscapingRuleTests
{
    // #67 is rewriting these two files and removes this exclusion when it rebases.
    private static readonly string[] SkippedUntilPr67 = ["ProjectsCommand.cs", "AccountsCommand.cs"];

    // Spectre.Console members that take their text literally, or escape interpolated values themselves.
    private static readonly HashSet<string> PlainTextMembers = ["Write", "WriteLine", "Escape", "Remove", "Parse", "TryParse", "Text"];

    private static readonly HashSet<string> MarkupHelpers = ["Success", "Info", "Warn"];

    private static readonly HashSet<string> EscapingHelpers = ["Error", "Header", "KeyValue", "AddRow"];

    private static readonly Lazy<CSharpCompilation> SourceCompilation = new(CompileSource);

    // ── Rule: text bound for markup (spinner, message line, prompt, table cell, error) never carries a raw string ──

    [Fact]
    public void MarkupText_NeverCarriesARawString()
    {
        var compilation = SourceCompilation.Value;
        var violations = new List<string>();
        var inspected = new List<string>();
        var scanned = new List<string>();

        foreach (var tree in compilation.SyntaxTrees.Where(IsScanned))
        {
            var result = Scan(compilation, tree);
            violations.AddRange(result.Violations);
            inspected.AddRange(result.Inspected);
            scanned.Add(Path.GetFileName(tree.FilePath));
        }

        scanned.Should().Contain("Renderer.cs", "the helpers that forward text to markup are checked too");
        inspected.Count(n => n == "StartAsync").Should().BeGreaterThan(40, "the scan must actually see the spinners");
        inspected.Count(n => n is "Success" or "Info" or "MarkupLine").Should().BeGreaterThan(100, "the scan must actually see the message lines");
        string.Join("\n", violations).Should().BeEmpty(
            "wrap spinner text in Renderer.Status(...), escape every other value with Markup.Escape(...), " +
            "and give Renderer.Error/Header/KeyValue/AddRow plain text, since they escape it themselves");
    }

    // ── Rule: the scanner catches every way a raw string can reach markup ──

    [Theory]
    [InlineData("foreach (var s in list) AnsiConsole.MarkupLine(s);", "MarkupLine(s)")]
    [InlineData("map.TryGetValue(\"k\", out var v); AnsiConsole.MarkupLine(v);", "MarkupLine(v)")]
    [InlineData("if (obj is string s) AnsiConsole.MarkupLine(s);", "MarkupLine(s)")]
    [InlineData("foreach (var (k, v) in map) AnsiConsole.MarkupLine(v);", "MarkupLine(v)")]
    [InlineData("var (a, b) = (raw, raw); AnsiConsole.MarkupLine(a);", "MarkupLine(a)")]
    [InlineData("var m = \"safe\"; m = $\"{raw}\"; AnsiConsole.MarkupLine(m);", "MarkupLine(m)")]
    [InlineData("var m = \"safe\"; m += raw; AnsiConsole.MarkupLine(m);", "MarkupLine(m)")]
    [InlineData("var m = \"safe\"; Fill(out m); AnsiConsole.MarkupLine(m);", "MarkupLine(m)")]
    [InlineData("var m = raw; AnsiConsole.MarkupLine(m);", "MarkupLine(m)")]
    [InlineData("AnsiConsole.Status().Start(\"go\", ctx => { ctx.Status($\"Working on {raw}\"); });", "Status(")]
    [InlineData("AnsiConsole.Status().Start(\"go\", ctx => { ctx.Status = $\"Working on {raw}\"; });", "Status")]
    [InlineData("AnsiConsole.Status().Start($\"Working on {raw}\", _ => { });", "Start(")]
    [InlineData("new SelectionPrompt<string>().AddChoices(list);", "AddChoices(list)")]
    [InlineData("new SelectionPrompt<string>().AddChoices(raw, \"other\");", "AddChoices(raw)")]
    [InlineData("new SelectionPrompt<string>().AddChoice(raw);", "AddChoice(raw)")]
    [InlineData("new SelectionPrompt<string>().UseConverter(x => x).AddChoices(list);", "AddChoices(list)")]
    [InlineData("AnsiConsole.Ask<string>($\"Name for {raw}?\");", "Ask(")]
    [InlineData("new ConfirmationPrompt($\"Delete {raw}?\");", "ConfirmationPrompt(")]
    [InlineData("ValidationResult.Error($\"{raw} is not valid\");", "Error(")]
    [InlineData("new SelectionPrompt<string>().MoreChoicesText(raw);", "MoreChoicesText(raw)")]
    [InlineData("new MultiSelectionPrompt<string>().InstructionsText(raw);", "InstructionsText(raw)")]
    [InlineData("table.AddRow(raw, \"other\");", "AddRow(raw)")]
    [InlineData("table.AddRow(array);", "AddRow(array)")]
    [InlineData("table.AddRow(list.ToArray());", "AddRow(list.ToArray())")]
    [InlineData("AnsiConsole.MarkupLine(\"{0}\", raw);", "MarkupLine(raw)")]
    [InlineData("AnsiConsole.MarkupLine(\"{0}\", obj);", "MarkupLine(obj)")]
    [InlineData("throw new CliException($\"Role {raw} not found\");", "CliException(")]
    [InlineData("new Panel(raw);", "Panel(raw)")]
    [InlineData("static void Show(string text) => AnsiConsole.MarkupLine($\"[green]{text}[/]\"); Show(raw);", "MarkupLine($\"[green]{text}[/]\")")]
    [InlineData("static void Success(string msg) => AnsiConsole.MarkupLine(msg); Success(raw);", "MarkupLine(msg)")]
    [InlineData("AnsiConsole.MarkupLine(MissingThing.Text);", "MarkupLine(MissingThing.Text)")]
    public void Scanner_CatchesARawString(string body, string expected)
    {
        var violations = ScanSnippet(body, allowCompileErrors: body.Contains("MissingThing"));

        violations.Should().NotBeEmpty($"`{body}` hands a raw string to markup");
        string.Join("\n", violations).Should().Contain(expected);
    }

    [Theory]
    [InlineData("Renderer.Error(Markup.Escape(raw));", "Error(")]
    [InlineData("Renderer.Header($\"Role {Markup.Escape(raw)}\");", "Header(")]
    [InlineData("Renderer.KeyValue(\"Name\", Markup.Escape(raw));", "KeyValue(")]
    [InlineData("Renderer.AddRow(table, \"Name\", Markup.Escape(raw));", "AddRow(")]
    [InlineData("Renderer.AddRow(table, \"Name\", Renderer.Status(raw));", "AddRow(")]
    [InlineData("Renderer.Error(\"Run [bold]anythink login[/] first.\");", "Error(")]
    [InlineData("Renderer.Header($\"[bold]{raw}[/]\");", "Header(")]
    public void Scanner_CatchesTextThatRendererEscapesAgain(string body, string expected)
    {
        var violations = ScanSnippet(body);

        violations.Should().NotBeEmpty($"`{body}` is escaped twice or shows its tags literally");
        string.Join("\n", violations).Should().Contain(expected);
    }

    [Theory]
    [InlineData("AnsiConsole.MarkupLine($\"[green]{Markup.Escape(raw)}[/] {number}\");")]
    [InlineData("AnsiConsole.MarkupLine($\"{(number > 1 ? \"[red]many[/]\" : \"[green]one[/]\")}\");")]
    [InlineData("AnsiConsole.Status().Start(Renderer.Status($\"Working on {raw}\"), ctx => { ctx.Status(Renderer.Status($\"Still on {raw}\")); });")]
    [InlineData("AnsiConsole.Status().Start(\"go\", ctx => { ctx.Status = Renderer.Status(raw); });")]
    [InlineData("var m = $\"[green]{number}[/]\"; AnsiConsole.MarkupLine(m);")]
    [InlineData("var m = Markup.Escape(raw); AnsiConsole.MarkupLine(m);")]
    [InlineData("new SelectionPrompt<string>().UseConverter(Markup.Escape).AddChoices(list);")]
    [InlineData("new SelectionPrompt<string>().AddChoices(\"one\", \"two\");")]
    [InlineData("new SelectionPrompt<string>().AddChoices([\"one\", \"two\"]);")]
    [InlineData("table.AddRow(Markup.Escape(raw), \"[green]yes[/]\");")]
    [InlineData("table.AddRow(array.Select(c => Markup.Escape(c)).ToArray());")]
    [InlineData("Renderer.Error(raw);")]
    [InlineData("Renderer.Header($\"Role {raw}\");")]
    [InlineData("Renderer.AddRow(table, \"Name\", raw);")]
    [InlineData("Renderer.Success($\"Saved [bold]{Markup.Escape(raw)}[/]\");")]
    [InlineData("throw new CliException($\"Role {Markup.Escape(raw)} not found\");")]
    [InlineData("AnsiConsole.MarkupLine($\"[red]✗[/] {cliEx.MarkupMessage}\");")]
    [InlineData("AnsiConsole.WriteLine(raw);")]
    [InlineData("AnsiConsole.MarkupLineInterpolated($\"{raw}\");")]
    public void Scanner_AcceptsEscapedOrPlainText(string body)
    {
        ScanSnippet(body).Should().BeEmpty();
    }

    // ── Scanner ──────────────────────────────────────────────────────────────

    private record ScanResult(List<string> Violations, List<string> Inspected);

    private static ScanResult Scan(CSharpCompilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        var violations = new List<string>();
        var inspected = new List<string>();

        void Report(SyntaxNode at, string what) =>
            violations.Add($"{Path.GetFileName(tree.FilePath)}:{at.GetLocation().GetLineSpan().StartLinePosition.Line + 1}  {Regex.Replace(what, @"\s+", " ")}");

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax:
                    CheckCall(node, model, inspected, Report);
                    break;
                case AssignmentExpressionSyntax assignment
                    when model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property
                        && property.ContainingNamespace.ToDisplayString() == "Spectre.Console"
                        && CarriesText(property.Type):
                    inspected.Add(property.Name);
                    if (!IsMarkupSafe(assignment.Right, model))
                        Report(assignment, assignment.ToString());
                    break;
            }
        }

        return new ScanResult(violations, inspected);
    }

    private static void CheckCall(SyntaxNode call, SemanticModel model, List<string> inspected, Action<SyntaxNode, string> report)
    {
        var arguments = call switch
        {
            InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments,
            BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments ?? default,
            _ => default
        };
        if (arguments.Count == 0 || model.GetConstantValue(call).HasValue)
            return;

        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol method)
        {
            report(call, $"{call} did not resolve, so the scan is blind to it");
            return;
        }

        var name = method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name;
        var owner = method.ContainingType.Name;
        var inSpectre = method.ContainingNamespace.ToDisplayString() == "Spectre.Console";

        if (owner == "Renderer" && EscapingHelpers.Contains(name))
        {
            foreach (var argument in arguments)
            {
                if (argument.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(i => IsEscaper(i, model)))
                    report(call, $"{name}({argument.Expression}) escapes its own text, so escaping it first shows the brackets twice");
                else if (LiteralText(argument.Expression).Contains("[/]"))
                    report(call, $"{name}({argument.Expression}) escapes its own text, so the tags show literally");
            }
            return;
        }

        var isSink = (inSpectre && !PlainTextMembers.Contains(name) && !name.Contains("Interpolated"))
            || (owner == "Renderer" && MarkupHelpers.Contains(name))
            || (method.MethodKind == MethodKind.Constructor && owner == "CliException");
        if (!isSink)
            return;

        inspected.Add(name);
        if (name is "AddChoices" or "AddChoice" or "AddChoiceGroup" && HasEscapingConverter(call))
            return;

        foreach (var argument in arguments)
        {
            var info = model.GetTypeInfo(argument.Expression);
            if (info.ConvertedType is null || info.Type?.TypeKind == TypeKind.Error || info.ConvertedType.TypeKind == TypeKind.Error)
                report(call, $"{name}({argument.Expression}) has an argument whose type did not resolve");
            else if (CarriesText(info.Type) && !IsMarkupSafe(argument.Expression, model))
                report(call, $"{name}({argument.Expression})");
        }
    }

    private static bool IsMarkupSafe(ExpressionSyntax expression, SemanticModel model, int depth = 0)
    {
        if (depth > 8)
            return false;

        if (model.GetConstantValue(expression).HasValue || IsValueType(model.GetTypeInfo(expression).Type))
            return true;

        bool Safe(ExpressionSyntax e) => IsMarkupSafe(e, model, depth + 1);

        return expression switch
        {
            ParenthesizedExpressionSyntax p => Safe(p.Expression),
            PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } p => Safe(p.Operand),
            InterpolatedStringExpressionSyntax s => s.Contents.OfType<InterpolationSyntax>().All(h => Safe(h.Expression)),
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) || b.IsKind(SyntaxKind.CoalesceExpression) => Safe(b.Left) && Safe(b.Right),
            ConditionalExpressionSyntax c => Safe(c.WhenTrue) && Safe(c.WhenFalse),
            SwitchExpressionSyntax sw => sw.Arms.All(arm => Safe(arm.Expression)),
            CollectionExpressionSyntax c => c.Elements.All(e => e is ExpressionElementSyntax x && Safe(x.Expression)),
            ArrayCreationExpressionSyntax { Initializer: { } init } => init.Expressions.All(Safe),
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions.All(Safe),
            InvocationExpressionSyntax call => IsEscaper(call, model) || IsSafeSequence(call, Safe),
            MemberAccessExpressionSyntax member => model.GetSymbolInfo(member).Symbol is IPropertySymbol { Name: "MarkupMessage", ContainingType.Name: "CliException" },
            IdentifierNameSyntax name => model.GetSymbolInfo(name).Symbol switch
            {
                ILocalSymbol local => IsSafeLocal(local, name, model, Safe),
                IParameterSymbol { ContainingSymbol: IMethodSymbol { ContainingType.Name: "Renderer" } owner } => MarkupHelpers.Contains(owner.Name),
                _ => false
            },
            _ => false
        };
    }

    private static bool IsEscaper(InvocationExpressionSyntax call, SemanticModel model) =>
        model.GetSymbolInfo(call).Symbol is IMethodSymbol method
        && method.ContainingType.ToDisplayString() + "." + method.Name is "Spectre.Console.Markup.Escape" or "AnythinkCli.Output.Renderer.Status";

    private static bool IsSafeSequence(InvocationExpressionSyntax call, Func<ExpressionSyntax, bool> safe)
    {
        if (call.Expression is not MemberAccessExpressionSyntax member)
            return false;

        switch (member.Name.Identifier.Text)
        {
            case "Select" when call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LambdaExpressionSyntax lambda:
                return lambda.ExpressionBody is { } body
                    ? safe(body)
                    : lambda.Block!.DescendantNodes().OfType<ReturnStatementSyntax>().All(r => r.Expression is { } e && safe(e));
            case "ToArray" or "ToList" or "AsEnumerable" or "Where" or "Distinct" or "OrderBy" or "Reverse" or "Take" or "Skip":
                return safe(member.Expression);
            default:
                return false;
        }
    }

    private static bool IsSafeLocal(ILocalSymbol local, SyntaxNode use, SemanticModel model, Func<ExpressionSyntax, bool> safe)
    {
        var declarators = local.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>().ToList();
        if (declarators.Count == 0 || declarators.Count != local.DeclaringSyntaxReferences.Length || declarators.Any(d => d.Initializer is null))
            return false;

        var scope = (SyntaxNode?)declarators[0].FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? use.SyntaxTree.GetRoot();
        return !IsReassigned(local, scope, model) && declarators.All(d => safe(d.Initializer!.Value));
    }

    private static bool IsReassigned(ILocalSymbol local, SyntaxNode scope, SemanticModel model)
    {
        bool Refers(SyntaxNode node) =>
            node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, local));

        return scope.DescendantNodes().Any(node => node switch
        {
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax or TupleExpressionSyntax or ParenthesizedExpressionSyntax } assignment => Refers(assignment.Left),
            ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } argument => Refers(argument.Expression),
            _ => false
        });
    }

    private static bool HasEscapingConverter(SyntaxNode call)
    {
        var receiver = (call as InvocationExpressionSyntax)?.Expression is MemberAccessExpressionSyntax member ? member.Expression : null;
        while (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax next } invocation)
        {
            if (next.Name.Identifier.Text == "UseConverter"
                && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() == "Markup.Escape")
                return true;
            receiver = next.Expression;
        }

        return false;
    }

    private static string LiteralText(ExpressionSyntax expression) =>
        string.Concat(expression.DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.InterpolatedStringTextToken))
            .Select(t => t.ValueText));

    private static bool CarriesText(ITypeSymbol? type) => type switch
    {
        null => false,
        { SpecialType: SpecialType.System_String or SpecialType.System_Object } => true,
        IArrayTypeSymbol array => CarriesText(array.ElementType),
        INamedTypeSymbol named => named.AllInterfaces.Prepend(named)
            .Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T && CarriesText(i.TypeArguments[0])),
        _ => false
    };

    private static bool IsValueType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var inner] })
            type = inner;

        return type is not null
            && (type.TypeKind == TypeKind.Enum
                || type.SpecialType is SpecialType.System_Boolean or (>= SpecialType.System_SByte and <= SpecialType.System_Double)
                || type.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan");
    }

    // ── Compilation ──────────────────────────────────────────────────────────

    private static List<string> ScanSnippet(string body, bool allowCompileErrors = false)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using Spectre.Console;
            using AnythinkCli.Client;
            using AnythinkCli.Output;

            class Snippet
            {
                static void Fill(out string value) => value = "";

                void Run(string raw, int number, object obj, string[] array, List<string> list,
                    Dictionary<string, string> map, Table table, CliException cliEx)
                {
                    {{body}}
                }
            }
            """, path: "Snippet.cs");
        var compilation = SourceCompilation.Value.AddSyntaxTrees(tree);

        var errors = compilation.GetSemanticModel(tree).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (!allowCompileErrors && errors.Count > 0)
            throw new InvalidOperationException($"The snippet does not compile, so it proves nothing: {string.Join("; ", errors)}");

        return Scan(compilation, tree).Violations;
    }

    private static bool IsScanned(SyntaxTree tree) =>
        tree.FilePath.Length > 0 && !SkippedUntilPr67.Contains(Path.GetFileName(tree.FilePath));

    private static CSharpCompilation CompileSource()
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

        return CSharpCompilation.Create("commands", trees, references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AnythinkCli.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("AnythinkCli.sln not found above the test output.");
    }
}
