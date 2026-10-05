using System.Text.RegularExpressions;

namespace EpamPrep.Api.Infrastructure;

public static partial class TextStats
{
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-\.#+]*")]
    private static partial Regex WordRegex();

    public static int WordCount(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : WordRegex().Matches(text).Count;
}
