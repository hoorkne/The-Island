using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RioIsland;

internal sealed record LyricLine(TimeSpan At, string Text);

internal static class LrcLyrics
{
    // Supports [mm:ss.xx] and multiple timestamps per line.
    private static readonly Regex TimeTag = new(@"\[(?<min>\d{1,3}):(?<sec>\d{2})(?:\.(?<frac>\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<LyricLine> Parse(string contents)
    {
        List<LyricLine> output = new();
        foreach (string line in contents.Split('\n'))
        {
            var matches = TimeTag.Matches(line);
            if (matches.Count == 0) continue;
            string words = TimeTag.Replace(line, "").Trim();
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["min"].Value, out int min) ||
                    !int.TryParse(match.Groups["sec"].Value, out int sec) || sec > 59) continue;
                string fraction = match.Groups["frac"].Value;
                // hundredths or milliseconds -> milliseconds
                int milliseconds = fraction.Length switch
                {
                    1 => int.Parse(fraction, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(fraction, CultureInfo.InvariantCulture) * 10,
                    3 => int.Parse(fraction, CultureInfo.InvariantCulture),
                    _ => 0
                };
                output.Add(new LyricLine(TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) +
                                         TimeSpan.FromMilliseconds(milliseconds), words));
            }
        }
        output.Sort((a, b) => a.At.CompareTo(b.At));
        return output;
    }

    public static string AtTime(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        if (lines.Count == 0) return "No lyric timestamps found";
        int lo = 0, hi = lines.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].At <= position) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result == -1 ? "♫" : lines[result].Text;
    }
}
