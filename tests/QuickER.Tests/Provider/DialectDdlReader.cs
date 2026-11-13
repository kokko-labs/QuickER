using System.Text;
using System.Text.RegularExpressions;

namespace QuickER.Tests.Provider;

/// <summary>
/// DDL・同期スクリプトの出力を、行ごとに構造の行へ写す読み取り器（テスト側の独立した読み取り。製品のクォートの関数を使わない）
/// </summary>
/// <remarks>
/// <para>写す形（識別子はアダプタの区切り文字で読み戻した素の名前）：</para>
/// <list type="bullet">
/// <item><description><c>header</c>・<c>generated-at</c>（DDL の先頭のコメント）</description></item>
/// <item><description><c>table {表}</c>・<c>end-table[ {後続句}]</c>（CREATE TABLE の始まりと閉じ括弧）</description></item>
/// <item><description><c>column {表}.{列} {型} {NULL|NOT NULL}[ {後続句}]</c>（表の中の行は、末尾の区切りの <c>,</c> をそのまま残す）</description></item>
/// <item><description>
/// <c>pk {表} {制約名|-} ({列}, …)</c>・<c>uq {表} {制約名|-} ({列}, …)</c>（表を作る文の中）・<c>uq {表} {制約名} ({列}, …) alter</c>
/// （<c>ALTER TABLE … ADD CONSTRAINT … UNIQUE</c> の 1 行の文）
/// </description></item>
/// <item><description>
/// <c>fk {子} {制約名|-} ({列}, …) -&gt; {親} ({列}, …)[ on-delete={句}][ on-update={句}] {inline|alter}</c>
/// </description></item>
/// <item><description><c>note {種類} | {素の行}</c>（注記の種類はこのクラスの分類表で決める）・<c>comment {素の行}</c>（ほかの <c>--</c> の行）</description></item>
/// <item><description><c>text {素の行}</c>（上のどれにも読めない行）</description></item>
/// </list>
/// <para>
/// 表の名前は、最初の <c>.</c> で 2 つに分けてクォートした名前を <c>{スキーマ}.{表}</c>、<c>.</c> を含む 1 つの識別子を
/// <c>{{名前}}</c>（波括弧で囲む）と書き分ける。区切りやエスケープが壊れた識別子は読めず、その行は <c>text</c> のまま残る
/// （構造の期待が赤になる）。
/// </para>
/// </remarks>
internal static class DialectDdlReader
{
    /// <summary>注記の種類の分類表（行の頭の文。英語の文面で分け、製品の定数を使わない）</summary>
    private static readonly (string Kind, Regex Pattern)[] NoteKinds =
    [
        ("many-to-many", Prefix("-- Many-to-many (")),
        ("set-null-not-null", Prefix("-- Note: SET NULL on the NOT NULL referencing column(s)")),
        ("set-default-not-null", Prefix("-- Note: SET DEFAULT needs a DEFAULT constraint")),
        ("set-default-omitted", Prefix("-- Note: MySQL (InnoDB) does not support SET DEFAULT")),
        ("set-default-omitted", Prefix("-- Note: Oracle does not support ON DELETE SET DEFAULT")),
        ("on-update-omitted", Prefix("-- Note: Oracle does not support ON UPDATE")),
        ("dotted-table-name", Prefix("-- Note: this database reads ")),
        ("cross-schema-fk", Prefix("-- Note: this database cannot reference ")),
        (
            "unique-on-primary-key",
            Prefix(
                "-- Note: this database cannot have a unique constraint on the primary key columns"
            )
        ),
        (
            "description-characters",
            Prefix(
                "-- Note: this database stores each character outside the Basic Multilingual Plane"
            )
        ),
        ("auto-increment-removed", Prefix("-- Note: AUTO_INCREMENT on an old primary key")),
        (
            "string-length-mismatch",
            new Regex("^-- Note: .* differ in length", RegexOptions.CultureInvariant)
        ),
        ("computed-column", Prefix("-- Computed column '")),
        ("identity-column", Prefix("-- Identity column '")),
        ("skipped", Prefix("-- Skipped")),
        ("section", Prefix("-- =====")),
    ];

    private static readonly Regex ActionsPattern = new(
        "^(?: ON DELETE (?<delete>.+?))?(?: ON UPDATE (?<update>.+?))?$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex ColumnRest = new(
        "^(?<type>.+?) (?<null>NOT NULL|NULL)(?<suffix>.*)$",
        RegexOptions.CultureInvariant
    );

