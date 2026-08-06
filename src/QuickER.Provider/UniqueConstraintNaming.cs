using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// DDL 出力用に <see cref="UniqueConstraint"/> の制約名・構成列名を解決する共通ヘルパー（5 方言が共有する）
/// </summary>
/// <remarks>
/// 制約名がモデルに無い場合の合成規則は、外部キーの <c>FK_{子}_{親}</c>（<see cref="DdlGeneratorBase"/>）と
/// 対称に <c>UQ_{テーブル名}_{列名…}</c>（宣言順の連結）とする。規則そのものの正本は
/// <see cref="UniqueConstraint.SynthesizeName"/>（モデル側＝Provider を参照しない MCP ツールホストとも共有する）で、
/// 識別子として安全な形への正規化は方言ごとの <c>SafeName</c> に委ねる。
/// </remarks>
public static class UniqueConstraintNaming
{
    /// <summary>制約名を解決する（モデルに名前があればそのまま、無ければ列構成から合成する）</summary>
    /// <param name="constraintName">モデルが保持する制約名（未設定は <c>null</c> / 空）</param>
    /// <param name="tableName">対象テーブル名</param>
    /// <param name="columnNames">構成列名（宣言順）</param>
    /// <param name="safeName">方言別の識別子安全化関数（"." と空白を "_" へ置換する等）</param>
    /// <returns>DDL へ出力する制約名（クォートは呼び出し側の責務）</returns>
    public static string Resolve(
        string? constraintName,
        string tableName,
        IReadOnlyList<string> columnNames,
        Func<string, string> safeName
    )
    {
        if (!string.IsNullOrWhiteSpace(constraintName))
        {
            return constraintName;
        }

        // 合成規則そのものはモデル側（UniqueConstraint.SynthesizeName）が正本。ここは方言別の安全化を挟むだけ
        return UniqueConstraint.SynthesizeName(safeName(tableName), columnNames.Select(safeName));
    }

    /// <summary>エンティティの一意制約を「解決済み制約名 ＋ 宣言順の構成列名」へ展開する</summary>
    /// <param name="entity">対象エンティティ</param>
    /// <param name="safeName">方言別の識別子安全化関数</param>
    /// <returns>DDL へ出力可能な一意制約の一覧（モデルの並び順を保つ）</returns>
    /// <remarks>
    /// 構成列が空の制約、およびエンティティ内で解決できないカラム ID を含む制約は黙って除外する
    /// （SQLite の同期再構築が壊れた参照を無視するのと同じ流儀。DDL 生成は編集操作ではないため
    /// 不整合を例外にせず、出力可能なものだけを出す）。
    /// </remarks>
    public static List<ResolvedUniqueConstraint> ResolveAll(
        Entity entity,
        Func<string, string> safeName
    )
    {
        var resolved = new List<ResolvedUniqueConstraint>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (!TryResolveColumnNames(entity, constraint, out var columnNames))
            {
                continue;
            }

            resolved.Add(
                new ResolvedUniqueConstraint(
                    Resolve(constraint.Name, entity.TableName, columnNames, safeName),
                    columnNames
                )
            );
        }

        return resolved;
    }

    /// <summary>一意制約の構成列 ID を、エンティティ内の列名（宣言順）へ解決する</summary>
    /// <param name="entity">構成列の解決に用いるエンティティ</param>
    /// <param name="constraint">対象の一意制約</param>
    /// <param name="columnNames">解決できた場合の構成列名（宣言順）。失敗時は空リスト</param>
    /// <returns>構成列が 1 つ以上あり、そのすべてを解決できたら <c>true</c></returns>
    /// <remarks>
    /// DDL 生成（<see cref="ResolveAll"/>）・差分計算（<see cref="SchemaDiffService"/>）・
    /// 実行計画（<see cref="SyncPlanner"/>）が同じ「解決できない制約は無かったことにする」規則を共有するため、
    /// 判定をここへ 1 本化する。
    /// </remarks>
    public static bool TryResolveColumnNames(
        Entity entity,
        UniqueConstraint constraint,
        out List<string> columnNames
    )
    {
        columnNames = [];

        if (constraint.ColumnIds.Count == 0)
        {
            return false;
        }

        foreach (var columnId in constraint.ColumnIds)
        {
            var column = entity.Columns.FirstOrDefault(c => c.Id == columnId);

            if (column is null)
            {
                columnNames = [];
                return false;
            }

            columnNames.Add(column.Name);
        }

        return true;
    }

    /// <summary>一意制約の同一性判定に使う列集合シグネチャを作る（大文字小文字を無視し、順序は問わない）</summary>
    /// <param name="columnNames">構成列名</param>
    /// <remarks>
    /// 制約名は DB 側の実名と図側の未設定（<c>null</c>＝合成名）で恒常的に食い違うため、差分・計画の照合は
    /// 「どの列に一意性を課しているか」だけで行う。順序を無視するのは、UNIQUE の意味論が列の並びに依存しない
    /// ため（インデックスの走査効率は変わるが、制約としては同一）。
    /// </remarks>
    public static string ColumnSetSignature(IEnumerable<string> columnNames) =>
        string.Join(
            "|",
            columnNames
                .Select(name => (name ?? string.Empty).Trim().ToLowerInvariant())
                .OrderBy(name => name, StringComparer.Ordinal)
        );
}

/// <summary>DDL 出力用に解決済みの一意制約 1 件</summary>
/// <param name="Name">解決済みの制約名（モデルの名前、または合成名）</param>
/// <param name="ColumnNames">構成列名（宣言順）</param>
public sealed record ResolvedUniqueConstraint(string Name, IReadOnlyList<string> ColumnNames);
