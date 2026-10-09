namespace Marshal.Artifacts.Highlighting;

/// <summary>
/// What a highlighter carries from one line to the next: an open block, open brackets, an open XML tag, an embedded
/// language (Vue <c>&lt;script&gt;</c>) or a Markdown fence, each embedded language with its own state.
/// </summary>
internal sealed class HighlightState
{
    public CodeBlock? Block { get; set; }
    public List<char> Brackets { get; } = [];
    public bool InTag { get; set; }
    public string? TagName { get; set; }
    public bool ClosingTag { get; set; }
    public string? LastAttribute { get; set; }
    public string? LangAttribute { get; set; }
    public (CodeLanguage Language, HighlightState State, string CloseTag)? Embedded { get; set; }
    public (CodeLanguage? Language, HighlightState State, string Marker)? Fence { get; set; }
}

/// <summary>
/// Heuristic syntax highlighter driven by <see cref="CodeLanguages"/>. A port of (and kept in step with)
/// <c>web/src/artifacts/highlighting/highlighter.ts</c>; both run the shared <c>cases.json</c>. Not a parser.
/// </summary>
internal static class CodeHighlighter
{
    /// <summary>Spans per line, sorted by start.</summary>
    public static IReadOnlyList<IReadOnlyList<CodeSpan>> Highlight(CodeLanguage language, IReadOnlyList<string> lines)
    {
        var state = new HighlightState();
        return lines.Select(line => HighlightLine(language, line, state)).ToList();
    }

    /// <summary>Spans of one line; <paramref name="state"/> carries over to the next line.</summary>
    public static IReadOnlyList<CodeSpan> HighlightLine(CodeLanguage language, string line, HighlightState state)
    {
        var spans = new List<CodeSpan>();
        Line(language, line, 0, state, spans);
        return spans.Where(span => span.Length > 0).OrderBy(span => span.Start).ToList();
    }

    private static void Line(CodeLanguage language, string line, int from, HighlightState state, List<CodeSpan> spans)
    {
        switch (language.Mode)
        {
            case "markdown":
                Markdown(line, state, spans);
                break;
            case "xml":
                Xml(language, line, from, state, spans);
                break;
            default:
                Code(language, line, from, state, spans);
                break;
        }
    }

    private static void Code(CodeLanguage language, string line, int from, HighlightState state, List<CodeSpan> spans)
    {
        int index = from;
        if (state.Block is { } open)
        {
            index = BlockEnd(line, from, open, out bool closed);
            spans.Add(new CodeSpan(from, index - from, open.Token));
            if (!closed)
                return;
            state.Block = null;
        }
        else if (from == 0 && WholeLine(language, line, spans))
        {
            return;
        }

        var scanner = new Scanner(language, line, state, spans);
        while (index < line.Length)
            index = scanner.Step(index);
    }

    /// <summary>Meta lines (<c>#include</c>, <c>[Fact]</c>) and INI sections, which color the whole trimmed line.</summary>
    private static bool WholeLine(CodeLanguage language, string line, List<CodeSpan> spans)
    {
        int first = FirstNonSpace(line);
        if (first == line.Length)
            return false;

        int end = line.Length;
        while (IsSpace(line[end - 1]))
            end--;
        string trimmed = line[first..end];
        foreach (MetaLine meta in language.MetaLines)
        {
            if (trimmed.StartsWith(meta.Prefix, StringComparison.Ordinal) && (meta.Suffix is null || trimmed.EndsWith(meta.Suffix, StringComparison.Ordinal)))
            {
                spans.Add(new CodeSpan(first, end - first, CodeToken.Meta));
                return true;
            }
        }

        if (language.Keys == "ini" && trimmed.Length > 1 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            spans.Add(new CodeSpan(first, end - first, CodeToken.Type));
            return true;
        }

        return false;
    }

    /// <summary>The scan of one code line; <see cref="Step"/> consumes one token or character and returns the next index.</summary>
    private sealed class Scanner(CodeLanguage language, string line, HighlightState state, List<CodeSpan> spans)
    {
        private readonly int _selectorEnd = language.Mode == "css" ? SelectorEnd(line) : -1;
        private CodeToken? _pending;

