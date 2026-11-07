using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncOp;
using static QuickER.Tests.Db.UI.ImportSync.ImportSyncState;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI.ImportSync;

internal static partial class ImportSyncRows
{
    /// <summary>主キー列を 1 つ持つエンティティ 1 個の図（構造の比較用）</summary>
    private static ErDiagram DiagramWith(string tableName) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>取込の結果として返すエンティティ</summary>
    private static IReadOnlyList<Entity> ImportedEntities(string tableName) =>
        DiagramWith(tableName).Entities;

    /// <summary>接続ダイアログが確定する答え（既定の接続設定・取込のプロバイダ）</summary>
    private static DbConnectionDialogResult Picked(
        ImportSyncProvider provider,
        DbConnectionSettings? settings = null
    ) => new(settings ?? new DbConnectionSettings(), provider);

    /// <summary>DB 取込のコマンドサービス</summary>
    private static IEnumerable<ScenarioRow<ImportSyncCase>> ImportRows() =>
        [
            Row(
                "import-picker-cancelled-does-nothing",
                "接続ダイアログを取り消すと、図を置き換えずダイアログも出さない",
                [(ImportClean, Pick)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"));
                    var presenter = ImportSyncHarness.Presenter(host);

                    await h.RunImportAsync(host, presenter);

                    host.Replaced.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                }
            ),
            Row(
                "import-passes-command-timeout-from-settings",
                "接続設定のコマンドタイムアウトが取込まで届く",
                [(ImportClean, Pick)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"));
                    h.Dialogs.ConfirmResult = false;
                    var provider = ImportSyncHarness.ImportProvider(ImportedEntities("Imported"));
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        provider,
                        new DbConnectionSettings { CommandTimeoutSeconds = 240 }
                    );

                    await h.RunImportAsync(host, presenter);

                    provider.Importer.LastCommandTimeoutSeconds.Should().Be(240);
                }
            ),
            Row(
                "import-opens-connection-dialog-in-import-mode-without-file-creation",
                "取込は接続ダイアログを取込モード・SQLite の新規作成なしで開く",
                [(ImportEmpty, Pick)],
                async h =>
                {
                    var host = ImportSyncHarness.Host();
                    var presenter = ImportSyncHarness.Presenter(host);

                    await h.RunImportAsync(host, presenter);

                    presenter.Calls.Should().ContainSingle();
                    presenter.Calls[0].Mode.Should().Be(DbConnectionDialogMode.Import);
                    presenter.Calls[0].AllowSqliteFileCreation.Should().BeFalse();
                }
            ),
            Row(
                "import-resolves-target-dbms-as-initial-dialect",
                "対象の DBMS をホストで解決できるとき、その方言を接続ダイアログの初期選択に渡す",
                [(ImportClean, Pick)],
                async h =>
                {
                    var dialect = new ImportOnlyDialect();
                    var host = ImportSyncHarness.Host(
                        DiagramWith("Existing"),
                        target: ImportSyncProvider.ImportOnlyName
                    );
                    host.Registry = new DatabaseProviderRegistry([dialect]);
                    var presenter = ImportSyncHarness.Presenter(host);

                    await h.RunImportAsync(host, presenter);

                    presenter.Calls.Single().FixedProvider.Should().BeSameAs(dialect);
                    presenter.Calls.Single().Title.Should().Be(DbStrings.Db_ImportTitle);
                }
            ),
            Row(
                "import-replacement-declined-does-not-replace",
                "構造が違う図の置き換えの確認を断ると、図を置き換えない",
                [(ImportClean, Confirm)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"));
                    h.Dialogs.ConfirmResult = false;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(DbStrings.Db_ImportReplaceConfirm);
                    host.Replaced.Should().BeEmpty();
                }
            ),
            Row(
                "import-diagram-changed-during-confirm-is-abandoned",
                "置き換えの確認の最中に図が変わったら、取り込まずに取りやめをダイアログで知らせる",
                [(ImportClean, Confirm)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"));
                    h.Dialogs.OnConfirm = () => host.Revision++;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().ContainSingle("確認は出た");
                    host.Replaced.Should().BeEmpty("確認の前の図から作ったマージ結果を当てない");
                    h.Dialogs.InformationMessages.Should()
                        .Equal(DbStrings.Db_ImportDiagramChangedDuringConfirm);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "import-dirty-diagram-changed-during-confirm-is-abandoned",
                "未保存の図でも、警告つきの確認の最中に図が変わったら取り込まずに取りやめを知らせる",
                [(ImportDirty, Confirm)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"), dirty: true);
                    h.Dialogs.OnConfirmWarning = () => host.Revision++;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    host.Replaced.Should().BeEmpty();
                    h.Dialogs.InformationMessages.Should()
                        .Equal(DbStrings.Db_ImportDiagramChangedDuringConfirm);
                }
            ),
            Row(
                "import-dirty-host-uses-warning-confirmation",
                "未保存の変更があるときの置き換えの確認は警告つきで、通常の確認は使わない",
                [(ImportDirty, Confirm)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"), dirty: true);
                    h.Dialogs.ConfirmResult = false;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(DbStrings.Db_ImportReplaceConfirm);
                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    host.Replaced.Should().BeEmpty();
                }
            ),
            Row(
                "import-dirty-confirmed-replaces",
                "未保存の図でも、警告つきの確認で続行すれば取込の結果で置き換えて完了を知らせる",
                [(ImportDirty, Apply)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"), dirty: true);
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    host.Replaced.Should()
                        .ContainSingle()
                        .Which.Diagram.Entities.Should()
                        .ContainSingle(entity => entity.TableName == "Imported");
                    h.Dialogs.InformationMessages.Should().Equal(DbStrings.Db_ImportCompleted);
                }
            ),
            Row(
                "import-failure-on-dirty-diagram-shows-error-without-confirm",
                "未保存の図への取込が例外で失敗すると、確認を出さずにエラーで知らせ、図を置き換えない",
                [(ImportDirty, Fail)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Existing"), dirty: true);
                    var provider = ImportSyncHarness.ImportProvider([]);
                    provider.Importer.Fail("boom");
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(provider);

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    host.Replaced.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should()
                        .Equal(string.Format(DbStrings.Db_ImportFailed, "boom"));
                }
            ),
            Row(
                "import-empty-diagram-replaced-with-target-dbms",
                "失うものが無い図は確認なしで、取込先の方言を持つ図へ置き換え、完了を単文のモーダルで知らせる（内訳は出さない）",
                [(ImportEmpty, Apply)],
                async h =>
                {
                    var host = ImportSyncHarness.Host();
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    var replaced = host.Replaced.Should().ContainSingle().Subject.Diagram;
                    replaced.TargetDbms.Should().Be(ImportSyncProvider.ImportOnlyName);
                    replaced
                        .Entities.Should()
                        .ContainSingle()
                        .Which.TableName.Should()
                        .Be("Imported");
                    h.Dialogs.InformationMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(DbStrings.Db_ImportCompleted);
                    h.Dialogs.InformationDetailsMessages.Should().BeEmpty();
                }
            ),
            Row(
                "import-warnings-shown-as-details",
                "取込の警告は完了の文に内訳を添えた ShowInformationDetails で、種別ごとの見出しと 1 行 1 件で出す",
                [(ImportEmpty, Apply)],
                async h =>
                {
                    var host = ImportSyncHarness.Host();
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(
                            ImportedEntities("Imported"),
                            [
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.DomainTypeFlattened,
                                    "person",
                                    "mail",
                                    "email_dom"
                                ),
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.ForeignKeyOutsideScope,
                                    "po",
                                    "FK_po_vendor",
                                    "other_scope.vendor"
                                ),
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.ColumnTypeNotEmittable,
                                    "weird",
                                    "kind",
                                    "\"od;d\""
                                ),
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.PartitionDefinitionLost,
                                    "sales"
                                ),
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.VirtualTableExcluded,
                                    "docs"
                                ),
                                // 式が取れた方言（括弧書きの文面）と取れない方言（式なしの文面）の両方
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.ComputedColumnExpressionLost,
                                    "invoices",
                                    "total",
                                    "([qty]*[price])"
                                ),
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.ComputedColumnExpressionLost,
                                    "ledger",
                                    "amount"
                                ),
                                // 名前に改行が混ざっても内訳の 1 行 1 件が崩れないこと
                                new SchemaImportWarning(
                                    SchemaImportWarningKind.TableColumnsUnavailable,
                                    "line\nbreak"
                                ),
                            ]
                        )
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.InformationMessages.Should()
                        .BeEmpty("内訳があるときは単文の完了通知にしない");
                    var (message, details, title) = h
                        .Dialogs.InformationDetailsMessages.Should()
                        .ContainSingle()
                        .Subject;
                    message.Should().StartWith(DbStrings.Db_ImportCompleted);
                    message.Should().Contain(DbStrings.Db_ImportWarningsHeader);
                    title.Should().Be(DbStrings.Common_Complete);
                    details.Should().Contain("person");
                    details.Should().Contain("email_dom");
                    details.Should().Contain("FK_po_vendor");
                    details.Should().Contain("other_scope.vendor");
                    details.Should().Contain("kind");
                    details.Should().Contain("sales");
                    details.Should().Contain("docs");
                    details
                        .Should()
                        .Contain(
                            SchemaImportWarningText.Heading(
                                SchemaImportWarningKind.VirtualTableExcluded,
                                1
                            )
                        )
                        .And.Contain("  • docs");
                    details.Should().Contain("invoices.total");
                    details.Should().Contain("([qty]*[price])");
                    details.Should().Contain("ledger.amount");
                    details.Should().Contain("line break");
                    details
                        .Should()
                        .NotContain(
                            "line" + Environment.NewLine,
                            "名前の改行が行区切りに化けてはいけない"
                        );
                    var lines = details.Split(
                        Environment.NewLine,
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    lines
                        .Count(line => line.StartsWith("  • ", StringComparison.Ordinal))
                        .Should()
                        .Be(8, "警告 8 件が 1 行 1 件で並ぶこと");
                    lines
                        .Count(line => !line.StartsWith("  • ", StringComparison.Ordinal))
                        .Should()
                        .Be(7, "見出しは種別ごとに 1 行（計算列の 2 件は同じ見出しの下）");
                }
            ),
            Row(
                "import-warning-formatting-failure-is-not-reported-as-import-failure",
                "警告の整形が例外で落ちても、置き換え済みの取込を「取込に失敗しました」と言わずに例外を投げ直す",
                [(ImportEmpty, Apply)],
                async h =>
                {
                    var host = ImportSyncHarness.Host();
                    var presenter = ImportSyncHarness.Presenter(host);
                    // 定義されていない種別＝整形の switch が ArgumentOutOfRangeException を投げる
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(
                            ImportedEntities("Imported"),
                            [new SchemaImportWarning((SchemaImportWarningKind)99, "t")]
                        )
                    );

                    var thrown = await h.RunImportAsync(host, presenter);

                    thrown.Should().BeOfType<ArgumentOutOfRangeException>();
                    host.Replaced.Should().ContainSingle();
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "import-failure-shows-error-without-details",
                "取込の例外は Db_ImportFailed の文言のエラー 1 件で知らせ、図を置き換えず、内訳も出さない",
                [(ImportEmpty, Fail)],
                async h =>
                {
                    var host = ImportSyncHarness.Host();
                    var provider = ImportSyncHarness.ImportProvider([]);
                    provider.Importer.Fail("boom");
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(provider);

                    await h.RunImportAsync(host, presenter);

                    host.Replaced.Should().BeEmpty();
                    h.Dialogs.ErrorMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(DbStrings.Db_ImportFailed, "boom"));
                    h.Dialogs.InformationDetailsMessages.Should().BeEmpty();
                }
            ),
            Row(
                "import-same-structure-no-confirm-keeps-surviving-query",
                "構造が同じ再取込は確認せずに続け、生き残るクエリを置き換えた図へ引き継ぐ",
                [(ImportClean, Apply)],
                async h =>
                {
                    var entityId = Guid.NewGuid();
                    var current = new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                Id = entityId,
                                TableName = "Customer",
                                Columns =
                                {
                                    new Column
                                    {
                                        Name = "Id",
                                        DataType = "int",
                                        IsPrimaryKey = true,
                                        IsNullable = false,
                                    },
                                },
                            },
                        },
                        Queries =
                        {
                            new QueryDefinition { Name = "GetAll", EntityId = entityId },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    var presenter = ImportSyncHarness.Presenter(host);
                    // 取込の結果は同じ構造（同名の表・列・主キーの型）だが Id は新しい
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Customer"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    host.Replaced.Should()
                        .ContainSingle()
                        .Which.Diagram.Queries.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("GetAll");
                }
            ),
            Row(
                "import-same-structure-description-overwrite-confirms-with-count",
                "構造が同じでも説明が取込の値で上書きされるときは確認し、上書きする件数を文に載せる",
                [(ImportClean, Confirm)],
                async h =>
                {
                    var current = new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "Customer",
                                Description = "手書きしたテーブル説明",
                                Columns =
                                {
                                    new Column
                                    {
                                        Name = "Id",
                                        DataType = "int",
                                        IsPrimaryKey = true,
                                        IsNullable = false,
                                        Description = "手書きした列説明",
                                    },
                                },
                            },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    h.Dialogs.ConfirmResult = false;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Customer"))
                    );

                    await h.RunImportAsync(host, presenter);

                    var shown = h.Dialogs.ConfirmMessages.Should().ContainSingle().Which;
                    shown.Should().StartWith(DbStrings.Db_ImportReplaceConfirm);
                    shown
                        .Should()
                        .Contain(string.Format(DbStrings.Db_ImportDescriptionOverwriteWarning, 2));
                    host.Replaced.Should().BeEmpty();
                }
            ),
            Row(
                "import-same-structure-and-descriptions-no-confirm",
                "構造も説明も同じ再取込は確認しない",
                [(ImportClean, Apply)],
                async h =>
                {
                    var current = new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "Customer",
                                Description = "顧客",
                                Columns =
                                {
                                    new Column
                                    {
                                        Name = "Id",
                                        DataType = "int",
                                        IsPrimaryKey = true,
                                        IsNullable = false,
                                        Description = "主キー",
                                    },
                                },
                            },
                        },
                    };
                    var imported = new Entity
                    {
                        TableName = "Customer",
                        Description = "顧客",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                                IsNullable = false,
                                Description = "主キー",
                            },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(ImportSyncHarness.ImportProvider([imported]));

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    host.Replaced.Should().ContainSingle();
                }
            ),
            Row(
                "import-dirty-same-structure-skips-confirmation-and-replaces",
                "未保存の図でも、構造が同じで壊れるクエリも説明の上書きも無ければ、確認（警告つきも）を出さずに置き換えて完了を知らせる"
                    + "（確認を省く条件は失うものの有無と構造の一致のどちらか＋失う中身が無いこと。未保存かどうかは構造の一致の枝を止めない）",
                [(ImportDirty, Apply)],
                async h =>
                {
                    var host = ImportSyncHarness.Host(DiagramWith("Customer"), dirty: true);
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Customer"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().BeEmpty();
                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    host.Replaced.Should()
                        .ContainSingle()
                        .Which.Diagram.Entities.Should()
                        .ContainSingle(entity => entity.TableName == "Customer");
                    h.Dialogs.InformationMessages.Should().Equal(DbStrings.Db_ImportCompleted);
                }
            ),
            Row(
                "import-reimport-confirmed-keeps-surviving-query",
                "列が増えた再取込は確認し、承認すると参照が保たれるクエリを引き継いで現在の図の Id へ寄せる",
                [(ImportClean, Confirm), (ImportClean, Apply)],
                async h =>
                {
                    var entityId = Guid.NewGuid();
                    var columnId = Guid.NewGuid();
                    var current = new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                Id = entityId,
                                TableName = "Customer",
                                Columns =
                                {
                                    new Column
                                    {
                                        Id = columnId,
                                        Name = "Id",
                                        DataType = "int",
                                        IsPrimaryKey = true,
                                        IsNullable = false,
                                    },
                                },
                            },
                        },
                        Queries =
                        {
                            new QueryDefinition
                            {
                                Name = "GetById",
                                EntityId = entityId,
                                Parameters =
                                {
                                    new QueryParameter { Name = "id", SourceColumnId = columnId },
                                },
                            },
                        },
                    };
                    var importedEntity = new Entity
                    {
                        TableName = "Customer",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                            new Column { Name = "Name", DataType = "nvarchar(50)" },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(ImportSyncHarness.ImportProvider([importedEntity]));

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(DbStrings.Db_ImportReplaceConfirm);
                    var replaced = host.Replaced.Should().ContainSingle().Subject.Diagram;
                    replaced.Queries.Should().ContainSingle().Which.Name.Should().Be("GetById");
                    replaced.Entities.Should().Contain(entity => entity.Id == entityId);
                }
            ),
            Row(
                "import-broken-query-listed-and-declined",
                "壊れるクエリは確認の文に名前を並べ、断ると図を置き換えない",
                [(ImportClean, Confirm)],
                async h =>
                {
                    var entityId = Guid.NewGuid();
                    var columnId = Guid.NewGuid();
                    var current = new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                Id = entityId,
                                TableName = "Customer",
                                Columns =
                                {
                                    new Column
                                    {
                                        Id = columnId,
                                        Name = "OldCol",
                                        DataType = "int",
                                    },
                                },
                            },
                        },
                        Queries =
                        {
                            new QueryDefinition
                            {
                                Name = "UsesOldCol",
                                EntityId = entityId,
                                OrderBy = { new QueryOrdering { ColumnId = columnId } },
                            },
                        },
                    };
                    // 取込の結果は同名の表だが参照する列の名前が変わっている＝クエリが壊れる
                    var importedEntity = new Entity
                    {
                        TableName = "Customer",
                        Columns =
                        {
                            new Column { Name = "NewCol", DataType = "int" },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    h.Dialogs.ConfirmResult = false;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(ImportSyncHarness.ImportProvider([importedEntity]));

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain("UsesOldCol");
                    host.Replaced.Should().BeEmpty();
                }
            ),
            Row(
                "import-query-only-diagram-asks-before-replacing",
                "エンティティが無くてもクエリを持つ図は失うものがあるので、置き換えの前に確認する",
                [(ImportClean, Confirm)],
                async h =>
                {
                    var current = new ErDiagram
                    {
                        Queries =
                        {
                            new QueryDefinition { Name = "Orphan", EntityId = Guid.NewGuid() },
                        },
                    };
                    var host = ImportSyncHarness.Host(current);
                    h.Dialogs.ConfirmResult = false;
                    var presenter = ImportSyncHarness.Presenter(host);
                    presenter.Result = Picked(
                        ImportSyncHarness.ImportProvider(ImportedEntities("Imported"))
                    );

                    await h.RunImportAsync(host, presenter);

                    h.Dialogs.ConfirmMessages.Should().ContainSingle();
                    host.Replaced.Should().BeEmpty();
                }
            ),
        ];
}
