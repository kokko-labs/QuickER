using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using QuickER.Model;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>ER 図のテーブル定義から単一自己完結の HTML テーブル定義書を生成するサービス</summary>
/// <remarks>
/// 固定サイドバーのナビゲーション付きドキュメント型 HTML を出力する。外部リソース参照・JavaScript を持たず、
/// CSS はすべて <c>&lt;style&gt;</c> にインラインする。表記（キー・関係・参照先）は Excel 版と共有の
/// <see cref="TableDefinitionContentBuilder"/> を用いて表記ドリフトを防ぎ、固定文言は明示カルチャで解決する。
/// アンカー ID は名前昇順の連番 <c>table-{n}</c> のみで、テーブル名を ID/属性値へ入れないため同名・記号・
/// 日本語名でも衝突しない。
/// </remarks>
public static class TableDefinitionHtmlExporter
{
    /// <summary>ER 図定義からテーブル定義書の HTML 文字列を生成する</summary>
    /// <param name="diagram">対象の ER 図定義</param>
    /// <param name="culture">固定文言の言語（未指定は <see cref="CultureInfo.CurrentUICulture"/>）</param>
    /// <returns>生成済みの HTML 文字列</returns>
    public static string Build(ErDiagram diagram, CultureInfo? culture = null)
    {
        var builder = new HtmlBuilder(culture ?? CultureInfo.CurrentUICulture);

        return builder.Build(diagram);
    }

    /// <summary>テーブル定義書を HTML ファイル（UTF-8）として保存する</summary>
    /// <param name="diagram">対象の ER 図定義</param>
    /// <param name="path">出力先ファイルパス</param>
    public static void SaveTo(ErDiagram diagram, string path)
    {
        File.WriteAllText(path, Build(diagram), Encoding.UTF8);
    }

    /// <summary>1 回の HTML 生成で共有するカルチャを保持しつつ組み立てるビルダー</summary>
    /// <remarks>固定文言は <see cref="L"/> を通じて明示カルチャで解決する（静的プロパティ直読みは行わない）。</remarks>
    private sealed class HtmlBuilder(CultureInfo culture)
    {
        /// <summary>エンティティ ID からテーブル名を引くための辞書</summary>
        private IReadOnlyDictionary<Guid, Entity> _entitiesById = new Dictionary<Guid, Entity>();

        /// <summary>エンティティ ID からアンカー番号（名前昇順の連番）を引くための辞書</summary>
        private IReadOnlyDictionary<Guid, int> _entityAnchors = new Dictionary<Guid, int>();

        /// <summary>固定文言を明示カルチャで解決する（未定義キーはキー名を返す）</summary>
        private string L(string key) => Strings.ResourceManager.GetString(key, culture) ?? key;

