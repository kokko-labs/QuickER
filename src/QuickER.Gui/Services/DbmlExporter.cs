using System.IO;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Services;

/// <summary>
/// ER 図を DBML (Database Markup Language) テキストへ変換するエクスポーター
/// </summary>
/// <remarks>
/// 出力は <see cref="DbmlImporter"/> が解釈できる記法の範囲に限定する
/// <list type="bullet">
///   <item><c>Table</c> ブロック: カラム設定は <c>pk</c> / <c>ref</c> / <c>unique</c> / <c>null</c> / <c>not null</c> / <c>note</c> のみ出力（Enum 等は対象外）</item>
///   <item><c>Indexes</c> ブロック: 一意制約のうちカラム設定 <c>unique</c> で表せないもの（複合・名前付き）を <c>(列, …) [unique, name: '…']</c> として出力</item>
///   <item><c>Ref:</c> 行: 多重度を <c>-</c>（1対1）/ <c>&lt;</c>（1対多）/ <c>&lt;&gt;</c>（多対多）の記号で表現し、複合外部キーは DBML 標準の複合 Ref 構文 <c>親.(a, b) &lt; 子.(x, y)</c> で表現</item>
///   <item>note 文字列中のシングルクォートは <c>\'</c> にエスケープ</item>
/// </list>
/// </remarks>
public static class DbmlExporter
{
    /// <summary>
    /// ER 図定義から DBML 文字列を生成する
    /// </summary>
    /// <returns>全 Table ブロックの後に Ref 行をまとめた DBML テキスト（末尾は改行 1 つ）</returns>
    public static string Build(ErDiagram diagram)
    {
        var builder = new StringBuilder();

        foreach (var entity in diagram.Entities)
        {
            builder.AppendLine($"Table {entity.TableName} {{");

            var (inlineUniqueColumnIds, indexConstraints) = ClassifyUniqueConstraints(entity);

            foreach (var column in entity.Columns)
            {
                builder.AppendLine(
                    $"  {BuildColumnLine(column, inlineUniqueColumnIds.Contains(column.Id))}"
                );
            }

            AppendIndexesBlock(builder, indexConstraints);
            AppendTableNote(builder, entity);
            builder.AppendLine("}");
            builder.AppendLine();
        }

        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        foreach (var relationship in diagram.Relationships)
        {
            var line = BuildRelationshipLine(relationship, entitiesById);
            if (line is not null)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// DBML 文字列を UTF-8 でファイルへ保存し、この形式では表現できず落ちた情報の種類を返す
    /// </summary>
    public static IReadOnlyList<ExportOmissionKind> SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);

        return DetectOmissions(diagram);
    }

    /// <summary>この形式では表現できないために落ちる情報の種類を、図の実際の中身から判定する</summary>
    /// <remarks>
    /// DBML はテーブル説明（<c>Note:</c>）・列の説明（<c>note</c>）・NULL 許可・一意制約（複合・名前付きとも
    /// <c>Indexes</c> ブロック）・外部キーの列対応（複合 Ref 構文）・参照アクション（<c>delete</c> / <c>update</c>）を
    /// すべて表現できる。載せられないのは、<c>Note</c> を説明に使うため独自拡張なしでは書けないテーブルのメモと、
    /// DBML がそもそも持たない名前付きクエリ定義だけ。
    /// 中身が実際に入っているときだけ返す（空の説明では告知しない）
    /// </remarks>
    public static IReadOnlyList<ExportOmissionKind> DetectOmissions(ErDiagram diagram)
    {
        var checks = new (bool Detected, ExportOmissionKind Kind)[]
        {
            (
                diagram.Entities.Any(entity => !string.IsNullOrWhiteSpace(entity.Memo)),
                ExportOmissionKind.TableMemo
            ),
            (diagram.Queries.Count > 0, ExportOmissionKind.NamedQuery),
        };

        return checks.Where(check => check.Detected).Select(check => check.Kind).ToList();
    }

