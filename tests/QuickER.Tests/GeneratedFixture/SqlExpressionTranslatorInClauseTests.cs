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
using QueryFixtureUnwrap = QuickER.Tests.GeneratedQueryFixture.SqlParameterValue;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が <c>collection.Contains(x.Col)</c>（IN 検索）の null 意味論を
/// C#／EF Core へ揃えることを、生成 SQL 文字列の固定で検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// null 要素: SQL の IN はどの値も NULL とは一致しない（比較が UNKNOWN になる）ため、コレクションに混ざった null を
/// そのままパラメータ化しても「列が NULL の行」には決して当たらない。C#（インメモリ実行器の式木コンパイル）も
/// EF Core も <c>list.Contains(col)</c> を「list に null が入っていれば col が null の行は一致」と読むので、
/// null 要素はパラメータ化せず列への <c>IS NULL</c> テストへ畳む。
/// </para>
/// <para>
/// NOT IN の列側: <c>NOT ([col] IN (...))</c> は列が NULL の行で UNKNOWN になり、その行が結果から落ちる。C# も
/// EF Core も「null は非 null 値のリストに含まれない＝否定は真」と扱うため、ここが 3 実装先の乖離になる。
/// <c>!=</c> の列側補償と同じ理由で、列の NULL 許容性は式木から確実には判定できないので無条件に
/// <c>OR [col] IS NULL</c> を足す（非 NULL 列では追加句が成立しないだけで意味不変）。null 要素が混ざっている
/// ときは逆に「null は一致する＝否定では落とす」ので <c>AND [col] IS NOT NULL</c> になる。
/// </para>
/// <para>
/// 否定の経路: <c>!(list.Contains(col))</c> を <c>NOT (...)</c> で包むと補償の外側に出てしまうため、単項 Not の
/// 枝が否定を IN 句の組み立て側へ畳み込む（等値比較の演算子反転と同じ流儀）。二重否定は入口で畳まれるので、
/// <c>!(!(list.Contains(col)))</c> は素の IN と同じ形へ戻る。
/// </para>
/// <para>
/// 空リスト: <c>IN ()</c> は SQL として不正なので定数条件へ倒す。IN は <c>1 = 0</c>（全行不一致）、NOT IN は
/// <c>1 = 1</c>（全行一致）で、いずれも C# の <c>Contains</c> と同じ結果になる。全要素が null のリストは
/// null を畳んだ結果として比較すべき値が残らないため、<c>IS NULL</c>／<c>IS NOT NULL</c> 単独になる。
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

    /// <summary>
    /// 述語本体を指定回数だけ明示的な単項 Not で包む（<c>!!x</c> と書くと C# コンパイラが畳んでしまう可能性があるため、
    /// 木の形をテスト側で確定させる）。
    /// </summary>
    private static Expression Negate(Expression<Func<Probe, bool>> predicate, int times)
    {
        Expression body = predicate.Body;

        for (var index = 0; index < times; index++)
        {
            body = Expression.Not(body);
        }

        return body;
    }

    /// <summary>SQL Server 方言のトランスレータで、組み立て済みの式木を条件へ変換する</summary>
    private static (string Sql, List<SqlServerParam> Parameters) RunSqlServerBody(Expression body)
    {
        var parameters = new List<SqlServerParam>();
        var sql = SqlServerTranslator.ToCondition(body, parameters);
        return (sql, parameters);
    }

    [Fact(
        DisplayName = "回帰: null を含まないリストの IN は素の IN (...) のまま（括弧も補償も足さない）"
    )]
    public void In_WithoutNullElements_KeepsPlainInClause()
    {
        var names = new List<string?> { "Alice", "Bob" };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("[Name1] IN (@p0, @p1)");
        sqlServer.Parameters.Should().HaveCount(2);
        sqlServer.Parameters.Select(x => x.Value).Should().Equal("Alice", "Bob");
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");

        var sqlite = RunSqlite(p => names.Contains(p.Name1));
        sqlite.Sql.Should().Be("\"Name1\" IN (@p0, @p1)");
        sqlite.Parameters.Should().HaveCount(2);
    }

    [Fact(DisplayName = "null 要素を含む IN は (IN (...) OR IS NULL) へ補償される（両方言）")]
    public void In_WithNullElement_FoldsNullIntoIsNull()
    {
        var names = new List<string?> { "Alice", null };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("([Name1] IN (@p0) OR [Name1] IS NULL)");
        sqlServer.Parameters.Should().ContainSingle("null 要素はパラメータ化しない");
        sqlServer.Parameters[0].Value.Should().Be("Alice");

        var sqlite = RunSqlite(p => names.Contains(p.Name1));
        sqlite.Sql.Should().Be("(\"Name1\" IN (@p0) OR \"Name1\" IS NULL)");
        sqlite.Parameters.Should().ContainSingle();
    }

    [Fact(DisplayName = "全要素が null の IN は IS NULL 単独になる（パラメータなし）")]
    public void In_WithOnlyNullElements_EmitsIsNull()
    {
        var names = new List<string?> { null, null };

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("[Name1] IS NULL");
        sqlServer.Parameters.Should().BeEmpty();

        RunSqlite(p => names.Contains(p.Name1)).Sql.Should().Be("\"Name1\" IS NULL");
    }

    [Fact(DisplayName = "回帰: 空リストの IN は 1 = 0（全行不一致）のまま")]
    public void In_WithEmptyCollection_EmitsAlwaysFalse()
    {
        var names = new List<string?>();

        var sqlServer = RunSqlServer(p => names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("1 = 0");
        sqlServer.Parameters.Should().BeEmpty();

        RunSqlite(p => names.Contains(p.Name1)).Sql.Should().Be("1 = 0");
    }

    [Fact(
        DisplayName = "null を含まないリストの NOT IN は (NOT IN (...) OR IS NULL) へ補償される（両方言）"
    )]
    public void NotIn_WithoutNullElements_CompensatesNullColumn()
    {
        var names = new List<string?> { "Alice", "Bob" };

        // 素の NOT ([Name1] IN (...)) は列が NULL の行を UNKNOWN で落とすが、C# も EF Core もその行を残す
        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("([Name1] NOT IN (@p0, @p1) OR [Name1] IS NULL)");
        sqlServer.Parameters.Should().HaveCount(2, "補償しても値は 1 回ずつしかバインドしない");

        var sqlite = RunSqlite(p => !names.Contains(p.Name1));
        sqlite.Sql.Should().Be("(\"Name1\" NOT IN (@p0, @p1) OR \"Name1\" IS NULL)");
    }

    [Fact(
        DisplayName = "null 要素を含む NOT IN は (NOT IN (...) AND IS NOT NULL) になる（null は一致なので否定では落とす）"
    )]
    public void NotIn_WithNullElement_ExcludesNullRows()
    {
        var names = new List<string?> { "Alice", null };

        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("([Name1] NOT IN (@p0) AND [Name1] IS NOT NULL)");
        sqlServer.Parameters.Should().ContainSingle();

        RunSqlite(p => !names.Contains(p.Name1))
            .Sql.Should()
            .Be("(\"Name1\" NOT IN (@p0) AND \"Name1\" IS NOT NULL)");
    }

    [Fact(DisplayName = "全要素が null の NOT IN は IS NOT NULL 単独になる")]
    public void NotIn_WithOnlyNullElements_EmitsIsNotNull()
    {
        var names = new List<string?> { null };

        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("[Name1] IS NOT NULL");
        sqlServer.Parameters.Should().BeEmpty();

        RunSqlite(p => !names.Contains(p.Name1)).Sql.Should().Be("\"Name1\" IS NOT NULL");
    }

    [Fact(DisplayName = "空リストの NOT IN は 1 = 1（全行一致）になる")]
    public void NotIn_WithEmptyCollection_EmitsAlwaysTrue()
    {
        var names = new List<string?>();

        var sqlServer = RunSqlServer(p => !names.Contains(p.Name1));
        sqlServer.Sql.Should().Be("1 = 1", "C# の !list.Contains(x) は空リストなら全行 true");
        sqlServer.Parameters.Should().BeEmpty();

        RunSqlite(p => !names.Contains(p.Name1)).Sql.Should().Be("1 = 1");
    }

    [Fact(DisplayName = "IN の二重否定は畳み込まれ、素の IN とまったく同じ形になる")]
    public void DoubleNegatedIn_CollapsesToPlainInClause()
    {
        var names = new List<string?> { "Alice", "Bob" };

        // 畳み込みが無いと、外側の Not はオペランドが UnaryExpression のため NOT (...) 枝へ落ち、
        // 内側の反転（IN → 補償付き NOT IN）と合成されて NOT (...) の中に補償が閉じ込められる
        var doubled = RunSqlServerBody(Negate(p => names.Contains(p.Name1), 2));
        doubled.Sql.Should().Be("[Name1] IN (@p0, @p1)");
        doubled.Parameters.Should().HaveCount(2);

        RunSqlServerBody(Negate(p => names.Contains(p.Name1), 3))
            .Sql.Should()
            .Be("([Name1] NOT IN (@p0, @p1) OR [Name1] IS NULL)", "三重否定は 1 回の否定と同義");
    }

    [Fact(DisplayName = "配列（span 経由の Contains）でも同じ補償形になる")]
    public void In_WithArrayCollection_CompensatesTheSameWay()
    {
        var names = new string?[] { "Alice", null };

        RunSqlServer(p => names.Contains(p.Name1))
            .Sql.Should()
            .Be("([Name1] IN (@p0) OR [Name1] IS NULL)");

        RunSqlServer(p => !names.Contains(p.Name1))
            .Sql.Should()
            .Be("([Name1] NOT IN (@p0) AND [Name1] IS NOT NULL)");
    }

    [Fact(DisplayName = "null 許容値型のリストでも null 要素が IS NULL へ畳まれる")]
    public void In_WithNullableValueTypeElements_FoldsNullIntoIsNull()
    {
        var numbers = new List<int?> { 1, null, 3 };

        var sqlServer = RunSqlServer(p => numbers.Contains(p.A));
        sqlServer.Sql.Should().Be("([A] IN (@p0, @p1) OR [A] IS NULL)");
        sqlServer.Parameters.Select(x => x.Value).Should().Equal(1, 3);
    }

    [Fact(
        DisplayName = "値オブジェクト列の IN でも null 要素は IS NULL へ畳まれ、非 null 要素は生値へ解ける（VO 有効の図）"
    )]
    public void ValueObjectColumn_In_FoldsNullAndUnwrapsValues()
    {
        var memos = new List<QueryFixtureMemo?> { QueryFixtureMemo.Create("urgent"), null };

        var parameters = new List<QueryFixtureParam>();
        var predicate =
            (Expression<Func<QueryFixtureOrder, bool>>)(order => memos.Contains(order.Memo));

        QueryFixtureTranslator
            .ToCondition(predicate.Body, parameters)
            .Should()
            .Be("(\"memo\" IN (@p0) OR \"memo\" IS NULL)");
        parameters.Should().ContainSingle("null 要素はパラメータ化しない");
        QueryFixtureUnwrap
            .Unwrap(parameters[0].Value)
            .Should()
            .Be("urgent", "値オブジェクトはバインド時に生値へ解ける");
    }

    [Fact(DisplayName = "値オブジェクト列の NOT IN も列側の NULL を補償する（VO 有効の図）")]
    public void ValueObjectColumn_NotIn_CompensatesNullColumn()
    {
        var memos = new List<QueryFixtureMemo?> { QueryFixtureMemo.Create("urgent") };

        var parameters = new List<QueryFixtureParam>();
        var predicate =
            (Expression<Func<QueryFixtureOrder, bool>>)(order => !memos.Contains(order.Memo));

        QueryFixtureTranslator
            .ToCondition(predicate.Body, parameters)
            .Should()
            .Be("(\"memo\" NOT IN (@p0) OR \"memo\" IS NULL)");
        parameters.Should().ContainSingle();
    }
}
