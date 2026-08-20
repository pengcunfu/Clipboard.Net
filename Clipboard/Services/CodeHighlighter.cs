using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
// Disambiguate against System.Drawing.* which is brought in by the WPF+WinForms combo.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClipboardApp.Services;

/// <summary>
/// Lightweight, regex-based syntax highlighter that renders code as a WPF <see cref="FlowDocument"/>.
/// Supports a curated set of languages with a light theme that matches the rest of the app.
/// </summary>
public static class CodeHighlighter
{
    // Light-theme palette (matches Visual Studio "Light+")
    private static readonly Brush CommentBrush  = Freeze(Brush("#008000")); // green
    private static readonly Brush StringBrush   = Freeze(Brush("#A31515")); // dark red
    private static readonly Brush KeywordBrush  = Freeze(Brush("#0000FF")); // blue
    private static readonly Brush NumberBrush   = Freeze(Brush("#098658")); // dark green
    private static readonly Brush TypeBrush     = Freeze(Brush("#267F99")); // teal
    private static readonly Brush AttributeBrush = Freeze(Brush("#FF8C00")); // dark orange
    private static readonly Brush FunctionBrush = Freeze(Brush("#795E26")); // brown
    private static readonly Brush PunctBrush    = Freeze(Brush("#000000")); // black
    private static readonly Brush TagBrush      = Freeze(Brush("#800000")); // maroon
    private static readonly Brush DefaultBrush  = Freeze(Brush("#000000")); // black

