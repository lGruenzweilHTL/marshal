using System.Text.Json;

namespace Marshal.Artifacts.Highlighting;

/// <summary>Highlight classes; the names (<see cref="CodeTokens.Name"/>) are shared with the web highlighter and its CSS.</summary>
internal enum CodeToken
{
    Keyword,
    Type,
    Function,
    String,
    Number,
    Literal,
    Comment,
    Property,
    Meta,
    Tag,
    Variable,
    Bracket1,
    Bracket2,
    Bracket3,
    BracketUnmatched,
}

/// <summary>One highlighted range of a line.</summary>
internal readonly record struct CodeSpan(int Start, int Length, CodeToken Token);

internal static class CodeTokens
{
    /// <summary>The shared name: <c>keyword</c>, …, <c>bracket-1</c>, <c>bracket-unmatched</c>.</summary>
    public static string Name(CodeToken token) => token switch
    {
        CodeToken.Bracket1 => "bracket-1",
        CodeToken.Bracket2 => "bracket-2",
        CodeToken.Bracket3 => "bracket-3",
        CodeToken.BracketUnmatched => "bracket-unmatched",
        _ => token.ToString().ToLowerInvariant(),
    };

    public static CodeToken Parse(string name)
    {
        foreach (CodeToken token in Enum.GetValues<CodeToken>())
        {
            if (Name(token) == name)
                return token;
        }

        throw new ArgumentException($"Unknown token '{name}'.", nameof(name));
    }
}

/// <summary>A block that may span lines: a block comment, a multi-line string or an XML declaration.</summary>
internal sealed record CodeBlock(string Open, string Close, CodeToken Token);

/// <summary>A line starting with <c>Prefix</c> (and ending with <c>Suffix</c>, when set) is one <see cref="CodeToken.Meta"/> range.</summary>
internal sealed record MetaLine(string Prefix, string? Suffix);

/// <summary>One language from <c>languages.json</c>; field meanings in the display-surfaces design, section 6.</summary>
internal sealed class CodeLanguage
{
    public required string Id { get; init; }
    public required string Mode { get; init; }
    public required IReadOnlySet<string> Keywords { get; init; }
    public required IReadOnlySet<string> Types { get; init; }
    public required IReadOnlySet<string> Literals { get; init; }
    public required IReadOnlySet<string> FunctionKeywords { get; init; }
    public required IReadOnlySet<string> TypeKeywords { get; init; }
    public required IReadOnlyList<string> LineComments { get; init; }
    public required IReadOnlyList<CodeBlock> Blocks { get; init; }
    public required string Quotes { get; init; }
    public required string StringPrefixes { get; init; }
    public required string VerbatimPrefixes { get; init; }
    public required string WordChars { get; init; }
    public required string? Keys { get; init; }
    public required IReadOnlyList<MetaLine> MetaLines { get; init; }
    public required IReadOnlyDictionary<string, string> Embedded { get; init; }
    public bool Loose { get; init; }
    public bool PascalTypes { get; init; }
    public bool Calls { get; init; } = true;
    public bool Brackets { get; init; } = true;
    public bool Guides { get; init; } = true;
    public bool Annotations { get; init; }
    public bool Variables { get; init; }
}

