# Spec Delta

## Purpose

One syntax highlighter for code, diffs and Markdown, implemented twice (C# for display pages, TypeScript for the web viewer) from shared language definitions, so a file looks the same on the TV and in the browser.

## ADDED Requirements

### Requirement: Shared language definitions
The languages SHALL be defined once in a data file (`web/src/artifacts/highlighting/languages.json`) that both implementations load: per language its id, file extensions (and exact file names such as `Dockerfile`), keywords, literals, built-in types, definition keywords, comment markers, block delimiters, quotes and string prefixes, and the rules it uses (keys, tags, CSS, variables, meta lines, annotations, type naming, calls, brackets, indent guides). The language SHALL be chosen from the file name; an unknown extension SHALL leave the text plain. The definitions SHALL cover at least C#, TypeScript, JavaScript, Vue and XML (including `.csproj` and `.props`), Python, OpenSCAD, YAML, JSON, TOML and INI, Bash, PowerShell, C, C++, Rust, Go, Java, Kotlin, Swift, Ruby, PHP, SQL, CSS, SCSS, G-code, Dockerfile and Markdown.

#### Scenario: Language by name
- **WHEN** an artifact is named `Dockerfile`
- **THEN** both implementations highlight it as Dockerfile

#### Scenario: Unknown extension stays plain
- **WHEN** a `.txt` artifact containing `if "x"` is highlighted
- **THEN** the line has no highlighted range

### Requirement: Token classes
The highlighter SHALL split each line into ranges of these classes, everything else being plain text: `keyword`, `type`, `function`, `string`, `number`, `literal` (`true`, `false`, `null` and their equivalents), `comment`, `property` (object keys, attributes, CSS properties), `meta` (preprocessor lines, annotations, attributes, Markdown markers), `tag` (XML tag names, CSS selectors), `variable` (`$name` in shells and PHP), `bracket-1`, `bracket-2`, `bracket-3` and `bracket-unmatched`. It SHALL work line by line and carry open block comments, multi-line strings, open XML tags, embedded languages and open brackets to the next line. It SHALL NOT parse the language; misclassification of unusual code is acceptable.

#### Scenario: Keyword, string and comment
- **WHEN** a `.cs` line `var name = "Marshal"; // greeting` is highlighted
- **THEN** `var` is a `keyword`, `"Marshal"` a `string` and `// greeting` a `comment`

#### Scenario: Comment across lines
- **WHEN** a `.ts` file has a `/* … */` comment spanning three lines
- **THEN** all three lines are `comment` ranges

### Requirement: Functions and types
The highlighter SHALL mark as `function` a name directly followed (optionally after spaces) by `(`, and a name following a function definition keyword (`def`, `fn`, `func`, `fun`, `function`); as `type` a name following a type definition keyword (`class`, `struct`, `interface`, `enum`, `record`, `trait`, `type`, `new`, …), a built-in type name of the language, and, in languages whose types are PascalCase by convention, a capitalized name that contains a lowercase letter, does not follow a `.` and is not followed by `(`, `{`, `=` or `;` (those are calls, members and properties). Keywords SHALL never be marked as functions.

#### Scenario: Call and type in C#
- **WHEN** a `.cs` line `Console.WriteLine(new Greeter());` is highlighted
- **THEN** `Console` is a `type`, `WriteLine` a `function`, `new` a `keyword` and `Greeter` a `type`

#### Scenario: Python definition
- **WHEN** a `.py` line `def greet(name):` is highlighted
- **THEN** `def` is a `keyword` and `greet` a `function`

### Requirement: Bracket pair colors
In languages with brackets enabled, `(`, `[` and `{` SHALL be colored by nesting depth, cycling through `bracket-1`, `bracket-2` and `bracket-3`, with the closing bracket getting the color of its opening bracket. A closing bracket that does not match the innermost open bracket SHALL be `bracket-unmatched`. Brackets inside strings and comments SHALL be ignored.

#### Scenario: Nested brackets
- **WHEN** a `.ts` line `f(a[0], { b: 1 })` is highlighted
- **THEN** `(` and `)` are `bracket-1`, `[`, `]`, `{` and `}` are `bracket-2`

#### Scenario: Unmatched bracket
- **WHEN** a `.cs` line `x = (1]` is highlighted
- **THEN** `]` is `bracket-unmatched`

### Requirement: Indent guides
For code, the highlighter SHALL compute indent guides: the indent unit of the file (the most common positive indent step between consecutive non-blank lines, 4 when there is none, between 2 and 8), and per line the number of guides (its indent divided by the unit, rounded up). A blank line SHALL get the smaller guide count of its nearest non-blank neighbors. Tabs SHALL count as four spaces. Diffs and Markdown SHALL have no indent guides.

#### Scenario: Two-space YAML
- **WHEN** a `.yaml` file has lines indented by 0, 2 and 4 spaces
- **THEN** the unit is 2 and the lines get 0, 1 and 2 guides

### Requirement: Diff highlighting
In a unified diff, the highlighter SHALL take the language of each file from its `+++ b/<path>` header (or `diff --git` line) and highlight the code after the `+`, `-` or space marker in that language, tracking removed and context lines and added and context lines as two separate states. Header and hunk lines SHALL stay unhighlighted. A diff for a file without a known language SHALL show only the diff markers' colors.

#### Scenario: Highlighted change
- **WHEN** a diff of `src/App.cs` adds the line `+var x = 1;`
- **THEN** the row is an added row and `var` is a `keyword` and `1` a `number`, offset by the marker

### Requirement: Markdown highlighting
Markdown source SHALL be highlighted: headings as `keyword`, list markers, horizontal rules and fence lines as `meta`, block quotes as `comment`, inline code as `string`, link targets as `property`, and fenced code blocks in the language named after the fence (plain when unknown).

#### Scenario: Fenced block
- **WHEN** a `.md` file contains a fence ```` ```python ```` followed by `def run():`
- **THEN** the fence line is `meta` and `run` in the next line is a `function`

### Requirement: Parity between implementations
The C# and TypeScript implementations SHALL produce the same ranges for the same input. A shared case file (`web/src/artifacts/highlighting/cases.json`) of inputs and expected ranges, covering every token class, rule and language family, SHALL be run by the test suites of both implementations. The token colors of the dark theme SHALL be defined as `--syntax-*` variables in the web theme, and the display pages SHALL use the same values, checked by a test.

#### Scenario: Same ranges on both sides
- **WHEN** a case from the shared case file is run by the .NET tests and by the web tests
- **THEN** both produce exactly the expected ranges
