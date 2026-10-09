namespace Marshal.Artifacts;

/// <summary>
/// Coarse syntax highlighting for code on display pages: keywords, strings, numbers and comments, by the file
/// extension (the languages the web code viewer highlights). Not a parser; a page needs colors, not exact scopes.
/// </summary>
internal static class CodeHighlighter
{
    private const string CKeywords =
        "auto break case char const continue default do double else enum extern float for goto if inline int long register restrict " +
        "return short signed sizeof static struct switch typedef union unsigned void volatile while bool true false NULL";

    private const string ScriptKeywords =
        "break case catch class const continue debugger default delete do else export extends false finally for function if import in " +
        "instanceof let new null return super switch this throw true try typeof undefined var void while with yield async await of static " +
        "get set from as";

    private static readonly Block _cComment = new("/*", "*/", CodeToken.Comment);
    private static readonly Block _tripleQuote = new("\"\"\"", "\"\"\"", CodeToken.String);

    private static readonly CodeLanguage _csharp = new(
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event " +
        "explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null " +
        "object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string " +
        "struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var async await record " +
        "init required get set when where yield nameof dynamic global partial file with not and or",
        ["//"], [_cComment, _tripleQuote], "\"'");

    private static readonly CodeLanguage _script = new(ScriptKeywords, ["//"], [_cComment, new("`", "`", CodeToken.String)], "\"'");

    private static readonly CodeLanguage _typescript = _script with
    {
        Keywords = Words(ScriptKeywords + " interface type enum implements namespace declare readonly private public protected abstract any " +
            "string number boolean never unknown keyof satisfies"),
    };

    private static readonly CodeLanguage _c = new(CKeywords, ["//"], [_cComment], "\"'");

    private static readonly CodeLanguage _cpp = _c with
    {
        Keywords = Words(CKeywords + " class namespace template typename public private protected virtual override new delete this throw try " +
            "catch using nullptr constexpr noexcept operator friend explicit mutable static_cast dynamic_cast reinterpret_cast const_cast final"),
    };

    private static readonly CodeLanguage _rust = new(
        "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return " +
        "self Self static struct super trait true type unsafe use where while i8 i16 i32 i64 u8 u16 u32 u64 usize isize f32 f64 bool str " +
        "String Option Some None Result Ok Err Vec",
        ["//"], [_cComment], "\"");

    private static readonly CodeLanguage _go = new(
        "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select " +
        "struct switch type var true false nil iota string int int64 float64 bool error byte rune",
        ["//"], [_cComment, new("`", "`", CodeToken.String)], "\"'");

    private static readonly CodeLanguage _java = new(
        "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for " +
        "goto if implements import instanceof int interface long native new null package private protected public return short static " +
        "strictfp super switch synchronized this throw throws transient true false try void volatile while var record sealed permits yield",
        ["//"], [_cComment, _tripleQuote], "\"'");

    private static readonly CodeLanguage _kotlin = new(
        "as break class continue do else false for fun if in interface is null object package return super this throw true try typealias val " +
        "var when while by catch constructor companion data enum final finally get import init internal open override private protected " +
        "public sealed set suspend lateinit",
        ["//"], [_cComment, _tripleQuote], "\"'");

    private static readonly CodeLanguage _swift = new(
        "associatedtype class deinit enum extension func import init inout internal let operator private protocol public static struct " +
        "subscript typealias var break case continue default defer do else fallthrough for guard if in repeat return switch where while as " +
        "catch false is nil rethrows super self Self throw throws true try async await some any",
        ["//"], [_cComment, _tripleQuote], "\"");

    private static readonly CodeLanguage _php = new(
        "abstract and array as break callable case catch class clone const continue declare default do echo else elseif empty extends final " +
        "finally fn for foreach function global goto if implements include instanceof interface isset list match namespace new null or print " +
        "private protected public readonly require return static switch throw trait true false try unset use var while yield",
        ["//", "#"], [_cComment], "\"'", IgnoreCase: true);

    private static readonly CodeLanguage _css = new("", [], [_cComment], "\"'");

    private static readonly CodeLanguage _scss = _css with { LineComments = ["//"] };

    private static readonly CodeLanguage _sql = new(
        "select from where and or not insert into values update set delete create table drop alter index primary key foreign references " +
        "join left right inner outer on group by order having limit offset as distinct null is in like between case when then else end union " +
        "all exists default integer int text varchar boolean true false begin commit rollback view with returning",
        ["--"], [_cComment], "'\"", IgnoreCase: true);

