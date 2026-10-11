namespace AnythinkCli.Commands;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandGuideAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
