using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using QueryFixtureMemo = QuickER.Tests.GeneratedQueryFixture.MemoValue;
using QueryFixtureOrder = QuickER.Tests.GeneratedQueryFixture.OrderEntity;
using QueryFixtureParam = QuickER.Tests.GeneratedQueryFixture.SqlQueryParameter;
using QueryFixtureTranslator = QuickER.Tests.GeneratedQueryFixture.SqlExpressionTranslator;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> の <c>collection.Contains(x.Col)</c>（IN 検索）の翻訳のうち、
/// 生成 SQL の断片でしか言えない表明（パラメータの <c>ColumnName</c>・null の要素をパラメータにしない数）だけを固定する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// null の意味論（null の要素を <c>IS NULL</c> へ畳む・NOT IN の列側の補償・全要素が null・空のリストの定数条件・否定の畳み込み）は、
/// 実行結果を C# の意味論と比べる翻訳器の一致の述語の表（Integration の <c>TranslatorParityPredicateRows</c>。SQL Server・SQLite・
/// インメモリ）と、値オブジェクトの列は実装先の一致（<c>BackendParityRuntimeTests</c> のシナリオ
/// <c>query-null-comparison-and-ordering</c>・<c>BackendParitySqlServerRuntimeTests</c> のケース <c>value-object-in-null-semantics</c>）が守る。
/// </para>
/// <para>
/// null の要素はパラメータにせず列への <c>IS NULL</c> テストへ畳むので、パラメータの数は null でない要素の数になる。
/// SQL Server の 1 文あたりのパラメータの上限（2,100）に効くが、行の集合には現れないので、ここで数を固定する。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorInClauseTests
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

    /// <summary>SQLite 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static (string Sql, List<SqliteParam> Parameters) RunSqlite(
        Expression<Func<Probe, bool>> predicate
    )
    {
        var parameters = new List<SqliteParam>();
        var sql = SqliteTranslator.ToCondition(predicate.Body, parameters);
        return (sql, parameters);
    }

    // 残す理由（P3-6）：IN のパラメータの ColumnName（明示型付けに使う生成ランタイムの内部契約）は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 in-list）
    [Fact(
        DisplayName = "回帰: null を含まないリストの IN は素の IN (...) のまま（括弧も補償も足さない）"
    )]
    public void In_WithoutNullElements_KeepsPlainInClause()
    {
        var names = new List<string?> { "Alice", "Bob" };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Parameters.Should().HaveCount(2);
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");
    }

    // 残す理由（P3-6）：IN の null の要素をパラメータにしない数は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 in-list-with-null）
    [Fact(DisplayName = "null 要素を含む IN は (IN (...) OR IS NULL) へ補償される（両方言）")]
    public void In_WithNullElement_FoldsNullIntoIsNull()
    {
        var names = new List<string?> { "Alice", null };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Parameters.Should().ContainSingle("null 要素はパラメータ化しない");
        sqlServer.Parameters[0].Value.Should().Be("Alice");

        var sqlite = RunSqlite(p => names.Contains(p.Name1));
        sqlite.Parameters.Should().ContainSingle();
    }

    // 残す理由（P3-6）：IN の null の要素をパラメータにしない数は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 in-list-only-null）
    [Fact(DisplayName = "全要素が null の IN は IS NULL 単独になる（パラメータなし）")]
    public void In_WithOnlyNullElements_EmitsIsNull()
    {
        var names = new List<string?> { null, null };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Parameters.Should().BeEmpty();
    }

    // 残す理由（P3-6）：NOT IN の null の要素をパラメータにしない数は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 not-in-list-with-null）
    [Fact(
        DisplayName = "null 要素を含む NOT IN は (NOT IN (...) AND IS NOT NULL) になる（null は一致なので否定では落とす）"
    )]
    public void NotIn_WithNullElement_ExcludesNullRows()
    {
        var names = new List<string?> { "Alice", null };

        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Parameters.Should().ContainSingle();
    }

    // 残す理由（P3-6）：NOT IN の null の要素をパラメータにしない数は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 not-in-list-only-null）
    [Fact(DisplayName = "全要素が null の NOT IN は IS NOT NULL 単独になる")]
    public void NotIn_WithOnlyNullElements_EmitsIsNotNull()
    {
        var names = new List<string?> { null };

        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Parameters.Should().BeEmpty();
    }

    // 残す理由（P3-6）：null 許容の値型の IN で null の要素をパラメータにしないこと（値の並び）は実行結果では言えない（移した先：TranslatorParityPredicateRows の行 in-nullable-int-list-with-null）
    [Fact(DisplayName = "null 許容値型のリストでも null 要素が IS NULL へ畳まれる")]
    public void In_WithNullableValueTypeElements_FoldsNullIntoIsNull()
    {
        var numbers = new List<int?> { 1, null, 3 };

        var sqlServer = RunSqlServer(p => numbers.Contains(p.A));
        sqlServer.Parameters.Select(x => x.Value).Should().Equal(1, 3);
    }

    // 残す理由（P3-6）：値オブジェクトの列の IN で null の要素をパラメータにしない数は実行結果では言えない（移した先：BackendParityRuntimeTests のシナリオ query-null-comparison-and-ordering の期待 query-in-with-null-element）
    [Fact(
        DisplayName = "値オブジェクト列の IN でも null 要素は IS NULL へ畳まれ、非 null 要素は生値へ解ける（VO 有効の図）"
    )]
    public void ValueObjectColumn_In_FoldsNullAndUnwrapsValues()
    {
        var memos = new List<QueryFixtureMemo?> { QueryFixtureMemo.Create("urgent"), null };

        var parameters = new List<QueryFixtureParam>();
        var predicate =
            (Expression<Func<QueryFixtureOrder, bool>>)(order => memos.Contains(order.Memo));

        QueryFixtureTranslator.ToCondition(predicate.Body, parameters);
        parameters.Should().ContainSingle("null 要素はパラメータ化しない");
    }
}
