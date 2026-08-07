using System.IO;
using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>Mermaid の <c>erDiagram</c> 記法を ER 図へ変換するサービス</summary>
/// <remarks>
/// キー標識 <c>UK</c> はその 1 列だけの一意制約として取り込む（Mermaid には複数列をまとめる構文が無いため、
/// 複合の一意制約は表現できない＝<see cref="MermaidExporter"/> も出力しない）。
/// </remarks>
public static partial class MermaidImporter
{
    /// <summary>リレーション行の解析に用いる正規表現</summary>
    private static readonly Regex RelationshipRegex = RelationshipLineRegex();

    /// <summary>Mermaid ファイルを読み込み ER 図へ変換する</summary>
    public static ErDiagram Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Mermaid テキストを解析して ER 図を生成する</summary>
    /// <exception cref="InvalidDataException">ヘッダー欠落・構文不正・定義不足の場合にスローする</exception>
    public static ErDiagram Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(Strings.Mermaid_EmptyText);
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

            // 最初の有効行は erDiagram ヘッダーでなければならない
            if (!foundHeader)
            {
                if (!string.Equals(line, "erDiagram", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(Strings.Mermaid_MissingHeader);
                }

                foundHeader = true;
                continue;
            }

            // エンティティブロック内の行はカラム定義、閉じ括弧でブロックを抜ける
            if (currentEntity is not null)
            {
                if (line == "}")
                {
                    currentEntity = null;
                    continue;
                }

                var (column, isUnique) = ParseColumn(line, currentEntity.TableName);
                currentEntity.Columns.Add(column);

                // UK 標識はその 1 列だけの一意制約を意味する（Mermaid に複合制約の構文が無いため）
                if (isUnique)
                {
                    currentEntity.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [column.Id] }
                    );
                }