    /// <summary>出力を構造の行へ写す</summary>
    /// <param name="output">DDL・同期スクリプト</param>
    /// <param name="adapter">方言のアダプタ（区切り文字）</param>
    public static IReadOnlyList<string> Read(string output, DialectDdlAdapter adapter)
    {
        var lines = new List<string>();
        string? table = null;

        foreach (var raw in SplitLines(output))
        {
            var line = raw.Trim();
            var reader = new Cursor(line, adapter);

            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                lines.Add(Comment(line));
                continue;
            }

            if (table is null)
            {
                if (reader.Keyword("CREATE TABLE ") && reader.TableName() is { } created)
                {
                    if (reader.Keyword(" (") && reader.AtEnd)
                    {
                        table = created;
                        lines.Add($"table {created}");
                        continue;
                    }
                }

                reader.Reset();

                if (
                    reader.Keyword("ALTER TABLE ")
                    && reader.TableName() is { } child
                    && reader.Keyword(" ADD ")
                    && ForeignKey(ref reader, child, "alter", requireSemicolon: true) is { } fk
                )
                {
                    lines.Add(fk);
                    continue;
                }

                reader.Reset();

                if (
                    reader.Keyword("ALTER TABLE ")
                    && reader.TableName() is { } owner
                    && reader.Keyword(" ADD CONSTRAINT ")
                    && reader.Identifier() is { } uniqueName
                    && reader.Keyword(" UNIQUE ")
                    && reader.List() is { } uniqueColumns
                    && reader.Keyword(";")
                    && reader.AtEnd
                )
                {
                    lines.Add($"uq {owner} {uniqueName} ({uniqueColumns}) alter");
                    continue;
                }

                lines.Add($"text {line}");
                continue;
            }

            if (line.StartsWith(')'))
            {
                var suffix = line[1..].TrimEnd(';').Trim();
                lines.Add(suffix.Length == 0 ? "end-table" : $"end-table {suffix}");
                table = null;
                continue;
            }

            var comma = line.EndsWith(',') ? "," : "";
            var body = comma.Length > 0 ? line[..^1] : line;
            var cursor = new Cursor(body, adapter);

            if (Constraint(ref cursor, table) is { } constraint)
            {
                lines.Add(constraint + comma);
                continue;
            }

            cursor = new Cursor(body, adapter);

            if (cursor.Identifier() is { } column && cursor.Keyword(" "))
            {
                var match = ColumnRest.Match(cursor.Rest);

                if (match.Success)
                {
                    lines.Add(
                        $"column {table}.{column} {match.Groups["type"].Value} {match.Groups["null"].Value}{match.Groups["suffix"].Value}{comma}"
                    );
                    continue;
                }
            }

            lines.Add($"text {line}");
        }

