using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestDoubles;
using DbStrings = QuickER.Db.UI.Resources.Strings;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// <see cref="DbImportCommandService"/> の取込フロー（キャンセル・置換確認・成功反映・例外提示）を検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>MainViewModel</c> 由来の DB 取込ロジックを、フィーチャーモジュール側サービスへ移植したもの。
/// ホストは <see cref="StubErDiagramHost"/>、接続ダイアログ提示はテスト内フェイクに差し替える。
/// resx 期待値は Db.UI の厳密型アクセサ経由（グローバルカルチャは変更しない）。
/// </remarks>
public class DbImportCommandServiceTests
{
    /// <summary>PK 列を 1 つ持つエンティティ 1 個の図（構造比較用）</summary>
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

    /// <summary>取込結果として返すエンティティ一覧を生成する</summary>
    private static IReadOnlyList<Entity> ImportedEntities(string tableName) =>
        DiagramWith(tableName).Entities;

    /// <summary>キャンセル（presenter が null 返却）のとき、図の差し替えもダイアログ提示も行わない</summary>
    [Fact(DisplayName = "接続ダイアログのキャンセルでは何もしない")]
    public async Task RunAsync_Cancelled_DoesNothing()
    {
        var host = new StubErDiagramHost { DiagramToReturn = DiagramWith("Existing") };
        var dialogs = new StubDialogService();
        var service = new DbImportCommandService(host, dialogs, new FakeConnectionPresenter(null));

        await service.RunAsync();

        host.LastReplacedDiagram.Should().BeNull();
        dialogs.ErrorMessages.Should().BeEmpty();
        dialogs.ConfirmMessages.Should().BeEmpty();
    }

    /// <summary>接続ダイアログで指定したコマンドタイムアウトが、取込のカタログ照会まで届く</summary>
    [Fact(DisplayName = "接続設定のコマンドタイムアウトが取込へ渡る")]
    public async Task RunAsync_PassesCommandTimeoutFromSettings()
    {
        var host = new StubErDiagramHost { DiagramToReturn = DiagramWith("Existing") };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var provider = new FakeImportProvider(ImportedEntities("Imported"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(
                    new DbConnectionSettings { CommandTimeoutSeconds = 240 },
                    provider
                )
            )
        );

        await service.RunAsync();

        ((FakeSchemaImporter)provider.SchemaImporter).LastCommandTimeoutSeconds.Should().Be(240);
    }

