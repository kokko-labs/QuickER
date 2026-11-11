using System.IO;
using QuickER.Documents;
using QuickER.Services;
using Xunit.Sdk;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 文書の状態機械の手の列を流す（VM で流す → 観測 → 参照モデルで流す → 突き合わせ → 不変条件 → 元のテストの表明）
/// </summary>
/// <remarks>
/// 食い違い・違反・表明の失敗は手の番号つきで集め、最後にまとめて赤にする（1 つの赤が後の手の違反を隠さないよう、
/// 製品の例外以外では止めない）。報告には手の列の全部を添える（そのまま再現できる）。
/// </remarks>
internal static class DocumentFileRunner
{
    /// <summary>手の列を流し、問題を返す（空なら合格）</summary>
    public static IReadOnlyList<string> Run(IReadOnlyList<DocStep> steps)
    {
        using var harness = new DocumentFileHarness();
        var model = new DocumentFileModel(harness.Same, harness.PathOf);
        var problems = new List<string>();

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var before = Before(harness, step);
            var generationBefore = step.Op is RestartOp ? 0 : harness.Vm.StatusGenerationForTests;

            try
            {
                harness.Apply(index, step);
            }
            catch (Exception ex)
            {
                problems.Add($"#{index} {step}: VM が例外を投げた: {ex}");
                break;
            }

            var after = harness.Observe(generationBefore);
            model.Apply(index, step);

            foreach (var mismatch in Compare(model, after, harness))
            {
                problems.Add($"#{index} {step}: モデルと食い違い: {mismatch}");
            }

            foreach (var violation in DocumentFileInvariants.Check(step, before, after, harness))
            {
                problems.Add($"#{index} {step}: 不変条件の違反: {violation}");
            }

            foreach (var expectation in step.Expectations)
            {
                try
                {
                    expectation(after);
                }
                catch (Exception ex) when (ex is XunitException or InvalidOperationException)
                {
                    problems.Add($"#{index} {step}: 表明の失敗: {ex.Message}");
                }
            }
        }

