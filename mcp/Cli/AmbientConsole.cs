using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AnythinkMcp.Cli;

internal sealed class AmbientConsole : IAnsiConsole
{
    private static readonly AsyncLocal<(TextWriter Writer, IAnsiConsole Console)?> Ambient = new();
    private static readonly IAnsiConsole Discard = Create(TextWriter.Null);

    public static readonly AmbientConsole Instance = new();

    public static TextWriter? Writer => Ambient.Value?.Writer;

    private static IAnsiConsole Current => Ambient.Value?.Console ?? Discard;

    public static void Install()
    {
        AnsiConsole.Console = Instance;
        Console.SetOut(new AmbientWriter(Console.Out));
        Console.SetError(new AmbientWriter(Console.Error));
    }

    public static void Use(TextWriter writer) => Ambient.Value = (writer, Create(writer));

    private static IAnsiConsole Create(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 4096;
        return console;
    }

    public Profile Profile => Current.Profile;

    public IAnsiConsoleCursor Cursor => Current.Cursor;

    public IAnsiConsoleInput Input => Current.Input;

    public IExclusivityMode ExclusivityMode => Current.ExclusivityMode;

    public RenderPipeline Pipeline => Current.Pipeline;

    public void Clear(bool home) => Current.Clear(home);

    public void Write(IRenderable renderable) => Current.Write(renderable);

    private sealed class AmbientWriter(TextWriter fallback) : TextWriter
    {
        private TextWriter Target => Writer ?? fallback;

        public override Encoding Encoding => fallback.Encoding;

        public override void Write(char value) => Target.Write(value);

        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);

        public override void Write(string? value) => Target.Write(value);

        public override void WriteLine(string? value) => Target.WriteLine(value);

        public override void Flush() => Target.Flush();
    }
}
