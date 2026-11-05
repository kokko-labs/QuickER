using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 要素を削除するコマンドが、Undo で元の位置へ戻す（一覧の並びが削除の前後で変わらない）ための作りを、
/// ソース走査で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図のテーブル・リレーション・列・一意制約の並びは、保存内容・DDL・制約の既定名に現れる。名前を持たない外部キーの既定名は
/// 並びの先頭から割り当てるため、リレーションが末尾へ戻ると 2 本の既定名が入れ替わり、次の DDL や同期が実在する
/// データベースに対して偽の drop と add を出す。
/// </para>
/// <para>
/// 位置の記録と復元は <c>IndexedRemoval</c> に 1 本化している。削除コマンドが手書きで位置を扱うと、
/// 一覧が変わったときの頭打ちの規則が写しごとにずれるため、ソース走査でも固定する。
/// </para>
/// <para>
/// 削除の操作ごとの往復（Undo で元の位置・元の並びへ戻ること）は、操作の往復の表（<see cref="UndoOperationRows"/> の
/// <c>removal-order-*</c> の行）が流す。
/// </para>
/// </remarks>
public class RemovalOrderRoundTripTests
{
    [Fact(DisplayName = "要素を取り除く削除コマンドは、位置の記録と復元を IndexedRemoval に任せる")]
    public void RemoveCommands_UseIndexedRemoval() =>
        UndoKnownFailures.Run(
            nameof(RemoveCommands_UseIndexedRemoval),
            () =>
            {
                var directory = Path.Combine(RepoRoot(), "src", "QuickER.Gui", "UndoRedo");
                var offenders = Directory
                    .EnumerateFiles(directory, "*Remove*Command.cs")
                    .Where(path =>
                    {
                        var source = File.ReadAllText(path);

                        // 取り除いた要素を Undo で戻す削除コマンドが対象（Execute が Remove を呼ぶもの）
                        return Regex.IsMatch(source, @"\.Remove(At)?\(")
                            && !source.Contains("IndexedRemoval.RemoveWhere");
                    })
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                offenders
                    .Should()
                    .BeEmpty(
                        "位置の記録と復元の写しを削除コマンドごとに持たない（IndexedRemoval.RemoveWhere / Restore を使う）"
                    );
            }
        );

    /// <summary>リポジトリ直下（QuickER.slnx のある場所）を、テストアセンブリの位置から遡って解決する</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("リポジトリ直下（QuickER.slnx）が見つかりません。");
    }
}