/// <summary>The languages from the embedded <c>languages.json</c> (shared with the web viewer), by file name and by alias.</summary>
internal static class CodeLanguages
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Catalog _catalog = Load();

    public static IReadOnlyList<CodeLanguage> All => _catalog.Languages;

    /// <summary>The language of a file (exact name like <c>Dockerfile</c>, else the extension), or <see langword="null"/> for plain text.</summary>
    public static CodeLanguage? ForFile(string name)
    {
        string file = name.ToLowerInvariant();
        file = file[(file.LastIndexOf('/') + 1)..];
        if (_catalog.ByName.TryGetValue(file, out CodeLanguage? named))
            return named;

        int dot = file.LastIndexOf('.');
        return dot < 0 ? null : _catalog.ByExtension.GetValueOrDefault(file[(dot + 1)..]);
    }

    /// <summary>A language by id, alias or extension (Markdown fence info, <c>lang</c> attributes).</summary>
    public static CodeLanguage? ForAlias(string alias)
    {
        string key = alias.Trim().ToLowerInvariant();
        return _catalog.ById.GetValueOrDefault(key) ?? _catalog.ByAlias.GetValueOrDefault(key) ?? _catalog.ByExtension.GetValueOrDefault(key);
    }

    private static Catalog Load()
    {
        using Stream stream = typeof(CodeLanguages).Assembly.GetManifestResourceStream("Marshal.Artifacts.Highlighting.languages.json")
            ?? throw new InvalidOperationException("The embedded language definitions are missing.");
        LanguageFile file = JsonSerializer.Deserialize<LanguageFile>(stream, _json)
            ?? throw new InvalidOperationException("The embedded language definitions are empty.");

        var catalog = new Catalog();
        foreach (LanguageEntry entry in file.Languages)
        {
            StringComparer comparer = entry.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var language = new CodeLanguage
            {
                Id = entry.Id,
                Mode = entry.Mode ?? "code",
                Keywords = Words(entry.Keywords, comparer),
                Types = Words(entry.Types, comparer),
                Literals = Words(entry.Literals, comparer),
                FunctionKeywords = Words(entry.FunctionKeywords, comparer),
                TypeKeywords = Words(entry.TypeKeywords, comparer),
                LineComments = entry.LineComments ?? [],
                Blocks = (entry.Blocks ?? []).Select(block => new CodeBlock(block.Open, block.Close, CodeTokens.Parse(block.Token))).ToList(),
                Quotes = entry.Quotes ?? "",
                StringPrefixes = entry.StringPrefixes ?? "",
                VerbatimPrefixes = entry.VerbatimPrefixes ?? "",
                WordChars = entry.WordChars ?? "",
                Keys = entry.Keys,
                MetaLines = (entry.MetaLines ?? []).Select(meta => new MetaLine(meta.Prefix, meta.Suffix)).ToList(),
                Embedded = entry.Embedded ?? new Dictionary<string, string>(),
                Loose = entry.Loose,
                PascalTypes = entry.PascalTypes,
                Calls = entry.Calls ?? true,
                Brackets = entry.Brackets ?? true,
                Guides = entry.Guides ?? true,
                Annotations = entry.Annotations,
                Variables = entry.Variables,
            };
            catalog.Languages.Add(language);
            catalog.ById[language.Id] = language;
            foreach (string extension in entry.Extensions ?? [])
                catalog.ByExtension[extension] = language;
            foreach (string name in entry.Names ?? [])
                catalog.ByName[name] = language;
            foreach (string alias in entry.Aliases ?? [])
                catalog.ByAlias[alias] = language;
        }

        return catalog;
    }

    private static HashSet<string> Words(string? words, StringComparer comparer) =>
        new((words ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries), comparer);

    private sealed class Catalog
    {
        public List<CodeLanguage> Languages { get; } = [];
        public Dictionary<string, CodeLanguage> ById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CodeLanguage> ByExtension { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CodeLanguage> ByName { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CodeLanguage> ByAlias { get; } = new(StringComparer.Ordinal);
    }

    private sealed record LanguageFile(IReadOnlyList<LanguageEntry> Languages);

    private sealed record LanguageEntry(
        string Id,
        string? Mode,
        string[]? Extensions,
        string[]? Names,
        string[]? Aliases,
        string? Keywords,
        string? Types,
        string? Literals,
        string? FunctionKeywords,
        string? TypeKeywords,
        bool IgnoreCase,
        string[]? LineComments,
        BlockEntry[]? Blocks,
        string? Quotes,
        string? StringPrefixes,
        string? VerbatimPrefixes,
        string? WordChars,
        bool Loose,
        bool PascalTypes,
        bool? Calls,
        bool? Brackets,
        bool? Guides,
        bool Annotations,
        bool Variables,
        string? Keys,
        MetaEntry[]? MetaLines,
        Dictionary<string, string>? Embedded);

    private sealed record BlockEntry(string Open, string Close, string Token);

    private sealed record MetaEntry(string Prefix, string? Suffix);
}
