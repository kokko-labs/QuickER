using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using QueryFixtureOrder = QuickER.Tests.GeneratedQueryFixture.OrderEntity;
using QueryFixtureParam = QuickER.Tests.GeneratedQueryFixture.SqlQueryParameter;
using QueryFixtureTranslator = QuickER.Tests.GeneratedQueryFixture.SqlExpressionTranslator;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が等値比較の null 意味論を C#／EF Core へ揃えることを、
/// 生成 SQL 文字列の固定で検証する単体テスト（値側の null 補償と、列側の null 補償の両方）。
/// </summary>
/// <remarks>
/// <para>
/// 値側: 補償前は「リテラル null」だけが <c>IS NULL</c> になり、変数・式が評価の結果 null になる場合は
/// <c>col = @p</c>（@p=NULL）としてバインドされていた。SQL の 3 値論理ではこれが全行 UNKNOWN になるため、
/// C#（インメモリ実行器の式木コンパイル）や EF Core と結果が割れていた（一意性チェックの自分自身除外が
/// 主キー未設定の新規行で全行を弾いていた不具合の根本原因）。
/// </para>
/// <para>
/// 列側: <c>col &lt;&gt; @p</c> は列が NULL の行で UNKNOWN になり、その行が結果から落ちる。C# も EF Core も
/// 「NULL は非 null 値と等しくない＝一致」と扱うため、ここが 3 実装先の乖離になっていた（実測で
/// ADO 1 件・EF Core 2 件・InMemory 2 件）。<c>!=</c> は <c>(col &lt;&gt; @p OR col IS NULL)</c> へ補償する。
/// 列の NULL 許容性は式木から確実には判定できないため無条件に補償する（非 NULL 列では意味不変）。
/// </para>
/// <para>
/// 補償範囲は <c>==</c> / <c>!=</c> のみで、関係演算子（&lt; &lt;= &gt; &gt;=）は従来どおり NULL パラメータの
/// ままにする（null 対応の SQL 対応物が無いため）。ここではその線引きも対照として固定する。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorNullComparisonTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public string? Name1 { get; set; }
        public string? Name2 { get; set; }
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

    [Fact(DisplayName = "null 変数との == は IS NULL へ補償される（両方言・パラメータなし）")]
    public void EqualsNullVariable_EmitsIsNull()
    {
        string? missing = null;

        var sqlServer = RunSqlServer(p => p.Name1 == missing);
        sqlServer.Sql.Should().Be("[Name1] IS NULL");
        sqlServer.Parameters.Should().BeEmpty("補償された比較はパラメータ化しない");

        var sqlite = RunSqlite(p => p.Name1 == missing);
        sqlite.Sql.Should().Be("\"Name1\" IS NULL");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "null 変数との != は IS NOT NULL へ補償される（両方言）")]
    public void NotEqualsNullVariable_EmitsIsNotNull()
    {
        string? missing = null;

        RunSqlServer(p => p.Name1 != missing).Sql.Should().Be("[Name1] IS NOT NULL");
        RunSqlite(p => p.Name1 != missing).Sql.Should().Be("\"Name1\" IS NOT NULL");
    }

    [Fact(DisplayName = "左右どちらに列があっても補償される")]
    public void NullVariableOnEitherSide_EmitsIsNull()
    {
        string? missing = null;

        RunSqlServer(p => missing == p.Name1).Sql.Should().Be("[Name1] IS NULL");
        RunSqlServer(p => missing != p.Name1).Sql.Should().Be("[Name1] IS NOT NULL");
    }

    [Fact(DisplayName = "回帰: リテラル null の等値比較は従来どおり IS NULL のまま")]
    public void LiteralNull_StillEmitsIsNull()
    {
        RunSqlServer(p => p.Name1 == null).Sql.Should().Be("[Name1] IS NULL");
        RunSqlServer(p => p.Name1 != null).Sql.Should().Be("[Name1] IS NOT NULL");
        RunSqlite(p => p.Name1 == null).Sql.Should().Be("\"Name1\" IS NULL");
    }

    [Fact(DisplayName = "回帰: null でない変数は従来どおりパラメータ化される")]
    public void NonNullVariable_StillParameterizes()
    {
        var name = "Alice";

        var sqlServer = RunSqlServer(p => p.Name1 == name);
        sqlServer.Sql.Should().Be("[Name1] = @p0");
        sqlServer.Parameters.Should().ContainSingle();
        sqlServer.Parameters[0].Value.Should().Be("Alice");
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");
    }

    [Fact(DisplayName = "null を返すメソッド呼び出しも評価結果で補償される")]
    public void NullReturningCall_EmitsIsNull()
    {
        RunSqlServer(p => p.Name1 == ResolveMissing()).Sql.Should().Be("[Name1] IS NULL");
    }

    /// <summary>値の位置に置く「評価すると null になる」メソッド呼び出し（式木コンパイル経由の評価を通る）</summary>
    private static string? ResolveMissing() => null;

    [Fact(
        DisplayName = "対照: 関係演算子（>）の null は補償せずパラメータのまま（null 対応の SQL 対応物が無い）"
    )]
    public void RelationalOperator_KeepsNullParameter()
    {
        int? missing = null;

        var sqlServer = RunSqlServer(p => p.A > missing);
        sqlServer.Sql.Should().Be("[A] > @p0");
        sqlServer.Parameters.Should().ContainSingle();
        sqlServer.Parameters[0].Value.Should().BeNull();
    }

    [Fact(
        DisplayName = "null でない値との != は (col <> @p OR col IS NULL) へ補償される（両方言）"
    )]
    public void NotEqualNonNullValue_CompensatesNullColumn()
    {
        var name = "Alice";

        var sqlServer = RunSqlServer(p => p.Name1 != name);
        sqlServer.Sql.Should().Be("([Name1] <> @p0 OR [Name1] IS NULL)");
        sqlServer.Parameters.Should().ContainSingle("補償しても値は 1 回だけバインドする");
        sqlServer.Parameters[0].Value.Should().Be("Alice");
        sqlServer.Parameters[0].ColumnName.Should().Be("Name1", "対辺の列名は明示型付けに使う");

        var sqlite = RunSqlite(p => p.Name1 != name);
        sqlite.Sql.Should().Be("(\"Name1\" <> @p0 OR \"Name1\" IS NULL)");
        sqlite.Parameters.Should().ContainSingle();
    }

    [Fact(DisplayName = "!= の列側補償は列が左右どちらにあっても働く")]
    public void NotEqualNonNullValue_OnEitherSide_Compensates()
    {
        var name = "Alice";

        RunSqlServer(p => name != p.Name1).Sql.Should().Be("([Name1] <> @p0 OR [Name1] IS NULL)");
    }

    [Fact(DisplayName = "対照: == は列側を補償しない（NULL 行は元から不一致で C# と一致する）")]
    public void EqualNonNullValue_IsNotCompensated()
    {
        var name = "Alice";

        RunSqlServer(p => p.Name1 == name).Sql.Should().Be("[Name1] = @p0");
        RunSqlite(p => p.Name1 == name).Sql.Should().Be("\"Name1\" = @p0");
    }

    [Fact(DisplayName = "対照: 列同士の != は補償しない（値側が無く両辺が SQL のため）")]
    public void NotEqualColumnToColumn_IsNotCompensated()
    {
        RunSqlServer(p => p.Name1 != p.Name2).Sql.Should().Be("[Name1] <> [Name2]");
    }

    [Fact(DisplayName = "値オブジェクト列でも null 変数との比較が補償される（VO 有効の図）")]
    public void ValueObjectColumn_NullVariable_EmitsIsNull()
    {
        // 一意性チェックの「自分自身の除外」が主キー未設定の新規行で通る形そのもの（VO 型 PK・null）
        QuickER.Tests.GeneratedQueryFixture.OrderIdValue? missingKey = null;

        var parameters = new List<QueryFixtureParam>();
        var predicate =
            (Expression<Func<QueryFixtureOrder, bool>>)(
                candidate => candidate.OrderId != missingKey
            );

        QueryFixtureTranslator
            .ToCondition(predicate.Body, parameters)
            .Should()
            .Be("\"order_id\" IS NOT NULL");
        parameters.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "一意性チェックの自分自身除外（主キーあり）は列側補償の形で生成される（VO 有効の図）"
    )]
    public void ValueObjectColumn_NotEqualNonNullKey_CompensatesNullColumn()
    {
        var existingKey = QuickER.Tests.GeneratedQueryFixture.OrderIdValue.Create(42);

        var parameters = new List<QueryFixtureParam>();
        var predicate =
            (Expression<Func<QueryFixtureOrder, bool>>)(
                candidate => candidate.OrderId != existingKey
            );

        QueryFixtureTranslator
            .ToCondition(predicate.Body, parameters)
            .Should()
            .Be("(\"order_id\" <> @p0 OR \"order_id\" IS NULL)");
        parameters.Should().ContainSingle();
    }
}