    private static readonly CodeLanguage _bash = new(
        "if then else elif fi for while until do done case esac in function return local export readonly select break continue exit echo " +
        "source set unset shift trap",
        ["#"], [], "\"'", Loose: true);

    private static readonly CodeLanguage _powershell = new(
        "begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach function if in param process " +
        "return switch throw trap try until using var while",
        ["#"], [new("<#", "#>", CodeToken.Comment)], "\"'", IgnoreCase: true, Loose: true);

    private static readonly CodeLanguage _python = new(
        "False None True and as assert async await break class continue def del elif else except finally for from global if import in is " +
        "lambda nonlocal not or pass raise return try while with yield match case self",
        ["#"], [_tripleQuote, new("'''", "'''", CodeToken.String)], "\"'");

    private static readonly CodeLanguage _ruby = new(
        "alias and begin break case class def do else elsif end ensure false for if in module next nil not or redo rescue retry return self " +
        "super then true undef unless until when while yield require",
        ["#"], [], "\"'");

    private static readonly CodeLanguage _yaml = new("true false null yes no on off", ["#"], [], "\"'", Loose: true);

    private static readonly CodeLanguage _json = new("true false null", [], [], "\"");

    private static readonly CodeLanguage _ini = new("true false", ["#", ";"], [], "\"'", Loose: true);

    private static readonly CodeLanguage _xml = new("", [], [new("<!--", "-->", CodeToken.Comment)], "\"'", Loose: true);

    private static readonly CodeLanguage _gcode = new("", [";"], [new("(", ")", CodeToken.Comment)], "", Commands: true);

    private static readonly CodeLanguage _dockerfile = new(
        "from as run cmd label expose env add copy entrypoint volume user workdir arg onbuild stopsignal healthcheck shell",
        ["#"], [], "\"'", IgnoreCase: true, Loose: true);

    /// <summary>Extension → language; the same mapping as the web viewer's <c>highlight.ts</c>.</summary>
    private static readonly Dictionary<string, CodeLanguage> _byExtension = new(StringComparer.Ordinal)
    {
        ["cs"] = _csharp, ["csproj"] = _xml, ["props"] = _xml, ["ts"] = _typescript, ["tsx"] = _typescript, ["js"] = _script,
        ["jsx"] = _script, ["mjs"] = _script, ["cjs"] = _script, ["vue"] = _xml, ["py"] = _python, ["scad"] = _c, ["yaml"] = _yaml,
        ["yml"] = _yaml, ["json"] = _json, ["toml"] = _ini, ["ini"] = _ini, ["cfg"] = _ini, ["conf"] = _ini, ["sh"] = _bash,
        ["bash"] = _bash, ["ps1"] = _powershell, ["c"] = _c, ["h"] = _c, ["cpp"] = _cpp, ["hpp"] = _cpp, ["rs"] = _rust, ["go"] = _go,
        ["java"] = _java, ["kt"] = _kotlin, ["swift"] = _swift, ["rb"] = _ruby, ["php"] = _php, ["sql"] = _sql, ["css"] = _css,
        ["scss"] = _scss, ["xml"] = _xml, ["gcode"] = _gcode, ["dockerfile"] = _dockerfile,
    };

    /// <summary>The language for a file name, or <see langword="null"/> when the text stays plain.</summary>
    public static CodeLanguage? LanguageOf(string name)
    {
        string file = name.ToLowerInvariant();
        string extension = file == "dockerfile" ? file : file.Contains('.', StringComparison.Ordinal) ? file[(file.LastIndexOf('.') + 1)..] : "";
        return _byExtension.GetValueOrDefault(extension);
    }

    /// <summary>Spans per line, sorted by start; a block comment or string left open continues on the next line.</summary>
    public static IReadOnlyList<IReadOnlyList<CodeSpan>> Highlight(CodeLanguage language, IReadOnlyList<string> lines)
    {
        var result = new List<IReadOnlyList<CodeSpan>>(lines.Count);
        Block? open = null;
        foreach (string line in lines)
        {
            var spans = new List<CodeSpan>();
            int index = 0;
            if (open is { } continued)
            {
                index = BlockEnd(line, 0, continued, out bool closed);
                spans.Add(new CodeSpan(0, index, continued.Token));
                open = closed ? null : continued;
            }

            while (index < line.Length && open is null)
                index = Next(language, line, index, spans, ref open);

            result.Add(spans.Where(span => span.Length > 0).ToList());
        }

        return result;
    }