        public int Step(int index)
        {
            char current = line[index];
            bool boundary = index == 0 || !IsWordChar(line[index - 1]);
            if (!IsSpace(current) && !IsWordStart(current))
                _pending = null;

            foreach (CodeBlock block in language.Blocks)
            {
                if (At(line, index, block.Open))
                    return OpenBlock(index, index, block);
            }

            foreach (string marker in language.LineComments)
            {
                if (At(line, index, marker) && (!language.Loose || index == 0 || IsSpace(line[index - 1])))
                    return Add(index, line.Length, CodeToken.Comment);
            }

            if (boundary && language.StringPrefixes.Contains(current, StringComparison.Ordinal))
            {
                int quote = index;
                while (quote < line.Length && quote - index < 3 && language.StringPrefixes.Contains(line[quote], StringComparison.Ordinal))
                    quote++;
                if (quote < line.Length)
                {
                    foreach (CodeBlock block in language.Blocks)
                    {
                        if (block.Token == CodeToken.String && At(line, quote, block.Open))
                            return OpenBlock(index, quote, block);
                    }

                    if (language.Quotes.Contains(line[quote], StringComparison.Ordinal))
                    {
                        bool verbatim = line[index..quote].Any(prefix => language.VerbatimPrefixes.Contains(prefix, StringComparison.Ordinal));
                        return String(index, quote, verbatim);
                    }
                }
            }

            if (language.Quotes.Contains(current, StringComparison.Ordinal) && (!language.Loose || boundary))
                return String(index, index, verbatim: false);

            if (language.Variables && current == '$' && index + 1 < line.Length)
            {
                char next = line[index + 1];
                if (next == '{')
                {
                    int close = line.IndexOf('}', index + 2);
                    return Add(index, close < 0 ? line.Length : close + 1, CodeToken.Variable);
                }

                if (IsWordStart(next))
                    return Add(index, BaseWordEnd(index + 1), CodeToken.Variable);
                if (char.IsAsciiDigit(next) || "@?#*!$".Contains(next, StringComparison.Ordinal))
                    return Add(index, index + 2, CodeToken.Variable);
            }

            if (language.Annotations && current == '@' && index + 1 < line.Length && char.IsLetter(line[index + 1]))
            {
                int end = index + 1;
                while (end < line.Length && (IsWordChar(line[end]) || line[end] == '.' || language.WordChars.Contains(line[end], StringComparison.Ordinal)))
                    end++;
                return Add(index, end, CodeToken.Meta);
            }

            if (language.Mode == "css")
            {
                bool selector = index < _selectorEnd;
                if (selector && current is '.' or '#' or ':' && index + 1 < line.Length && (IsWordStart(line[index + 1]) || line[index + 1] == '-'))
                    return Add(index, WordEnd(index + 1), CodeToken.Tag);
                if (!selector && current == '#' && index + 1 < line.Length && char.IsAsciiHexDigit(line[index + 1]))
                {
                    int end = index + 1;
                    while (end < line.Length && char.IsAsciiLetterOrDigit(line[end]))
                        end++;
                    return Add(index, end, CodeToken.Number);
                }
            }

            if (char.IsAsciiDigit(current) && boundary)
                return Add(index, NumberEnd(index), CodeToken.Number);

            if (IsWordStart(current))
                return language.Mode == "gcode" ? GCodeWord(index) : Word(index);

            if (language.Brackets && current is '(' or '[' or '{')
            {
                CodeToken token = Bracket(state.Brackets.Count);
                state.Brackets.Add(current);
                return Add(index, index + 1, token);
            }

            if (language.Brackets && current is ')' or ']' or '}')
            {
                char opening = current switch { ')' => '(', ']' => '[', _ => '{' };
                if (state.Brackets.Count > 0 && state.Brackets[^1] == opening)
                {
                    state.Brackets.RemoveAt(state.Brackets.Count - 1);
                    return Add(index, index + 1, Bracket(state.Brackets.Count));
                }

                return Add(index, index + 1, CodeToken.BracketUnmatched);
            }

            return index + 1;
        }

        private int OpenBlock(int start, int at, CodeBlock block)
        {
            int end = BlockEnd(line, at + block.Open.Length, block, out bool closed);
            if (!closed)
                state.Block = block;
            return Add(start, end, block.Token);
        }

        private int String(int start, int quote, bool verbatim)
        {
            int end = StringEnd(line, quote, verbatim);
            CodeToken token = CodeToken.String;
            if (language.Keys is "json" or "yaml")
            {
                int next = SkipSpaces(line, end);
                if (next < line.Length && line[next] == ':')
                    token = CodeToken.Property;
            }

            return Add(start, end, token);
        }

