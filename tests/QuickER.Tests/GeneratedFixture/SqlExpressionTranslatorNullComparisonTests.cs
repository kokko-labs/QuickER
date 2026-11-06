using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> の等値比較の翻訳のうち、生成 SQL の断片でしか言えない表明
/// （パラメータの <c>ColumnName</c>・等値以外の否定を <c>NOT (...)</c> で包む形）だけを固定する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// null の意味論（値の側が null の <c>==</c> / <c>!=</c> の <c>IS [NOT] NULL</c> への補償・列側の <c>!=</c> の補償・
/// 列どうしの補償・否定の反転と二重否定の畳み込み・bool 列の短縮形）は、実行結果を C# の意味論と比べる
/// 翻訳器の一致の述語の表（Integration の <c>TranslatorParityPredicateRows</c>。SQL Server・SQLite・インメモリ）と、
/// 値オブジェクトの列は実装先の一致（<c>BackendParityRuntimeTests</c> のシナリオ <c>query-null-comparison-and-ordering</c>）が守る。
/// </para>
/// <para>
/// 射程（既知の乖離）: 補償が効くのは「否定が比較に直接乗っている」場合だけで、論理演算子を挟んだ複合否定
/// （<c>!(a == b &amp;&amp; c)</c> など）は De Morgan 展開されず <c>NOT (...)</c> で包まれる。NULL 許容の列の関係演算の否定も
/// 同じく <c>NOT (UNKNOWN)</c> で NULL の行が落ち、C# とは割れるので、結果の比較では固定できない。ここではその形を固定する。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorNullComparisonTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public string? Name1 { get; set; }
        public int? A { get; set; }
    }

    /// <summary>SQL Server 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static (string Sql, List<SqlServerParam> Parameters) RunSqlServer(
        Expression<Func<Probe, bool>> predicate
    )
    {
        var parameters = new List<SqlServerParam>();
        var sql = SqlServerTranslator.ToCondition(predicate.Body, parameters);
        return (sql, parameters);
    }

    // 残す理由（P3-6）：パラメータの ColumnName（明示型付けに使う生成ランタイムの内部契約）は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 value-equals）
    [Fact(DisplayName = "回帰: null でない変数はパラメータ化される")]
    public void NonNullVariable_StillParameterizes()
    {
        var name = "Alice";

        var sqlServer = RunSqlServer(p => p.Name1 == name);
        sqlServer.Parameters.Should().ContainSingle();
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");
    }

    // 残す理由（P3-6）：補償した != でもパラメータの ColumnName が対辺の列名になることは実行結果では言えない（移した先：TranslatorParityPredicateRows の行 value-not-equals）
    [Fact(
        DisplayName = "null でない値との != は (col <> @p OR col IS NULL) へ補償される（両方言）"
    )]
    public void NotEqualNonNullValue_CompensatesNullColumn()
    {
        var name = "Alice";

        var sqlServer = RunSqlServer(p => p.Name1 != name);
        sqlServer.Parameters.Should().ContainSingle("補償しても値は 1 回だけバインドする");
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");
    }

    // 残す理由（P3-6）：等値以外の否定を NOT (...) で包む形は、NULL 許容の列では C# と割れる既知の乖離のため結果では固定できない（移した先：TranslatorParityPredicateRows の行 relational-negated-greater-not-null-column）
    [Fact(DisplayName = "対照: 等値以外の否定は NOT (...) で包まれる")]
    public void NegatedNonEqualityComparison_StillWrapsInNot()
    {
        RunSqlServer(p => !(p.A > 1)).Sql.Should().Be("NOT ([A] > @p0)");
    }
}
