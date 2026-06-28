using System.Text;

namespace ScreenSubtitleTranslator.Pipeline;

public static class SubtitleTextUtilities
{
    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = true;
        foreach (var character in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static string TrimRepeatedPrefixFromPrevious(string? previousText, string currentText)
    {
        if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
        {
            return currentText;
        }

        var previousWords = SplitWords(previousText);
        var currentWords = SplitWords(currentText);
        var maxOverlapWords = Math.Min(6, Math.Min(previousWords.Length, currentWords.Length));

        for (var wordCount = maxOverlapWords; wordCount >= 2; wordCount--)
        {
            if (SuffixMatchesPrefix(previousWords, currentWords, wordCount))
            {
                return string.Join(' ', currentWords.Skip(wordCount)).Trim();
            }
        }

        if (maxOverlapWords >= 1
            && NormalizeToken(previousWords[^1]) == NormalizeToken(currentWords[0])
            && NormalizeToken(currentWords[0]).Length >= 5)
        {
            return string.Join(' ', currentWords.Skip(1)).Trim();
        }

        return currentText;
    }

    public static bool EndsWithEllipsis(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith("...", StringComparison.Ordinal)
            || trimmed.EndsWith('…');
    }

    private static bool SuffixMatchesPrefix(string[] previousWords, string[] currentWords, int wordCount)
    {
        for (var index = 0; index < wordCount; index++)
        {
            var previousWord = NormalizeToken(previousWords[previousWords.Length - wordCount + index]);
            var currentWord = NormalizeToken(currentWords[index]);
            if (!string.Equals(previousWord, currentWord, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] SplitWords(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeToken(string token)
    {
        return NormalizeForComparison(token).Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
