using System.IO;
using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// DBML (Database Markup Language) テキストを解析して ER 図モデルへ変換するインポーター
/// </summary>
/// <remarks>
/// 対応する記法は <see cref="DbmlExporter"/> の出力と往復可能な範囲に限定する
/// <list type="bullet">
///   <item><c>Table 名前 {</c> 〜 <c>}</c> ブロック（1 行 1 カラム定義）</item>
///   <item>カラム設定: <c>pk</c> / <c>ref</c> / <c>null</c> / <c>not null</c> / <c>note: '...'</c>（大文字小文字を区別しない）</item>
///   <item><c>Ref:</c> 行: 多重度記号は <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&lt;&gt;</c>（多対多）のみ。<c>&gt;</c>（多対1）は未対応</item>
///   <item><c>//</c> 行コメント</item>
/// </list>
/// Project・Enum・Indexes・TableGroup・複数行 Note ブロック等の DBML 構文は未対応
/// </remarks>
public static partial class DbmlImporter
{
    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行を検出する正規表現</summary>
    private static readonly Regex TableHeaderRegex = TableHeaderLineRegex();

    /// <summary><c>Ref:</c> 行を解析する正規表現</summary>
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c> を解析する正規表現</summary>
    private static readonly Regex NoteRegex = ColumnNoteRegex();

    /// <summary>
    /// DBML ファイルを読み込み ER 図へ変換する
    /// </summary>
    public static ErDiagram Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// DBML テキストを解析して ER 図を生成する
    /// </summary>
    /// <returns>復元した <see cref="ErDiagram"/></returns>
    /// <exception cref="InvalidDataException">構文不正・テーブル名重複・未定義テーブル参照などを検出した場合</exception>
    public static ErDiagram Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(Strings.Dbml_EmptyText);
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<Relationship>();
        Entity? currentEntity = null;

        // 行単位の状態機械: currentEntity が非 null の間は Table ブロック内としてカラム行を解釈する
        foreach (var rawLine in lines)
        {
            var line = RemoveComment(rawLine).Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (currentEntity is not null)
            {
                if (line == "}")
                {
                    currentEntity = null;
                    continue;
                }

                currentEntity.Columns.Add(ParseColumn(line, currentEntity.TableName));
                continue;
            }

            var tableMatch = TableHeaderRegex.Match(line);

            if (tableMatch.Success)
            {
                var tableName = tableMatch.Groups["table"].Value;

                if (!entities.TryAdd(tableName, new Entity { TableName = tableName }))
                {
                    throw new InvalidDataException(
                        string.Format(Strings.Dbml_DuplicateEntity, tableName)
                    );
                }

                currentEntity = entities[tableName];
                continue;
            }

            if (line.StartsWith("Ref:", StringComparison.OrdinalIgnoreCase))
            {
                relationships.Add(ParseRelationship(line, entities));
                continue;
            }
        }

