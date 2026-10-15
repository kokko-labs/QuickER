using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Dialects;
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
    /// <param name="entity">対象エンティティ（主キー列・一意制約の正本。主キーの並びは実効順＝<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）</param>
    /// <param name="tableName">制約名の基にするテーブル名</param>
    /// <param name="quoteSimpleName">カラム名を方言のクォート方式でクォートする関数</param>
    /// <param name="quoteConstraintName">制約名を方言のクォート方式でクォートする関数（エスケープ込み）</param>
    /// <param name="limit">既定名（主キー・一意制約の合成名）に使う識別子の長さの上限（DDL・同期の方言の上限）</param>
    /// <param name="dialect">出力の方言（<see cref="DialectNames"/>。持てない一意制約を省くかを扱いの表で引く）</param>
    /// <param name="feature">DDL か同期か</param>
    /// <returns>
    /// 行頭インデント付きの制約行（区切りカンマは付かない）と、省いた一意制約の注記の行
    /// （<see cref="UniqueConstraintOnPrimaryKeyNote"/>）。制約行が無ければ <see cref="TableConstraintLines.Count"/> は 0
    /// </returns>
    public static TableConstraintLines Build(
        Entity entity,
        string tableName,
        Func<string, string> quoteSimpleName,
        Func<string, string> quoteConstraintName,
        IdentifierLengthLimit limit,
        string dialect,
        UnsupportedFeature feature
    )
    {
        var lines = new List<string>();
        var pks = entity.GetPrimaryKeyColumnsInOrder();

        // PRIMARY KEY 制約（複合 PK 対応のため列定義とは分離して出力）
        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => quoteSimpleName(p.Name)));
            lines.Add(
                $"    CONSTRAINT {quoteConstraintName(ConstraintNames.PrimaryKey(tableName, limit))} PRIMARY KEY ({pkCols})"
            );
        }

        // UNIQUE 制約（制約名が未設定なら UQ_{テーブル}_{列…} を合成する）。
        // 主キーと同じ列集合の一意制約を持てない方言では省いて注記する
        var (uniques, notes) = UniqueConstraintOnPrimaryKeyNote.Split(
            entity,
            UniqueConstraintNaming.ResolveAll(entity, tableName, limit),
            dialect,
            feature
        );

        foreach (var unique in uniques)
        {
            var uniqueCols = string.Join(", ", unique.ColumnNames.Select(quoteSimpleName));
            lines.Add($"    CONSTRAINT {quoteConstraintName(unique.Name)} UNIQUE ({uniqueCols})");
        }

        return new TableConstraintLines(lines, notes);
    }

    /// <summary>注記の行を書き、続けて制約行を書き出す（最後の行を除いて区切りカンマを付ける）</summary>
    /// <remarks>
    /// 列定義側の末尾カンマは「後続の制約行があるか」で決まるため、呼び出し側は
    /// <see cref="Build"/> の結果の件数を見てから列定義を書く。注記の行は区切りカンマを持たないコメントなので、
    /// 列定義と制約行の間に置いても構文は変わらない。
    /// </remarks>
    public static void Append(StringBuilder sb, TableConstraintLines constraints)
    {
        foreach (var note in constraints.Notes)
        {
            sb.AppendLine(note);
        }

        var lines = constraints.Lines;

        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            sb.AppendLine(lines[i] + (isLast ? string.Empty : ","));
        }
    }
}

/// <summary><see cref="TableConstraintLineBuilder.Build"/> の結果（制約行と、省いた一意制約の注記の行）</summary>
/// <param name="Lines">行頭インデント付きの制約行（区切りカンマは付かない）</param>
/// <param name="Notes">省いた一意制約の注記の行（行頭インデント付きのコメント）</param>
public sealed record TableConstraintLines(IReadOnlyList<string> Lines, IReadOnlyList<string> Notes)
{
    /// <summary>制約行の数（列定義の末尾カンマの判定に使う。注記の行は数えない）</summary>
    public int Count => Lines.Count;
}