    /// <summary>テーブルの説明を DBML 標準の <c>Note:</c> 行として出力する（説明が空なら出さない）</summary>
    private static void AppendTableNote(StringBuilder builder, Entity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Description))
        {
            return;
        }

        builder.AppendLine($"  Note: '{EscapeNote(entity.Description)}'");
    }

    /// <summary>
    /// DBML のカラム定義行（<c>名前 型 [設定, ...]</c>）を構築する
    /// </summary>
    /// <remarks>
    /// PK 列には <c>pk</c> のみを出力し <c>ref</c> は併記しない。NULL 許可は常に
    /// <c>null</c> / <c>not null</c> のどちらかを明示し、インポート時の既定値依存を避ける。
    /// <paramref name="isUnique"/> は「名前なし単一列の一意制約の構成列」を表す
    /// （<see cref="ClassifyUniqueConstraints"/> の判定結果）
    /// </remarks>
    private static string BuildColumnLine(Column column, bool isUnique)
    {
        var settings = new List<string>();

        if (column.IsPrimaryKey)
        {
            settings.Add("pk");
        }

        if (column.IsForeignKey && !column.IsPrimaryKey)
        {
            settings.Add("ref");
        }

        if (isUnique)
        {
            settings.Add("unique");
        }

        settings.Add(column.IsNullable ? "null" : "not null");

        if (!string.IsNullOrWhiteSpace(column.Description))
        {
            settings.Add($"note: '{EscapeNote(column.Description)}'");
        }

        return $"{column.Name} {column.DataType} [{string.Join(", ", settings)}]";
    }

    /// <summary>
    /// 一意制約を「カラム設定 <c>unique</c> で表せるもの」と「<c>Indexes</c> ブロックへ出すもの」へ振り分ける
    /// </summary>
    /// <returns>インライン出力する構成列 ID の集合と、<c>Indexes</c> ブロックへ出す制約（構成列名つき）の一覧</returns>
    /// <remarks>
    /// カラム設定の <c>unique</c> は制約名を持てないため、名前なし単一列の制約だけをインラインにし、
    /// 複合制約と名前付き制約は <c>(列, …) [unique, name: '…']</c> として <c>Indexes</c> ブロックへ出す。
    /// 構成列が空、または解決できないカラム ID を含む制約は黙って除外する（DDL 生成と同じ規則）
    /// </remarks>
    private static (
        HashSet<Guid> InlineColumnIds,
        List<(string? Name, List<string> ColumnNames)> IndexConstraints
    ) ClassifyUniqueConstraints(Entity entity)
    {
        var inlineColumnIds = new HashSet<Guid>();
        var indexConstraints = new List<(string? Name, List<string> ColumnNames)>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            var columns = constraint
                .ColumnIds.Select(columnId =>
                    entity.Columns.FirstOrDefault(column => column.Id == columnId)
                )
                .ToList();

            if (columns.Count == 0 || columns.Any(column => column is null))
            {
                continue;
            }

            if (columns.Count == 1 && string.IsNullOrWhiteSpace(constraint.Name))
            {
                inlineColumnIds.Add(columns[0]!.Id);
                continue;
            }

            indexConstraints.Add(
                (constraint.Name, columns.Select(column => column!.Name).ToList())
            );
        }

        return (inlineColumnIds, indexConstraints);
    }

    /// <summary>
    /// <c>Indexes</c> ブロック（複合・名前付きの一意制約）をテーブルブロック内へ出力する
    /// </summary>
    private static void AppendIndexesBlock(
        StringBuilder builder,
        IReadOnlyList<(string? Name, List<string> ColumnNames)> indexConstraints
    )
    {
        if (indexConstraints.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("  Indexes {");

        foreach (var (name, columnNames) in indexConstraints)
        {
            var settings = string.IsNullOrWhiteSpace(name)
                ? "unique"
                : $"unique, name: '{EscapeNote(name!)}'";
            builder.AppendLine($"    ({string.Join(", ", columnNames)}) [{settings}]");
        }

        builder.AppendLine("  }");
    }

    /// <summary>
    /// DBML のリレーション行（<c>Ref:</c> 行）を構築する
    /// </summary>
    /// <remarks>
    /// 制約名は note 設定として出力する。標準 DBML では設定をエンドポイントの後ろへ置くが、
    /// ここでは <see cref="DbmlImporter"/> との往復を前提に <c>Ref:</c> 直後へ配置する独自形式を採る。
    /// 列ペアが 2 組以上（複合外部キー）なら DBML 標準の複合 Ref 構文 <c>親.(a, b) &lt; 子.(x, y)</c> で書き出し、
    /// 1 組なら従来どおりの単一列形式を保つ。参照カラム未指定のリレーションは各エンティティの先頭カラムで代用する
    /// </remarks>
    private static string? BuildRelationshipLine(
        Relationship relationship,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        if (
            !entitiesById.TryGetValue(relationship.SourceEntityId, out var source)
            || !entitiesById.TryGetValue(relationship.TargetEntityId, out var target)
        )
        {
            return null;
        }

        // 列ペアの解決は DDL 生成と同じ共通ヘルパーに委ねる（1 列でも解決できなければ null）
        var pairs = ForeignKeyColumnPairResolver.Resolve(relationship, source, target);

        // 列ペアを持たない（多対多・未割当）リレーションは先頭カラムで代用する
        var sourceColumnNames = pairs is null
            ? [source.Columns.First().Name]
            : ForeignKeyColumnPairResolver.ParentColumns(pairs).ToList();
        var targetColumnNames = pairs is null
            ? new List<string> { target.Columns.First().Name }
            : ForeignKeyColumnPairResolver.ChildColumns(pairs).ToList();

        var symbol = relationship.Type switch
        {
            RelationshipType.OneToOne => "-",
            RelationshipType.OneToMany => "<",
            RelationshipType.ManyToMany => "<>",
            _ => "<",
        };
        var settings = BuildRelationshipSettings(relationship);

        return $"Ref:{settings} {source.TableName}.{FormatEndpointColumns(sourceColumnNames)} {symbol} {target.TableName}.{FormatEndpointColumns(targetColumnNames)}";
    }

    /// <summary><c>Ref:</c> 行の設定ブロック（制約名・参照アクション）を組み立てる</summary>
    /// <remarks>
    /// 既定の参照アクション（<see cref="ForeignKeyReferentialAction.NoAction"/>）は出力しない
    /// （指定があるものだけを刻む＝生成コードの <c>[NavigationReference]</c> と同じ流儀）。
    /// アクション値は DBML の慣例に合わせ小文字で書く。設定が 1 つも無ければ空文字を返す
    /// </remarks>
    private static string BuildRelationshipSettings(Relationship relationship)
    {
        var settings = new List<string>();

        if (!string.IsNullOrWhiteSpace(relationship.ConstraintName))
        {
            settings.Add($"note: '{EscapeNote(relationship.ConstraintName!)}'");
        }

        if (relationship.OnDelete != ForeignKeyReferentialAction.NoAction)
        {
            settings.Add($"delete: {relationship.OnDelete.ToSqlText().ToLowerInvariant()}");
        }

        if (relationship.OnUpdate != ForeignKeyReferentialAction.NoAction)
        {
            settings.Add($"update: {relationship.OnUpdate.ToSqlText().ToLowerInvariant()}");
        }

        return settings.Count == 0 ? string.Empty : $" [{string.Join(", ", settings)}]";
    }

    /// <summary><c>Ref:</c> 行のエンドポイント列を表記する（単一列はそのまま・複数列は <c>(a, b)</c>）</summary>
    private static string FormatEndpointColumns(IReadOnlyList<string> columnNames) =>
        columnNames.Count == 1 ? columnNames[0] : $"({string.Join(", ", columnNames)})";

    /// <summary>
    /// DBML の note リテラル内で使えないシングルクォートを <c>\'</c> へエスケープする
    /// </summary>
    private static string EscapeNote(string text)
    {
        return text.Replace("'", "\\'");
    }
}