                continue;
            }

            if (line.EndsWith("{", StringComparison.Ordinal))
            {
                var tableName = line[..^1].Trim();

                if (string.IsNullOrWhiteSpace(tableName))
                {
                    throw new InvalidDataException(Strings.Mermaid_MissingEntityName);
                }

                if (!entities.TryAdd(tableName, new Entity { TableName = tableName }))
                {
                    throw new InvalidDataException(
                        string.Format(Strings.Mermaid_DuplicateEntity, tableName)
                    );
                }

                currentEntity = entities[tableName];
                continue;
            }

            relationships.Add(ParseRelationship(line, entities));
        }

        if (!foundHeader)
        {
            throw new InvalidDataException(Strings.Mermaid_HeaderNotFound);
        }

        if (currentEntity is not null)
        {
            throw new InvalidDataException(
                string.Format(Strings.Mermaid_MissingClosingBrace, currentEntity.TableName)
            );
        }

        if (entities.Count == 0)
        {
            throw new InvalidDataException(Strings.Mermaid_NoEntities);
        }

        EnsureEntitiesHaveColumns(entities.Values);
        ResolveRelationshipColumns(entities, relationships);

        return new ErDiagram { Entities = entities.Values.ToList(), Relationships = relationships };
    }

    /// <summary>カラム定義行を解析して <see cref="Column"/> を生成する（PK / FK / UK マーカーを反映する）</summary>
    /// <returns>復元したカラムと、<c>UK</c> マーカー（単一列の一意制約）が付いていたか</returns>
    private static (Column Column, bool IsUnique) ParseColumn(string line, string tableName)
    {
        var content = RemoveTrailingComment(line);
        var tokens = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            throw new InvalidDataException(
                string.Format(Strings.Mermaid_ColumnParseError, tableName, line)
            );
        }

        var column = new Column
        {
            DataType = DenormalizeDataType(tokens[0]),
            Name = tokens[1],
            IsNullable = true,
        };
        var isUnique = false;

        foreach (var token in tokens.Skip(2))
        {
            foreach (
                var key in token.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
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
                    continue;
                }

                if (string.Equals(key, "UK", StringComparison.OrdinalIgnoreCase))
                {
                    isUnique = true;
                }
            }
        }

        return (column, isUnique);
    }

    /// <summary>リレーション定義行を解析して <see cref="Relationship"/> を生成する</summary>
    private static Relationship ParseRelationship(
        string line,
        IReadOnlyDictionary<string, Entity> entities
    )
    {
        var match = RelationshipRegex.Match(line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                string.Format(Strings.Mermaid_RelationshipParseError, line)
            );
        }

        var leftTable = match.Groups["left"].Value;
        var rightTable = match.Groups["right"].Value;
        var symbol = match.Groups["symbol"].Value;
        var label = match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null;

        if (!entities.ContainsKey(leftTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Mermaid_RelationshipLeftUndefined, leftTable)
            );
        }

        if (!entities.ContainsKey(rightTable))
        {
            throw new InvalidDataException(
                string.Format(Strings.Mermaid_RelationshipRightUndefined, rightTable)
            );
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

    /// <summary>Mermaid のカーディナリティ記号からリレーション種別を判定する</summary>
    private static RelationshipType ParseRelationshipType(string symbol)
    {
        return symbol switch
        {
            "||--||" or "|o--o|" or "|o--||" or "||--o|" => RelationshipType.OneToOne,
            "||--o{" or "||--|{" or "|o--o{" or "|o--|{" => RelationshipType.OneToMany,
            "}o--o{" or "}|--|{" or "}o--|{" or "}|--o{" => RelationshipType.ManyToMany,
            _ => throw new InvalidDataException(
                string.Format(Strings.Mermaid_UnsupportedRelationshipSymbol, symbol)
            ),
        };
    }

    /// <summary>カラムを持たないエンティティに既定の主キー列を補う</summary>
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

    /// <summary>Mermaid に列情報が無いリレーションの参照列を既定ルールで補完する</summary>
    /// <remarks>
    /// 補完は「親 PK 全列の自動ペア化」（<see cref="ForeignKeyColumnResolver.ResolveColumnPairs(Entity, Entity, IEnumerable{Relationship})"/>）で、
    /// GUI の作成フロー・AI ツール（列省略時）と同じ意味論に揃える（単一 PK なら 1 組の列ペア）。
    /// 引き当てられた子列には FK フラグを立てる。多対多は中間テーブルを介する設計のため列補完の対象外とする
    /// </remarks>
    private static void ResolveRelationshipColumns(
        IReadOnlyDictionary<string, Entity> entities,
        IReadOnlyList<Relationship> relationships
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

            relationship.ColumnPairs = ForeignKeyColumnResolver.ResolveColumnPairs(
                source,
                target,
                relationships
            );

            foreach (var pair in relationship.ColumnPairs)
            {
                var targetColumn = target.Columns.First(column => column.Id == pair.TargetColumnId);
                targetColumn.IsForeignKey = true;
            }
        }
    }

    /// <summary>Mermaid 出力で正規化された型名を元の SQL 型名へ復元する</summary>
    /// <remarks>
    /// 末尾にアンダースコア連結された数値引数を括弧記法へ戻す
    /// 例: <c>nvarchar_100</c> → <c>nvarchar(100)</c>、<c>decimal_10_2</c> → <c>decimal(10,2)</c>
    /// 数値以外のアンダースコアはそのまま保持する
    /// </remarks>
    private static string DenormalizeDataType(string dataType)
    {
        // 「識別子 + (_数値)の繰り返し」に一致する型のみ復元対象とする
        var match = System.Text.RegularExpressions.Regex.Match(
            dataType,
            @"^([A-Za-z][A-Za-z0-9]*)(_\d+)+$"
        );

        if (!match.Success)
        {
            return dataType;
        }

        // 最初の「_数値」位置で型名部分と引数部分を分割する
        var firstUnderscoreDigit = System.Text.RegularExpressions.Regex.Match(dataType, @"_\d");
        var typeName = dataType[..firstUnderscoreDigit.Index];
        var argsPart = dataType[(firstUnderscoreDigit.Index + 1)..];
        var args = argsPart.Replace('_', ',');

        return $"{typeName}({args})";
    }

    /// <summary>Mermaid 属性行末尾の二重引用符で始まる説明文字列を除去する</summary>
    private static string RemoveTrailingComment(string line)
    {
        var commentIndex = line.IndexOf('"');
        return commentIndex >= 0 ? line[..commentIndex].TrimEnd() : line;
    }

    /// <summary>リレーション行（左テーブル・カーディナリティ記号・右テーブル・任意ラベル）にマッチする正規表現</summary>
    [GeneratedRegex(
        @"^(?<left>\S+)\s+(?<symbol>[|}{o]+--[|}{o]+)\s+(?<right>\S+)(?:\s*:\s*(?<label>.+))?$",
        RegexOptions.Compiled
    )]
    private static partial Regex RelationshipLineRegex();
}
