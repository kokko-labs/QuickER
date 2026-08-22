using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using AwesomeAssertions;
using Xunit;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が、述語に現れたナビゲーションプロパティを
/// 「列」として扱わず明確な <see cref="NotSupportedException"/> で弾くことを検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 修正前の列判定は「ラムダ引数直下のプロパティ参照」だけを見ていたため、<c>[NavigationReference]</c> 付きの
/// ナビゲーション（例 <c>x.Customer == null</c>）がそのまま列名として出力され、存在しない列を参照する無効な
/// SQL（<c>[Customer] IS NULL</c>）になって実行時に DB 側のエラーになっていた。インメモリ（式木コンパイル）と
/// EF Core は同じ述語を問題なく扱うため、3 実装先の挙動が割れる唯一の箇所でもあった。
/// </para>
/// <para>
/// 翻訳器は自分の名前空間の <c>NavigationReferenceAttribute</c> を見るため、プローブは方言ごとに用意する
/// （SQL Server フィクスチャ用・SQLite フィクスチャ用）。両方言で同じ結論になることを対称に固定する。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorNavigationTests
{
    /// <summary>SQL Server フィクスチャの属性でナビゲーションを表すプローブ</summary>
    private sealed class SqlServerNavProbe
    {
        public int Id { get; set; }

        [QuickER.Tests.GeneratedFixture.NavigationReference(
            "customers",
            "customer_id",
            "orders",
            "customer_id",
            false,
            false,
            true
        )]
        public SqlServerNavProbe? Parent { get; set; }
    }

    /// <summary>SQLite フィクスチャの属性でナビゲーションを表すプローブ</summary>
    private sealed class SqliteNavProbe
    {
        public int Id { get; set; }

        [QuickER.Tests.GeneratedSqliteFixture.NavigationReference(
            "customers",
            "customer_id",
            "orders",
            "customer_id",
            false,
            false,
            true
        )]
        public SqliteNavProbe? Parent { get; set; }
    }

    /// <summary>SQL Server 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static string RunSqlServer(Expression<Func<SqlServerNavProbe, bool>> predicate) =>
        SqlServerTranslator.ToCondition(predicate.Body, new List<SqlServerParam>());

    /// <summary>SQLite 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static string RunSqlite(Expression<Func<SqliteNavProbe, bool>> predicate) =>
        SqliteTranslator.ToCondition(predicate.Body, new List<SqliteParam>());

    [Fact(
        DisplayName = "ナビゲーションの null 比較は列扱いされず翻訳不能の NotSupportedException になる（両方言）"
    )]
    public void NavigationNullComparison_ThrowsNotSupported()
    {
        var sqlServer = () => RunSqlServer(p => p.Parent == null);
        sqlServer
            .Should()
            .Throw<NotSupportedException>()
            .WithMessage("*Parent*navigation property*foreign-key column*");

        var sqlite = () => RunSqlite(p => p.Parent == null);
        sqlite
            .Should()
            .Throw<NotSupportedException>()
            .WithMessage("*Parent*navigation property*foreign-key column*");
    }

    [Fact(
        DisplayName = "ナビゲーション越しのプロパティ参照も NotSupportedException になる（両方言）"
    )]
    public void NavigationMemberAccess_ThrowsNotSupported()
    {
        var sqlServer = () => RunSqlServer(p => p.Parent!.Id == 1);
        sqlServer.Should().Throw<NotSupportedException>().WithMessage("*navigation property*");

        var sqlite = () => RunSqlite(p => p.Parent!.Id == 1);
        sqlite.Should().Throw<NotSupportedException>().WithMessage("*navigation property*");
    }

    [Fact(
        DisplayName = "並び替えキーにナビゲーションを指定しても NotSupportedException になる（両方言）"
    )]
    public void NavigationOrderingKey_ThrowsNotSupported()
    {
        Expression<Func<SqlServerNavProbe, object?>> sqlServerKey = p => p.Parent;
        var sqlServer = () => SqlServerTranslator.ToColumn(sqlServerKey);
        sqlServer.Should().Throw<NotSupportedException>().WithMessage("*navigation property*");

        Expression<Func<SqliteNavProbe, object?>> sqliteKey = p => p.Parent;
        var sqlite = () => SqliteTranslator.ToColumn(sqliteKey);
        sqlite.Should().Throw<NotSupportedException>().WithMessage("*navigation property*");
    }

    [Fact(DisplayName = "対照: 素の列は列として翻訳される（両方言）")]
    public void PlainColumn_StillTranslates()
    {
        RunSqlServer(p => p.Id == 1).Should().Be("[Id] = @p0");
        RunSqlite(p => p.Id == 1).Should().Be("\"Id\" = @p0");
    }
}
