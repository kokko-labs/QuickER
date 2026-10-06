using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncFixture;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 削除伝搬が「残す行」の祖先を残す閉包（生成ランタイムの <c>SyncKeptRows.CloseAsync</c>）を、テーブルに触れずに
/// 確かめる。
/// </summary>
/// <remarks>
/// 同期のランタイムテストのフィクスチャには自己参照のテーブルが無いため、同じテーブルの親を辿る閉包はここで固定する
/// （固定ランタイムは SyncFixture.g.cs としてこのアセンブリへコンパイルされるので internal のまま呼べる）。
/// 親の読み出しは辞書で代役する。
/// </remarks>
public sealed class SyncKeptRowsTests
{
    private const string Table = "nodes";

    /// <summary>キー → 親（テーブル名, キー）の表を、閉包が使う読み出し関数にする。</summary>
    private static System.Func<
        string,
        Task<IReadOnlyList<(string TableName, string KeyText)>>
    > Parents(Dictionary<string, (string TableName, string KeyText)[]> parents) =>
        keyText =>
            Task.FromResult<IReadOnlyList<(string TableName, string KeyText)>>(
                parents.TryGetValue(keyText, out var found) ? found : []
            );

    [Fact(
        DisplayName = "同じテーブルの親を 2 段以上たどって残し、サーバーにある親でたどるのをやめる"
    )]
    public async Task Close_FollowsParentsWithinTheTableAcrossSeveralSteps()
    {
        // c → b → a → root（root はサーバーにある＝候補でない）
        var (kept, others) = await SyncKeptRows.CloseAsync(
            Table,
            ["c"],
            new HashSet<string> { "a", "b", "c", "x" },
            Parents(
                new()
                {
                    ["c"] = [(Table, "b")],
                    ["b"] = [(Table, "a")],
                    ["a"] = [(Table, "root")],
                    ["x"] = [(Table, "a")],
                }
            )
        );

        kept.Should().BeEquivalentTo(["a", "b", "c"], "x は誰からも参照されない＝消えてよい");
        others.Should().BeEmpty();
    }

    [Fact(DisplayName = "参照が循環していても閉包は終わり、循環の全員を残す")]
    public async Task Close_EndsOnACycle()
    {
        var (kept, _) = await SyncKeptRows.CloseAsync(
            Table,
            ["a"],
            new HashSet<string> { "a", "b", "c" },
            Parents(
                new()
                {
                    ["a"] = [(Table, "b")],
                    ["b"] = [(Table, "c")],
                    ["c"] = [(Table, "a")],
                }
            )
        );

        kept.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact(DisplayName = "別テーブルの親は残す行に含めず、呼び出し元へ返す")]
    public async Task Close_HandsBackParentsInOtherTables()
    {
        var (kept, others) = await SyncKeptRows.CloseAsync(
            Table,
            ["a", "missing"],
            new HashSet<string> { "a", "b" },
            Parents(new() { ["a"] = [("orders", "1"), (Table, "b")], ["b"] = [("orders", "2")] })
        );

        kept.Should().BeEquivalentTo(["a", "b"], "候補でない種（missing）は残す対象にならない");
        others.Should().BeEquivalentTo([("orders", "1"), ("orders", "2")]);
    }
}
