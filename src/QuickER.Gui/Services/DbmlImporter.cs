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
///   <item>カラム設定: <c>pk</c> / <c>ref</c> / <c>unique</c> / <c>null</c> / <c>not null</c> / <c>note: '...'</c>（大文字小文字を区別しない）</item>
///   <item><c>Indexes { … }</c> ブロック: <c>unique</c> 設定を持つ索引のみ一意制約として取り込む（<c>(a, b) [unique, name: '…']</c> / 単一列は括弧なしも可）</item>
///   <item><c>Ref:</c> 行: 多重度記号は <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&lt;&gt;</c>（多対多）のみ（<c>&gt;</c>（多対1）は未対応）。エンドポイントは単一列 <c>親.a</c> と複合 Ref 構文 <c>親.(a, b)</c> の双方に対応し、<b>行に書かれた列名がそのまま外部キーの構成列になる</b>（推論しない）</item>
///   <item><c>//</c> 行コメント</item>
/// </list>
/// Project・Enum・TableGroup・複数行 Note ブロック等の DBML 構文は未対応
/// （<c>unique</c> でない索引は「一意制約ではない」ため読み飛ばす）
/// </remarks>
public static partial class DbmlImporter
{
    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行を検出する正規表現</summary>
    private static readonly Regex TableHeaderRegex = TableHeaderLineRegex();

    /// <summary><c>Ref:</c> 行を解析する正規表現</summary>
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c> を解析する正規表現</summary>
    private static readonly Regex NoteRegex = ColumnNoteRegex();

    /// <summary><c>Indexes {</c> ブロック開始行を検出する正規表現</summary>
    private static readonly Regex IndexesHeaderRegex = IndexesHeaderLineRegex();

    /// <summary><c>Indexes</c> ブロック内の索引定義行を解析する正規表現</summary>
    private static readonly Regex IndexLineRegex = IndexDefinitionLineRegex();

    /// <summary>索引設定の <c>name: '...'</c> を解析する正規表現</summary>
    private static readonly Regex IndexNameRegex = IndexSettingNameRegex();

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
        // Ref: 行の列名は、空テーブルへの既定 PK 列補完まで済んだ後にまとめて列ペアへ解決する
        var pendingRelationshipColumns =
            new List<(Relationship Relationship, List<string> Source, List<string> Target)>();
        // Indexes ブロックの一意索引は、列定義より前に書かれていても解決できるよう最後にまとめて紐付ける
        var pendingUniqueIndexes = new List<(Entity Entity, string? Name, List<string> Columns)>();
        Entity? currentEntity = null;
        var inIndexesBlock = false;

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
                // Indexes ブロック内は索引定義として解釈する（閉じ括弧で Table ブロックへ戻る）
                if (inIndexesBlock)
                {
                    if (line == "}")
                    {
                        inIndexesBlock = false;
                        continue;
                    }

                    var uniqueIndex = ParseUniqueIndex(line, currentEntity.TableName);

                    if (uniqueIndex is not null)
                    {
                        pendingUniqueIndexes.Add(
                            (currentEntity, uniqueIndex.Value.Name, uniqueIndex.Value.Columns)
                        );
                    }

                    continue;
                }

                if (line == "}")
                {
                    currentEntity = null;
                    continue;
                }

                if (IndexesHeaderRegex.IsMatch(line))
                {
                    inIndexesBlock = true;
                    continue;
                }

                var (column, isUnique) = ParseColumn(line, currentEntity.TableName);
                currentEntity.Columns.Add(column);

