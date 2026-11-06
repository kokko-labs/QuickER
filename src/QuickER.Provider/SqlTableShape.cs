using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 図の表が DDL・同期スクリプトの <c>CREATE TABLE</c> へ出せる形か（列を 1 本以上持つか）を判定する。
/// </summary>
/// <remarks>
/// <para>
/// 列の無い表は <c>CREATE TABLE t ( );</c> になり、SQL Server・MySQL・Oracle・SQLite は構文エラーで断る。PostgreSQL だけは
/// 列の無い表を作るが、取り込み直すと列を読めない表（<see cref="SchemaImportWarningKind.TableColumnsUnavailable"/>）になって
/// 図へ戻らない。方言で結果を割らないよう、5 方言とも同じ判定で断る（方言の宣言に差を持たせない）。
/// </para>
/// <para>
/// 図には列の無い表が入り得る（MCP の <c>add_entity</c> の直後・GUI で全列を消した表・列を復元できなかった C# リバース・
/// 列の行の無い Excel 定義書のシート）。DDL は入口（<see cref="Validate(ErDiagram)"/>）で図全体を止め、同期は計画
/// （<see cref="SyncPlanner"/>）がその表の追加を外して <see cref="SyncPlanWarningKind.TableWithoutColumns"/> で名指しする。
/// 同期スクリプトの入口（<see cref="Validate(SyncPlan)"/>）は、計画を経ずに組んだ計画への二重の守り。
/// 判定は <see cref="HasNoColumns"/>（差分の項目では <see cref="IsAddTableWithoutColumns"/>）の 1 つだけで、3 つの経路が共有する。
/// </para>
/// </remarks>
public static class SqlTableShape
{
    /// <summary>表が列を 1 本も持たないか（<c>CREATE TABLE</c> を出せないか）</summary>
    /// <param name="entity">表</param>
    public static bool HasNoColumns(Entity entity) => entity.Columns.Count == 0;

    /// <summary>差分の項目が列の無い表の追加か（計画の除外と同期スクリプトの入口が共有する）</summary>
    /// <param name="item">差分の項目</param>
    public static bool IsAddTableWithoutColumns(SchemaDiffItem item) =>
        item.Kind == SchemaDiffKind.AddTable && item.Entity is { } entity && HasNoColumns(entity);

    /// <summary>図に列の無い表があれば名指しで例外を投げる（DDL 生成の入口）</summary>
    /// <param name="diagram">図</param>
    /// <exception cref="InvalidOperationException">列の無い表がある場合</exception>
    public static void Validate(ErDiagram diagram) =>
        Validate(diagram.Entities.Where(HasNoColumns).Select(entity => entity.TableName));

    /// <summary>実行計画に列の無い表を作る項目があれば名指しで例外を投げる（同期生成の入口）</summary>
    /// <remarks>
    /// 表を作る経路は 2 つ＝表の追加のセクション項目（逐次 DDL の方言）と、テーブル再構築の合成後の定義（SQLite。表の追加は
    /// 作るだけの再構築になる）。
    /// </remarks>
    /// <param name="plan">実行計画</param>
    /// <exception cref="InvalidOperationException">列の無い表を作る項目がある場合</exception>
    public static void Validate(SyncPlan plan) =>
        Validate(
            plan.Sections.SelectMany(section => section.Items)
                .Where(IsAddTableWithoutColumns)
                .Select(item => item.TableName)
                .Concat(
                    plan.Rebuilds.Where(rebuild => HasNoColumns(rebuild.NewDefinition))
                        .Select(rebuild => rebuild.TableName)
                )
        );

    /// <summary>列の無い表の名前を並べて例外を投げる（例外文は英語が正本）</summary>
    /// <remarks>
    /// 名前の改行・制御文字は先に <see cref="SqlNameText.Validate(ErDiagram, IdentifierLengthLimit)"/> が断るので、
    /// ここでは名前をそのまま載せる。場所の表記は <see cref="SqlNameText"/> と同じ SQL の綴り（<c>TABLE 'x'</c>）。
    /// </remarks>
    private static void Validate(IEnumerable<string> tableNames)
    {
        var tables = tableNames
            .Select(name => $"TABLE '{name}'")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tables.Count > 0)
        {
            throw new InvalidOperationException(
                "The following tables have no columns and cannot be created: "
                    + string.Join(", ", tables)
                    + ". Add at least one column to each of them in the diagram."
            );
        }
    }
}
