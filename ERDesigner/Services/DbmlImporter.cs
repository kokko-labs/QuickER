using System.IO;
using System.Text.RegularExpressions;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// DBML 形式を ER 図へ変換するサービスです。
/// </summary>
public static partial class DbmlImporter
{
    private static readonly Regex TableHeaderRegex = TableHeaderLineRegex();
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();
    private static readonly Regex NoteRegex = ColumnNoteRegex();

    /// <summary>
    /// DBML ファイルを読み込みます。
    /// </summary>
    public static ErDiagram Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// DBML テキストを解析して ER 図を生成します。
    /// </summary>
    public static ErDiagram Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("DBML テキストが空です。");
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<Relationship>();
        Entity? currentEntity = null;

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
                    throw new InvalidDataException($"エンティティ '{tableName}' が重複しています。");
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
            throw new InvalidDataException($"エンティティ '{currentEntity.TableName}' の閉じ括弧 '}}' がありません。");
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException("DBML にエンティティ定義がありません。");
        }

        EnsureEntitiesHaveColumns(entities.Values);
        ResolveRelationshipColumns(entities, relationships);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>
    /// DBML のカラム定義を解析します。
    /// </summary>
    private static Column ParseColumn(string line, string tableName)
    {
        var trimmed = line.Trim();
        var bracketStart = trimmed.IndexOf('[');
        var bracketEnd = trimmed.LastIndexOf(']');
        var definition = bracketStart >= 0 ? trimmed[..bracketStart].Trim() : trimmed;
        var optionText = bracketStart >= 0 && bracketEnd > bracketStart ? trimmed[(bracketStart + 1)..bracketEnd] : string.Empty;
        var tokens = definition.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            throw new InvalidDataException($"テーブル '{tableName}' のカラム定義 '{line}' を解析できません。");
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
    /// DBML のリレーション定義を解析します。
    /// </summary>
    private static Relationship ParseRelationship(string line, IReadOnlyDictionary<string, Entity> entities)
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException($"リレーション定義 '{line}' を解析できません。");
        }

        var leftTable = match.Groups["leftTable"].Value;
        var rightTable = match.Groups["rightTable"].Value;
        var symbol = match.Groups["symbol"].Value;
        var note = match.Groups["note"].Success ? match.Groups["note"].Value.Replace("\\'", "'") : null;

        if (!entities.ContainsKey(leftTable))
        {
            throw new InvalidDataException($"リレーションの参照元テーブル '{leftTable}' が未定義です。");
        }

        if (!entities.ContainsKey(rightTable))
        {
            throw new InvalidDataException($"リレーションの参照先テーブル '{rightTable}' が未定義です。");
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
                _ => throw new InvalidDataException($"未対応のリレーション記号 '{symbol}' です。"),
            },
            ConstraintName = note,
        };
    }

    /// <summary>
    /// カラム設定文字列をカンマ区切りで分割します。
    /// note 内のカンマは分割対象から除外します。
    /// </summary>
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
    /// 行コメントを除去します。
    /// </summary>
    private static string RemoveComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    /// <summary>
    /// すべてのエンティティに最低 1 列があることを保証します。
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
    /// リレーションの参照列を既定ルールで補完します。
    /// </summary>
    private static void ResolveRelationshipColumns(IReadOnlyDictionary<string, Entity> entities, IEnumerable<Relationship> relationships)
    {
        foreach (var relationship in relationships)
        {
            var source = entities.Values.First(entity => entity.Id == relationship.SourceEntityId);
            var target = entities.Values.First(entity => entity.Id == relationship.TargetEntityId);

            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var sourceColumn = source.Columns.FirstOrDefault(column => column.IsPrimaryKey) ?? source.Columns.First();
            relationship.SourceColumnId = sourceColumn.Id;

            var targetColumn = ResolveTargetColumn(sourceColumn, target);
            relationship.TargetColumnId = targetColumn.Id;
            targetColumn.IsForeignKey = true;
        }
    }

    /// <summary>
    /// 参照先 PK に対応する外部キー列を選択します。
    /// </summary>
    private static Column ResolveTargetColumn(Column sourcePrimaryKey, Entity target)
    {
        var sameNameForeignKey = target.Columns.FirstOrDefault(column =>
            column.IsForeignKey && string.Equals(column.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase)
        );

        if (sameNameForeignKey is not null)
        {
            return sameNameForeignKey;
        }

        var sameName = target.Columns.FirstOrDefault(column => string.Equals(column.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase));

        if (sameName is not null)
        {
            return sameName;
        }

        var firstForeignKey = target.Columns.FirstOrDefault(column => column.IsForeignKey && !column.IsPrimaryKey);

        if (firstForeignKey is not null)
        {
            return firstForeignKey;
        }

        return target.Columns.FirstOrDefault(column => !column.IsPrimaryKey) ?? target.Columns.First();
    }

    [GeneratedRegex(@"^Table\s+(?<table>\S+)\s*\{$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TableHeaderLineRegex();

    [GeneratedRegex(
        @"^Ref:(?:\s*\[note:\s*'(?<note>(?:\\'|[^'])*)'\])?\s*(?<leftTable>\w+)\.(?<leftColumn>\w+)\s*(?<symbol><>|<|-)\s*(?<rightTable>\w+)\.(?<rightColumn>\w+)\s*$",
        RegexOptions.Compiled
    )]
    private static partial Regex RelationshipLineRegex();

    [GeneratedRegex(@"^note:\s*'(?<note>(?:\\'|[^'])*)'$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ColumnNoteRegex();
}