                // カラム設定の unique は「その 1 列だけの名前なし一意制約」を意味する
                if (isUnique)
                {
                    currentEntity.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [column.Id] }
                    );
                }

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
                var (relationship, sourceColumns, targetColumns) = ParseRelationship(
                    line,
                    entities
                );
                relationships.Add(relationship);
                pendingRelationshipColumns.Add((relationship, sourceColumns, targetColumns));
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
        ResolveUniqueIndexes(pendingUniqueIndexes);
        ResolveRelationshipColumns(entities, pendingRelationshipColumns);

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
    /// <returns>復元したカラムと、カラム設定 <c>unique</c> が指定されていたか</returns>
    /// <exception cref="InvalidDataException">名前と型の 2 トークンに満たない場合</exception>
    private static (Column Column, bool IsUnique) ParseColumn(string line, string tableName)
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
        var isUnique = false;

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

            if (string.Equals(option, "unique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = true;
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

        return (column, isUnique);
    }

    /// <summary>
    /// <c>Indexes</c> ブロック内の 1 行を解析し、一意索引なら制約名と構成列名を返す
    /// </summary>
    /// <returns>一意索引なら（名前・構成列名）。<c>unique</c> でない索引は <c>null</c>（読み飛ばす）</returns>
    /// <exception cref="InvalidDataException">索引行の構文が解釈できない場合</exception>
    private static (string? Name, List<string> Columns)? ParseUniqueIndex(
        string line,
        string tableName
    )
    {
        var match = IndexLineRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_IndexParseError, tableName, line)
            );
        }

        // 括弧つき（複数列可）と括弧なし（単一列）のどちらの記法でも列名一覧として扱う
        var columnsText = match.Groups["columns"].Success
            ? match.Groups["columns"].Value
            : match.Groups["singleColumn"].Value;
        var columns = columnsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (columns.Count == 0)
        {
            throw new InvalidDataException(
                string.Format(Strings.Dbml_IndexParseError, tableName, line)
            );
        }

        var isUnique = false;
        string? name = null;

        foreach (var option in SplitOptions(match.Groups["settings"].Value))
        {
            if (string.Equals(option, "unique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = true;
                continue;
            }

            var nameMatch = IndexNameRegex.Match(option);

            if (nameMatch.Success)
            {
                name = nameMatch.Groups["name"].Value.Replace("\\'", "'");
            }
        }

        // unique でない索引は一意制約ではないため取り込まない（インデックス自体はモデルに持たない）
        return isUnique ? (name, columns) : null;
    }

    /// <summary>
    /// <c>Indexes</c> ブロックから集めた一意索引を、対象エンティティの一意制約として確定する
    /// </summary>
    /// <exception cref="InvalidDataException">索引が参照する列がテーブルに存在しない場合</exception>
    private static void ResolveUniqueIndexes(
        IEnumerable<(Entity Entity, string? Name, List<string> Columns)> uniqueIndexes
    )
    {
        foreach (var (entity, name, columns) in uniqueIndexes)
        {
            var columnIds = new List<Guid>(columns.Count);

            foreach (var columnName in columns)
            {
                var column = entity.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
                );

                if (column is null)
                {
                    throw new InvalidDataException(
                        string.Format(
                            Strings.Dbml_IndexColumnNotFound,
                            entity.TableName,
                            columnName
                        )
                    );
                }

                columnIds.Add(column.Id);
            }

            entity.UniqueConstraints.Add(
                new UniqueConstraint { Name = name, ColumnIds = columnIds }
            );
        }
    }

    /// <summary>
    /// DBML の <c>Ref:</c> 行を解析してリレーションを生成する
    /// </summary>
    /// <remarks>
    /// 左辺テーブルを親（Source）、右辺テーブルを子（Target）として扱う。
    /// 行中のカラム名（単一列 <c>親.a</c> / 複合 <c>親.(a, b)</c>）は外部キーの構成列そのものとして持ち帰り、
    /// 後段の <see cref="ResolveRelationshipColumns"/> が列ペアへ解決する（推論はしない）
    /// </remarks>
    /// <exception cref="InvalidDataException">構文不一致、または参照先テーブルが未定義の場合</exception>
    private static (
        Relationship Relationship,
        List<string> SourceColumns,
        List<string> TargetColumns
    ) ParseRelationship(string line, IReadOnlyDictionary<string, Entity> entities)
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

        var relationship = new Relationship
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

        return (
            relationship,
            ReadEndpointColumns(match, "leftColumns", "leftColumn"),
            ReadEndpointColumns(match, "rightColumns", "rightColumn")
        );
    }

    /// <summary><c>Ref:</c> 行のエンドポイント列名を取り出す（単一列・複合 <c>(a, b)</c> のどちらでも列名一覧を返す）</summary>
    private static List<string> ReadEndpointColumns(
        Match match,
        string listGroupName,
        string singleGroupName
    )
    {
        if (!match.Groups[listGroupName].Success)
        {
            return [match.Groups[singleGroupName].Value];
        }

        return match
            .Groups[listGroupName]
            .Value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToList();
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
    /// <c>Ref:</c> 行に書かれた列名を、各リレーションの列ペア（宣言順）へ解決する
    /// </summary>
    /// <remarks>
    /// <b>行の列名が正本</b>で推論は行わない（複合外部キーがそのまま往復する）。解決した子列には FK フラグを立てる。
    /// 多対多はジャンクションテーブル前提のためカラムを割り当てない。
    /// 両側の列数が食い違う行や、テーブルに存在しない列名を含む行は、その行の列対応だけを捨てて
    /// リレーション自体は残す（不正な索引行を読み飛ばすのと同じ寛容さ。列ペアなしのリレーションは
    /// 外部キー句を作らないため、GUI 側で対応付けを補える）
    /// </remarks>
    private static void ResolveRelationshipColumns(
        IReadOnlyDictionary<string, Entity> entities,
        IEnumerable<(
            Relationship Relationship,
            List<string> SourceColumns,
            List<string> TargetColumns
        )> pendingRelationshipColumns
    )
    {
        foreach (var (relationship, sourceColumns, targetColumns) in pendingRelationshipColumns)
        {
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            if (sourceColumns.Count == 0 || sourceColumns.Count != targetColumns.Count)
            {
                continue;
            }

            var source = entities.Values.First(entity => entity.Id == relationship.SourceEntityId);
            var target = entities.Values.First(entity => entity.Id == relationship.TargetEntityId);
            var pairs = new List<RelationshipColumnPair>();
            var foreignKeyColumns = new List<Column>();

            for (var i = 0; i < sourceColumns.Count; i++)
            {
                var sourceColumn = FindColumn(source, sourceColumns[i]);
                var targetColumn = FindColumn(target, targetColumns[i]);

                if (sourceColumn is null || targetColumn is null)
                {
                    pairs.Clear();
                    break;
                }

                pairs.Add(new RelationshipColumnPair(sourceColumn.Id, targetColumn.Id));
                foreignKeyColumns.Add(targetColumn);
            }

            if (pairs.Count == 0)
            {
                continue;
            }

            relationship.ColumnPairs = pairs;

            foreach (var column in foreignKeyColumns)
            {
                column.IsForeignKey = true;
            }
        }
    }

    /// <summary>テーブルの列を名前で検索する（大文字小文字を区別しない）</summary>
    private static Column? FindColumn(Entity entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary><c>Table 名前 {</c> 形式のテーブル開始行に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^Table\s+(?<table>\S+)\s*\{$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex TableHeaderLineRegex();

    /// <summary>
    /// <c>Ref:</c> 行に一致する正規表現を生成する。note は <see cref="DbmlExporter"/> 独自形式に合わせ
    /// <c>Ref:</c> 直後の <c>[note: '...']</c> のみ受け付ける。エンドポイントは単一列（<c>親.a</c>）と
    /// 複合 Ref 構文（<c>親.(a, b)</c>）の双方を受け付ける
    /// </summary>
    [GeneratedRegex(
        @"^Ref:(?:\s*\[note:\s*'(?<note>(?:\\'|[^'])*)'\])?\s*(?<leftTable>\w+)\.(?:\((?<leftColumns>[^)]*)\)|(?<leftColumn>\w+))\s*(?<symbol><>|<|-)\s*(?<rightTable>\w+)\.(?:\((?<rightColumns>[^)]*)\)|(?<rightColumn>\w+))\s*$",
        RegexOptions.Compiled
    )]
    private static partial Regex RelationshipLineRegex();

    /// <summary>カラム設定の <c>note: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^note:\s*'(?<note>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex ColumnNoteRegex();

    /// <summary><c>Indexes {</c> 形式のブロック開始行に一致する正規表現を生成する</summary>
    [GeneratedRegex(@"^Indexes\s*\{$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IndexesHeaderLineRegex();

    /// <summary>
    /// 索引定義行（<c>(列, …) [設定, …]</c> または単一列の <c>列 [設定, …]</c>）に一致する正規表現を生成する
    /// </summary>
    [GeneratedRegex(
        @"^(?:\((?<columns>[^)]*)\)|(?<singleColumn>[^\s\[\]()]+))\s*(?:\[(?<settings>.*)\])?$",
        RegexOptions.Compiled
    )]
    private static partial Regex IndexDefinitionLineRegex();

    /// <summary>索引設定の <c>name: '...'</c>（<c>\'</c> エスケープ対応）に一致する正規表現を生成する</summary>
    [GeneratedRegex(
        @"^name:\s*'(?<name>(?:\\'|[^'])*)'$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    )]
    private static partial Regex IndexSettingNameRegex();
}
