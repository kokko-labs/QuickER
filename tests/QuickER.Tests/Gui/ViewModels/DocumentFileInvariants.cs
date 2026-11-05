using QuickER.Resources;
using QuickER.Services;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>手の前の状態（不変条件が「手の前と比べて」を判定するための控え）</summary>
internal sealed record DocBefore(
    DocFile? PathFile,
    bool IsDirty,
    IReadOnlyList<string> Tables,
    string? LastKnownHash,
    string? IgnoredHash,
    string? AutoSaveHash,
    string? MetaAutoSaveHash,
    string? DeliveredHash
);

/// <summary>
/// 文書の状態機械の不変条件（参照モデルと独立に、製品の約束だけから判定する）
/// </summary>
/// <remarks>
/// 5 つ：(1) 利用者が見ていない外部の版・将来版を黙って書き潰さない、(2) 最終既知ハッシュは書いた／読んだ内容そのもの、
/// (3) クリーンなら自動再読込・ダーティなら確認、(4) 自動保存のメタが食い違えば前のパスで復元しない、
/// (5) 読込の検査は拒否だけで修復しない。違反は文で返す（空なら守られた）。
/// </remarks>
internal static class DocumentFileInvariants
{
    public static IEnumerable<string> Check(
        DocStep step,
        DocBefore before,
        DocObservation after,
        DocumentFileHarness harness
    )
    {
        // (1) 書いた先が「いま開いているファイル」で、見ていない版（最終既知でも無視した版でもない）を書き潰したなら、
        //     その書き込みの前に上書きの確認へ「はい」と答えている。将来版はどのファイルでも将来版の確認が要る
        foreach (var (file, replaced, label, confirmsBefore) in harness.VmWrites)
        {
            if (replaced is null)
            {
                continue;
            }

            var asked = after.Confirms.Take(confirmsBefore).Select(c => c.Message).ToList();
            var replacedHash = harness.HashOf(replaced);

            if (harness.IsNewerFormatFile(replaced))
            {
                if (!step.Answer || !asked.Contains(Strings.Confirm_OverwriteNewerFormat))
                {
                    yield return $"(1) 将来版 {replaced} を確認なしで {label} に書き換えた（{file}）";
                }

                continue;
            }

            var seen = replacedHash == before.LastKnownHash || replacedHash == before.IgnoredHash;

            if (file == before.PathFile && !seen)
            {
                var confirmed =
                    step.Answer
                    && (
                        asked.Contains(Strings.Confirm_OverwriteExternalChange)
                        || asked.Contains(Strings.Confirm_OverwriteNewerFormat)
                    );

                if (!confirmed)
                {
                    yield return $"(1) 見ていない外部の版 {replaced} を確認なしで {label} に書き換えた（{file}）";
                }
            }
        }

        // (1)(5) 利用者の文書は、上書き保存の口を通った VM の書き込みと外部の書き込みでしか変わらない（修復も書き出しも書かない）
        foreach (var file in new[] { DocFile.A, DocFile.B })
        {
            var expected = harness.Disk[file] is { } known ? harness.HashOf(known) : null;

            if (after.DiskHashes[file] != expected)
            {
                yield return $"(1)(5) {file} が上書き保存の外で変わった（{harness.LabelOf(expected)} → {harness.LabelOf(after.DiskHashes[file])}）";
            }
        }

        // (2) 上書き保存・開く・再読込の口を通ったなら、最終既知ハッシュは最後に書いた／読んだ版
        if (harness.LastIo is { } io && after.LastKnownHash != harness.HashOf(io))
        {
            yield return $"(2) 最終既知ハッシュ {harness.LabelOf(after.LastKnownHash)} が、最後に読み書きした版 {io} でない";
        }

        // (3) 現在の文書宛ての内容の変更の配送
        if (
            step.Op is DeliverOp { Change: DocumentFileChangeKind.Modified, OtherPath: false }
            && before.PathFile is not null
        )
        {
            var reloadConfirms = after.WarningConfirms.Count(m =>
                m == Strings.Confirm_ExternalChangeReload
            );

            if (before.DeliveredHash is not null && before.DeliveredHash == before.LastKnownHash)
            {
                if (after.Confirms.Count > 0 || !after.Tables.SequenceEqual(before.Tables))
                {
                    yield return "(3) 反映済みの内容の変更で確認・読み直しをした";
                }
            }
            else if (!before.IsDirty)
            {
                if (reloadConfirms > 0 || after.IsDirty)
                {
                    yield return "(3) クリーンな文書の外部変更で確認を出した、またはダーティになった";
                }
            }
            else if (before.DeliveredHash is null || before.DeliveredHash != before.IgnoredHash)
            {
                if (reloadConfirms == 0)
                {
                    yield return "(3) ダーティな文書の外部変更で確認を出さなかった";
                }

                if (after.MaxConfirmDepth > 1)
                {
                    yield return $"(3) 確認が {after.MaxConfirmDepth} 枚重なった";
                }
            }
        }

        // (4) 復元：自動保存の図とメタの AutoSaveHash が食い違えば、無題・未保存で開く（自動保存の図は書き直さない）
        if (step.Op is RestartOp)
        {
            if (
                before.MetaAutoSaveHash is { } recorded
                && before.AutoSaveHash is { } actual
                && !string.Equals(recorded, actual, StringComparison.OrdinalIgnoreCase)
                && (after.Path is not null || !after.IsDirty)
            )
            {
                yield return "(4) メタが別の自動保存のものなのに、パス付き／クリーンで復元した";
            }

            if (after.AutoSaveHash != before.AutoSaveHash)
            {
                yield return "(4)(5) 復元が自動保存の図を書き直した";
            }
        }

        // (5) 断った読込は、メモリの図・パス・未保存を変えない
        if (step.Op is OpenOp && after.Errors.Count > 0)
        {
            if (
                after.PathFile != before.PathFile
                || after.IsDirty != before.IsDirty
                || !after.Tables.SequenceEqual(before.Tables)
            )
            {
                yield return "(5) 開けなかったのに図・パス・未保存が変わった";
            }
        }

        if (
            step.Op is DeliverOp
            && after.Notified
            && after.Status == Strings.Status_ExternalReloadFailed
            && (after.PathFile != before.PathFile || !after.Tables.SequenceEqual(before.Tables))
        )
        {
            yield return "(5) 再読込に失敗したのに図・パスが変わった";
        }
    }
}
