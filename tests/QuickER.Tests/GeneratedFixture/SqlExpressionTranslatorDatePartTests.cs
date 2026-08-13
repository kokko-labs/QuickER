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
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が、日付部品（<c>x.Col.Year</c> など）への変換を
/// <b>日付型の列に限る</b>ことを検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 判定はメンバー名（Year / Month / …）だけでは足りない。名前だけを見ていると、値オブジェクトの partial
/// 実装などで同名のプロパティを足した瞬間に、日付でない列が <c>YEAR([col])</c> へ翻訳され、DB 側で
/// 型エラーになるか（方言によっては）黙って別の値を返す。読み出し元の型が日付型であることまで確かめれば、
/// 該当しない参照は既存の値評価経路へ落ち、<see cref="NotSupportedException"/> として明示的に失敗する。
/// </para>
/// <para>両方言（SQL Server / SQLite）で同じ線引きになることを対称に固定する。</para>
/// </remarks>
public sealed class SqlExpressionTranslatorDatePartTests
{
    /// <summary>日付型でないのに日付部品と同名のプロパティを持つ型（VO へ partial で足した状況の再現）</summary>
    private sealed class YearCarrier
    {
        public int Year { get; set; }
    }

    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public DateTime Created { get; set; }
        public DateOnly Due { get; set; }
        public DateTimeOffset Stamped { get; set; }
        public YearCarrier Tag { get; set; } = new();
    }

    /// <summary>SQL Server 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static string RunSqlServer(Expression<Func<Probe, bool>> predicate) =>
        SqlServerTranslator.ToCondition(predicate.Body, new List<SqlServerParam>());

    /// <summary>SQLite 方言のトランスレータで述語本体を条件へ変換する</summary>
    private static string RunSqlite(Expression<Func<Probe, bool>> predicate) =>
        SqliteTranslator.ToCondition(predicate.Body, new List<SqliteParam>());

    [Fact(DisplayName = "日付型の列の Year は従来どおり日付部品 SQL へ翻訳される（両方言）")]
    public void DateTimeColumn_YearTranslatesToDatePart()
    {
        RunSqlServer(p => p.Created.Year == 2020).Should().Be("YEAR([Created]) = @p0");
        RunSqlite(p => p.Created.Year == 2020)
            .Should()
            .Be("CAST(strftime('%Y', \"Created\") AS INTEGER) = @p0");
    }

    [Fact(DisplayName = "DateOnly / DateTimeOffset の列も日付部品として扱われる")]
    public void DateOnlyAndOffsetColumns_TranslateToDatePart()
    {
        RunSqlServer(p => p.Due.Month == 3).Should().Be("MONTH([Due]) = @p0");
        RunSqlServer(p => p.Stamped.Day == 5).Should().Be("DAY([Stamped]) = @p0");
    }

    [Fact(
        DisplayName = "日付型でない列の Year は日付部品にならず NotSupportedException になる（両方言）"
    )]
    public void NonDateColumn_YearIsNotTranslatedAsDatePart()
    {
        var sqlServer = () => RunSqlServer(p => p.Tag.Year == 2020);
        sqlServer
            .Should()
            .Throw<NotSupportedException>("日付でない列を YEAR() で読むと DB 側の型エラーになる");

        var sqlite = () => RunSqlite(p => p.Tag.Year == 2020);
        sqlite.Should().Throw<NotSupportedException>();
    }
}
