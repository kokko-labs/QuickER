using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerEvaluator = QuickER.Tests.GeneratedFixture.QueryValueEvaluator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が、列を含まない部分式の畳み込み・<c>Nullable&lt;T&gt;.Value</c> の剥がし・
/// char の文字列一致・LIKE の束縛の型・空と空白の判定・SQLite の日付部品を、意図した SQL へ翻訳することを固定する単体テスト。
/// </summary>
/// <remarks>
/// 行集合としての意味論は実 DB の <c>TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests</c> が C# と突き合わせる。
/// ここは出力の形（列を含む比較の NULL 補償が畳み込みで変わらないこと、など）を方言ごとに名指しする。
/// </remarks>
public sealed class SqlExpressionTranslatorFoldingTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public string? Name { get; set; }
        public bool IsActive { get; set; }
        public DateTime? At { get; set; }
        public DateTime Created { get; set; }
        public DateTimeOffset Stamped { get; set; }
    }

    private static string RunSqlServer(
        Expression<Func<Probe, bool>> predicate,
        List<SqlServerParam>? parameters = null
    ) => SqlServerTranslator.ToCondition(predicate.Body, parameters ?? new List<SqlServerParam>());

    private static string RunSqlite(
        Expression<Func<Probe, bool>> predicate,
        List<SqliteParam>? parameters = null
    ) => SqliteTranslator.ToCondition(predicate.Body, parameters ?? new List<SqliteParam>());

    [Fact(DisplayName = "列を含まない比較は評価して定数条件に畳み、列の側だけを翻訳する")]
    public void ColumnFreeComparison_IsFolded()
    {
        string? none = null;
        string? alice = "Alice";

        RunSqlServer(x => none == null || x.Name == none).Should().Be("1 = 1");
        RunSqlServer(x => alice == null || x.Name == alice).Should().Be("[Name] = @p0");
        RunSqlServer(x => none != null && x.Name == none).Should().Be("1 = 0");
        RunSqlite(x => alice != null && x.Name == alice).Should().Be("\"Name\" = @p0");
    }

    [Fact(DisplayName = "列を含まない bool はどちらの辺にあっても畳まれ、論理演算を簡約する")]
    public void ColumnFreeBoolean_SimplifiesLogicalOperators()
    {
        var on = true;
        var off = false;

        RunSqlServer(x => on).Should().Be("1 = 1");
        RunSqlServer(x => !on).Should().Be("1 = 0");
        RunSqlServer(x => off || x.IsActive).Should().Be("[IsActive] = 1");
        RunSqlServer(x => x.IsActive || on).Should().Be("1 = 1");
        RunSqlServer(x => x.IsActive && on).Should().Be("[IsActive] = 1");
        RunSqlServer(x => x.IsActive && off).Should().Be("1 = 0");
    }

    [Fact(DisplayName = "左辺で結論が決まれば右辺を評価しない（C# の短絡評価）")]
    public void LogicalOperators_ShortCircuit()
    {
        string? text = null;

        RunSqlServer(x => text == null || text.Length > 3).Should().Be("1 = 1");
        RunSqlServer(x => text != null && text.Length > 3).Should().Be("1 = 0");
        RunSqlServer(x => text == null || (text.Length > 3 && x.IsActive)).Should().Be("1 = 1");
    }

    [Fact(DisplayName = "列を含む比較の NULL 補償は畳み込みの導入で変わらない")]
    public void ColumnComparisons_KeepNullCompensation()
    {
        string? none = null;

        RunSqlServer(x => x.Name == none).Should().Be("[Name] IS NULL");
        RunSqlServer(x => x.Name != "a").Should().Be("([Name] <> @p0 OR [Name] IS NULL)");
        RunSqlServer(x => !(x.Name == "a")).Should().Be("([Name] <> @p0 OR [Name] IS NULL)");
    }

    [Fact(DisplayName = "内側のラムダが束縛した引数だけを含む式は値として評価される")]
    public void InnerLambdaParameters_DoNotBlockFolding()
    {
        var ids = new[] { 1, 2, 3 };

        RunSqlServer(x => ids.Any(i => i > 2) && x.IsActive).Should().Be("[IsActive] = 1");
        RunSqlServer(x => ids.Any(i => i > 5) && x.IsActive).Should().Be("1 = 0");
    }

    [Fact(DisplayName = "「null なら絞らない」の値の比較は式木をコンパイルせずに評価する")]
    public void NullFilterIdiom_DoesNotCompile()
    {
        string? none = null;
        var on = true;
        SqlServerEvaluator.FallbackCompileCount = 0;

        RunSqlServer(x => none == null || x.Name == none);
        RunSqlServer(x => none != null && !on || x.IsActive);

        SqlServerEvaluator.FallbackCompileCount.Should().Be(0);
    }

    [Fact(DisplayName = "Nullable の Value を経由した列の参照は列へ解決される")]
    public void NullableValue_ResolvesToColumn()
    {
        var at = new DateTime(2024, 1, 1);

        RunSqlServer(x => x.At!.Value.Year == 2024).Should().Be("YEAR([At]) = @p0");
        RunSqlServer(x => x.At!.Value == at).Should().Be("[At] = @p0");
    }

    [Fact(DisplayName = "char を受ける文字列一致はその文字のパターンとして翻訳される")]
    public void CharArgument_IsUsedAsPattern()
    {
        var parameters = new List<SqlServerParam>();

        RunSqlServer(x => x.Name!.StartsWith('A'), parameters)
            .Should()
            .Be("[Name] LIKE @p0 ESCAPE '\\'");
        parameters.Single().Value.Should().Be("A%");
    }

    [Fact(DisplayName = "LIKE のパターンは可変長で束縛するよう印を付け、等号の値には付けない")]
    public void LikePattern_IsMarkedVariableLength()
    {
        var like = new List<SqlServerParam>();
        RunSqlServer(x => x.Name!.EndsWith("c"), like);
        like.Single().VariableLength.Should().BeTrue();

        var equality = new List<SqlServerParam>();
        RunSqlServer(x => x.Name == "c", equality);
        equality.Single().VariableLength.Should().BeFalse();
    }

    [Fact(DisplayName = "空と空白の判定は NULL を含み、方言ごとに空白の全文字を見る形で翻訳される")]
    public void NullOrEmptyChecks_TranslatePerDialect()
    {
        RunSqlServer(x => string.IsNullOrEmpty(x.Name))
            .Should()
            .Be("([Name] IS NULL OR DATALENGTH([Name]) = 0)");
        RunSqlServer(x => string.IsNullOrWhiteSpace(x.Name))
            .Should()
            .StartWith("([Name] IS NULL OR [Name] NOT LIKE N'%[^' + NCHAR(9) + NCHAR(10) +")
            .And.Contain("NCHAR(12288)")
            .And.EndWith(" + N']%')");

        RunSqlite(x => string.IsNullOrEmpty(x.Name))
            .Should()
            .Be("(\"Name\" IS NULL OR \"Name\" = '')");
        RunSqlite(x => string.IsNullOrWhiteSpace(x.Name))
            .Should()
            .StartWith("(\"Name\" IS NULL OR trim(\"Name\", char(9, 10, 11, 12, 13, 32,")
            .And.Contain("12288)");
    }

    [Fact(
        DisplayName = "SQLite の Date はパラメータと同じ時刻つきの形で、オフセットつき日時は格納した時差のまま取り出す"
    )]
    public void SqliteDateParts_MatchParameterFormAndKeepOffset()
    {
        var day = new DateTime(2024, 1, 1);

        RunSqlite(x => x.Created.Date == day).Should().Be("datetime(date(\"Created\")) = @p0");
        RunSqlite(x => x.Stamped.Year == 2024)
            .Should()
            .Be("CAST(substr(\"Stamped\", 1, 4) AS INTEGER) = @p0");
        RunSqlite(x => x.Stamped.Hour == 5)
            .Should()
            .Be("CAST(substr(\"Stamped\", 12, 2) AS INTEGER) = @p0");
        RunSqlite(x => x.Stamped.Date == day)
            .Should()
            .Be("substr(\"Stamped\", 1, 10) || ' 00:00:00' = @p0");
        RunServerDateUnchanged();
    }

    /// <summary>SQL Server の日付部品は C# と一致しているので変えていないことを名指しする</summary>
    private static void RunServerDateUnchanged() =>
        RunSqlServer(x => x.Stamped.Hour == 5).Should().Be("DATEPART(HOUR, [Stamped]) = @p0");
}