        /// <summary>HTML 特殊文字をエスケープする（全ユーザーデータへ適用する）</summary>
        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        /// <summary>ER 図からテーブル定義書 HTML を組み立てる</summary>
        public string Build(ErDiagram diagram)
        {
            _entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
            var entities = TableDefinitionContentBuilder.OrderEntities(diagram.Entities);

            // エンティティ ID → アンカー番号（名前昇順 1 始まり連番＝Excel の No. と同一番号）
            _entityAnchors = entities
                .Select((entity, index) => new { entity.Id, Number = index + 1 })
                .ToDictionary(item => item.Id, item => item.Number);

            var builder = new StringBuilder();
            var langTag = culture.TwoLetterISOLanguageName;
            var documentTitle = L(nameof(Strings.TableDoc_DocumentTitle));

            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine($"<html lang=\"{Encode(langTag)}\">");
            builder.AppendLine("<head>");
            builder.AppendLine("<meta charset=\"utf-8\">");
            builder.AppendLine(
                "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            );
            builder.AppendLine($"<title>{Encode(documentTitle)}</title>");
            AppendStyle(builder);
            builder.AppendLine("</head>");
            builder.AppendLine("<body>");

            AppendSidebar(builder, entities, documentTitle);

            builder.AppendLine("<main>");
            AppendOverview(builder, diagram, entities.Count, documentTitle);
            AppendTableList(builder, entities);
            AppendRelationshipList(builder, diagram.Relationships);

            foreach (var entity in entities)
            {
                AppendEntityDetail(builder, entity, diagram.Relationships);
            }

            builder.AppendLine("</main>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");

            return builder.ToString();
        }

        /// <summary>全インライン CSS を出力する（外部参照・JavaScript なし）</summary>
        private static void AppendStyle(StringBuilder builder)
        {
            builder.AppendLine("<style>");
            builder.AppendLine(
                """
                * { box-sizing: border-box; }
                body {
                    margin: 0;
                    font-family: "Segoe UI", "游ゴシック", "Yu Gothic", "Meiryo", sans-serif;
                    font-size: 14px;
                    color: #202020;
                    background: #ffffff;
                }
                #sidebar {
                    position: fixed;
                    top: 0;
                    left: 0;
                    width: 240px;
                    height: 100vh;
                    overflow-y: auto;
                    background: #f3f5f9;
                    border-right: 1px solid #c9d2e0;
                    padding: 16px 12px;
                }
                #sidebar .nav-title {
                    font-weight: bold;
                    font-size: 15px;
                    color: #1F4E79;
                    margin-bottom: 12px;
                    display: block;
                    text-decoration: none;
                }
                #sidebar a { color: #1F4E79; text-decoration: none; }
                #sidebar a:hover { text-decoration: underline; }
                #sidebar ul { list-style: none; margin: 8px 0 0; padding: 0; }
                #sidebar li { margin: 2px 0; }
                #sidebar .nav-section { display: block; margin: 10px 0 4px; font-weight: bold; }
                #sidebar .nav-tables li a { display: block; padding: 2px 0 2px 8px; font-size: 13px; }
                main {
                    margin-left: 240px;
                    padding: 24px 32px;
                    max-width: 1100px;
                }
                h1 { color: #1F4E79; font-size: 26px; margin: 0 0 16px; }
                h2 { color: #1F4E79; font-size: 20px; margin: 28px 0 12px; border-bottom: 2px solid #1F4E79; padding-bottom: 4px; }
                dl.overview { display: grid; grid-template-columns: max-content 1fr; gap: 6px 24px; margin: 0; }
                dl.overview dt { font-weight: bold; color: #404040; }
                dl.overview dd { margin: 0; }
                table { border-collapse: collapse; width: 100%; margin: 8px 0 4px; }
                th, td { border: 1px solid #b9c2d0; padding: 6px 8px; text-align: left; vertical-align: top; word-break: break-word; }
                thead th {
                    background: #1F4E79;
                    color: #ffffff;
                    position: sticky;
                    top: 0;
                    z-index: 1;
                }
                tbody tr:nth-child(even) { background: #f5f7fb; }
                td a, th a { color: #1F4E79; }
                section.table-detail { margin-top: 32px; }
                p.detail-note { margin: 4px 0; }
                @media print {
                    #sidebar { display: none; }
                    main { margin-left: 0; max-width: none; }
                    section.table-detail { break-before: page; }
                }
                """
            );
            builder.AppendLine("</style>");
        }

        /// <summary>固定サイドバー（概要・一覧・各テーブルへのアンカーナビ）を出力する</summary>
        private void AppendSidebar(
            StringBuilder builder,
            IReadOnlyList<Entity> entities,
            string documentTitle
        )
        {
            builder.AppendLine("<nav id=\"sidebar\">");
            builder.AppendLine(
                $"<a class=\"nav-title\" href=\"#overview\">{Encode(documentTitle)}</a>"
            );
            builder.AppendLine(
                $"<a class=\"nav-section\" href=\"#table-list\">{Encode(L(nameof(Strings.TableDoc_Sheet_Summary)))}</a>"
            );
            builder.AppendLine(
                $"<a class=\"nav-section\" href=\"#relationship-list\">{Encode(L(nameof(Strings.TableDoc_Sheet_Relationships)))}</a>"
            );

            builder.AppendLine("<ul class=\"nav-tables\">");

            foreach (var entity in entities)
            {
                var number = _entityAnchors[entity.Id];
                builder.AppendLine(
                    $"<li><a href=\"#table-{number}\">{number}. {Encode(entity.TableName)}</a></li>"
                );
            }

            builder.AppendLine("</ul>");
            builder.AppendLine("</nav>");
        }

        /// <summary>概要セクション（文書名＋書誌情報の定義リスト）を出力する</summary>
        private void AppendOverview(
            StringBuilder builder,
            ErDiagram diagram,
            int tableCount,
            string documentTitle
        )
        {
            builder.AppendLine("<header id=\"overview\">");
            builder.AppendLine($"<h1>{Encode(documentTitle)}</h1>");
            builder.AppendLine("<dl class=\"overview\">");
            AppendDefinition(
                builder,
                L(nameof(Strings.TableDoc_Cover_TargetDbms)),
                diagram.TargetDbms
            );
            AppendDefinition(
                builder,
                L(nameof(Strings.TableDoc_Cover_TableCount)),
                tableCount.ToString(CultureInfo.InvariantCulture)
            );
            AppendDefinition(
                builder,
                L(nameof(Strings.TableDoc_Cover_RelationshipCount)),
                diagram.Relationships.Count.ToString(CultureInfo.InvariantCulture)
            );
            builder.AppendLine("</dl>");
            builder.AppendLine("</header>");
        }

        /// <summary>概要の 1 項目（ラベルと値）を定義リストへ出力する</summary>
        private static void AppendDefinition(StringBuilder builder, string label, string value)
        {
            builder.AppendLine($"<dt>{Encode(label)}</dt><dd>{Encode(value)}</dd>");
        }

        /// <summary>テーブル一覧セクション（No./テーブル名（アンカーリンク）/説明/備考）を出力する</summary>
        private void AppendTableList(StringBuilder builder, IReadOnlyList<Entity> entities)
        {
            builder.AppendLine("<section id=\"table-list\">");
            builder.AppendLine($"<h2>{Encode(L(nameof(Strings.TableDoc_Sheet_Summary)))}</h2>");
            builder.AppendLine("<table>");
            AppendHeaderRow(
                builder,
                L(nameof(Strings.TableDoc_Header_No)),
                L(nameof(Strings.TableDoc_Header_TableName)),
                L(nameof(Strings.TableDoc_Header_Description)),
                L(nameof(Strings.TableDoc_Header_Memo))
            );
            builder.AppendLine("<tbody>");

            foreach (var entity in entities)
            {
                var number = _entityAnchors[entity.Id];
                builder.AppendLine("<tr>");
                builder.AppendLine($"<td>{number}</td>");
                builder.AppendLine(
                    $"<td><a href=\"#table-{number}\">{Encode(entity.TableName)}</a></td>"
                );
                builder.AppendLine($"<td>{Encode(entity.Description)}</td>");
                builder.AppendLine($"<td>{Encode(entity.Memo)}</td>");
                builder.AppendLine("</tr>");
            }

            builder.AppendLine("</tbody>");
            builder.AppendLine("</table>");
            builder.AppendLine("</section>");
        }

        /// <summary>リレーション一覧セクション（9 列・Excel 版と同一順序。備考列は常に空のため持たない）を出力する</summary>
        private void AppendRelationshipList(
            StringBuilder builder,
            IReadOnlyList<Relationship> relationships
        )
        {
            // 並び順は Excel 版と共有ヘルパー経由で同一
            var orderedRelationships = TableDefinitionContentBuilder.OrderRelationships(
                relationships,
                _entitiesById
            );

            builder.AppendLine("<section id=\"relationship-list\">");
            builder.AppendLine(
                $"<h2>{Encode(L(nameof(Strings.TableDoc_Sheet_Relationships)))}</h2>"
            );
            builder.AppendLine("<table>");
            AppendHeaderRow(
                builder,
                L(nameof(Strings.TableDoc_Header_No)),
                L(nameof(Strings.TableDoc_Header_ConstraintName)),
                L(nameof(Strings.TableDoc_Header_SourceTable)),
                L(nameof(Strings.TableDoc_Header_SourceColumn)),
                L(nameof(Strings.TableDoc_Header_TargetTable)),
                L(nameof(Strings.TableDoc_Header_TargetColumn)),
                L(nameof(Strings.TableDoc_Header_Relation)),
                // ON DELETE / ON UPDATE は SQL 用語のためリテラル維持
                "ON DELETE",
                "ON UPDATE"
            );
            builder.AppendLine("<tbody>");

            for (var i = 0; i < orderedRelationships.Count; i++)
            {
                var relationship = orderedRelationships[i];

                // 参照元（FK 保有側）は Target、参照先（PK 側）は Source に対応する（Excel 版と同一）
                var sourceTable = TableDefinitionContentBuilder.TableNameOf(
                    _entitiesById,
                    relationship.TargetEntityId
                );
                var sourceColumn = TableDefinitionContentBuilder.ColumnNameOf(
                    _entitiesById,
                    relationship.TargetEntityId,
                    relationship.TargetColumnId
                );
                var targetTable = TableDefinitionContentBuilder.TableNameOf(
                    _entitiesById,
                    relationship.SourceEntityId
                );
                var targetColumn = TableDefinitionContentBuilder.ColumnNameOf(
                    _entitiesById,
                    relationship.SourceEntityId,
                    relationship.SourceColumnId
                );

                builder.AppendLine("<tr>");
                builder.AppendLine($"<td>{i + 1}</td>");
                builder.AppendLine(
                    $"<td>{Encode(relationship.ConstraintName ?? string.Empty)}</td>"
                );
                builder.AppendLine(
                    $"<td>{TableCellWithLink(relationship.TargetEntityId, sourceTable)}</td>"
                );
                builder.AppendLine($"<td>{Encode(sourceColumn)}</td>");
                builder.AppendLine(
                    $"<td>{TableCellWithLink(relationship.SourceEntityId, targetTable)}</td>"
                );
                builder.AppendLine($"<td>{Encode(targetColumn)}</td>");
                builder.AppendLine(
                    $"<td>{Encode(TableDefinitionContentBuilder.GetRelationshipTypeLabel(relationship.Type))}</td>"
                );
                builder.AppendLine($"<td>{Encode(relationship.OnDelete.ToDisplayText())}</td>");
                builder.AppendLine($"<td>{Encode(relationship.OnUpdate.ToDisplayText())}</td>");
                builder.AppendLine("</tr>");
            }

            builder.AppendLine("</tbody>");
            builder.AppendLine("</table>");
            builder.AppendLine("</section>");
        }

        /// <summary>テーブル名セルを該当詳細セクションのアンカーへリンク化する（未解決時はテキストのみ）</summary>
        private string TableCellWithLink(Guid entityId, string tableName)
        {
            if (
                !string.IsNullOrEmpty(tableName)
                && _entityAnchors.TryGetValue(entityId, out var number)
            )
            {
                return $"<a href=\"#table-{number}\">{Encode(tableName)}</a>";
            }

            return Encode(tableName);
        }

        /// <summary>テーブル単位の詳細セクション（説明・備考＋カラム表）を出力する</summary>
        private void AppendEntityDetail(
            StringBuilder builder,
            Entity entity,
            IReadOnlyList<Relationship> relationships
        )
        {
            var number = _entityAnchors[entity.Id];
            var relatedRelationships = relationships
                .Where(relationship =>
                    relationship.SourceEntityId == entity.Id
                    || relationship.TargetEntityId == entity.Id
                )
                .ToList();
            var foreignKeyLabels = TableDefinitionContentBuilder.BuildForeignKeyLabels(
                entity,
                relatedRelationships,
                _entitiesById
            );
            var requiredMark = L(nameof(Strings.TableDoc_RequiredMark));

            builder.AppendLine($"<section class=\"table-detail\" id=\"table-{number}\">");
            builder.AppendLine($"<h2>{number}. {Encode(entity.TableName)}</h2>");

            // 説明・備考は値があるときのみ出力する
            if (!string.IsNullOrEmpty(entity.Description))
            {
                builder.AppendLine($"<p class=\"detail-note\">{Encode(entity.Description)}</p>");
            }

            if (!string.IsNullOrEmpty(entity.Memo))
            {
                builder.AppendLine($"<p class=\"detail-note\">{Encode(entity.Memo)}</p>");
            }

            builder.AppendLine("<table>");
            AppendHeaderRow(
                builder,
                L(nameof(Strings.TableDoc_Header_No)),
                L(nameof(Strings.TableDoc_Header_ColumnName)),
                L(nameof(Strings.TableDoc_Header_Description)),
                L(nameof(Strings.TableDoc_Header_DataType)),
                L(nameof(Strings.TableDoc_Header_Required)),
                L(nameof(Strings.TableDoc_Header_Key)),
                L(nameof(Strings.TableDoc_Header_Reference))
            );
            builder.AppendLine("<tbody>");

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var column = entity.Columns[i];
                var keyLabel = TableDefinitionContentBuilder.GetKeyLabel(
                    column,
                    foreignKeyLabels.TryGetValue(column.Id, out var foreignKeyLabel)
                        ? foreignKeyLabel
                        : null
                );

                builder.AppendLine("<tr>");
                builder.AppendLine($"<td>{i + 1}</td>");
                builder.AppendLine($"<td>{Encode(column.Name)}</td>");
                builder.AppendLine($"<td>{Encode(column.Description)}</td>");
                builder.AppendLine($"<td>{Encode(column.DataType)}</td>");
                builder.AppendLine(
                    $"<td>{Encode(column.IsNullable ? string.Empty : requiredMark)}</td>"
                );
                builder.AppendLine($"<td>{Encode(keyLabel)}</td>");
                builder.AppendLine(
                    $"<td>{BuildReferenceCell(entity, column, relatedRelationships)}</td>"
                );
                builder.AppendLine("</tr>");
            }

            builder.AppendLine("</tbody>");
            builder.AppendLine("</table>");
            builder.AppendLine("</section>");
        }