    /// <summary>現在図と構造が異なり置換確認を拒否した場合は、図を差し替えない</summary>
    [Fact(DisplayName = "置換確認を拒否すると図を差し替えない")]
    public async Task RunAsync_ReplacementDeclined_DoesNotReplace()
    {
        // 現在図（非空・構造が異なる）とインポート結果（別テーブル）で確認ダイアログを誘発する
        var host = new StubErDiagramHost { DiagramToReturn = DiagramWith("Existing") };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var provider = new FakeImportProvider(ImportedEntities("Imported"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        // 置換確認は表示され、拒否されたため差し替えは起きない
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(DbStrings.Db_ImportReplaceConfirm);
        host.LastReplacedDiagram.Should().BeNull();
    }

    /// <summary>ホストに未保存変更があるときの置換確認は、警告水準（ConfirmWarning）で表示される</summary>
    [Fact(DisplayName = "ダーティ時の置換確認は警告水準（Warning）になる")]
    public async Task RunAsync_DirtyHost_UsesWarningConfirmation()
    {
        // 現在図は非空・構造が異なり、ホストは未保存変更あり（IsDirty=true）
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWith("Existing"),
            IsDirtyToReturn = true,
        };
        var dialogs = new StubDialogService { ConfirmResult = false };
        var provider = new FakeImportProvider(ImportedEntities("Imported"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        // 未保存変更が失われる置換のため、警告水準の確認になる（通常確認は使わない）
        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(DbStrings.Db_ImportReplaceConfirm);
        dialogs.ConfirmMessages.Should().BeEmpty();
        host.LastReplacedDiagram.Should().BeNull();
    }

    /// <summary>成功時は取込先方言込みの図が host.ReplaceDiagram へ渡る（空の現在図は確認なし）</summary>
    [Fact(DisplayName = "成功時は TargetDbms 込みの図が ReplaceDiagram へ渡る")]
    public async Task RunAsync_Success_ReplacesDiagramWithTargetDbms()
    {
        // 現在図は空（Entities.Count == 0）なので置換確認は省略される
        var host = new StubErDiagramHost { DiagramToReturn = new ErDiagram() };
        var dialogs = new StubDialogService();
        var provider = new FakeImportProvider(ImportedEntities("Imported"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        dialogs.ConfirmMessages.Should().BeEmpty();
        host.LastReplacedDiagram.Should().NotBeNull();
        host.LastReplacedDiagram!.TargetDbms.Should().Be(FakeImportProvider.ProviderName);
        host.LastReplacedDiagram.Entities.Should()
            .ContainSingle()
            .Which.TableName.Should()
            .Be("Imported");

        // 外部（DB）からの取込はファイル取込と同水準のため、完了はモーダルで知らせる
        dialogs
            .InformationMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(DbStrings.Db_ImportCompleted);
    }

    /// <summary>取込中の例外は、Db_ImportFailed 文言のエラーダイアログで提示される</summary>
    [Fact(DisplayName = "取込例外は ShowError（Db_ImportFailed 文言）で提示される")]
    public async Task RunAsync_ImportThrows_ShowsError()
    {
        var host = new StubErDiagramHost { DiagramToReturn = new ErDiagram() };
        var dialogs = new StubDialogService();
        var provider = new FakeImportProvider(new InvalidOperationException("boom"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        host.LastReplacedDiagram.Should().BeNull();
        dialogs
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(DbStrings.Db_ImportFailed, "boom"));
    }

    /// <summary>取込では接続ダイアログに新規 SQLite ファイル作成を許可せず（取込では無意味）開くことを検証する</summary>
    [Fact(
        DisplayName = "取込では allowSqliteFileCreation=false・Import モードで接続ダイアログを開く"
    )]
    public async Task RunAsync_PassesAllowSqliteFileCreationFalse()
    {
        var host = new StubErDiagramHost { DiagramToReturn = new ErDiagram() };
        var dialogs = new StubDialogService();
        var presenter = new FakeConnectionPresenter(null);
        var service = new DbImportCommandService(host, dialogs, presenter);

        await service.RunAsync();

        presenter.LastMode.Should().Be(DbConnectionDialogMode.Import);
        presenter.LastAllowSqliteFileCreation.Should().BeFalse();
    }

    /// <summary>構造同一の再取込は無確認で続行し、生存クエリが差替え図へ引き継がれる</summary>
    [Fact(DisplayName = "構造同一の再取込は無確認で、生存クエリが引き継がれる")]
    public async Task RunAsync_SameStructure_NoConfirm_SurvivesQuery()
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
                new QueryDefinition { Name = "GetAll", EntityId = entityId },
            },
        };
        var host = new StubErDiagramHost { DiagramToReturn = current };
        var dialogs = new StubDialogService();
        // 取込結果は同一構造（同名テーブル・同名列・同型 PK）だが Id は新規
        var provider = new FakeImportProvider(ImportedEntities("Customer"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        // マージ後に署名一致するため確認は出ない
        dialogs.ConfirmMessages.Should().BeEmpty();
        host.LastReplacedDiagram.Should().NotBeNull();
        host.LastReplacedDiagram!.Queries.Should().ContainSingle().Which.Name.Should().Be("GetAll");
    }

    /// <summary>構造同一でも説明が取込値で上書きされる場合は確認を出し、件数を文言へ載せる</summary>
    /// <remarks>
    /// 構造署名は説明を含まないため、実差分（<c>DescriptionOverwriteCount</c>）を見ないと
    /// 手書きした説明が無確認で消える。ここではその確認が出ることと件数の表示を固定する。
    /// </remarks>
    [Fact(DisplayName = "構造同一でも説明が上書きされる場合は確認を出し件数を載せる")]
    public async Task RunAsync_SameStructureWithDescriptionOverwrite_Confirms()
    {
        // 現在図は構造こそ取込結果と同一だが、テーブル・列に手書きの説明を持つ
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
        var host = new StubErDiagramHost { DiagramToReturn = current };
        var dialogs = new StubDialogService { ConfirmResult = false };
        // 取込結果は同一構造だが説明を持たない（＝取り込むと現在図の説明が消える）
        var provider = new FakeImportProvider(ImportedEntities("Customer"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        // テーブル 1 件＋列 1 件＝2 件が上書き対象として文言へ載る
        var shown = dialogs.ConfirmMessages.Should().ContainSingle().Which;
        shown.Should().StartWith(DbStrings.Db_ImportReplaceConfirm);
        shown.Should().Contain(string.Format(DbStrings.Db_ImportDescriptionOverwriteWarning, 2));
        host.LastReplacedDiagram.Should().BeNull();
    }

    /// <summary>構造同一かつ説明も一致する再取込は、従来どおり無確認で続行する</summary>
    [Fact(DisplayName = "構造同一かつ説明も一致する再取込は無確認のまま")]
    public async Task RunAsync_SameStructureAndDescriptions_NoConfirm()
    {
        // 現在図・取込結果とも同じ説明を持つ＝上書きで失われるものが無い
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
        var host = new StubErDiagramHost { DiagramToReturn = current };
        var dialogs = new StubDialogService();
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
        var provider = new FakeImportProvider(new[] { imported });
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().BeEmpty();
        host.LastReplacedDiagram.Should().NotBeNull();
    }

    /// <summary>列追加で構造差分がある再取込でも、参照が保たれるクエリは生存する（確認は承認）</summary>
    [Fact(DisplayName = "再取込でクエリが生存する（列追加で構造差分・確認を承認）")]
    public async Task RunAsync_Reimport_QuerySurvives()
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
        var host = new StubErDiagramHost { DiagramToReturn = current };
        var dialogs = new StubDialogService { ConfirmResult = true };
        // 取込結果は Id 列に加え Name 列が追加されている（構造差分）
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
        var provider = new FakeImportProvider(new[] { importedEntity });
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        // 構造差分（列追加）で確認は出るが、壊れクエリはないため文言は基本メッセージのまま・承認済み
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(DbStrings.Db_ImportReplaceConfirm);
        host.LastReplacedDiagram.Should().NotBeNull();
        host.LastReplacedDiagram!.Queries.Should()
            .ContainSingle()
            .Which.Name.Should()
            .Be("GetById");
        host.LastReplacedDiagram.Entities.Should().Contain(entity => entity.Id == entityId);
    }

    /// <summary>壊れクエリがあると確認メッセージに名前が列挙され、キャンセルで図を差し替えない</summary>
    [Fact(DisplayName = "壊れクエリありは確認メッセージに名前を含み、キャンセルで差し替えない")]
    public async Task RunAsync_BrokenQuery_ListsNameAndCancels()
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
        var host = new StubErDiagramHost { DiagramToReturn = current };
        var dialogs = new StubDialogService { ConfirmResult = false };
        // 取込結果は同名テーブルだが参照列がリネームされている → クエリが壊れる
        var importedEntity = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                new Column { Name = "NewCol", DataType = "int" },
            },
        };
        var provider = new FakeImportProvider(new[] { importedEntity });
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        dialogs.ConfirmMessages.Should().ContainSingle().Which.Should().Contain("UsesOldCol");
        host.LastReplacedDiagram.Should().BeNull();
    }

    /// <summary>取込は案内水準の詳細ダイアログを出さない（複合外部キーもそのまま取り込むため）</summary>
    [Fact(DisplayName = "取込では詳細ダイアログを出さない")]
    public async Task RunAsync_NoWarnings_ShowsNoDetails()
    {
        var host = new StubErDiagramHost { DiagramToReturn = new ErDiagram() };
        var dialogs = new StubDialogService();
        var provider = new FakeImportProvider(ImportedEntities("Imported"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        host.LastReplacedDiagram.Should().NotBeNull();
        dialogs.InformationDetailsMessages.Should().BeEmpty();
    }

    /// <summary>取込が例外で失敗した場合は、エラー報告のみで詳細ダイアログを出さない</summary>
    [Fact(DisplayName = "取込失敗時は詳細ダイアログを出さない")]
    public async Task RunAsync_ImportThrows_ShowsNoWarningDetails()
    {
        var host = new StubErDiagramHost { DiagramToReturn = new ErDiagram() };
        var dialogs = new StubDialogService();
        var provider = new FakeImportProvider(new InvalidOperationException("boom"));
        var service = new DbImportCommandService(
            host,
            dialogs,
            new FakeConnectionPresenter(
                new DbConnectionDialogResult(new DbConnectionSettings(), provider)
            )
        );

        await service.RunAsync();

        dialogs.InformationDetailsMessages.Should().BeEmpty();
        dialogs.ErrorMessages.Should().ContainSingle();
    }

    // FakeConnectionPresenter は共有版（QuickER.Tests.Db.UI.FakeConnectionPresenter）を使用する

    /// <summary>スキーマ取込のみ実カに近く振る舞う擬似プロバイダ（成功結果または例外を返す）</summary>
    private sealed class FakeImportProvider : IDatabaseProvider
    {
        public const string ProviderName = "fakeimport";

        public FakeImportProvider(IReadOnlyList<Entity> entities) =>
            SchemaImporter = new FakeSchemaImporter(new SchemaImportResult { Entities = entities });

        public FakeImportProvider(Exception toThrow) =>
            SchemaImporter = new FakeSchemaImporter(toThrow);

        public string Name => ProviderName;

        public string DisplayName => "Fake Import";

        public int? DefaultPort => null;

        public ISchemaImporter SchemaImporter { get; }

        public IColumnTypeMapper TypeMapper => null!;

        public ITypeCatalog TypeCatalog => null!;

        public ISyncScriptBuilder SyncScriptBuilder => null!;

        public SyncDialectCapabilities SyncCapabilities => null!;

        public ISchemaSyncExecutor SyncExecutor => null!;

        public IDdlGenerator DdlGenerator => null!;

        public string BuildConnectionString(DbConnectionSettings settings) => "fake";
    }

    /// <summary>プリセットの結果を返すか、指定例外を投げるスキーマインポーターのフェイク</summary>
    private sealed class FakeSchemaImporter : ISchemaImporter
    {
        private readonly SchemaImportResult? _result;
        private readonly Exception? _toThrow;

        public FakeSchemaImporter(SchemaImportResult result) => _result = result;

        public FakeSchemaImporter(Exception toThrow) => _toThrow = toThrow;

        /// <summary>直近の取込で渡されたコマンドタイムアウト（接続設定からの伝搬を検証するために記録する）</summary>
        public int? LastCommandTimeoutSeconds { get; private set; }

        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken = default
        )
        {
            LastCommandTimeoutSeconds = commandTimeoutSeconds;

            if (_toThrow is not null)
            {
                throw _toThrow;
            }

            return Task.FromResult(_result!);
        }
    }
}