        private int GCodeWord(int index)
        {
            int end = index + 1;
            if (char.ToUpperInvariant(line[index]) is 'G' or 'M' or 'T' && end < line.Length && char.IsAsciiDigit(line[end]))
            {
                while (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] == '.'))
                    end++;
                return Add(index, end, CodeToken.Keyword);
            }

            if (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] is '-' or '.'))
            {
                int number = end;
                while (end < line.Length && (char.IsAsciiDigit(line[end]) || line[end] is '-' or '.'))
                    end++;
                spans.Add(new CodeSpan(number, end - number, CodeToken.Number));
                return end;
            }

            return BaseWordEnd(index);
        }

        private int Word(int start)
        {
            int end = WordEnd(start);
            string word = line[start..end];
            int next = SkipSpaces(line, end);
            char after = next < line.Length ? line[next] : '\0';

            if (language.Mode == "css" && start < _selectorEnd)
                return Add(start, end, CodeToken.Tag);

            bool colonKey = after == ':' && (language.Keys != "yaml" || next + 1 == line.Length || IsSpace(line[next + 1]));
            if ((language.Keys is "yaml" or "json" || language.Mode == "css") && colonKey)
                return Add(start, end, CodeToken.Property);
            if (language.Keys == "ini" && start == FirstNonSpace(line) && after is '=' or ':')
                return Add(start, end, CodeToken.Property);

            if (language.Keywords.Contains(word))
            {
                _pending = language.FunctionKeywords.Contains(word) ? CodeToken.Function : language.TypeKeywords.Contains(word) ? CodeToken.Type : null;
                return Add(start, end, CodeToken.Keyword);
            }

            if (language.Literals.Contains(word))
            {
                _pending = null;
                return Add(start, end, CodeToken.Literal);
            }

            if (_pending is { } pending)
            {
                _pending = null;
                return Add(start, end, pending);
            }

            if (language.Calls)
            {
                if (end + 1 < line.Length && line[end] == '!' && line[end + 1] is '(' or '[')
                    return Add(start, end + 1, CodeToken.Function);
                if (after == '(')
                    return Add(start, end, CodeToken.Function);
            }

            if (language.Types.Contains(word))
                return Add(start, end, CodeToken.Type);

            // A capitalized name before `{`, `=` or `;` is a property or member being declared or assigned, not a type.
            if (language.PascalTypes && char.IsUpper(word[0]) && word.Any(char.IsLower) && (start == 0 || line[start - 1] != '.') && after is not ('{' or '=' or ';'))
                return Add(start, end, CodeToken.Type);

            return end;
        }

        private int Add(int start, int end, CodeToken token)
        {
            spans.Add(new CodeSpan(start, end - start, token));
            return end;
        }

        private int WordEnd(int start)
        {
            int end = start + 1;
            while (end < line.Length && (IsWordChar(line[end]) || language.WordChars.Contains(line[end], StringComparison.Ordinal)))
                end++;
            return end;
        }

        private int BaseWordEnd(int start)
        {
            int end = start + 1;
            while (end < line.Length && IsWordChar(line[end]))
                end++;
            return end;
        }

        private int NumberEnd(int start)
        {
            int end = start + 1;
            while (end < line.Length)
            {
                if (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '_')
                    end++;
                else if (line[end] == '.' && end + 1 < line.Length && char.IsAsciiDigit(line[end + 1]))
                    end++;
                else
                    break;
            }

            return end;
        }

        /// <summary>CSS: the selector part of a line, up to <c>{</c>, or the whole line when it ends a selector list with <c>,</c>.</summary>
        private static int SelectorEnd(string line)
        {
            int brace = line.IndexOf('{');
            if (brace >= 0)
                return brace;
            return line.TrimEnd().EndsWith(',') ? line.Length : -1;
        }
    }

    private static void Xml(CodeLanguage language, string line, int from, HighlightState state, List<CodeSpan> spans)
    {
        int index = from;
        while (index < line.Length)
        {
            if (state.Embedded is { } embedded)
            {
                int close = line.IndexOf("</" + embedded.CloseTag, index, StringComparison.OrdinalIgnoreCase);
                int end = close < 0 ? line.Length : close;
                if (end > index)
                    Line(embedded.Language, line[..end], index, embedded.State, spans);
                if (close < 0)
                    return;
                state.Embedded = null;
                index = close;
                continue;
            }

            if (state.Block is { } open)
            {
                int end = BlockEnd(line, index, open, out bool closed);
                spans.Add(new CodeSpan(index, end - index, open.Token));
                if (!closed)
                    return;
                state.Block = null;
                index = end;
                continue;
            }

            index = state.InTag ? TagStep(language, line, index, state, spans) : TextStep(language, line, index, state, spans);
        }
    }

    private static int TextStep(CodeLanguage language, string line, int index, HighlightState state, List<CodeSpan> spans)
    {
        foreach (CodeBlock block in language.Blocks)
        {
            if (At(line, index, block.Open))
            {
                int end = BlockEnd(line, index + block.Open.Length, block, out bool closed);
                spans.Add(new CodeSpan(index, end - index, block.Token));
                if (!closed)
                    state.Block = block;
                return end;
            }
        }

        char current = line[index];
        if (current == '<' && index + 1 < line.Length)
        {
            bool closing = line[index + 1] == '/';
            int name = index + (closing ? 2 : 1);
            if (name < line.Length && IsWordStart(line[name]))
            {
                int end = name + 1;
                while (end < line.Length && (IsWordChar(line[end]) || language.WordChars.Contains(line[end], StringComparison.Ordinal)))
                    end++;
                spans.Add(new CodeSpan(name, end - name, CodeToken.Tag));
                state.InTag = true;
                state.TagName = line[name..end].ToLowerInvariant();
                state.ClosingTag = closing;
                state.LastAttribute = null;
                state.LangAttribute = null;
                return end;
            }
        }

        if (current == '&')
        {
            int semicolon = line.IndexOf(';', index + 1);
            if (semicolon > index + 1 && semicolon - index <= 10 && line[(index + 1)..semicolon].All(character => char.IsAsciiLetterOrDigit(character) || character == '#'))
            {
                spans.Add(new CodeSpan(index, semicolon + 1 - index, CodeToken.Literal));
                return semicolon + 1;
            }
        }

        return index + 1;
    }

    private static int TagStep(CodeLanguage language, string line, int index, HighlightState state, List<CodeSpan> spans)
    {
        char current = line[index];
        if (current == '>')
        {
            state.InTag = false;
            bool selfClosing = index > 0 && line[index - 1] == '/';
            if (!state.ClosingTag && !selfClosing && state.TagName is { } tag && language.Embedded.TryGetValue(tag, out string? fallback))
            {
                CodeLanguage? embedded = (state.LangAttribute is { } lang ? CodeLanguages.ForAlias(lang) : null) ?? CodeLanguages.ForAlias(fallback);
                if (embedded is not null)
                    state.Embedded = (embedded, new HighlightState(), tag);
            }

            return index + 1;
        }

        if (language.Quotes.Contains(current, StringComparison.Ordinal))
        {
            int close = line.IndexOf(current, index + 1);
            int end = close < 0 ? line.Length : close + 1;
            spans.Add(new CodeSpan(index, end - index, CodeToken.String));
            if (state.LastAttribute == "lang")
                state.LangAttribute = line[(index + 1)..(close < 0 ? line.Length : close)];
            state.LastAttribute = null;
            return end;
        }

        if (IsWordStart(current))
        {
            int end = index + 1;
            while (end < line.Length && (IsWordChar(line[end]) || language.WordChars.Contains(line[end], StringComparison.Ordinal)))
                end++;
            spans.Add(new CodeSpan(index, end - index, CodeToken.Property));
            state.LastAttribute = line[index..end].ToLowerInvariant();
            return end;
        }

        return index + 1;
    }

    private static void Markdown(string line, HighlightState state, List<CodeSpan> spans)
    {
        int first = FirstNonSpace(line);
        int end = line.Length;
        while (end > first && IsSpace(line[end - 1]))
            end--;
        string trimmed = line[first..end];

        if (state.Fence is { } fence)
        {
            if (trimmed.StartsWith(fence.Marker, StringComparison.Ordinal))
            {
                spans.Add(new CodeSpan(first, end - first, CodeToken.Meta));
                state.Fence = null;
            }
            else if (fence.Language is { } language)
            {
                Line(language, line, 0, fence.State, spans);
            }

            return;
        }

        if (trimmed.Length == 0)
            return;

        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            string info = trimmed[3..].TrimStart('`', '~').Trim();
            int stop = info.IndexOfAny([' ', '{']);
            info = stop < 0 ? info : info[..stop];
            state.Fence = (info.Length == 0 ? null : CodeLanguages.ForAlias(info), new HighlightState(), trimmed[..3]);
            spans.Add(new CodeSpan(first, end - first, CodeToken.Meta));
            return;
        }

        if (trimmed[0] == '#')
        {
            int level = trimmed.TakeWhile(character => character == '#').Count();
            if (level <= 6 && (level == trimmed.Length || trimmed[level] == ' '))
            {
                spans.Add(new CodeSpan(first, end - first, CodeToken.Keyword));
                return;
            }
        }

        if (trimmed[0] == '>')
        {
            spans.Add(new CodeSpan(first, end - first, CodeToken.Comment));
            return;
        }

        if (IsRule(trimmed))
        {
            spans.Add(new CodeSpan(first, end - first, CodeToken.Meta));
            return;
        }

        int index = first;
        if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            spans.Add(new CodeSpan(first, 1, CodeToken.Meta));
            index = first + 2;
        }
        else
        {
            int digits = trimmed.TakeWhile(char.IsAsciiDigit).Count();
            if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
            {
                spans.Add(new CodeSpan(first, digits + 1, CodeToken.Meta));
                index = first + digits + 2;
            }
        }

        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                int run = 1;
                while (index + run < line.Length && line[index + run] == '`')
                    run++;
                int close = line.IndexOf(new string('`', run), index + run, StringComparison.Ordinal);
                if (close < 0)
                {
                    index += run;
                    continue;
                }

                spans.Add(new CodeSpan(index, close + run - index, CodeToken.String));
                index = close + run;
                continue;
            }

            if (line[index] == ']' && index + 1 < line.Length && line[index + 1] == '(')
            {
                int close = line.IndexOf(')', index + 2);
                if (close >= 0)
                {
                    spans.Add(new CodeSpan(index + 2, close - index - 2, CodeToken.Property));
                    index = close + 1;
                    continue;
                }
            }

            index++;
        }
    }

    /// <summary>A Markdown thematic break: three or more of the same <c>-</c>, <c>*</c> or <c>_</c>, spaces allowed.</summary>
    private static bool IsRule(string trimmed)
    {
        char marker = trimmed[0];
        if (marker is not ('-' or '*' or '_'))
            return false;
        int count = 0;
        foreach (char character in trimmed)
        {
            if (character == marker)
                count++;
            else if (character != ' ')
                return false;
        }

        return count >= 3;
    }

    private static CodeToken Bracket(int depth) => (depth % 3) switch
    {
        0 => CodeToken.Bracket1,
        1 => CodeToken.Bracket2,
        _ => CodeToken.Bracket3,
    };

    /// <summary>The index after <paramref name="block"/>'s close marker, or the line length when it stays open.</summary>
    private static int BlockEnd(string line, int from, CodeBlock block, out bool closed)
    {
        int close = from <= line.Length ? line.IndexOf(block.Close, from, StringComparison.Ordinal) : -1;
        closed = close >= 0;
        return closed ? close + block.Close.Length : line.Length;
    }

    /// <summary>The index after the closing quote, or the line length for an unterminated string.</summary>
    private static int StringEnd(string line, int quoteIndex, bool verbatim)
    {
        char quote = line[quoteIndex];
        for (int index = quoteIndex + 1; index < line.Length; index++)
        {
            if (!verbatim && line[index] == '\\')
            {
                index++;
            }
            else if (line[index] == quote)
            {
                if (verbatim && index + 1 < line.Length && line[index + 1] == quote)
                    index++;
                else
                    return index + 1;
            }
        }

        return line.Length;
    }

    private static bool At(string line, int index, string marker) =>
        index + marker.Length <= line.Length && string.CompareOrdinal(line, index, marker, 0, marker.Length) == 0;

    private static int SkipSpaces(string line, int index)
    {
        while (index < line.Length && IsSpace(line[index]))
            index++;
        return index;
    }

    private static int FirstNonSpace(string line) => SkipSpaces(line, 0);

    private static bool IsSpace(char character) => character is ' ' or '\t';

    private static bool IsWordStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsWordChar(char character) => char.IsLetter(character) || char.IsAsciiDigit(character) || character == '_';
}
