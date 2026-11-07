using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 参照アクションと文字列表現の相互変換（<see cref="ForeignKeyReferentialActionHelper"/>）の規則を検証するテストクラス
/// </summary>
/// <remarks>
/// 取込（5 方言）・差分・DBML と Excel 定義書の入出力が共有する正本なので、ここで規則を固定する。
/// SQL Server の取込は <c>SET_NULL</c> の形、DBML は小文字の <c>set null</c> の形を読む。
/// </remarks>
public class ForeignKeyReferentialActionTests
{
    /// <summary>SQL 表記の往復の 1 行（参照アクションの数値と、書く SQL 表記・読み戻した参照アクション）</summary>
    /// <param name="Id">行の名前</param>
    /// <param name="Value">参照アクションの数値（範囲外の値を含む）</param>
    /// <param name="SqlText">書く SQL 表記（画面表示も同じ）</param>
    /// <param name="ReadBack">SQL 表記を読み戻した参照アクション</param>
    public sealed record SqlTextRow(
        string Id,
        int Value,
        string SqlText,
        ForeignKeyReferentialAction ReadBack
    )
    {
        public override string ToString() => Id;
    }

    /// <summary>列挙のどの値も書いて読み戻すと元の値になり、範囲外の値は NO ACTION と書く</summary>
    public static TheoryData<SqlTextRow> SqlTextRows =>
        new()
        {
            new SqlTextRow(
                "no-action",
                (int)ForeignKeyReferentialAction.NoAction,
                "NO ACTION",
                ForeignKeyReferentialAction.NoAction
            ),
            new SqlTextRow(
                "cascade",
                (int)ForeignKeyReferentialAction.Cascade,
                "CASCADE",
                ForeignKeyReferentialAction.Cascade
            ),
            new SqlTextRow(
                "set-null",
                (int)ForeignKeyReferentialAction.SetNull,
                "SET NULL",
                ForeignKeyReferentialAction.SetNull
            ),
            new SqlTextRow(
                "set-default",
                (int)ForeignKeyReferentialAction.SetDefault,
                "SET DEFAULT",
                ForeignKeyReferentialAction.SetDefault
            ),
            // 範囲外の値は NO ACTION と書く（読み戻すと NoAction）
            new SqlTextRow("out-of-range", 99, "NO ACTION", ForeignKeyReferentialAction.NoAction),
        };

    [Theory(
        DisplayName = "参照アクションは SQL 表記へ書いて読み戻すと元の値になる（範囲外は NO ACTION）"
    )]
    [MemberData(nameof(SqlTextRows))]
    public void SqlText_RoundTrips(SqlTextRow row)
    {
        var action = (ForeignKeyReferentialAction)row.Value;

        action.ToSqlText().Should().Be(row.SqlText);
        action.ToDisplayText().Should().Be(row.SqlText);
        ForeignKeyReferentialActionHelper.Parse(row.SqlText).Should().Be(row.ReadBack);
    }

    /// <summary>表記ゆれの読みの 1 行（取込値などの文字列表現と、読んだ参照アクション）</summary>
    /// <param name="Id">行の名前</param>
    /// <param name="Value">文字列表現</param>
    /// <param name="Expected">読んだ参照アクション</param>
    public sealed record ParseRow(string Id, string? Value, ForeignKeyReferentialAction Expected)
    {
        public override string ToString() => Id;
    }

    /// <summary>前後の空白・アンダースコア・ハイフン・大文字小文字を吸収し、解釈できない値は NoAction へ倒す</summary>
    public static TheoryData<ParseRow> ParseRows =>
        new()
        {
            // SQL Server の取込（sys.foreign_keys）の形
            new ParseRow("underscore", "SET_NULL", ForeignKeyReferentialAction.SetNull),
            new ParseRow("hyphen-lower", "set-null", ForeignKeyReferentialAction.SetNull),
            new ParseRow(
                "padded-mixed-case",
                " Set Default ",
                ForeignKeyReferentialAction.SetDefault
            ),
            new ParseRow(
                "underscore-set-default",
                "SET_DEFAULT",
                ForeignKeyReferentialAction.SetDefault
            ),
            new ParseRow("lower", "cascade", ForeignKeyReferentialAction.Cascade),
            new ParseRow("underscore-no-action", "NO_ACTION", ForeignKeyReferentialAction.NoAction),
            // 列挙に無い動作は NoAction
            new ParseRow("unknown", "RESTRICT", ForeignKeyReferentialAction.NoAction),
            new ParseRow("empty", "", ForeignKeyReferentialAction.NoAction),
            new ParseRow("blank", "   ", ForeignKeyReferentialAction.NoAction),
            new ParseRow("null", null, ForeignKeyReferentialAction.NoAction),
        };

    [Theory(
        DisplayName = "参照アクションの読みは表記ゆれを吸収し、解釈できない値は NoAction にする"
    )]
    [MemberData(nameof(ParseRows))]
    public void Parse_AbsorbsSpellingVariants(ParseRow row)
    {
        ForeignKeyReferentialActionHelper.Parse(row.Value).Should().Be(row.Expected);
    }
}