        return lines;
    }

    /// <summary>出力の素の行（行末の空白を除く・空行を除く）</summary>
    /// <param name="output">DDL・同期スクリプト</param>
    public static IReadOnlyList<string> Text(string output) =>
        SplitLines(output).Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToList();

    private static IEnumerable<string> SplitLines(string output) =>
        output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.Trim().Length > 0);

    private static string Comment(string line)
    {
        foreach (var (kind, pattern) in NoteKinds)
        {
            if (pattern.IsMatch(line))
            {
                return $"note {kind} | {line}";
            }
        }

        return line.StartsWith("-- DDL auto-generated by QuickER", StringComparison.Ordinal)
                ? "header"
            : line.StartsWith("-- Generated at: ", StringComparison.Ordinal) ? "generated-at"
            : $"comment {line}";
    }

    private static string? Constraint(ref Cursor cursor, string table)
    {
        string name = "-";

        if (cursor.Keyword("CONSTRAINT "))
        {
            if (cursor.Identifier() is not { } named || !cursor.Keyword(" "))
            {
                return null;
            }

            name = named;
        }

        var start = cursor.Position;

        if (cursor.Keyword("PRIMARY KEY ") && cursor.List() is { } pk && cursor.AtEnd)
        {
            return $"pk {table} {name} ({pk})";
        }

        cursor.Position = start;

        if (cursor.Keyword("UNIQUE ") && cursor.List() is { } uq && cursor.AtEnd)
        {
            return $"uq {table} {name} ({uq})";
        }

        cursor.Position = start;

        return cursor.Keyword("FOREIGN KEY ")
            ? ForeignKeyBody(ref cursor, table, name, "inline", requireSemicolon: false)
            : null;
    }

    private static string? ForeignKey(
        ref Cursor cursor,
        string child,
        string form,
        bool requireSemicolon
    )
    {
        if (
            !cursor.Keyword("CONSTRAINT ")
            || cursor.Identifier() is not { } name
            || !cursor.Keyword(" FOREIGN KEY ")
        )
        {
            return null;
        }

        return ForeignKeyBody(ref cursor, child, name, form, requireSemicolon);
    }

    private static string? ForeignKeyBody(
        ref Cursor cursor,
        string child,
        string name,
        string form,
        bool requireSemicolon
    )
    {
        if (
            cursor.List() is not { } columns
            || !cursor.Keyword(" REFERENCES ")
            || cursor.TableName() is not { } parent
            || !cursor.Keyword(" ")
            || cursor.List() is not { } referenced
        )
        {
            return null;
        }

        var rest = cursor.Rest;

        if (requireSemicolon)
        {
            if (!rest.EndsWith(';'))
            {
                return null;
            }

            rest = rest[..^1];
        }

        var actions = ActionsPattern.Match(rest);

        if (!actions.Success)
        {
            return null;
        }

        var builder = new StringBuilder(
            $"fk {child} {name} ({columns}) -> {parent} ({referenced})"
        );

        if (actions.Groups["delete"].Success)
        {
            builder.Append(" on-delete=").Append(actions.Groups["delete"].Value);
        }

        if (actions.Groups["update"].Success)
        {
            builder.Append(" on-update=").Append(actions.Groups["update"].Value);
        }

        return builder.Append(' ').Append(form).ToString();
    }

    private static Regex Prefix(string prefix) =>
        new("^" + Regex.Escape(prefix), RegexOptions.CultureInvariant);

    /// <summary>1 行を読み進める位置</summary>
    private struct Cursor(string text, DialectDdlAdapter adapter)
    {
        private readonly string _text = text;
        private readonly string _open = adapter.OpenQuote;
        private readonly string _close = adapter.CloseQuote;

        public int Position { get; set; }

        public readonly bool AtEnd => Position == _text.Length;

        public readonly string Rest => _text[Position..];

        public void Reset() => Position = 0;

        /// <summary>決まった文字列を読む（無ければ位置を動かさず偽）</summary>
        public bool Keyword(string keyword)
        {
            if (string.CompareOrdinal(_text, Position, keyword, 0, keyword.Length) != 0)
            {
                return false;
            }

            Position += keyword.Length;
            return true;
        }

        /// <summary>クォートした識別子を 1 つ読み戻す（読めなければ位置を動かさず null）</summary>
        public string? Identifier()
        {
            var start = Position;

            if (!Keyword(_open))
            {
                return null;
            }

            var builder = new StringBuilder();

            while (Position < _text.Length)
            {
                if (string.CompareOrdinal(_text, Position, _close, 0, _close.Length) == 0)
                {
                    // 終端の文字が 2 つ続けば、識別子の中の 1 文字
                    if (
                        string.CompareOrdinal(
                            _text,
                            Position + _close.Length,
                            _close,
                            0,
                            _close.Length
                        ) == 0
                    )
                    {
                        builder.Append(_close);
                        Position += _close.Length * 2;
                        continue;
                    }

                    Position += _close.Length;
                    return builder.ToString();
                }

                builder.Append(_text[Position]);
                Position++;
            }

            Position = start;
            return null;
        }

        /// <summary>テーブル名（1 つの識別子か、<c>.</c> でつないだ 2 つ）を読む</summary>
        public string? TableName()
        {
            var start = Position;

            if (Identifier() is not { } first)
            {
                return null;
            }

            var afterFirst = Position;

            if (Keyword(".") && Identifier() is { } second)
            {
                return $"{first}.{second}";
            }

            Position = afterFirst;

            if (first.Contains('.', StringComparison.Ordinal))
            {
                return $"{{{first}}}";
            }

            _ = start;
            return first;
        }

        /// <summary>括弧で囲んだ識別子の並び（<c>("a", "b")</c>）を <c>a, b</c> として読む</summary>
        public string? List()
        {
            var start = Position;

            if (!Keyword("("))
            {
                return null;
            }

            var names = new List<string>();

            while (true)
            {
                if (Identifier() is not { } name)
                {
                    Position = start;
                    return null;
                }

                names.Add(name);

                if (Keyword(", "))
                {
                    continue;
                }

                if (Keyword(")"))
                {
                    return string.Join(", ", names);
                }

                Position = start;
                return null;
            }
        }
    }
}