        /// <summary>外部キー列の参照先セルを構築する（単一参照時は該当詳細セクションへリンク化）</summary>
        /// <remarks>表示文字列は <see cref="TableDefinitionContentBuilder.GetReferenceText"/> と同一になる。</remarks>
        private string BuildReferenceCell(
            Entity entity,
            Column column,
            IReadOnlyList<Relationship> relationships
        )
        {
            var referenceText = TableDefinitionContentBuilder.GetReferenceText(
                entity,
                column,
                relationships,
                _entitiesById
            );

            if (string.IsNullOrEmpty(referenceText))
            {
                return string.Empty;
            }

            // 参照先が単一テーブルのときは該当詳細セクションへリンク化（複数参照時はテキストのみ）
            var referencedIds = TableDefinitionContentBuilder.GetReferencedEntityIds(
                entity,
                column,
                relationships
            );

            if (
                referencedIds.Count == 1
                && _entityAnchors.TryGetValue(referencedIds[0], out var number)
            )
            {
                return $"<a href=\"#table-{number}\">{Encode(referenceText)}</a>";
            }

            return Encode(referenceText);
        }

        /// <summary>表の見出し行（<c>thead</c>）を出力する</summary>
        private static void AppendHeaderRow(StringBuilder builder, params string[] headers)
        {
            builder.AppendLine("<thead><tr>");

            foreach (var header in headers)
            {
                builder.AppendLine($"<th>{Encode(header)}</th>");
            }

            builder.AppendLine("</tr></thead>");
        }
    }
}
