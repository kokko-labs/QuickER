using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SqlTypeText"/> の値リスト型の検証と、DDL / 同期スクリプト生成がそれを関門にしていることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 安全パターンの通過・拒否の個々の表記は方言の型の表（<c>DialectTypeRows.SafeText.cs</c> の <c>safe-*</c>）が固定する。
/// </para>
/// <para>
/// <see cref="Column.DataType"/> は図の自由記述で、DDL・同期スクリプトへクォートもエスケープもせず補間される。
/// ここでは「長い値リスト型が通ること」（＝セキュリティ修正が実 DB でのデータ欠落へ化けないこと）と、
/// 「文を終わらせる・文字列を開く・コメントを始める文字を含む表記は DDL・同期の入口で名指しで失敗すること」を固定する。
/// </para>
/// </remarks>
public class SqlTypeTextTests
{
    /// <summary>
    /// 長さの上限は値リスト型（MySQL の enum / set）には掛からないことを検証する。値が 14 個程度の実在の enum でも
    /// 200 文字を超え、掛けると取込が持ち帰ったその 1 列のせいで図全体の DDL・同期が止まる。
    /// 値リストの判定は線形なので、非常に長い値リストも短時間で判定できる（上限で先に切らなくてよいことの確認）。
    /// </summary>
    [Fact(DisplayName = "長さ上限は値リスト型には掛からない（長い enum も通る）")]
    public void IsSafe_LongValueList_Accepted()
    {
        var values = string.Join(
            ",",
            Enumerable.Range(1, 30).Select(i => $"'status_value_{i:D2}'")
        );
        var longEnum = $"enum({values})";
        longEnum.Length.Should().BeGreaterThan(SqlTypeText.MaxLength);

        SqlTypeText.IsSafe(longEnum).Should().BeTrue();
        SqlTypeText.IsSafe($"set({values})").Should().BeTrue();

        // 非常に長い値リスト（MySQL の上限に近い数）でも短時間で判定できる
        var huge = $"enum({string.Join(",", Enumerable.Range(1, 60000).Select(i => $"'v{i}'"))})";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SqlTypeText.IsSafe(huge).Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        // 値リストに見せかけた形は長くても通さない
        SqlTypeText.IsSafe($"enum({values}); DROP TABLE t; --").Should().BeFalse();
    }

    // ---------------- DDL 生成の関門 ----------------

    // ---------------- 同期スクリプト生成の関門 ----------------

    /// <summary>orders テーブル（int 主キー＋指定型の memo 列）の最小の図を組み立てる</summary>
    private static ErDiagram BuildDiagram(string memoDataType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "memo",
                            DataType = memoDataType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>型の拒否の例外文に、テーブル名・列名の改行・制御文字がそのまま載らないことを検証する</summary>
    /// <remarks>
    /// 型の検証は名前の検証より先に走るため、改行を含む名前もこの例外文へ届く。型だけを畳んで名前を素のまま
    /// 載せると、例外文を表示する側の行構造を壊す
    /// </remarks>
    [Fact(DisplayName = "安全でない型の例外文は、テーブル名・列名の改行も空白へ畳む")]
    public void Validate_UnsafeType_SanitizesNamesInMessage()
    {
        var diagram = BuildDiagram("int; DROP TABLE users");
        diagram.Entities[0].TableName = "ord\ners";
        diagram.Entities[0].Columns[1].Name = "me\r\nmo";

        var act = () => SqlTypeText.Validate(diagram);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().NotContain("\n").And.NotContain("\r");
        message.Should().Contain("ord ers.me  mo");
    }
}
