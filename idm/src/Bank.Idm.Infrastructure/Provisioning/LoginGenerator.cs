using System.Globalization;
using System.Text;

namespace Bank.Idm.Infrastructure.Provisioning;

/// <summary>
/// Yangi akkaunt logini: ismning birinchi harfi + "." + familiya (masalan "a.karimov"), faqat a-z, 0-9 va nuqta.
/// O'zbek lotin (o', g') va kirill yozuvi lotinga o'giriladi. Band bo'lsa — oxiriga raqam (a.karimov2, a.karimov3...).
/// </summary>
public static class LoginGenerator
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "j", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "x", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "", ['ы'] = "i", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya", ['ў'] = "o", ['қ'] = "q", ['ғ'] = "g", ['ҳ'] = "h"
    };

    /// <summary>Nomzodlar tartibda; ism yoki familiyadan login yasab bo'lmasa — bo'sh.</summary>
    public static IEnumerable<string> Candidates(string firstName, string lastName, int maxLength, int maxSuffix = 99)
    {
        var first = Normalize(firstName);
        var last = Normalize(lastName);
        if (first.Length == 0 || last.Length == 0 || maxLength < 4) yield break;
        var baseLogin = $"{first[0]}.{last}";
        yield return Fit(baseLogin, maxLength);
        for (var i = 2; i <= maxSuffix; i++)
        {
            var suffix = i.ToString(CultureInfo.InvariantCulture);
            yield return Fit(baseLogin, maxLength - suffix.Length) + suffix;
        }
    }

    internal static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (Cyrillic.TryGetValue(c, out var latin)) { sb.Append(latin); continue; }
            // Diakritikalar (é → e); apostroflar (o', g', ʻ, ’) va boshqa belgilar tashlab yuboriladi
            foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
                if (d is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(d);
        }
        return sb.ToString();
    }

    private static string Fit(string value, int length) => (value.Length <= length ? value : value[..length]).TrimEnd('.');
}