        if (problems.Count > 0)
        {
            problems.Add(
                "手の列："
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, steps.Select((step, i) => $"  #{i} {step}"))
            );
        }

        return problems;
    }

    private static DocBefore Before(DocumentFileHarness harness, DocStep step)
    {
        var vm = harness.Vm;
        string? delivered = null;

        if (step.Op is DeliverOp { Change: DocumentFileChangeKind.Modified } deliver)
        {
            delivered = deliver.Hash switch
            {
                // 読めない条件を掛けた手では、ハーネスが配送するハッシュも null になる
                DocEventHash.CurrentDisk
                    when harness.UnreadableDuring(step, harness.FileOf(vm.CurrentFilePath)) => null,
                DocEventHash.CurrentDisk => vm.CurrentFilePath is { } current
                    ? DocumentContentHash.TryCompute(current)
                    : null,
                DocEventHash.Unknown => harness.HashOf(DocumentFileHarness.UnknownHashLabel),
                _ => null,
            };
        }

        return new DocBefore(
            harness.FileOf(vm.CurrentFilePath),
            vm.IsDirty,
            vm.Entities.Select(e => e.TableName).ToList(),
            DocumentFileProbe.LastKnownHash(vm),
            DocumentFileProbe.IgnoredHash(vm),
            DocumentContentHash.TryCompute(harness.AutoSavePath),
            new GuiAppSettingsStore(Path.GetDirectoryName(harness.SettingsPath)!)
                .Load()
                .CurrentDocument.AutoSaveHash,
            delivered
        );
    }

    /// <summary>モデルの予言と観測を突き合わせる</summary>
    private static IEnumerable<string> Compare(
        DocumentFileModel model,
        DocObservation after,
        DocumentFileHarness harness
    )
    {
        string? HashOf(string? label) => label is null ? null : harness.HashOf(label);

        if (after.AppliedAccess != model.AppliedAccess)
        {
            yield return $"掛かったアクセスの条件 {after.AppliedAccess?.ToString() ?? "(none)"} ≠ {model.AppliedAccess?.ToString() ?? "(none)"}";
        }

        if (after.PathFile != model.Path)
        {
            yield return $"パス {after.PathFile} ≠ {model.Path}";
        }

        if (after.IsDirty != model.Dirty)
        {
            yield return $"未保存 {after.IsDirty} ≠ {model.Dirty}";
        }

        var title = after.Path is null
            ? "QuickER"
            : $"{Path.GetFileNameWithoutExtension(after.Path)}{(model.Dirty ? "*" : string.Empty)} - QuickER";

        if (after.Title != title)
        {
            yield return $"タイトル {after.Title} ≠ {title}";
        }

        if (!after.Tables.SequenceEqual(model.Tables))
        {
            yield return $"表 [{string.Join(", ", after.Tables)}] ≠ [{string.Join(", ", model.Tables)}]";
        }

        if (after.Queries != model.Queries)
        {
            yield return $"クエリの数 {after.Queries} ≠ {model.Queries}";
        }

        if (after.Notified != model.Notified || (model.Notified && after.Status != model.Status))
        {
            yield return $"通知 {after.Notified}:{after.Status} ≠ {model.Notified}:{model.Status}";
        }

        if (after.CanUndo != model.CanUndo)
        {
            yield return $"Undo の履歴 {after.CanUndo} ≠ {model.CanUndo}";
        }

        if (!after.Confirms.SequenceEqual(model.Confirms))
        {
            yield return $"確認 [{string.Join(" | ", after.Confirms)}] ≠ [{string.Join(" | ", model.Confirms)}]";
        }

        var errors = after.Errors.Select(FirstLine).ToList();

        if (!errors.SequenceEqual(model.Errors))
        {
            yield return $"エラー [{string.Join(" | ", errors)}] ≠ [{string.Join(" | ", model.Errors)}]";
        }

        if (!after.Informations.SequenceEqual(model.Informations))
        {
            yield return $"完了の通知 [{string.Join(" | ", after.Informations)}] ≠ [{string.Join(" | ", model.Informations)}]";
        }

        if (after.DragActive != model.DragActive)
        {
            yield return $"進行中のドラッグ {after.DragActive} ≠ {model.DragActive}";
        }

        if (after.FitRequested != model.FitRequested)
        {
            yield return $"fit-to-window {after.FitRequested} ≠ {model.FitRequested}";
        }

        if (after.SaveDialogs != model.SaveDialogs)
        {
            yield return $"保存ダイアログ {after.SaveDialogs} ≠ {model.SaveDialogs}";
        }

        if (after.LastKnownHash != HashOf(model.LastKnown))
        {
            yield return $"最終既知 {harness.LabelOf(after.LastKnownHash)} ≠ {model.LastKnown ?? "(none)"}";
        }

        if (after.IgnoredHash != HashOf(model.Ignored))
        {
            yield return $"無視した版 {harness.LabelOf(after.IgnoredHash)} ≠ {model.Ignored ?? "(none)"}";
        }

        foreach (var file in new[] { DocFile.A, DocFile.B, DocFile.Unwritable })
        {
            if (after.DiskHashes[file] != HashOf(model.Disk[file]))
            {
                yield return $"ディスク {file} {harness.LabelOf(after.DiskHashes[file])} ≠ {model.Disk[file] ?? "(none)"}";
            }
        }

        if (after.AutoSaveHash != HashOf(model.AutoSaveFile))
        {
            yield return $"自動保存の図 {harness.LabelOf(after.AutoSaveHash)} ≠ {model.AutoSaveFile ?? "(none)"}";
        }

        var meta = model.Meta ?? new DocMeta(null, null, false, null);
        var actual = after.Meta;

        if (
            harness.FileOf(actual.FilePath) != meta.Path
            || actual.LastKnownHash != HashOf(meta.LastKnown)
            || actual.IsDirty != meta.IsDirty
            || !string.Equals(
                actual.AutoSaveHash,
                HashOf(meta.AutoSave),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            yield return $"メタ ({harness.FileOf(actual.FilePath)}, {harness.LabelOf(actual.LastKnownHash)}, {actual.IsDirty}, {harness.LabelOf(actual.AutoSaveHash)}) ≠ {meta}";
        }

        var writes = harness.VmWrites.Select(w => (w.File, w.Label)).ToList();

        if (!writes.SequenceEqual(model.Writes))
        {
            yield return $"VM の書き込み [{string.Join(", ", writes)}] ≠ [{string.Join(", ", model.Writes)}]";
        }

        // VM が書いた版の中身（表の並び）がモデルの予言どおりか
        foreach (var (_, label) in writes)
        {
            foreach (var mismatch in CompareContent(harness, model, label))
            {
                yield return mismatch;
            }
        }
    }

    private static IEnumerable<string> CompareContent(
        DocumentFileHarness harness,
        DocumentFileModel model,
        string label
    )
    {
        if (!model.Versions.TryGetValue(label, out var expected))
        {
            yield break;
        }

        var document = harness.Decode(label);
        var tables = document.Schema.Entities.Select(e => e.TableName).ToList();

        if (
            !tables.SequenceEqual(expected.Tables)
            || document.Schema.Queries.Count != expected.Queries
        )
        {
            yield return $"版 {label} の中身 [{string.Join(", ", tables)}]/{document.Schema.Queries.Count} ≠ [{string.Join(", ", expected.Tables)}]/{expected.Queries}";
        }
    }

    private static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return end < 0 ? message : message[..end];
    }
}
