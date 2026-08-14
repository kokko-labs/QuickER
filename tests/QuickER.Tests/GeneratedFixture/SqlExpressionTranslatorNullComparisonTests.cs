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
/// 列同士: 両側に NULL があり得るため両方の演算子を展開する。<c>a &lt;&gt; b</c> はどちらかが NULL の行を落とすが
/// C#／EF Core は「片側だけ NULL＝不一致」と扱うので
/// <c>(a &lt;&gt; b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL))</c> へ、
/// <c>a = b</c> は両側 NULL の行を落とすが C#／EF Core は「両側 NULL＝一致」と扱うので
/// <c>(a = b OR (a IS NULL AND b IS NULL))</c> へ展開する（列 vs 値の <c>==</c> にはこの問題が無いので無補償のまま）。
/// </para>
/// <para>
/// 否定: <c>!(a == b)</c> は <c>NOT (...)</c> で包むと補償の外側に出てしまうため、演算子を反転して
/// <c>a != b</c> と同じ経路へ流す（等値以外の否定は従来どおり <c>NOT (...)</c>）。二重否定は単項 Not の入口で
/// 畳み込む——畳まないと外側の Not は「オペランドが等値比較でない」ため <c>NOT (...)</c> 枝へ落ち、内側の反転と
/// 合成されて補償の外へ出てしまう。
/// </para>
/// <para>
/// 射程（既知の乖離）: 補償が効くのは「否定が比較に直接乗っている」場合だけで、論理演算子を挟んだ複合否定
/// （<c>!(a == b &amp;&amp; c)</c> など）は De Morgan 展開されない。個々の比較は補償された形で出るが、それを
/// <c>NOT (...)</c> で包むため、片側 NULL で UNKNOWN になった行は <c>NOT (UNKNOWN)</c> も UNKNOWN のまま落ち、
/// C#／EF Core とは割れる。ここで固定するのは直接の否定（および二重否定）までで、複合否定は
/// 「NULL があり得るなら比較側へ否定を書く（<c>a != b || !c</c>）」という回避を docs／翻訳器 XmlDoc に明記して
/// 割り切っている。
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
        public bool Flag { get; set; }
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

    [Fact(DisplayName = "列同士の != は両辺の NULL を明示した形へ補償される（両方言）")]
    public void NotEqualColumnToColumn_CompensatesBothSides()
    {
        RunSqlServer(p => p.Name1 != p.Name2)
            .Sql.Should()
            .Be(
                "([Name1] <> [Name2] OR ([Name1] IS NULL AND [Name2] IS NOT NULL) OR ([Name1] IS NOT NULL AND [Name2] IS NULL))"
            );

        RunSqlite(p => p.Name1 != p.Name2)
            .Sql.Should()
            .Be(
                "(\"Name1\" <> \"Name2\" OR (\"Name1\" IS NULL AND \"Name2\" IS NOT NULL) OR (\"Name1\" IS NOT NULL AND \"Name2\" IS NULL))"
            );
    }

    [Fact(DisplayName = "列同士の == は両側 NULL を一致とする形へ補償される（両方言）")]
    public void EqualColumnToColumn_CompensatesBothNulls()
    {
        // 素の [Name1] = [Name2] は両側 NULL の行を UNKNOWN で落とすが、C# も EF Core も「両側 NULL は一致」と扱う
        RunSqlServer(p => p.Name1 == p.Name2)
            .Sql.Should()
            .Be("([Name1] = [Name2] OR ([Name1] IS NULL AND [Name2] IS NULL))");

        RunSqlite(p => p.Name1 == p.Name2)
            .Sql.Should()
            .Be("(\"Name1\" = \"Name2\" OR (\"Name1\" IS NULL AND \"Name2\" IS NULL))");
    }

    [Fact(DisplayName = "列同士の == と != の補償形は互いの否定になっている（4 通りの網羅）")]
    public void ColumnToColumnCompensations_AreComplementary()
    {
        // (両側 NULL / 片側 NULL×2 / 両側非 NULL) の 4 通りを、== 形と != 形が過不足なく二分する
        RunSqlServer(p => p.Name1 == p.Name2)
            .Sql.Should()
            .Be("([Name1] = [Name2] OR ([Name1] IS NULL AND [Name2] IS NULL))");

        RunSqlServer(p => !(p.Name1 == p.Name2))
            .Sql.Should()
            .Be(
                "([Name1] <> [Name2] OR ([Name1] IS NULL AND [Name2] IS NOT NULL) OR ([Name1] IS NOT NULL AND [Name2] IS NULL))",
                "!(==) は != の補償形と同一"
            );

        RunSqlServer(p => !(p.Name1 != p.Name2))
            .Sql.Should()
            .Be(
                "([Name1] = [Name2] OR ([Name1] IS NULL AND [Name2] IS NULL))",
                "!(!=) は == の補償形と同一"
            );
    }

    [Fact(DisplayName = "!(==) は != と同じ補償形になる（NOT で包まない）")]
    public void NegatedEqual_TranslatesAsNotEqual()
    {
        var name = "Alice";

        var sqlServer = RunSqlServer(p => !(p.Name1 == name));
        sqlServer.Sql.Should().Be("([Name1] <> @p0 OR [Name1] IS NULL)", "!(==) と != は同義");
        sqlServer.Parameters.Should().ContainSingle();

        RunSqlite(p => !(p.Name1 == name))
            .Sql.Should()
            .Be("(\"Name1\" <> @p0 OR \"Name1\" IS NULL)");
    }

    [Fact(DisplayName = "!(!=) は == と同じ形になる（二重否定が畳まれる）")]
    public void NegatedNotEqual_TranslatesAsEqual()
    {
        var name = "Alice";

        RunSqlServer(p => !(p.Name1 != name)).Sql.Should().Be("[Name1] = @p0");
    }

    [Fact(DisplayName = "!(== null) / !(!= null) も IS [NOT] NULL へ畳まれる")]
    public void NegatedNullComparison_EmitsIsNull()
    {
        string? missing = null;

        RunSqlServer(p => !(p.Name1 == missing)).Sql.Should().Be("[Name1] IS NOT NULL");
        RunSqlServer(p => !(p.Name1 != null)).Sql.Should().Be("[Name1] IS NULL");
    }

    [Fact(DisplayName = "!(列 == 列) も列同士の補償形になる")]
    public void NegatedColumnToColumnEqual_CompensatesBothSides()
    {
        RunSqlServer(p => !(p.Name1 == p.Name2))
            .Sql.Should()
            .Be(
                "([Name1] <> [Name2] OR ([Name1] IS NULL AND [Name2] IS NOT NULL) OR ([Name1] IS NOT NULL AND [Name2] IS NULL))"
            );
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
    private static string RunSqlServerBody(Expression body) =>
        SqlServerTranslator.ToCondition(body, new List<SqlServerParam>());

    /// <summary>SQLite 方言のトランスレータで、組み立て済みの式木を条件へ変換する</summary>
    private static string RunSqliteBody(Expression body) =>
        SqliteTranslator.ToCondition(body, new List<SqliteParam>());

    [Fact(DisplayName = "二重否定は畳み込まれ、素の比較とまったく同じ補償形になる")]
    public void DoubleNegation_CollapsesToPlainComparison()
    {
        var name = "Alice";

        // 畳み込みが無いと、外側の Not はオペランドが UnaryExpression のため NOT (...) 枝へ落ち、
        // 内側の反転（!= → ==）と合成されて NOT ([Name1] = @p0) になる＝列が NULL の行が脱落する
        RunSqlServerBody(Negate(p => p.Name1 != name, 2))
            .Should()
            .Be("([Name1] <> @p0 OR [Name1] IS NULL)", "!(!(x != v)) は x != v と同義");

        RunSqlServerBody(Negate(p => p.Name1 == name, 2)).Should().Be("[Name1] = @p0");

        RunSqliteBody(Negate(p => p.Name1 != name, 2))
            .Should()
            .Be("(\"Name1\" <> @p0 OR \"Name1\" IS NULL)");
    }

    [Fact(DisplayName = "三重否定も再帰的に畳まれ、1 回の否定と同じ形になる")]
    public void TripleNegation_CollapsesToSingleNegation()
    {
        var name = "Alice";

        RunSqlServerBody(Negate(p => p.Name1 != name, 3))
            .Should()
            .Be("[Name1] = @p0", "三重否定は 1 回の否定＝x == v と同義");

        RunSqlServerBody(Negate(p => p.Name1 != name, 4))
            .Should()
            .Be("([Name1] <> @p0 OR [Name1] IS NULL)", "四重否定は素の比較へ戻る");
    }

    [Fact(DisplayName = "列同士の二重否定も畳まれ、補償形が保たれる")]
    public void DoubleNegation_ColumnToColumn_KeepsCompensation()
    {
        RunSqlServerBody(Negate(p => p.Name1 != p.Name2, 2))
            .Should()
            .Be(
                "([Name1] <> [Name2] OR ([Name1] IS NULL AND [Name2] IS NOT NULL) OR ([Name1] IS NOT NULL AND [Name2] IS NULL))"
            );

        RunSqlServerBody(Negate(p => p.Name1 == p.Name2, 2))
            .Should()
            .Be("([Name1] = [Name2] OR ([Name1] IS NULL AND [Name2] IS NULL))");
    }

    [Fact(DisplayName = "対照: 二重否定の中身が比較でなくても畳まれる（bool 列）")]
    public void DoubleNegation_BoolColumn_CollapsesToPlainColumn()
    {
        RunSqlServerBody(Negate(p => p.Flag, 2)).Should().Be("[Flag] = 1");
        RunSqlServerBody(Negate(p => p.Flag, 1)).Should().Be("[Flag] = 0");
    }

    [Fact(DisplayName = "対照: 等値以外の否定は従来どおり NOT (...) で包まれる")]
    public void NegatedNonEqualityComparison_StillWrapsInNot()
    {
        RunSqlServer(p => !(p.A > 1)).Sql.Should().Be("NOT ([A] > @p0)");
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
