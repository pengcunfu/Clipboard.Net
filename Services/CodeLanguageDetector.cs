using System.Text.RegularExpressions;

namespace ClipboardApp.Services;

/// <summary>
/// Heuristic-based detector that guesses the programming language of a text snippet.
/// </summary>
public static class CodeLanguageDetector
{
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.TrimStart();
        if (trimmed.Length < 2)
            return null;

        // ---- Strong structural signals ----

        // JSON: starts with { or [, contains quoted keys
        if (LooksLikeJson(trimmed))
            return "json";

        // XML / HTML: starts with < tag or <?xml or <!DOCTYPE
        if (LooksLikeMarkup(trimmed))
            return trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
                ? "html"
                : "xml";

        // Shebang
        if (trimmed.StartsWith("#!", StringComparison.Ordinal))
        {
            if (trimmed.Contains("python", StringComparison.OrdinalIgnoreCase)) return "python";
            if (trimmed.Contains("bash", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("/sh", StringComparison.OrdinalIgnoreCase)) return "bash";
            if (trimmed.Contains("node", StringComparison.OrdinalIgnoreCase)) return "javascript";
            if (trimmed.Contains("ruby", StringComparison.OrdinalIgnoreCase)) return "ruby";
            if (trimmed.Contains("perl", StringComparison.OrdinalIgnoreCase)) return "perl";
        }

        // CSS: starts with @media / @import / selector { ... }
        if (LooksLikeCss(text))
            return "css";

        // YAML: `key: value` lines at start, optional `---` header
        if (LooksLikeYaml(text))
            return "yaml";

        // SQL: leading keyword
        if (LooksLikeSql(trimmed))
            return "sql";

        // ---- Keyword-based scoring ----
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        Score(text, "csharp",
            new[] { "using System", "namespace ", "class ", "public class", "private void",
                    "Console.WriteLine", "static void Main", "public static", "int ", "string ",
                    "new ", "=>" }, scores);

        Score(text, "java",
            new[] { "public class", "public static void main", "System.out.println",
                    "private ", "protected ", "import java." }, scores);

        Score(text, "javascript",
            new[] { "function ", "var ", "let ", "const ", "console.log", "=>",
                    "document.", "window.", "require(", "module.exports" }, scores);

        Score(text, "typescript",
            new[] { "interface ", ": string", ": number", ": boolean",
                    "type ", "export ", "import {", ": void" }, scores);

        Score(text, "python",
            new[] { "def ", "import ", "from ", "print(", "self.",
                    "__init__", "elif ", "if __name__" }, scores);

        Score(text, "go",
            new[] { "package ", "func ", "import (", "fmt.Println", ":= ", "go func" }, scores);

        Score(text, "rust",
            new[] { "fn ", "let mut ", "impl ", "pub fn", "use std::", "println!",
                    "match ", "Option<" }, scores);

        Score(text, "php",
            new[] { "<?php", "echo ", "$this->", "public function", "private function" }, scores);

        Score(text, "ruby",
            new[] { "def ", "end\n", "puts ", "@", "do |", "require '", "attr_" }, scores);

        if (scores.Count == 0)
            return null;

        var top = scores.OrderByDescending(kv => kv.Value).First();
        // Require at least 2 hits so a single keyword like "import" doesn't trip a false positive.
        return top.Value >= 2 ? top.Key : null;
    }

    private static void Score(string text, string language, string[] needles,
        Dictionary<string, int> scores)
    {
        var hits = 0;
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.Ordinal))
                hits++;
        }
        if (hits > 0)
            scores[language] = hits;
    }

    private static bool LooksLikeJson(string trimmed)
    {
        if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
            return false;
        if (!(trimmed.EndsWith("}") || trimmed.EndsWith("]")))
            return false;
        // JSON almost always has a quoted key or bracket-paired value
        return trimmed.Contains(':') && (trimmed.Contains('"') || trimmed.Contains('}'));
    }

    private static bool LooksLikeMarkup(string trimmed)
    {
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("</") || trimmed.StartsWith("<")) {
            // loose check: has a closing tag somewhere
            return Regex.IsMatch(trimmed, @"</[A-Za-z][\w-]*>");
        }
        return false;
    }

    private static bool LooksLikeCss(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("@import", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("@media", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("@charset", StringComparison.OrdinalIgnoreCase))
            return true;
        // selector { property: value; }
        return Regex.IsMatch(text, @"[.#]?[\w\-]+\s*\{[^}]*:\s*[^;}]+;?");
    }

    private static bool LooksLikeYaml(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0) return false;
        // --- doc marker or first line is `key: value`
        if (lines[0].TrimStart().StartsWith("---", StringComparison.Ordinal))
            return true;
        int matched = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("#")) continue;
            if (Regex.IsMatch(line, @"^[ \t]*[\w.\-]+:\s+\S")) matched++;
            else if (Regex.IsMatch(line, @"^[ \t]*-\s+\S")) matched++;
            else return false;
        }
        return matched >= 2;
    }

    private static bool LooksLikeSql(string trimmed)
    {
        return Regex.IsMatch(trimmed,
            @"^\s*(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|TRUNCATE|WITH|MERGE|REPLACE)\b",
            RegexOptions.IgnoreCase);
    }
}