    /// <summary>
    /// Highlight <paramref name="text"/> as <paramref name="language"/>. Falls back to plain text if the
    /// language is null or unsupported.
    /// </summary>
    public static FlowDocument Highlight(string? text, string? language)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            PagePadding = new Thickness(10),
            Background = Brushes.White,
            Foreground = DefaultBrush,
        };

        if (string.IsNullOrEmpty(text))
        {
            doc.Blocks.Add(new Paragraph(new Run(" ")));
            return doc;
        }

        var rules = string.IsNullOrEmpty(language) ? null : GetRules(language);
        if (rules is null || rules.Count == 0)
        {
            doc.Blocks.Add(BuildPlainParagraph(text));
            return doc;
        }

        // Split on \n and preserve blank lines. Tabs become 4 spaces.
        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var para = new Paragraph { Margin = new Thickness(0) };
            if (line.Length == 0)
            {
                // keep empty lines visible
                para.Inlines.Add(new Run(" "));
            }
            else
            {
                EmitLine(para.Inlines, line, rules);
            }
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private static Paragraph BuildPlainParagraph(string text)
    {
        var para = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
        return para;
    }

    private static void EmitLine(InlineCollection inlines, string line, List<Rule> rules)
    {
        // Pair every match with its rule. Earlier rules win on overlap.
        var pairs = new List<(Match M, Rule R)>(64);
        foreach (var rule in rules)
        {
            foreach (Match m in rule.Regex.Matches(line))
            {
                if (m.Length == 0) continue;
                pairs.Add((m, rule));
            }
        }

        if (pairs.Count == 0)
        {
            inlines.Add(new Run(line));
            return;
        }

        // Sort: earlier start first; if equal start, prefer the rule that appears earlier in the list.
        pairs.Sort((a, b) =>
        {
            int c = a.M.Index.CompareTo(b.M.Index);
            if (c != 0) return c;
            // longer match wins on same start (e.g. "string" before "keyword")
            c = b.M.Length.CompareTo(a.M.Length);
            if (c != 0) return c;
            return a.R.Order.CompareTo(b.R.Order);
        });

        // Drop overlaps: keep the first accepted (highest-priority) match.
        var accepted = new List<(Match M, Rule R)>(pairs.Count);
        int lastEnd = 0;
        foreach (var p in pairs)
        {
            if (p.M.Index >= lastEnd)
            {
                accepted.Add(p);
                lastEnd = p.M.Index + p.M.Length;
            }
        }

        // Emit runs
        int pos = 0;
        foreach (var (m, r) in accepted)
        {
            if (m.Index > pos)
                inlines.Add(new Run(line.Substring(pos, m.Index - pos)));
            var run = new Run(line.Substring(m.Index, m.Length))
            {
                Foreground = r.Brush,
            };
            if (r.Bold) run.FontWeight = FontWeights.Bold;
            if (r.Italic) run.FontStyle = FontStyles.Italic;
            inlines.Add(run);
            pos = m.Index + m.Length;
        }
        if (pos < line.Length)
            inlines.Add(new Run(line.Substring(pos)));
    }

    // -- Rule construction --

    private sealed class Rule
    {
        public required Regex Regex { get; init; }
        public required Brush Brush { get; init; }
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public int Order { get; init; }  // smaller = higher priority on tie
    }

    private static List<Rule> GetRules(string language) => language.ToLowerInvariant() switch
    {
        "csharp" or "cs" or "c#"        => CSharpRules(),
        "javascript" or "js"            => JavaScriptRules(),
        "typescript" or "ts"            => TypeScriptRules(),
        "java"                          => JavaRules(),
        "python" or "py"                => PythonRules(),
        "go"                            => GoRules(),
        "rust" or "rs"                  => RustRules(),
        "sql"                           => SqlRules(),
        "json"                          => JsonRules(),
        "xml"                           => XmlRules(),
        "html"                          => HtmlRules(),
        "css"                           => CssRules(),
        "yaml" or "yml"                 => YamlRules(),
        "bash" or "sh" or "shell"       => BashRules(),
        "php"                           => PhpRules(),
        "ruby" or "rb"                  => RubyRules(),
        _ => new List<Rule>(),
    };

    private static List<Rule> CSharpRules() => KeywordRules(
        // C# 12 keyword set
        "abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|" +
        "default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|" +
        "foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|" +
        "operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|" +
        "short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|" +
        "unchecked|unsafe|ushort|using|virtual|void|volatile|while|async|await|var|yield|record|init",
        typeKeywords: "string|object|int|long|short|byte|bool|double|float|decimal|char|List|Dictionary|Array|Task|IEnumerable|StringBuilder|Exception|Console|Math"
    );

    private static List<Rule> JavaScriptRules() => KeywordRules(
        "var|let|const|function|return|if|else|for|while|do|switch|case|break|continue|default|" +
        "new|delete|typeof|instanceof|in|of|this|super|class|extends|static|get|set|async|await|" +
        "yield|import|export|from|as|true|false|null|undefined|try|catch|finally|throw",
        stringRules: new[] {
            (@"""﻿?(\\.|[^""\\\n])*""", StringBrush),
            (@"'(\\.|[^'\\\n])*'", StringBrush),
            (@"`(\\.|[^`\\])*`", StringBrush),
        }
    );

    private static List<Rule> TypeScriptRules() => KeywordRules(
        "var|let|const|function|return|if|else|for|while|do|switch|case|break|continue|default|" +
        "new|delete|typeof|instanceof|in|of|this|super|class|extends|implements|interface|type|" +
        "enum|public|private|protected|readonly|static|get|set|async|await|yield|import|export|" +
        "from|as|true|false|null|undefined|try|catch|finally|throw|namespace|declare|abstract",
        typeKeywords: "string|number|boolean|any|unknown|never|void|object|Array|Promise|Map|Set|Date|RegExp"
    );

    private static List<Rule> JavaRules() => KeywordRules(
        "abstract|assert|boolean|break|byte|case|catch|char|class|const|continue|default|do|double|" +
        "else|enum|extends|final|finally|float|for|goto|if|implements|import|instanceof|int|interface|" +
        "long|native|new|null|package|private|protected|public|return|short|static|strictfp|super|" +
        "switch|synchronized|this|throw|throws|transient|true|try|void|volatile|while|var|record|sealed",
        typeKeywords: "String|Object|Integer|Long|Short|Byte|Boolean|Double|Float|Character|List|Map|Set|ArrayList|HashMap|HashSet|Optional|Stream"
    );

    private static List<Rule> PythonRules() => new()
    {
        // Python triple-quoted strings (use a non-verbatim string so we can write """).
        Make("(?:[fFrRbBuU]|rb|br)?\"\"\"[\\s\\S]*?\"\"\"", StringBrush, italic: true, order: 0),
        Make("(?:[fFrRbBuU]|rb|br)?'''[\\s\\S]*?'''", StringBrush, italic: true, order: 0),
        // Regular strings (double- and single-quoted, with f/r/b/u prefix)
        Make(@"\b(?:[fFrRbBuU])?(?:""[^""\\\n]*(?:\\.[^""\\\n]*)*""|'[^'\\\n]*(?:\\.[^'\\\n]*)*')", StringBrush, order: 1),
        // Comments
        Make(@"#[^\n]*", CommentBrush, italic: true, order: 2),
        // Decorators
        Make(@"@[A-Za-z_][\w.]*", AttributeBrush, order: 3),
        // Numbers
        Make(@"\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", NumberBrush, order: 4),
        // Keywords
        Make(@"\b(False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield|match|case)\b", KeywordBrush, bold: true, order: 5),
        // Builtins
        Make(@"\b(print|len|range|list|dict|set|tuple|str|int|float|bool|open|input|type|isinstance|enumerate|zip|map|filter|sorted|sum|min|max|abs|round|self|cls)\b", TypeBrush, order: 6),
    };

    private static List<Rule> GoRules() => KeywordRules(
        "break|case|chan|const|continue|default|defer|else|fallthrough|for|func|go|goto|if|import|" +
        "interface|map|package|range|return|select|struct|switch|type|var|true|false|nil|iota",
        typeKeywords: "string|int|int8|int16|int32|int64|uint|uint8|uint16|uint32|uint64|byte|rune|" +
                       "float32|float64|bool|error|complex64|complex128|any|comparable"
    );

    private static List<Rule> RustRules() => KeywordRules(
        "as|async|await|break|const|continue|crate|dyn|else|enum|extern|false|fn|for|if|impl|in|let|" +
        "loop|match|mod|move|mut|pub|ref|return|self|Self|static|struct|super|trait|true|type|unsafe|" +
        "use|where|while|box|do|final|macro|override|priv|try|typeof|unsized|virtual|yield",
        typeKeywords: "i8|i16|i32|i64|i128|isize|u8|u16|u32|u64|u128|usize|f32|f64|bool|char|str|String|Vec|Option|Result|Box|Rc|Arc"
    );

    private static List<Rule> SqlRules() => new()
    {
        // Single-quoted strings with '' escape
        Make(@"'[^']*(?:''[^']*)*'", StringBrush, order: 0),
        // Double-quoted identifiers (Postgres / standard SQL)
        Make(@"""[^""]*(?:""""[^""]*)*""", TypeBrush, order: 1),
        // Line comments
        Make(@"--[^\n]*", CommentBrush, italic: true, order: 2),
        // Block comments
        Make(@"/\*[\s\S]*?\*/", CommentBrush, italic: true, order: 2),
        // Numbers
        Make(@"\b\d+(?:\.\d+)?\b", NumberBrush, order: 3),
        // Keywords (case-insensitive at the regex level via (?i) — but we want a stable look; rely on input casing)
        Make(@"(?i)\b(?:SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN|INNER|OUTER|LEFT|RIGHT|FULL|ON|GROUP|BY|ORDER|HAVING|LIMIT|OFFSET|UNION|ALL|AS|AND|OR|NOT|NULL|IS|IN|EXISTS|BETWEEN|LIKE|CREATE|TABLE|INDEX|VIEW|DATABASE|SCHEMA|ALTER|DROP|TRUNCATE|PRIMARY|KEY|FOREIGN|REFERENCES|UNIQUE|CHECK|DEFAULT|IF|CASE|WHEN|THEN|ELSE|END|WITH|RETURNING|INTO|VALUES|DISTINCT|COUNT|SUM|AVG|MAX|MIN)\b", KeywordBrush, bold: true, order: 4),
    };

    private static List<Rule> JsonRules() => new()
    {
        // Strings (keys are the same pattern — we don't differentiate visually here)
        Make(@"""(?:[^""\\]|\\.)*""", StringBrush, order: 0),
        // Numbers
        Make(@"\b-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", NumberBrush, order: 1),
        // Booleans / null
        Make(@"\b(?:true|false|null)\b", KeywordBrush, bold: true, order: 2),
    };

    private static List<Rule> XmlRules() => new()
    {
        // XML comments
        Make(@"<!--[\s\S]*?-->", CommentBrush, italic: true, order: 0),
        // CDATA
        Make(@"<!\[CDATA\[[\s\S]*?\]\]>", StringBrush, order: 0),
        // DOCTYPE / processing instructions
        Make(@"<\?[\s\S]*?\?>", AttributeBrush, order: 1),
        Make(@"<!DOCTYPE[\s\S]*?>", AttributeBrush, order: 1),
        // Opening / closing tag with optional attributes
        Make(@"</?[A-Za-z_][\w\-.]*", TagBrush, order: 2),
        Make(@"/?>", TagBrush, order: 2),
        // Attribute names
        Make(@"\b[A-Za-z_][\w\-.]*(?=\s*=)", AttributeBrush, order: 3),
        // Attribute string values
        Make(@"""\s*[^""]*\s*""", StringBrush, order: 4),
        Make(@"'\s*[^']*\s*'", StringBrush, order: 4),
    };

    private static List<Rule> HtmlRules() => new()
    {
        Make(@"<!--[\s\S]*?-->", CommentBrush, italic: true, order: 0),
        Make(@"<!DOCTYPE[\s\S]*?>", AttributeBrush, order: 1),
        // Tags
        Make(@"</?[A-Za-z][\w\-]*", TagBrush, order: 2),
        Make(@"/?>", TagBrush, order: 2),
        // Attribute names
        Make(@"\b(?:class|id|href|src|style|type|name|value|rel|target|alt|title|data-[A-Za-z\-]+|aria-[A-Za-z\-]+)\b(?=\s*=)", AttributeBrush, order: 3),
        // Attribute values
        Make(@"""[^""]*""", StringBrush, order: 4),
        Make(@"'[^']*'", StringBrush, order: 4),
    };

    private static List<Rule> CssRules() => new()
    {
        // Block comments
        Make(@"/\*[\s\S]*?\*/", CommentBrush, italic: true, order: 0),
        // Strings
        Make(@"""\s*[^""]*\s*""|'\s*[^']*\s*'", StringBrush, order: 1),
        // @-rules
        Make(@"@[A-Za-z\-]+", AttributeBrush, order: 2),
        // Selectors (before `{`)
        Make(@"[^{};\n]+(?=\s*\{)", FunctionBrush, order: 3),
        // Properties (before `:`)
        Make(@"[A-Za-z\-]+(?=\s*:)", TypeBrush, order: 4),
        // Numbers / units
        Make(@"\b\d+(?:\.\d+)?(?:px|em|rem|%|vh|vw|s|ms|deg|fr)?\b", NumberBrush, order: 5),
        // Hex colors
        Make(@"#[0-9A-Fa-f]{3,8}\b", NumberBrush, order: 5),
    };

    private static List<Rule> YamlRules() => new()
    {
        // Comments
        Make(@"#[^\n]*", CommentBrush, italic: true, order: 0),
        // Strings (double- or single-quoted)
        Make(@"""\s*[^""]*\s*""|'\s*[^']*\s*'", StringBrush, order: 1),
        // Numbers / booleans / null
        Make(@"\b\d+(?:\.\d+)?\b", NumberBrush, order: 2),
        Make(@"\b(?:true|false|True|False|TRUE|FALSE|null|Null|NULL|~)\b", KeywordBrush, bold: true, order: 3),
        // Document marker
        Make(@"^---", AttributeBrush, order: 4),
        // List marker
        Make(@"^\s*-\s", AttributeBrush, order: 4),
        // Keys (word before colon)
        Make(@"(?<=^|\s)[A-Za-z_][\w\-.]*(?=\s*:)", TypeBrush, order: 5),
    };

    private static List<Rule> BashRules() => new()
    {
        // Comments
        Make(@"#[^\n]*", CommentBrush, italic: true, order: 0),
        // Double-quoted strings
        Make(@"""[^""\\\n]*(?:\\.[^""\\\n]*)*""", StringBrush, order: 1),
        // Single-quoted strings
        Make(@"'[^'\n]*'", StringBrush, order: 1),
        // $ expansions
        Make(@"\$\{?[\w@#!?*\$]+\}?", FunctionBrush, order: 2),
        // Variables / assignments
        Make(@"^[A-Za-z_][\w]*=", AttributeBrush, order: 3),
        // Keywords
        Make(@"\b(?:if|then|else|elif|fi|case|esac|for|in|do|done|while|until|function|return|break|continue|exit|export|local|read|echo|printf|set|unset|alias|source)\b", KeywordBrush, bold: true, order: 4),
        // Numbers
        Make(@"\b\d+\b", NumberBrush, order: 5),
    };

    private static List<Rule> PhpRules() => new()
    {
        // PHP open tag
        Make(@"<\?php|<\?=", AttributeBrush, order: 0),
        // Single-quoted strings
        Make(@"'[^'\\\n]*(?:\\.[^'\\\n]*)*'", StringBrush, order: 1),
        // Double-quoted strings
        Make(@"""[^""\\\n]*(?:\\.[^""\\\n]*)*""", StringBrush, order: 1),
        // Line comments
        Make(@"//[^\n]*", CommentBrush, italic: true, order: 2),
        Make(@"#[^\n]*", CommentBrush, italic: true, order: 2),
        // Block comments
        Make(@"/\*[\s\S]*?\*/", CommentBrush, italic: true, order: 2),
        // Variables ($name)
        Make(@"\$[A-Za-z_][\w]*", FunctionBrush, order: 3),
        // Numbers
        Make(@"\b\d+(?:\.\d+)?\b", NumberBrush, order: 4),
        // Keywords
        Make(@"\b(?:abstract|and|array|as|break|callable|case|catch|class|clone|const|continue|declare|default|die|do|echo|else|elseif|empty|enddeclare|endfor|endforeach|endif|endswitch|endwhile|eval|exit|extends|final|finally|for|foreach|function|global|goto|if|implements|include|include_once|instanceof|insteadof|interface|isset|list|namespace|new|null|or|print|private|protected|public|require|require_once|return|static|switch|throw|trait|try|unset|use|var|while|xor|yield|true|false)\b", KeywordBrush, bold: true, order: 5),
    };

    private static List<Rule> RubyRules() => new()
    {
        // Comments
        Make(@"#[^\n]*", CommentBrush, italic: true, order: 0),
        // Strings
        Make(@"""[^""\\\n]*(?:\\.[^""\\\n]*)*""", StringBrush, order: 1),
        Make(@"'[^'\\\n]*(?:\\.[^'\\\n]*)*'", StringBrush, order: 1),
        // Symbols
        Make(@":[A-Za-z_][\w]*[?!]?", TypeBrush, order: 2),
        // Instance/class variables
        Make(@"@@?[A-Za-z_][\w]*", FunctionBrush, order: 3),
        // Numbers
        Make(@"\b\d+(?:\.\d+)?\b", NumberBrush, order: 4),
        // Keywords
        Make(@"\b(?:BEGIN|END|alias|and|begin|break|case|class|def|defined?|do|else|elsif|end|ensure|false|for|if|in|module|next|nil|not|or|redo|rescue|retry|return|self|super|then|true|undef|unless|until|when|while|yield)\b", KeywordBrush, bold: true, order: 5),
    };

    // -- Common helpers --

    /// <summary>
    /// Build a rule set for a C-style language: comments, single/double-quoted strings (with escapes),
    /// numbers, keywords, and (optional) type/builtin names.
    /// </summary>
    private static List<Rule> KeywordRules(string keywords, string? typeKeywords = null,
        (string Pattern, Brush Brush)[]? stringRules = null)
    {
        var list = new List<Rule>
        {
            // Multi-line block comment (rare in a per-line scan, but cheap to include)
            Make(@"/\*[\s\S]*?\*/", CommentBrush, italic: true, order: 0),
            // Double-quoted string with escapes
            Make(@"""(?:[^""\\]|\\.)*""", StringBrush, order: 1),
            // Single-quoted string with escapes
            Make(@"'[^'\\\n]*(?:\\.[^'\\\n]*)*'", StringBrush, order: 1),
        };
        if (stringRules is not null)
        {
            foreach (var (pat, brush) in stringRules)
                list.Add(Make(pat, brush, order: 1));
        }
        list.Add(Make(@"//[^\n]*", CommentBrush, italic: true, order: 2));
        list.Add(Make(@"\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", NumberBrush, order: 3));
        list.Add(Make(@"\b(?:" + keywords + @")\b", KeywordBrush, bold: true, order: 4));
        if (!string.IsNullOrEmpty(typeKeywords))
            list.Add(Make(@"\b(?:" + typeKeywords + @")\b", TypeBrush, order: 5));
        return list;
    }

    private static Rule Make(string pattern, Brush brush,
        bool bold = false, bool italic = false, int order = 100)
        => new()
        {
            Regex = new Regex(pattern, RegexOptions.Compiled),
            Brush = brush,
            Bold = bold,
            Italic = italic,
            Order = order,
        };

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Brush Freeze(Brush b)
    {
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}