        if (currentEntity is not null)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_MissingClosingBrace, currentEntity.TableName)
            );
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException(Strings.Dbml_NoEntities);
        }

        EnsureEntitiesHaveColumns(entities.Values);
        ResolveRelationshipColumns(entities, relationships);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>
    /// DBML のカラム定義行（<c>名前 型 [設定, ...]</c>）を解析する
    /// </summary>
    /// <remarks>
    /// 型名は空白を含んでもよい（2 番目以降のトークンをすべて型として連結する）。
    /// 設定省略時は NULL 許可を既定とし、<c>pk</c> 指定時は NOT NULL を強制する。
    /// note 内のエスケープ <c>\'</c> はシングルクォートへ復元する
    /// </remarks>
    /// <exception cref="InvalidDataException">名前と型の 2 トークンに満たない場合</exception>
    private static Column ParseColumn(string line, string tableName)
    {
        var trimmed = line.Trim();
        var bracketStart = trimmed.IndexOf('[');
        var bracketEnd = trimmed.LastIndexOf(']');
        var definition = bracketStart >= 0 ? trimmed[..bracketStart].Trim() : trimmed;
        var optionText =
            bracketStart >= 0 && bracketEnd > bracketStart
                ? trimmed[(bracketStart + 1)..bracketEnd]
                : string.Empty;
        var tokens = definition.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_ColumnParseError, tableName, line)
            );
        }

        var column = new Column
        {
            Name = tokens[0],
            DataType = string.Join(' ', tokens.Skip(1)),
            IsNullable = true,
        };

        foreach (var option in SplitOptions(optionText))
        {
            if (string.Equals(option, "pk", StringComparison.OrdinalIgnoreCase))
            {
                column.IsPrimaryKey = true;
                column.IsNullable = false;
                continue;
            }

            if (string.Equals(option, "ref", StringComparison.OrdinalIgnoreCase))
            {
                column.IsForeignKey = true;
                continue;
            }

            if (string.Equals(option, "not null", StringComparison.OrdinalIgnoreCase))
            {
                column.IsNullable = false;
                continue;
            }

            if (string.Equals(option, "null", StringComparison.OrdinalIgnoreCase))
            {
                column.IsNullable = true;
                continue;
            }

            var noteMatch = NoteRegex.Match(option);

            if (noteMatch.Success)
            {
                column.Description = noteMatch.Groups["note"].Value.Replace("\\'", "'");
            }
        }

        return column;
    }

    /// <summary>
    /// DBML の <c>Ref:</c> 行を解析してリレーションを生成する
    /// </summary>
    /// <remarks>
    /// 左辺テーブルを親（Source）、右辺テーブルを子（Target）として扱う。
    /// 行中のカラム名は構文上必須だがここでは使用せず、参照カラムは後段の
    /// <see cref="ResolveRelationshipColumns"/> が既定ルールで決定する
    /// </remarks>
    /// <exception cref="InvalidDataException">構文不一致、または参照先テーブルが未定義の場合</exception>
    private static Relationship ParseRelationship(
        string line,
        IReadOnlyDictionary<string, Entity> entities
    )
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipParseError, line)
            );
        }

        var leftTable = match.Groups["leftTable"].Value;
        var rightTable = match.Groups["rightTable"].Value;
        var symbol = match.Groups["symbol"].Value;
        var note = match.Groups["note"].Success
            ? match.Groups["note"].Value.Replace("\\'", "'")
            : null;

        if (!entities.ContainsKey(leftTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipSourceUndefined, leftTable)
            );
        }

        if (!entities.ContainsKey(rightTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_RelationshipTargetUndefined, rightTable)
            );
        }

        return new Relationship
        {
            SourceEntityId = entities[leftTable].Id,
            TargetEntityId = entities[rightTable].Id,
            Type = symbol switch
            {
                "-" => RelationshipType.OneToOne,
                "<" => RelationshipType.OneToMany,
                "<>" => RelationshipType.ManyToMany,
                _ => throw new InvalidDataException(
                    string.Format(Strings.Dbml_UnsupportedRelationshipSymbol, symbol)
                ),
            },
            ConstraintName = note,
        };
    }

    /// <summary>
    /// カラム設定文字列をカンマ区切りで分割する
    /// </summary>
    /// <remarks>
    /// <c>note: 'a, b'</c> のようにクォート内へ含まれるカンマは区切りとして扱わない。
    /// クォートの開閉判定では直前の <c>\</c> によるエスケープを考慮する
    /// </remarks>
    private static IEnumerable<string> SplitOptions(string optionText)
    {
        if (string.IsNullOrWhiteSpace(optionText))
        {
            yield break;
        }

        var builder = new System.Text.StringBuilder();
        var inQuote = false;

        foreach (var ch in optionText)
        {
            if (ch == '\'' && (builder.Length == 0 || builder[^1] != '\\'))
            {
                inQuote = !inQuote;
            }

            if (ch == ',' && !inQuote)
            {
                var item = builder.ToString().Trim();

                if (item.Length > 0)
                {
                    yield return item;
                }

                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        var last = builder.ToString().Trim();

        if (last.Length > 0)
        {
            yield return last;
        }
    }

    /// <summary>
    /// <c>//</c> 以降の行コメントを除去する
    /// </summary>
    private static string RemoveComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    /// <summary>
    /// カラムを 1 つも持たないエンティティへ既定の PK 列（<c>ID int</c>）を補う
    /// </summary>
    private static void EnsureEntitiesHaveColumns(IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.Columns.Count == 0)
            {
                entity.Columns.Add(
                    new Column
                    {
                        Name = "ID",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    }
                );
            }
        }
    }

    /// <summary>
    /// 各リレーションの参照カラムを既定ルールで補完する
    /// </summary>
    /// <remarks>
    /// 親（Source）側は最初の PK 列（無ければ先頭列）、子（Target）側は
    /// <see cref="ResolveTargetColumn"/> の優先順位で選び、選んだ列に FK フラグを立てる。
    /// 多対多はジャンクションテーブル前提のためカラムを割り当てない
    /// </remarks>
    private static void ResolveRelationshipColumns(
        IReadOnlyDictionary<string, Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        foreach (var relationship in relationships)
        {
            var source = entities.Values.First(entity => entity.Id == relationship.SourceEntityId);
            var target = entities.Values.First(entity => entity.Id == relationship.TargetEntityId);

            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var sourceColumn =
                source.Columns.FirstOrDefault(column => column.IsPrimaryKey)
                ?? source.Columns.First();
            relationship.SourceColumnId = sourceColumn.Id;

            var targetColumn = ResolveTargetColumn(sourceColumn, target);
            relationship.TargetColumnId = targetColumn.Id;
            targetColumn.IsForeignKey = true;
        }
    }

    /// <summary>
    /// 親の PK に対応する子側の外部キー列を選択する
    /// </summary>
    /// <remarks>
    /// 優先順位: 同名の FK 列 → 同名の列 → PK でない最初の FK 列 → PK でない先頭列 → 先頭列
    /// </remarks>
    private static Column ResolveTargetColumn(Column sourcePrimaryKey, Entity target)
    {
        var sameNameForeignKey = target.Columns.FirstOrDefault(column =>
            column.IsForeignKey
            && string.Equals(column.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase)
        );

        if (sameNameForeignKey is not null)
        {
            return sameNameForeignKey;
        }

        var sameName = target.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase)
        );

        if (sameName is not null)
        {
            return sameName;
        }

        var firstForeignKey = target.Columns.FirstOrDefault(column =>
            column.IsForeignKey && !column.IsPrimaryKey
        );

        if (firstForeignKey is not null)
        {
            return firstForeignKey;
        }

        return target.Columns.FirstOrDefault(column => !column.IsPrimaryKey)
            ?? target.Columns.First();
    }

    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^Table\s+(?<table>\S+)\s*\{$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex TableHeaderLineRegex();

    /// <summary>
    /// <c>Ref:</c> 行に一致する正規表現を生成する。note は <see cref="DbmlExporter"/> 独自形式に合わせ
    /// <c>Ref:</c> 直後の <c>[note: '...']</c> のみ受け付ける
    /// </summary>
    [GeneratedRegex(
        @"^Ref:(?:\s*\[note:\s*'(?<note>(?:\\'|[^'])*)'\])?\s*(?<leftTable>\w+)\.(?<leftColumn>\w+)\s*(?<symbol><>|<|-)\s*(?<rightTable>\w+)\.(?<rightColumn>\w+)\s*$",
        RegexOptions.Compiled
    )]
    private static partial Regex RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^note:\s*'(?<note>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ColumnNoteRegex();
}
