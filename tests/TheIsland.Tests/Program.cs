using System;
using RioIsland;

int checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    if (!Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
    checks++;
}
var lines = LrcLyrics.Parse("[ar:Artist]\n[00:02.5]Second\n[00:01.25][00:03.125]Repeat\n[00:99]Invalid");
Equal(3, lines.Count, "Multiple tags and invalid seconds");
Equal(1250d, lines[0].At.TotalMilliseconds, "Hundredths");
Equal(2500d, lines[1].At.TotalMilliseconds, "Tenths");
Equal(3125d, lines[2].At.TotalMilliseconds, "Milliseconds");
Equal("Waiting for the first line", LrcLyrics.AtTime(lines, TimeSpan.Zero), "Before first line");
Equal("Second", LrcLyrics.AtTime(lines, TimeSpan.FromSeconds(2.5)), "Exact boundary");
Equal("Repeat", LrcLyrics.AtTime(lines, TimeSpan.FromSeconds(1.3)), "Seek backwards");
Equal("Repeat", LrcLyrics.AtTime(lines, TimeSpan.FromHours(2)), "After final line");
var positive = LrcLyrics.Parse("[offset:+1000]\n[00:05]Early");
Equal(4d, positive[0].At.TotalSeconds, "Positive offset advances lyrics");
var negative = LrcLyrics.Parse("[offset:-500]\n[00:05]Late");
Equal(5.5d, negative[0].At.TotalSeconds, "Negative offset delays lyrics");
var duplicates = LrcLyrics.Parse("[00:01]First\n[00:01]Last");
Equal("Last", LrcLyrics.AtTime(duplicates, TimeSpan.FromSeconds(1)), "Duplicate timestamps");
var silence = LrcLyrics.Parse("[00:01]Singing\n[00:02]");
Equal("", LrcLyrics.AtTime(silence, TimeSpan.FromSeconds(2)), "Blank line clears lyric");
Equal(0, LrcLyrics.Parse("untimed\n[ar:Artist]\n[00:60]invalid").Count, "Malformed text");
Equal(1, LrcLyrics.Parse("[offset:9999999999999999]\n[00:01]Safe").Count, "Oversized offset");
Console.WriteLine($"PASS: {checks} lyric regression checks.");
