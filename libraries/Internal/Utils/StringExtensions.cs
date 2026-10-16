using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Integrations.Utils;

public static class StringExtensions
{
    extension(string text)
    {
        public string ToKebabCase()
        {
            ArgumentNullException.ThrowIfNull(text);
            return JsonNamingPolicy.KebabCaseLower.ConvertName(text);
        }

        public string ToAsciiSlug()
        {
            ArgumentNullException.ThrowIfNull(text);

            if (text.Length < 1) return "";

            var decomposed = text.Normalize(NormalizationForm.FormD)
                .Replace('\u0141', 'L')
                .Replace('\u0142', 'l');

            var name = string.Concat(decomposed.Where(c =>
                CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

            var kebabName = name.ToKebabCase();
            return Regex.Replace(kebabName, "[^a-z0-9]+", "-").Trim('-');
        }
    }
}
