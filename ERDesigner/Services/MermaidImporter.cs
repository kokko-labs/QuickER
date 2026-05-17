using System.IO;
using System.Text.RegularExpressions;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// Mermaid の <c>erDiagram</c> 記法を ER 図へ変換するサービスです。
/// </summary>
public static partial class MermaidImporter
{
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary>
    /// Mermaid ファイルを読み込みます。
    /// </summary>
    public static ErDiagram Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Mermaid テキストを解析して ER 図を生成します。
    /// </summary>
    public static ErDiagram Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("Mermaid テキストが空です。");
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var entities = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<Relationship>();
        Entity? currentEntity = null;
        var foundHeader = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (!foundHeader)
            {
                if (!string.Equals(line, "erDiagram", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Mermaid は先頭に erDiagram ヘッダーが必要です。");
                }

                foundHeader = true;
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

            if (line.EndsWith("{", StringComparison.Ordinal))
            {
                var tableName = line[..^1].Trim();

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    throw new InvalidDataException("エンティティ名がありません。");
                }

                if (!entities.TryAdd(tableName, new Entity { TableName = tableName }))
                {
                    throw new InvalidDataException($"エンティティ '{tableName}' が重複しています。");
                }

                currentEntity = entities[tableName];
                continue;
            }

            relationships.Add(ParseRelationship(line, entities));
        }

        if (!foundHeader)
        {
            throw new InvalidDataException("Mermaid の erDiagram ヘッダーが見つかりません。");
        }

        if (currentEntity is not null)
        {
            throw new InvalidDataException($"エンティティ '{currentEntity.TableName}' の閉じ括弧 '}}' がありません。");
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException("Mermaid にエンティティ定義がありません。");
        }

        EnsureEntitiesHaveColumns(entities.Values);
        ResolveRelationshipColumns(entities, relationships);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>
    /// カラム定義を解析します。
    /// </summary>
    private static Column ParseColumn(string line, string tableName)
    {
        var content = RemoveTrailingComment(line);
        var tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            throw new InvalidDataException($"テーブル '{tableName}' のカラム定義 '{line}' を解析できません。");
        }

        var column = new Column
        {
            DataType = DenormalizeDataType(tokens[0]),
            Name = tokens[1],
            IsNullable = true,
        };

        foreach (var token in tokens.Skip(2))
        {
            foreach (var key in token.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(key, "PK", StringComparison.OrdinalIgnoreCase))
                {
                    column.IsPrimaryKey = true;
                    column.IsNullable = false;
                    continue;
                }

                if (string.Equals(key, "FK", StringComparison.OrdinalIgnoreCase))
                {
                    column.IsForeignKey = true;
                }
            }
        }

        return column;
    }

    /// <summary>
    /// リレーション定義を解析します。
    /// </summary>
    private static Relationship ParseRelationship(string line, IReadOnlyDictionary<string, Entity> entities)
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException($"リレーション定義 '{line}' を解析できません。");
        }

        var leftTable = match.Groups["left"].Value;
        var rightTable = match.Groups["right"].Value;
        var symbol = match.Groups["symbol"].Value;
        var label = match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null;

        if (!entities.ContainsKey(leftTable))
        {
            throw new InvalidDataException($"リレーションの左側テーブル '{leftTable}' が未定義です。");
        }

        if (!entities.ContainsKey(rightTable))
        {
            throw new InvalidDataException($"リレーションの右側テーブル '{rightTable}' が未定義です。");
        }

        var relationshipType = ParseRelationshipType(symbol);
        return new Relationship
        {
            SourceEntityId = entities[leftTable].Id,
            TargetEntityId = entities[rightTable].Id,
            Type = relationshipType,
            ConstraintName = string.IsNullOrWhiteSpace(label) ? null : label,
        };
    }

    /// <summary>
    /// リレーション種別を Mermaid 記号から判定します。
    /// </summary>
    private static RelationshipType ParseRelationshipType(string symbol)
    {
        return symbol switch
        {
            "||--||" or "|o--o|" or "|o--||" or "||--o|" => RelationshipType.OneToOne,
            "||--o{" or "||--|{" or "|o--o{" or "|o--|{" => RelationshipType.OneToMany,
            "}o--o{" or "}|--|{" or "}o--|{" or "}|--o{" => RelationshipType.ManyToMany,
            _ => throw new InvalidDataException($"未対応のリレーション記号 '{symbol}' です。"),
        };
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

    /// <summary>
    /// Mermaid 出力時に正規化された型名を元の SQL 型名に復元します。
    /// 数値引数が末尾にアンダースコアで連結されている形式を括弧記法に変換します。
    /// 例: nvarchar_100 → nvarchar(100)、decimal_10_2 → decimal(10,2)
    /// 数値以外のアンダースコアはそのまま保持します。
    /// </summary>
    private static string DenormalizeDataType(string dataType)
    {
        // パターン: 識別子部分 + (_数値)+ の形式を検出
        var match = System.Text.RegularExpressions.Regex.Match(dataType, @"^([A-Za-z][A-Za-z0-9]*)(_\d+)+$");

        if (!match.Success)
        {
            return dataType;
        }

        // 先頭の型名と末尾の数値引数群を分離する
        var firstUnderscoreDigit = System.Text.RegularExpressions.Regex.Match(dataType, @"_\d");
        var typeName = dataType[..firstUnderscoreDigit.Index];
        var argsPart = dataType[(firstUnderscoreDigit.Index + 1)..];
        var args = argsPart.Replace('_', ',');

        return $"{typeName}({args})";
    }

    /// <summary>
    /// Mermaid 属性行末尾の説明文字列を除去します。
    /// </summary>
    private static string RemoveTrailingComment(string line)
    {
        var commentIndex = line.IndexOf('"');
        return commentIndex >= 0 ? line[..commentIndex].TrimEnd() : line;
    }

    [GeneratedRegex(@"^(?<left>\S+)\s+(?<symbol>[|}{o]+--[|}{o]+)\s+(?<right>\S+)(?:\s*:\s*(?<label>.+))?$", RegexOptions.Compiled)]
    private static partial Regex RelationshipLineRegex();
}
