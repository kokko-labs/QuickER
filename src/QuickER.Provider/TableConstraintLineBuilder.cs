using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// <c>CREATE TABLE</c> の列定義の後ろへまとめて出す制約行（<c>PRIMARY KEY</c> → <c>UNIQUE</c>）を組み立てる共通ヘルパー
/// </summary>
/// <remarks>
/// <para>
/// 同じテーブルを「DDL 生成（<see cref="DdlGeneratorBase"/>）で作る」場合と「差分同期の <c>CREATE TABLE</c> で作る」場合とで
/// 出来上がる形が違ってはならない。どの制約行をどの順で出すかという知識をここへ 1 本化し、
/// 逐次 DDL 方言 4 つ（SQL Server / PostgreSQL / MySQL / Oracle）の同期と DDL 生成が同じ実装を共有する。
/// </para>
/// <para>
/// SQLite は外部キーもインラインへ出すテーブル再構築方式のため本ヘルパーを使わず、独自に組み立てる
/// （制約行の集合そのものが違うため共有の利が無い）。
/// </para>
/// </remarks>
public static class TableConstraintLineBuilder
{
    /// <summary>制約行（<c>PRIMARY KEY</c> → <c>UNIQUE</c> の順）を組み立てる</summary>
    /// <param name="entity">対象エンティティ（主キー列・一意制約の正本）</param>
    /// <param name="tableName">制約名の基にするテーブル名</param>
    /// <param name="quoteSimpleName">カラム名を方言のクォート方式でクォートする関数</param>
    /// <param name="quoteConstraintName">制約名を方言のクォート方式でクォートする関数（エスケープ込み）</param>
    /// <param name="safeName">制約名に使う安全な ID を作る方言別の関数</param>
    /// <returns>行頭インデント付きの制約行（区切りカンマは付かない）。該当が無ければ空リスト</returns>
    public static List<string> Build(
        Entity entity,
        string tableName,
        Func<string, string> quoteSimpleName,
        Func<string, string> quoteConstraintName,
        Func<string, string> safeName
    )
    {
        var lines = new List<string>();
        var pks = entity.Columns.Where(c => c.IsPrimaryKey).ToList();

        // PRIMARY KEY 制約（複合 PK 対応のため列定義とは分離して出力）
        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => quoteSimpleName(p.Name)));
            lines.Add(
                $"    CONSTRAINT {quoteConstraintName($"PK_{safeName(tableName)}")} PRIMARY KEY ({pkCols})"
            );
        }

        // UNIQUE 制約（制約名が未設定なら UQ_{テーブル}_{列…} を合成する）
        foreach (var unique in UniqueConstraintNaming.ResolveAll(entity, tableName, safeName))
        {
            var uniqueCols = string.Join(", ", unique.ColumnNames.Select(quoteSimpleName));
            lines.Add($"    CONSTRAINT {quoteConstraintName(unique.Name)} UNIQUE ({uniqueCols})");
        }

        return lines;
    }

    /// <summary>制約行を書き出す（最後の行を除いて区切りカンマを付ける）</summary>
    /// <remarks>
    /// 列定義側の末尾カンマは「後続の制約行があるか」で決まるため、呼び出し側は
    /// <see cref="Build"/> の結果の件数を見てから列定義を書く。
    /// </remarks>
    public static void Append(StringBuilder sb, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            sb.AppendLine(lines[i] + (isLast ? string.Empty : ","));
        }
    }
}
