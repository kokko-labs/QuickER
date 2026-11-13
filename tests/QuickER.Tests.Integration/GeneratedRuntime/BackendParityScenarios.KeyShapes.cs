using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// キーと表名の形（GuidKey の主キー・binary(4) の主キー・ドットを含む表名）の決まった形のシナリオ。どの形も同じ手の並びを
/// 全部の実装先へ流し、QuickER 版（SQLite 実ファイル）を基準に比べる。
/// </summary>
/// <remarks>
/// <para>
/// 形ごとに 3 つ（ドットを含む表名は 4 つ）のシナリオに分ける。シナリオはその手をすべて持つ実装先でだけ流れるので、
/// リモート面の手（CRUD・グラフ保存）・全機能面の手（クエリ・Include・BulkInsert）・除外 blob の Stream の手を分けないと、
/// リモートや EF Core の実装先が比較から外れる。
/// </para>
/// <para>
/// 親の無い子の挿入は列の最後にだけ置き、受け入れた手の差（<see cref="ParityDeviation.OrphanInsert"/>＝インメモリは外部キーを
/// 検査しない）の印を付ける。ドットを含む表名では、DB が子の外部キーで断ることが、<c>main.region_notes</c> の外部キーが実際に
/// 作られていることの裏付けになる（SQLite の外部キーの句は表名にスキーマを書けない＝<c>REFERENCES "regions"</c>）。
/// </para>
/// </remarks>
public static partial class BackendParityScenarios
{
    private static IEnumerable<ParityScenario> KeyShapeScenarios()
    {
        foreach (
            var (shape, name) in new[]
            {
                (KeyShape.GuidKey, "guidkey"),
                (KeyShape.BinaryKey, "binary"),
                (KeyShape.DottedTable, "dotted"),
            }
        )
        {
            yield return KeyShapeRemoteScenario(shape, name);
            yield return KeyShapeFullSurfaceScenario(shape, name);
            yield return KeyShapeCascadeScenario(shape, name);
        }

        yield return new(
            "key-shape-dotted-blob-stream",
            [],
            [
                new KeyShapeOp(KeyShape.DottedTable, KeyShapeStep.InsertParent, 1, Text: "a"),
                new KeyShapeOp(KeyShape.DottedTable, KeyShapeStep.WriteBlob, 1),
                ParityExpect.Outcome("key-shape-dotted-blob-written", "written=True"),
                new KeyShapeOp(KeyShape.DottedTable, KeyShapeStep.ReadBlob, 1),
                ParityExpect.Outcome("key-shape-dotted-blob-read", "found=True bytes=010203"),
                new KeyShapeOp(KeyShape.DottedTable, KeyShapeStep.ReadBlob, 9),
                ParityExpect.Outcome("key-shape-dotted-blob-missing-row", "found=False bytes="),
            ]
        );
    }

    /// <summary>リモート面の手：挿入・取得・更新（読んでから・読まずに）・グラフ保存（新しい親子・編集）・削除・親の無い子の挿入</summary>
    private static ParityScenario KeyShapeRemoteScenario(KeyShape shape, string name)
    {
        var ops = new List<ParityOp>
        {
            new KeyShapeOp(shape, KeyShapeStep.InsertParent, 1, Text: "a"),
            new KeyShapeOp(shape, KeyShapeStep.InsertParent, 2, Text: "b"),
            new KeyShapeOp(shape, KeyShapeStep.InsertChild, 1, 10, "x"),
            new KeyShapeOp(shape, KeyShapeStep.GetParent, 1),
            ParityExpect.Outcome($"key-shape-{name}-get", $"{ParentKeyText(shape, 1)} text=a"),
            new KeyShapeOp(shape, KeyShapeStep.GetParent, 9),
            ParityExpect.Outcome($"key-shape-{name}-get-missing", "null"),
            new KeyShapeOp(shape, KeyShapeStep.UpdateParentLoaded, 1, Text: "a2"),
            ParityExpect.Outcome($"key-shape-{name}-update-loaded", "updated=True"),
            new KeyShapeOp(shape, KeyShapeStep.UpdateParentDetached, 2, Text: "b2"),
            ParityExpect.Outcome($"key-shape-{name}-update-detached", "updated=True"),
            new KeyShapeOp(shape, KeyShapeStep.UpdateParentDetached, 9, Text: "z"),
            ParityExpect.Outcome($"key-shape-{name}-update-missing", "updated=False"),
            new KeyShapeOp(shape, KeyShapeStep.SaveNewGraph, 3, 30, "c"),
            ParityExpect.Outcome(
                $"key-shape-{name}-save-new-graph",
                "saved=2 parent=Unchanged child=Unchanged"
            ),
            new KeyShapeOp(shape, KeyShapeStep.SaveEditGraph, 1, 10, "a3"),
            ParityExpect.Outcome(
                $"key-shape-{name}-save-edit-graph",
                $"saved=3 parent=Unchanged removed=Removed children=[{ChildKeyText(shape, 11)}:Unchanged]"
            ),
            new KeyShapeOp(shape, KeyShapeStep.DeleteChild, 0, 11),
            ParityExpect.Outcome($"key-shape-{name}-delete-child", "deleted=True"),
            new KeyShapeOp(shape, KeyShapeStep.DeleteParent, 2),
            ParityExpect.Outcome($"key-shape-{name}-delete-parent", "deleted=True"),
        };

        if (shape == KeyShape.GuidKey)
        {
            ops.Add(new KeyShapeOp(shape, KeyShapeStep.GeneratedKeyRoundTrip, 0, Text: "g"));
            ops.Add(
                ParityExpect.Outcome(
                    "key-shape-guidkey-generated-key",
                    "length=36 found=True deleted=True"
                )
            );
        }

        ops.Add(
            ParityExpect.StateKeys(
                $"key-shape-{name}-state",
                ParentKeyText(shape, 1),
                ParentKeyText(shape, 3),
                ChildKeyText(shape, 30)
            )
        );

        // 親の無い子：DB は外部キーで断る（インメモリは外部キーを検査しない＝受け入れた手の差）
        ops.Add(
            new DeviatingOp(
                new KeyShapeOp(shape, KeyShapeStep.InsertOrphanChild, 9, 90, "o"),
                ParityDeviation.OrphanInsert
            )
        );
        ops.Add(
            ParityExpect.OutcomeByKind(
                $"key-shape-{name}-orphan-child-is-refused",
                ParityStep.StoreRejected,
                inMemory: $"{ChildKeyText(shape, 90)} parent={ParentKeyPart(shape, 9)} text=o"
            )
        );

        return new($"key-shape-{name}", [], ops);
    }

