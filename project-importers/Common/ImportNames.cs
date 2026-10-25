using System.Security.Cryptography;
using System.Text;

namespace Anythink.ProjectImporters;

internal static class ImportNames
{
    public const int MaxLength = 63;

    public static string SnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsAsciiLetterOrDigit(c)) { sb.Append('_'); continue; }
            var startsWord = char.IsAsciiLetterUpper(c) && i > 0 && (
                char.IsAsciiLetterLower(name[i - 1]) || char.IsAsciiDigit(name[i - 1]) ||
                (char.IsAsciiLetterUpper(name[i - 1]) && i + 1 < name.Length && char.IsAsciiLetterLower(name[i + 1])));
            if (startsWord) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return string.Join('_', sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool IsValid(string name) =>
        name.Length is > 0 and <= MaxLength && char.IsAsciiLetter(name[0]) &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
        !name.StartsWith("anythink", StringComparison.OrdinalIgnoreCase);

    public static string? Entity(string source, string prefix)
    {
        var name = SnakeCase(source);
        return name.Length == 0 ? null : IsValid(name) ? name : IsValid($"{prefix}_{name}") ? $"{prefix}_{name}" : null;
    }

    public static long StableId(string text) =>
        (long)(BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0) >> 2);
}
