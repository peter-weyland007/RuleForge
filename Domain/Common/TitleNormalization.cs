using System.Globalization;
using System.Text.RegularExpressions;

namespace RuleForge.Domain.Common;

public static class TitleNormalization
{
    public static string ToPascalTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var compact = Regex.Replace(value.Trim(), "\\s+", " ");
        var lower = compact.ToLowerInvariant();
        var titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);

        return titled;
    }
}
