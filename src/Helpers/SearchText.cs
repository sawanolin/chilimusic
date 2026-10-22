using System.Globalization;
using System.Text;

namespace ChiliMusic;

internal static class SearchText
{
    private static readonly Lazy<Dictionary<string, string[]>> Readings = new(() => { using var stream = typeof(SearchText).Assembly.GetManifestResourceStream("ChiliMusic.Resources.pinyin.json")!; return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? []; });
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Cache = new();
    private static readonly int[] InitialRanges = [-20319,-20284,-19776,-19219,-18711,-18527,-18240,-17923,-17418,-16475,-16213,-15641,-15166,-14923,-14915,-14631,-14150,-14091,-13319,-12839,-12557,-11848,-11056,-10247];
    private const string Initials = "abcdefghjklmnopqrstwxyz";
    public static bool Matches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) return true;
        string needle = query.Trim();
        return GetInitials(text).Contains(needle, StringComparison.OrdinalIgnoreCase) || Cache.GetOrAdd(text, t => string.Concat(t.Select(c => Readings.Value.TryGetValue(c.ToString(), out var readings) ? readings[0] : c.ToString())).ToLowerInvariant()).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
    internal static string GetInitials(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); var gb = Encoding.GetEncoding(936); var result = new StringBuilder();
        foreach (char c in text)
        {
            if (c < 128) { if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c)); continue; }
            var bytes = gb.GetBytes(c.ToString()); if (bytes.Length != 2) continue; int code = bytes[0] * 256 + bytes[1] - 65536;
            for (int i = Initials.Length - 1; i >= 0; i--) if (code >= InitialRanges[i] && code < -10246) { result.Append(Initials[i]); break; }
        }
        return result.ToString();
    }
}