    private static int Next(CodeLanguage language, string line, int index, List<CodeSpan> spans, ref Block? open)
    {
        foreach (Block block in language.Blocks)
        {
            if (!At(line, index, block.Open))
                continue;

            int end = BlockEnd(line, index + block.Open.Length, block, out bool closed);
            spans.Add(new CodeSpan(index, end - index, block.Token));
            if (!closed)
                open = block;
            return end;
        }

        char current = line[index];
        bool boundary = index == 0 || !IsWordChar(line[index - 1]);
        foreach (string marker in language.LineComments)
        {
            if (At(line, index, marker) && (!language.Loose || index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                spans.Add(new CodeSpan(index, line.Length - index, CodeToken.Comment));
                return line.Length;
            }
        }

        if (language.Quotes.Contains(current) && (boundary || !language.Loose))
        {
            int end = StringEnd(line, index);
            spans.Add(new CodeSpan(index, end - index, CodeToken.String));
            return end;
        }

        if (char.IsAsciiDigit(current) && boundary)
        {
            int end = index + 1;
            while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] is '.' or '_'))
                end++;
            spans.Add(new CodeSpan(index, end - index, CodeToken.Number));
            return end;
        }

        if (char.IsLetter(current) || current == '_')
        {
            int end = index + 1;
            while (end < line.Length && (IsWordChar(line[end]) || (language.Commands && line[end] == '.')))
                end++;
            if (IsKeyword(language, line.AsSpan(index, end - index)))
                spans.Add(new CodeSpan(index, end - index, CodeToken.Keyword));
            return end;
        }

        return index + 1;
    }

    private static bool IsKeyword(CodeLanguage language, ReadOnlySpan<char> word) =>
        language.Commands
            ? word.Length > 1 && char.ToUpperInvariant(word[0]) is 'G' or 'M' or 'T' && word[1..].IndexOfAnyExceptInRange('0', '9') < 0
            : language.Keywords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word);

    /// <summary>The index after <paramref name="block"/>'s close marker, or the line length when the block stays open.</summary>
    private static int BlockEnd(string line, int from, Block block, out bool closed)
    {
        int close = line.IndexOf(block.Close, from, StringComparison.Ordinal);
        closed = close >= 0;
        return closed ? close + block.Close.Length : line.Length;
    }

    /// <summary>The index after the closing quote (backslash escapes), or the line length for an unterminated string.</summary>
    private static int StringEnd(string line, int start)
    {
        char quote = line[start];
        for (int index = start + 1; index < line.Length; index++)
        {
            if (line[index] == '\\')
                index++;
            else if (line[index] == quote)
                return index + 1;
        }

        return line.Length;
    }

    private static bool At(string line, int index, string marker) => string.CompareOrdinal(line, index, marker, 0, marker.Length) == 0;

    private static bool IsWordChar(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static HashSet<string> Words(string words, bool ignoreCase = false) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>One colored range of a line.</summary>
    internal readonly record struct CodeSpan(int Start, int Length, CodeToken Token);

    internal enum CodeToken
    {
        Keyword,
        String,
        Number,
        Comment,
    }

    /// <summary>A delimited range that may span lines: a block comment or a multi-line string.</summary>
    internal sealed record Block(string Open, string Close, CodeToken Token);

    /// <summary>
    /// A language family. <c>Loose</c>: quotes open a string and line comments start only after a word boundary
    /// (YAML <c>Marshal's</c>, Bash <c>${#x}</c>). <c>Commands</c>: G-code words like <c>G1</c> are keywords.
    /// </summary>
    internal sealed record CodeLanguage(HashSet<string> Keywords, string[] LineComments, Block[] Blocks, string Quotes, bool Loose = false, bool Commands = false)
    {
        public CodeLanguage(string keywords, string[] lineComments, Block[] blocks, string quotes, bool IgnoreCase = false, bool Loose = false, bool Commands = false)
            : this(Words(keywords, IgnoreCase), lineComments, blocks, quotes, Loose, Commands)
        {
        }
    }
}
