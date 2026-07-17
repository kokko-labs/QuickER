using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;
using SqliteParam = QuickER.Tests.GeneratedSqliteFixture.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedSqliteFixture.SqlExpressionTranslator;
using SqlServerParam = QuickER.Tests.GeneratedFixture.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedFixture.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c> が「値の位置に列参照（ラムダパラメータ）を置いた式」を
/// どう扱うかを直接検証する単体テスト。列引数の LIKE 系（Contains/StartsWith/EndsWith）・Equals は列同士の
/// SQL へ、それ以外（ToUpper・算術式など値評価が必要な式）は明示的な <see cref="NotSupportedException"/> へ
/// 落ちることを、SQL Server 方言（角括弧・<c>+</c> 連結）と SQLite 方言（二重引用符・<c>||</c> 連結）の
/// 両トランスレータで確認する。
/// </summary>
/// <remarks>
/// 固定フィクスチャの <c>SqlExpressionTranslator</c> は internal だが、テストと同一アセンブリのため直接呼べる。
/// <c>ToCondition(Expression, List&lt;SqlQueryParameter&gt;)</c> はラムダパラメータの型を問わず「パラメータの
/// プロパティ参照」だけで列判定するため、テストローカルの POCO（<see cref="Probe"/>）で式木を組める
/// （<c>[Column]</c> 属性がないためプロパティ名がそのまま列名になる）。
/// </remarks>
public sealed class SqlExpressionTranslatorColumnArgumentTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public string Name1 { get; set; } = string.Empty;
        public string Name2 { get; set; } = string.Empty;
        public int A { get; set; }
        public int B { get; set; }
    }

    // --- 各方言でトランスレータを起動する薄いラッパ（式本体を条件文字列へ変換し、生成パラメータも返す） ---

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

    // --- 期待 SQL 断片の組み立て（生成側の BuildLikePatternFromColumn / EscapeLike と同じ 4 文字エスケープを鏡映） ---

    /// <summary>列値をリテラル扱いする LIKE パターンの REPLACE 連鎖（生成コードと同じ順・同じ 4 文字）</summary>
    private static string Escaped(string column) =>
        $"REPLACE(REPLACE(REPLACE(REPLACE({column}, '\\', '\\\\'), '%', '\\%'), '_', '\\_'), '[', '\\[')";

    [Fact(DisplayName = "列引数の Contains は列同士の LIKE（%…% でパターン列を包む）へ変換される")]
    public void Contains_ColumnArgument_EmitsColumnToColumnLike()
    {
        var sqlServer = RunSqlServer(p => p.Name1.Contains(p.Name2));
        sqlServer.Sql.Should().Be($"[Name1] LIKE '%' + {Escaped("[Name2]")} + '%' ESCAPE '\\'");
        sqlServer.Parameters.Should().BeEmpty("列同士の比較はパラメータ化しない");

        var sqlite = RunSqlite(p => p.Name1.Contains(p.Name2));
        sqlite.Sql.Should().Be($"\"Name1\" LIKE '%' || {Escaped("\"Name2\"")} || '%' ESCAPE '\\'");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "列引数の StartsWith は列同士の LIKE（末尾 % のみ）へ変換される")]
    public void StartsWith_ColumnArgument_EmitsColumnToColumnLike()
    {
        var sqlServer = RunSqlServer(p => p.Name1.StartsWith(p.Name2));
        sqlServer.Sql.Should().Be($"[Name1] LIKE {Escaped("[Name2]")} + '%' ESCAPE '\\'");
        sqlServer.Parameters.Should().BeEmpty();

        var sqlite = RunSqlite(p => p.Name1.StartsWith(p.Name2));
        sqlite.Sql.Should().Be($"\"Name1\" LIKE {Escaped("\"Name2\"")} || '%' ESCAPE '\\'");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "列引数の EndsWith は列同士の LIKE（先頭 % のみ）へ変換される")]
    public void EndsWith_ColumnArgument_EmitsColumnToColumnLike()
    {
        var sqlServer = RunSqlServer(p => p.Name1.EndsWith(p.Name2));
        sqlServer.Sql.Should().Be($"[Name1] LIKE '%' + {Escaped("[Name2]")} ESCAPE '\\'");
        sqlServer.Parameters.Should().BeEmpty();

        var sqlite = RunSqlite(p => p.Name1.EndsWith(p.Name2));
        sqlite.Sql.Should().Be($"\"Name1\" LIKE '%' || {Escaped("\"Name2\"")} ESCAPE '\\'");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "列引数の Equals は列同士の等値比較へ変換される")]
    public void Equals_ColumnArgument_EmitsColumnToColumnEquality()
    {
        var sqlServer = RunSqlServer(p => p.Name1.Equals(p.Name2));
        sqlServer.Sql.Should().Be("[Name1] = [Name2]");
        sqlServer.Parameters.Should().BeEmpty();

        var sqlite = RunSqlite(p => p.Name1.Equals(p.Name2));
        sqlite.Sql.Should().Be("\"Name1\" = \"Name2\"");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "列引数の Equals(IgnoreCase) は両辺を LOWER で畳んだ等値比較へ変換される")]
    public void EqualsIgnoreCase_ColumnArgument_FoldsBothSidesWithLower()
    {
        var sqlServer = RunSqlServer(p =>
            p.Name1.Equals(p.Name2, StringComparison.OrdinalIgnoreCase)
        );
        sqlServer.Sql.Should().Be("LOWER([Name1]) = LOWER([Name2])");
        sqlServer.Parameters.Should().BeEmpty();

        var sqlite = RunSqlite(p => p.Name1.Equals(p.Name2, StringComparison.OrdinalIgnoreCase));
        sqlite.Sql.Should().Be("LOWER(\"Name1\") = LOWER(\"Name2\")");
        sqlite.Parameters.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "回帰: 値引数の Contains は従来どおりパラメータ化される（列版に巻き込まれない）"
    )]
    public void Contains_ValueArgument_StillParameterizes()
    {
        // ローカル変数（クロージャ捕捉）の値引数。ワイルドカードはリテラル一致になるようエスケープされる
        var keyword = "a%b";

        var sqlServer = RunSqlServer(p => p.Name1.Contains(keyword));
        sqlServer.Sql.Should().Be("[Name1] LIKE @p0 ESCAPE '\\'");
        sqlServer.Parameters.Should().ContainSingle();
        sqlServer.Parameters[0].Value.Should().Be("%a\\%b%");

        var sqlite = RunSqlite(p => p.Name1.Contains(keyword));
        sqlite.Sql.Should().Be("\"Name1\" LIKE @p0 ESCAPE '\\'");
        sqlite.Parameters.Should().ContainSingle();
        sqlite.Parameters[0].Value.Should().Be("%a\\%b%");
    }

    [Fact(DisplayName = "ガード: 値の位置に ToUpper() を置くと NotSupportedException（両方言）")]
    public void ToUpperInValuePosition_Throws()
    {
        var sqlServer = () => RunSqlServer(p => p.Name1.ToUpper() == "A");
        sqlServer.Should().Throw<NotSupportedException>().WithMessage("*SQL へ変換できません*");

        var sqlite = () => RunSqlite(p => p.Name1.ToUpper() == "A");
        sqlite.Should().Throw<NotSupportedException>().WithMessage("*SQL へ変換できません*");
    }

    [Fact(DisplayName = "ガード: 値の位置に列同士の算術式を置くと NotSupportedException（両方言）")]
    public void ColumnArithmeticInValuePosition_Throws()
    {
        var sqlServer = () => RunSqlServer(p => p.A + p.B > 5);
        sqlServer.Should().Throw<NotSupportedException>().WithMessage("*SQL へ変換できません*");

        var sqlite = () => RunSqlite(p => p.A + p.B > 5);
        sqlite.Should().Throw<NotSupportedException>().WithMessage("*SQL へ変換できません*");
    }
}
