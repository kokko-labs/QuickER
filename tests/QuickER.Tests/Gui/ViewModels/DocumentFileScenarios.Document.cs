using System.IO;
using System.Security.Cryptography;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.ViewModels.DocFile;

namespace QuickER.Tests.Gui.ViewModels;

internal static partial class DocumentFileScenarios
{
    /// <summary>現在のパス・保存・開く・置換・取込・未保存の推移・自動保存のメタと復元・完了の通知</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> DocumentRows() =>
        [
            Row(
                "doc-save-with-path-no-dialog",
                "Save: 現在パスありなら無ダイアログで上書き保存する",
                Doc.SetPath(A),
                Doc.Add(),
                Doc.Save(B)
                    .Expect(o =>
                    {
                        o.SaveDialogs.Should().Be(0, "現在パスありの保存はダイアログを開かない");
                        File.Exists(o.PathOf(A)).Should().BeTrue("現在パスへ上書き保存される");
                        File.Exists(o.PathOf(B)).Should().BeFalse();
                        o.Path.Should().Be(o.PathOf(A));
                    })
            ),
            Row(
                "doc-save-untitled-opens-dialog",
                "Save: 無題ならダイアログを開き、成功でパスを設定する",
                Doc.Add().Expect(o => o.Path.Should().BeNull()),
                Doc.Save(A)
                    .Expect(o =>
                    {
                        o.SaveDialogs.Should().Be(1, "無題保存はダイアログを開く");
                        o.Path.Should().Be(o.PathOf(A));
                        File.Exists(o.PathOf(A)).Should().BeTrue();
                        o.IsDirty.Should().BeFalse("保存直後はクリーン");
                    })
            ),
            Row(
                "doc-saveas-always-opens-dialog",
                "SaveAs: 現在パスありでも常にダイアログを開きパスを更新する",
                Doc.SetPath(A),
                Doc.Add(),
                Doc.SaveAs(B)
                    .Expect(o =>
                    {
                        o.SaveDialogs.Should().Be(1, "別名保存は常にダイアログを開く");
                        o.Path.Should().Be(o.PathOf(B));
                        File.Exists(o.PathOf(B)).Should().BeTrue();
                    })
            ),
            Row(
                "doc-open-sets-path-and-clean",
                "Open: 現在パスを設定しクリーン状態にする",
                Doc.Write(A, "NewTable"),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A));
                        o.IsDirty.Should().BeFalse();
                        o.Title.Should().Be("Doc - QuickER");
                    })
            ),
            Row(
                "doc-replace-keeps-path-dirty",
                "置換: 現在パスを維持しダーティになる",
                Doc.SetPath(A),
                Doc.Save().Expect(o => o.IsDirty.Should().BeFalse()),
                Doc.ReplaceFromModule("T")
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A), "置換ではパスを維持する（無題化しない）");
                        o.IsDirty.Should().BeTrue("置換は内容変更なのでダーティになる");
                    })
            ),
            Row(
                "doc-import-keeps-path",
                "インポート: 現在パスを維持する",
                Doc.SetPath(A),
                Doc.ImportMermaid("Customer")
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle();
                        o.Path.Should().Be(o.PathOf(A), "インポートではパスを維持する");
                    })
            ),
            Row(
                "doc-dirty-lifecycle",
                "ダーティ: 保存でクリーン、編集で * 付き、上書きで * 解消",
                Doc.Add(),
                Doc.Save(A)
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeFalse();
                        o.Title.Should().Be("Doc - QuickER");
                    }),
                Doc.Add()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeTrue();
                        o.Title.Should().Be("Doc* - QuickER");
                    }),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeFalse();
                        o.Title.Should().Be("Doc - QuickER");
                    })
            ),
            Row(
                "doc-untitled-dirty-no-star",
                "ダーティ: 無題は編集しても * を出さない",
                Doc.Add()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeTrue();
                        o.Title.Should().Be("QuickER");
                    })
            ),
            Row(
                "doc-autosave-persists-meta",
                "復元メタ: 保存時にパス・ハッシュ・ダーティを永続化する",
                Doc.Add(),
                Doc.Save(A),
                Doc.AutoSave()
                    .Expect(o =>
                    {
                        o.Meta.FilePath.Should().Be(o.PathOf(A));
                        o.Meta.IsDirty.Should().BeFalse();
                        o.Meta.LastKnownHash.Should()
                            .Be(Convert.ToHexString(SHA256.HashData(o.DiskBytes(A))));
                    })
            ),
            Row(
                "doc-restore-path-title-clean",
                "復元: パス・タイトル・クリーン状態を引き継ぐ",
                Doc.Add(),
                Doc.Save(A),
                Doc.AutoSave(),
                Doc.Restart()
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A));
                        o.IsDirty.Should().BeFalse();
                        o.Title.Should().Be("Doc - QuickER");
                    })
            ),
            Row(
                "doc-restore-carries-dirty",
                "復元: 前回未保存のダーティ状態を引き継ぐ",
                Doc.Add(),
                Doc.Save(A),
                Doc.Add().Expect(o => o.IsDirty.Should().BeTrue()),
                Doc.AutoSave(),
                Doc.Restart()
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A));
                        o.IsDirty.Should().BeTrue("前回未保存の状態を引き継ぐ");
                        o.Title.Should().Be("Doc* - QuickER");
                    })
            ),
            Row(
                "doc-restore-duplicate-ids-empty",
                "復元: Id が重複する自動保存ファイルは復元しない",
                Doc.WriteAutoSave(
                    DocContentKind.DuplicateId,
                    "duplicate-column-ids",
                    DuplicateColumnIdDocument
                ),
                Doc.Restart()
                    .Expect(o =>
                    {
                        o.Tables.Should().BeEmpty("重複 Id の作業状態は復元しない");
                        o.Path.Should().BeNull();
                    })
            ),
            Row(
                "doc-save-notifies-status-bar",
                "保存の完了はステータスバーで知らせる",
                Doc.Add(),
                Doc.Save(A)
                    .Expect(o =>
                    {
                        o.Status.Should().Be(Strings.Status_Saved);
                        o.Informations.Should().BeEmpty();
                    })
            ),
            Row(
                "doc-open-notifies-status-bar",
                "開くの完了はステータスバーで知らせる",
                Doc.Write(A, "NewTable"),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A));
                        o.Status.Should().Be(Strings.Status_Opened);
                        o.Informations.Should().BeEmpty();
                    })
            ),
        ];

    /// <summary>列 Id が表をまたいで重複する図（元のテストの自動保存ファイルと同じ形）</summary>
    private static DiagramDocument DuplicateColumnIdDocument()
    {
        var duplicated = Guid.NewGuid();

        return new DiagramDocument
        {
            Schema = new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "Orders",
                        Columns =
                        {
                            new Column { Id = duplicated, Name = "OrderId" },
                        },
                    },
                    new Entity
                    {
                        TableName = "OrderLines",
                        Columns =
                        {
                            new Column { Id = duplicated, Name = "LineId" },
                        },
                    },
                },
            },
            Layout = null,
        };
    }

    /// <summary>開く・保存の堅牢性（壊れた・無関係・明示 null の JSON・未保存の確認の水準・書き込みの失敗）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> RobustnessRows() =>
        [
            Row(
                "robust-open-corrupt-json-keeps-state",
                "Open: 壊れた JSON は落ちずにエラー通知し現状維持する",
                Doc.WriteRaw(
                    B,
                    DocContentKind.Corrupt,
                    "broken",
                    "{ \"Version\": 1, \"Schema\": {"
                ),
                Doc.Add(),
                Doc.SetPath(A),
                Doc.Open(B)
                    .Expect(o =>
                    {
                        o.Errors.Should().ContainSingle().Which.Should().Contain(o.PathOf(B));
                        o.Tables.Should().ContainSingle("読込に失敗したら現在の図を変えない");
                        o.Path.Should().Be(o.PathOf(A), "失敗したファイルへ紐付け直さない");
                        o.IsDirty.Should().BeTrue("未保存の変更はそのまま残る");
                    })
            ),
            Row(
                "robust-open-unrelated-json-rejected",
                "Open: 無関係な JSON は空の図として取り込まず拒否する",
                Doc.WriteRaw(
                    B,
                    DocContentKind.NotDiagram,
                    "package-json",
                    "{\"name\":\"x\",\"version\":\"1.0.0\"}"
                ),
                Doc.Add(),
                Doc.SetPath(A),
                Doc.Open(B)
                    .Expect(o =>
                    {
                        o.Errors.Should().ContainSingle().Which.Should().Contain(o.PathOf(B));
                        o.Tables.Should().ContainSingle("無関係な JSON で図を空にしない");
                        o.Path.Should()
                            .Be(o.PathOf(A), "無関係なファイルを次の上書き保存先にしない");
                        o.DiskText(B)
                            .Should()
                            .Be(
                                "{\"name\":\"x\",\"version\":\"1.0.0\"}",
                                "他人のファイルは書き換えない"
                            );
                    })
            ),
            Row(
                "robust-open-explicit-null-collections",
                "Open: 明示 null 入りの図ファイルは落ちずに空の図として開ける",
                Doc.WriteRaw(
                    A,
                    DocContentKind.NullCollections,
                    "null-entities",
                    """{"Version":1,"Schema":{"Entities":null}}"""
                ),
                Doc.Add(),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Errors.Should().BeEmpty("正規化で読めるため失敗通知は出さない");
                        o.Tables.Should().BeEmpty("Entities: null は空の図として読み込まれる");
                        o.Queries.Should().Be(0);
                        o.Path.Should().Be(o.PathOf(A), "読み込めたファイルへ紐付ける");
                    })
            ),
            Row(
                "robust-open-null-schema-rejected",
                "Open: schema が null の JSON は拒否し現状維持する",
                Doc.WriteRaw(
                    A,
                    DocContentKind.NullSchema,
                    "null-schema",
                    """{"Version":1,"Schema":null}"""
                ),
                Doc.Add(),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Errors.Should().ContainSingle().Which.Should().Contain(o.PathOf(A));
                        o.Tables.Should().ContainSingle("拒否したら現在の図を変えない");
                        o.Path.Should().BeNull("拒否したファイルへ紐付けない");
                    })
            ),
            Row(
                "robust-open-dirty-warning-cancel",
                "Open: ダーティなら警告確認し、キャンセルで現状維持する",
                Doc.Write(A, "FromFile"),
                Doc.Add().Expect(o => o.IsDirty.Should().BeTrue()),
                Doc.Open(A)
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OpenDiagram);
                        o.Tables.Should().ContainSingle().Which.Should().Be("NewTable");
                        o.Path.Should().BeNull("キャンセルではファイルへ紐付かない");
                    })
            ),
            Row(
                "robust-open-clean-with-entities-plain-confirm",
                "Open: クリーンで図があるときは通常確認（Question）",
                Doc.Write(A, "FromFile"),
                Doc.Open(A).Expect(o => o.IsDirty.Should().BeFalse()),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.PlainConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OpenDiagram);
                        o.WarningConfirms.Should().BeEmpty("クリーンなら警告水準へ引き上げない");
                        o.Tables.Should().ContainSingle().Which.Should().Be("FromFile");
                    })
            ),
            Row(
                "robust-open-clean-empty-no-confirm",
                "Open: 空でクリーンなら確認を出さない",
                Doc.Write(A, "FromFile"),
                Doc.Open(A)
                    .No()
                    .Expect(o =>
                    {
                        o.Confirms.Should().BeEmpty();
                        o.Tables.Should().ContainSingle().Which.Should().Be("FromFile");
                        o.Path.Should().Be(o.PathOf(A));
                    })
            ),
            Row(
                "robust-save-write-failure-keeps-dirty",
                "Save: 書き込み失敗はエラー通知しダーティを維持する",
                Doc.Add(),
                Doc.Save(Unwritable)
                    .Expect(o =>
                    {
                        o.Errors.Should()
                            .ContainSingle()
                            .Which.Should()
                            .StartWith(Strings.Save_Failed);
                        o.IsDirty.Should().BeTrue("保存できていない変更をクリーン扱いにしない");
                        o.Path.Should().BeNull("保存に失敗したファイルへ紐付けない");
                        File.Exists(o.PathOf(Unwritable)).Should().BeFalse();
                        Directory
                            .GetFiles(o.Harness.Folder, "*.tmp", SearchOption.AllDirectories)
                            .Should()
                            .BeEmpty("一時ファイルを残さない");
                    })
            ),
            Row(
                "robust-saveas-write-failure-keeps-path",
                "Save: 失敗しても直前の現在パスを書き換えない",
                Doc.SetPath(A),
                Doc.Add(),
                Doc.SaveAs(Unwritable)
                    .Expect(o =>
                    {
                        o.Errors.Should().ContainSingle();
                        o.Path.Should().Be(o.PathOf(A), "別名保存の失敗で現在パスを移さない");
                        o.IsDirty.Should().BeTrue();
                    })
            ),
        ];

    /// <summary>図を失う操作の確認の水準（新規作成・Mermaid の再取込）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> DiscardConfirmRows()
    {
        var withoutQueries = new EntityIdHolder();

        return
        [
            Row(
                "discard-new-clean-plain-confirm",
                "NewDiagram: クリーン時は通常確認（Question）のまま",
                Doc.Write(A, "Saved"),
                Doc.Open(A).No().Expect(o => o.IsDirty.Should().BeFalse()),
                Doc.New()
                    .No()
                    .Expect(o =>
                    {
                        o.PlainConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_ClearDiagram);
                        o.WarningConfirms.Should().BeEmpty();
                        o.Tables.Should().ContainSingle().Which.Should().Be("Saved");
                    })
            ),
            Row(
                "discard-new-queries-only-confirms",
                "NewDiagram: クエリだけの図（クリーン）でも確認を出す",
                Doc.WriteDocument(A, "queries-only", () => DocumentWith(null, 1)),
                Doc.Open(A)
                    .No()
                    .Expect(o =>
                    {
                        o.Tables.Should().BeEmpty();
                        o.Queries.Should().Be(1);
                        o.IsDirty.Should().BeFalse();
                    }),
                Doc.New()
                    .No()
                    .Expect(o =>
                    {
                        o.PlainConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_ClearDiagram);
                        o.Queries.Should().Be(1, "キャンセルすればクエリは残る");
                    })
            ),
            Row(
                "discard-new-empty-but-dirty-warns",
                "NewDiagram: 空でもダーティなら確認を出す",
                Doc.Add().No(),
                Doc.Undo()
                    .No()
                    .Expect(o =>
                    {
                        o.Tables.Should().BeEmpty();
                        o.IsDirty.Should().BeTrue();
                    }),
                Doc.New()
                    .No()
                    .Expect(o =>
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_ClearDiagram)
                    )
            ),
            Row(
                "discard-new-empty-clean-no-confirm",
                "NewDiagram: 空でクリーンなら確認を出さない",
                Doc.New().No().Expect(o => o.Confirms.Should().BeEmpty())
            ),
            Row(
                "discard-import-same-structure-with-queries",
                "Mermaid 再取込: 構造同一でもクエリがあれば件数付きで確認する",
                Doc.WriteDocument(
                    A,
                    "customer-with-queries",
                    () => DocumentWith(CustomerEntity(), 2)
                ),
                Doc.Open(A)
                    .No()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeFalse();
                        o.Queries.Should().Be(2);
                    }),
                Doc.ImportMermaid("Customer")
                    .No()
                    .Expect(o =>
                    {
                        o.PlainConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Contain(string.Format(Strings.Import_QueriesRemovedWarning, 2));
                        o.Queries.Should().Be(2, "キャンセルすればクエリは残る");
                    })
            ),
            Row(
                "discard-import-same-structure-without-queries",
                "Mermaid 再取込: 構造同一でクエリが無くても、空でない図なら確認する",
                Doc.WriteDocument(
                    A,
                    "customer-without-queries",
                    () => DocumentWith(CustomerEntity(), 0)
                ),
                Doc.Open(A).No().Expect(o => withoutQueries.Id = o.Vm.Entities.Single().Id),
                Doc.ImportMermaid("Customer")
                    .No()
                    .Expect(o =>
                    {
                        o.PlainConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .NotContain(string.Format(Strings.Import_QueriesRemovedWarning, 0));
                        o.WarningConfirms.Should().BeEmpty();
                        o.Vm.Entities.Should()
                            .ContainSingle()
                            .Which.Id.Should()
                            .Be(withoutQueries.Id, "キャンセルすれば置換しない");
                    })
            ),
        ];
    }

    /// <summary>手をまたいで表の Id を控える（開いた手で控え、後の手で比べる）</summary>
    private sealed class EntityIdHolder
    {
        public Guid Id { get; set; }
    }

    /// <summary>Customer 表（int の主キー列 CustomerId）</summary>
    private static Entity CustomerEntity() =>
        new()
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

    /// <summary>表（無ければ表なし）と名前付きクエリ <paramref name="queryCount"/> 件の図</summary>
    private static DiagramDocument DocumentWith(Entity? entity, int queryCount)
    {
        var schema = new ErDiagram { TargetDbms = "sqlserver" };

        if (entity is not null)
        {
            schema.Entities.Add(entity);
        }

        for (var index = 0; index < queryCount; index++)
        {
            schema.Queries.Add(
                new QueryDefinition
                {
                    Name = $"Query{index}",
                    EntityId = entity?.Id ?? Guid.NewGuid(),
                }
            );
        }

        return new DiagramDocument { Schema = schema, Layout = null };
    }

    /// <summary>Undo の外の変更（クエリの差し替え・幅の自動調整）と未保存</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> UntrackedChangeRows() =>
        [
            Row(
                "untracked-queries-replacement-dirty",
                "ReplaceQueries: クエリ差し替えでダーティになる",
                Doc.Add(),
                Doc.Save(A).Expect(o => o.IsDirty.Should().BeFalse("保存直後はクリーン")),
                Doc.ReplaceQueries(1)
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeTrue("クエリは保存文書の一部なので未保存変更になる");
                        o.Title.Should().Contain("*");
                    })
            ),
            Row(
                "untracked-queries-then-save-clean",
                "ReplaceQueries: 保存すると再びクリーンになる",
                Doc.Add(),
                Doc.Save(A),
                Doc.ReplaceQueries(1),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeFalse();
                        o.Title.Should().NotContain("*");
                    })
            ),
            Row(
                "untracked-autofit-width-change-dirty",
                "幅自動調整: 幅が変わるとダーティになる",
                Doc.Add(),
                Doc.Save(A),
                Doc.SetWidth(0, 999).Expect(o => o.IsDirty.Should().BeFalse()),
                Doc.AutoFitWidths(changes: true)
                    .Expect(o =>
                    {
                        o.Vm.Entities[0].Width.Should().NotBe(999, "内容に合う幅へ調整される");
                        o.IsDirty.Should()
                            .BeTrue("幅は保存対象（EntityLayout.Width）なので未保存変更になる");
                    })
            ),
            Row(
                "untracked-autofit-no-change-clean",
                "幅自動調整: 幅が変わらなければクリーンのまま",
                Doc.Add(),
                Doc.Save(A),
                Doc.AutoFitWidths(changes: false),
                Doc.Save().Expect(o => o.IsDirty.Should().BeFalse()),
                Doc.AutoFitWidths(changes: false)
                    .Expect(o => o.IsDirty.Should().BeFalse("無変更の自動調整でダーティにはしない"))
            ),
        ];

    /// <summary>ドラッグ中の外部変更（打ち切って開始位置へ戻す・反映済みなら打ち切らない）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> DragCancelRows() =>
        [
            Row(
                "drag-clean-reload-keeps-history-empty",
                "クリーン: ドラッグ中の外部変更で再読込しても履歴は空のまま",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.DragBegin(0, 250, 150),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle().Which.Should().Be("External");
                        o.DragActive.Should().BeFalse("再読込の入口でドラッグを打ち切る");
                        o.CanUndo.Should().BeFalse("幽霊 MoveEntityCommand を積まない");
                        o.IsDirty.Should().BeFalse("再読込直後はクリーンのまま");
                    })
            ),
            Row(
                "drag-dirty-continue-restores-start",
                "ダーティ: ドラッグ中の外部変更で続行してもドラッグは開始位置へ戻る",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add().Expect(o => o.IsDirty.Should().BeTrue()),
                Doc.DragBegin(0, 320, 240),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .No()
                    .Expect(o =>
                    {
                        var (dragged, startX, startY) = o.Harness.DragStart!.Value;
                        o.WarningConfirms.Should().ContainSingle("ダーティなので確認する");
                        dragged.X.Should().Be(startX, "ドラッグ分は開始位置へ戻す");
                        dragged.Y.Should().Be(startY);
                        o.DragActive.Should().BeFalse();
                        o.Vm.UndoRedo.ChangeGeneration.Should()
                            .Be(
                                o.Harness.StartChangeGeneration,
                                "打ち切りは履歴・変更世代へ触れない"
                            );
                        o.IsDirty.Should().BeTrue("元からあった未保存変更はそのまま残る");
                    })
            ),
            Row(
                "drag-already-reflected-not-cancelled",
                "反映済みの内容の変更検知ではドラッグを打ち切らない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.DragBegin(0, 0, 0),
                Doc.Modified()
                    .Expect(o => o.DragActive.Should().BeTrue("ディスクは読み込んだ内容のまま"))
            ),
        ];

    /// <summary>名前付きクエリの往復（開く → 保存文書）と新規作成での消去</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> QueriesRows() =>
        [
            Row(
                "queries-open-to-document-round-trip",
                "Open → ToDocument で名前付きクエリが往復する",
                Doc.WriteDocument(A, "order-with-query", OrderDocumentWithQuery),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Vm.Queries.Should().ContainSingle();
                        o.Vm.Queries[0].EntityId.Should().Be(OrderEntityId);
                        o.Vm.Queries[0].Condition.Should().Be("CustomerId = @customerId");
                        var document = o.Vm.ToDocument();
                        document.Schema.Queries.Should().ContainSingle();
                        document.Schema.Queries[0].Name.Should().Be("GetByCustomer");
                    })
            ),
            Row(
                "queries-new-diagram-clears",
                "新規作成（ファイル由来でない置換）でクエリが消去される",
                Doc.WriteDocument(A, "order-with-query", OrderDocumentWithQuery),
                Doc.Open(A).Expect(o => o.Queries.Should().Be(1)),
                Doc.New().Expect(o => o.Queries.Should().Be(0))
            ),
        ];

    /// <summary>名前付きクエリの往復のシナリオの表の Id（元のテストは実行ごとの新しい Guid）</summary>
    private static Guid OrderEntityId => new("9f6d2c1a-4b7e-4e0a-8a55-2c3d4e5f6a70");

    /// <summary>Order 表と、その表の名前付きクエリ 1 件の図（元のテストの SaveDocumentWithQuery と同じ形）</summary>
    private static DiagramDocument OrderDocumentWithQuery()
    {
        var entity = new Entity { Id = OrderEntityId, TableName = "Order" };
        entity.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Amount", DataType = "decimal(12,2)" });

        return new DiagramDocument
        {
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { entity },
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = entity.Id,
                        Name = "GetByCustomer",
                        Condition = "CustomerId = @customerId",
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int32" },
                        },
                    },
                },
            },
        };
    }
}