    /// <summary>全機能面の手：キーの等値・IN のクエリ・Include（親 → 子・子 → 親）・BulkInsert</summary>
    private static ParityScenario KeyShapeFullSurfaceScenario(KeyShape shape, string name) =>
        new(
            $"key-shape-{name}-full",
            [],
            [
                new KeyShapeOp(shape, KeyShapeStep.InsertParent, 1, Text: "a"),
                new KeyShapeOp(shape, KeyShapeStep.InsertParent, 2, Text: "b"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 1, 10, "x"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 1, 11, "y"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 2, 20, "z"),
                new KeyShapeOp(shape, KeyShapeStep.QueryParentByKey, 1),
                ParityExpect.Outcome(
                    $"key-shape-{name}-query-equal",
                    $"{ParentKeyText(shape, 1)} text=a"
                ),
                new KeyShapeOp(shape, KeyShapeStep.QueryParentsInKeys, 1, 2),
                ParityExpect.Outcome(
                    $"key-shape-{name}-query-in",
                    $"{ParentKeyText(shape, 1)} text=a|{ParentKeyText(shape, 2)} text=b"
                ),
                new KeyShapeOp(shape, KeyShapeStep.IncludeChildren, 1),
                ParityExpect.Outcome(
                    $"key-shape-{name}-include-children",
                    $"{ParentKeyText(shape, 1)} text=a children=[{ChildKeyText(shape, 10)},{ChildKeyText(shape, 11)}]"
                ),
                new KeyShapeOp(shape, KeyShapeStep.IncludeParent, 0),
                ParityExpect.Outcome(
                    $"key-shape-{name}-include-parent",
                    $"{ChildKeyText(shape, 10)}->{ParentKeyText(shape, 1)}|{ChildKeyText(shape, 11)}->{ParentKeyText(shape, 1)}"
                        + $"|{ChildKeyText(shape, 20)}->{ParentKeyText(shape, 2)}"
                ),
                new KeyShapeOp(shape, KeyShapeStep.BulkInsertParents, 5, 2, "bulk"),
                ParityExpect.Outcome($"key-shape-{name}-bulk-insert", "inserted=2"),
            ]
        );

    /// <summary>親を子ごと消すグラフ保存（子はカスケード）</summary>
    private static ParityScenario KeyShapeCascadeScenario(KeyShape shape, string name) =>
        new(
            $"key-shape-{name}-cascade",
            [],
            [
                new KeyShapeOp(shape, KeyShapeStep.InsertParent, 1, Text: "a"),
                new KeyShapeOp(shape, KeyShapeStep.InsertParent, 2, Text: "b"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 1, 10, "x"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 1, 11, "y"),
                new KeyShapeOp(shape, KeyShapeStep.InsertChild, 2, 20, "z"),
                new KeyShapeOp(shape, KeyShapeStep.SaveRemoveParentGraph, 1),
                ParityExpect.Outcome(
                    $"key-shape-{name}-remove-parent-graph",
                    "saved=3 children=2 parent=Removed"
                ),
                ParityExpect.StateKeys(
                    $"key-shape-{name}-remove-parent-graph-state",
                    ParentKeyText(shape, 2),
                    ChildKeyText(shape, 20)
                ),
            ]
        );

    /// <summary>親の行のキー（<c>tokens/…</c> の形）</summary>
    private static string ParentKeyText(KeyShape shape, int key) =>
        shape switch
        {
            KeyShape.GuidKey => $"tokens/{ParentKeyPart(shape, key)}",
            KeyShape.BinaryKey => $"blobs/{ParentKeyPart(shape, key)}",
            _ => $"main.regions/{ParentKeyPart(shape, key)}",
        };

    /// <summary>親のキーの値の部分</summary>
    private static string ParentKeyPart(KeyShape shape, int key) =>
        shape switch
        {
            KeyShape.GuidKey => KeyShapes.TokenKeyText(key),
            KeyShape.BinaryKey => System.Convert.ToHexString(KeyShapes.BlobKeyBytes(key)),
            _ => key.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    /// <summary>子の行のキー（<c>token_uses/10</c> の形）</summary>
    private static string ChildKeyText(KeyShape shape, int key) =>
        shape switch
        {
            KeyShape.GuidKey => $"token_uses/{key}",
            KeyShape.BinaryKey => $"blob_tags/{key}",
            _ => $"main.region_notes/{key}",
        };
}
