using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AnythinkMcp.Cli;

public enum CliParameterKind { Argument, Scalar, Flag, Vector }

public sealed record CliParameter(
    string Name, string Token, CliParameterKind Kind, int Position, bool Required, string ClrType, string Description,
    bool ExplicitBoolean = false);

public sealed record CliCommand(IReadOnlyList<string> Path, string Description, IReadOnlyList<CliParameter> Parameters)
{
    public string Key => string.Join(' ', Path);

    public string ToolName => string.Join('_', Path).Replace('-', '_');

    public string Verb => Path[^1];
}

public static partial class CliCommandModel
{
    private static readonly Lazy<IReadOnlyList<CliCommand>> Commands = new(Load);

    public static IReadOnlyList<CliCommand> All => Commands.Value;

    private static IReadOnlyList<CliCommand> Load()
    {
        var result = CliRunner.RunAsync(["cli", "xmldoc"], client: null, CliToolScope.Local).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Could not read the CLI command model.");

        return Parse(result.Output);
    }

    internal static IReadOnlyList<CliCommand> Parse(string xml) =>
        Walk(XDocument.Parse(xml).Root!, []).ToList();

    private static IEnumerable<CliCommand> Walk(XElement parent, IReadOnlyList<string> path)
    {
        foreach (var element in parent.Elements("Command"))
        {
            var commandPath = path.Append((string)element.Attribute("Name")!).ToList();

            if ((string?)element.Attribute("IsBranch") == "true")
            {
                foreach (var child in Walk(element, commandPath))
                    yield return child;
                continue;
            }

            yield return new CliCommand(
                commandPath,
                Text(element.Element("Description")),
                element.Element("Parameters")?.Elements().Select(ParseParameter).ToList() ?? []);
        }
    }

    private static CliParameter ParseParameter(XElement element)
    {
        var description = Text(element.Element("Description"));
        var required = (string?)element.Attribute("Required") == "true";
        var clrType = (string?)element.Attribute("ClrType") ?? "System.String";

        if (element.Name == "Argument")
        {
            var name = (string)element.Attribute("Name")!;
            return new CliParameter(
                PropertyName(name), "", CliParameterKind.Argument,
                (int?)element.Attribute("Position") ?? 0, required, clrType, description);
        }

        var longName = (string?)element.Attribute("Long");
        var optionName = string.IsNullOrEmpty(longName) ? (string)element.Attribute("Short")! : longName;
        var kind = (string?)element.Attribute("Kind") switch
        {
            "flag" => CliParameterKind.Flag,
            "vector" => CliParameterKind.Vector,
            _ => CliParameterKind.Scalar
        };

        var explicitBoolean = kind == CliParameterKind.Flag
            && (((string?)element.Attribute("Value") is { } value && value != "NULL")
                || clrType.StartsWith("System.Nullable`1[[System.Boolean", StringComparison.Ordinal));

        return new CliParameter(
            PropertyName(optionName), (optionName.Length == 1 ? "-" : "--") + optionName, kind, 0, required, clrType, description,
            explicitBoolean);
    }

    private static string PropertyName(string name) => name.ToLowerInvariant().Replace('-', '_');

    private static string Text(XElement? element) =>
        element is null ? "" : Whitespace().Replace(element.Value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
