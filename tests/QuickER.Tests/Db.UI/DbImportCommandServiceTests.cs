using FluentAssertions;
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

        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            CancellationToken cancellationToken = default
        )
        {
            if (_toThrow is not null)
            {
                throw _toThrow;
            }

            return Task.FromResult(_result!);
        }
    }
}
