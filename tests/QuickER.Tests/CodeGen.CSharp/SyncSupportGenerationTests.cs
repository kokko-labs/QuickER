using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSyncFixture;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 双方向同期支援（<see cref="CodeGenerationOptions.GenerateSyncSupport"/>）の生成挙動を、再生成に依存しない
/// 生成テキストと診断で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// ドリフト検知は「意図しない変化に気づく」ための仕掛けであって「変化が正しい」ことは保証しない
/// （<c>QUICKER_REGEN_FIXTURES=1</c> の 1 コマンドで誤った変更も緑になる）ため、オプションのゲート・診断・
/// 方言別 SQL のクォート・FK 順という「壊れても型検査に出ない」性質はここで名指しして表明する。
/// </para>
/// </remarks>
public class SyncSupportGenerationTests
{
    /// <summary>オプション OFF で生成した結果と ON で生成した結果を取り出すヘルパの共有図</summary>
    private static ErDiagram Diagram() => SyncFixtureDefinition.Build();

    /// <summary>同期支援 ON の基準オプション（フィクスチャと同一構成）</summary>
    private static CodeGenerationOptions SyncOptions() => SyncFixtureDefinition.Options;

    /// <summary>指定オプションで生成し、ファイル名 → 内容の辞書と診断を返す</summary>
    private static (
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyList<GenerationDiagnostic> Diagnostics
    ) Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var (primary, byDialect) = SyncFixtureDefinition.ResolveColumnTypes(diagram);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        return (
            result.Files.ToDictionary(file => file.FileName, file => file.Content),
            result.Diagnostics
        );
    }

    /// <summary>
    /// 同期支援 OFF の生成物には同期の型が 1 つも現れない（新オプションは純増で、既存構成の出力を変えない）。
    /// </summary>
    [Fact(DisplayName = "同期支援 OFF の生成物には同期の型が一切出ない（純増であることの表明）")]
    public void SyncSupportOff_EmitsNoSyncTypes()
    {
        var (files, diagnostics) = Generate(
            Diagram(),
            SyncOptions() with
            {
                GenerateSyncSupport = false,
            }
        );

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);
        content.Should().NotContain("SyncEngine");
        content.Should().NotContain("SyncJournal");
        content.Should().NotContain("AddGeneratedSyncSupport");
        content.Should().NotContain("Journaling");

        // リモートサービスは有効なまま＝サーバーファイルは出るが、そこに同期専用エンドポイントは無い
        files.Keys.Should().Contain(SyncFixtureDefinition.RemoteServerOutputFileName);
        content.Should().NotContain("MapSyncEndpoints");
        content.Should().NotContain("RemoteSyncOperations");
        content.Should().NotContain("HttpSyncServerSource");
    }

    /// <summary>
    /// 同期支援 ON × リモートサービス OFF の生成物には、HTTP 経路の型が 1 つも現れない。
    /// </summary>
    /// <remarks>
    /// HTTP 対応は同期支援の追加機能で、リモートサービスを生成しない構成（＝直結だけのハイブリッド）では
    /// 転送用のエンベロープも HTTP 差分ソースも DI 拡張も出てはならない。両アームを名指しで表明することで、
    /// 「ゲートを外しても生成は通る」たぐいの退行がドリフト再生成で承認されるのを防ぐ。
    /// </remarks>
    [Fact(DisplayName = "同期支援 ON × リモートサービス OFF では HTTP 経路の型が一切出ない")]
    public void SyncSupportWithoutRemoteServices_EmitsNoHttpTransport()
    {
        var (files, diagnostics) = Generate(
            Diagram(),
            SyncOptions() with
            {
                GenerateRemoteServices = false,
            }
        );

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        // サーバーファイルそのものが出ない（リモートサービス生成の産物）
        files
            .Keys.Should()
            .ContainSingle()
            .Which.Should()
            .Be(SyncFixtureDefinition.OutputFileName);

        var content = string.Concat(files.Values);

        // 同期エンジンと直結経路は従来どおり出る
        content.Should().Contain("public sealed class SyncEngine");
        content.Should().Contain("public sealed class SyncOrderDirectSyncSource");
        content.Should().Contain("AddGeneratedDirectSyncSources");

        // HTTP 経路（転送エンベロープ・固定基底・per-entity クライアント・DI）は 1 つも出ない
        content.Should().NotContain("RemoteSyncOperations");
        content.Should().NotContain("RemoteSyncChangesRequest");
        content.Should().NotContain("HttpSyncServerSource");
        content.Should().NotContain("HttpSyncOrderSyncSource");
        content.Should().NotContain("AddGeneratedHttpSyncSources");
        content.Should().NotContain("MapSyncEndpoints");
    }

    /// <summary>
    /// 同期支援 ON × リモートサービス ON では、同期専用エンドポイントと HTTP 差分ソースが両側に出る。
    /// </summary>
    /// <remarks>
    /// エンドポイントは同期対象テーブル<b>ごと</b>に張られなければならない（1 つでも漏れるとそのテーブルだけ
    /// 同期できず、しかもコンパイルは通る）。ルート名は既存のリモート面と同じ規則で、クライアントとサーバーが
    /// 同じ定数（<c>RemoteSyncOperations</c>）から組み立てることで食い違いようがない形にしている。
    /// </remarks>
    [Fact(
        DisplayName = "同期支援 × リモートサービスで同期専用エンドポイントと HTTP 差分ソースが出る"
    )]
    public void SyncSupportWithRemoteServices_EmitsEndpointsAndHttpSources()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var server = files[SyncFixtureDefinition.RemoteServerOutputFileName];

        // 同期対象テーブルごとに 1 行ずつ（漏れたテーブルは黙って同期不能になる）
        server.Should().Contain("MapSyncEndpoints<SyncOrderEntity, int>(group, \"SyncOrder\");");
        server
            .Should()
            .Contain("MapSyncEndpoints<SyncOrderLineEntity, int>(group, \"SyncOrderLine\");");

        // 経路はクライアントと共有の定数から組み立てる（片側だけ変えても食い違わない）
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Ceiling}\"");
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Changes}\"");
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Keys}\"");

        // エンドポイントは差分ソースの薄い remoting（実装の二重化をしない）
        server
            .Should()
            .Contain(
                "context.RequestServices.GetRequiredService<ISyncServerSource<TEntity, TKey>>()"
            );

        var main = files[SyncFixtureDefinition.OutputFileName];
        main.Should().Contain("public abstract class HttpSyncServerSource<TEntity, TKey>");
        main.Should().Contain("public sealed class HttpSyncOrderSyncSource(HttpClient httpClient)");
        main.Should()
            .Contain("public sealed class HttpSyncOrderLineSyncSource(HttpClient httpClient)");
        main.Should().Contain("public static IServiceCollection AddGeneratedHttpSyncSources(");

        // 転送経路の選択は DI 1 行の差でしかない（直結の登録も併存する）
        main.Should().Contain("public static IServiceCollection AddGeneratedDirectSyncSources(");
        main.Should().Contain("public static IServiceCollection AddGeneratedSyncEngine(");
    }

    /// <summary>
    /// 同期支援 ON では固定エンジン・記述子・直結差分ソース・デコレータ・DI 登録がすべて出力される。
    /// </summary>
    [Fact(DisplayName = "同期支援 ON で固定エンジン・記述子・差分ソース・デコレータ・DI が出る")]
    public void SyncSupportOn_EmitsEngineAndPerEntityCode()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);
        content.Should().Contain("public sealed class SyncEngine");
        content.Should().Contain("public sealed class SyncJournal");
        content.Should().Contain("public abstract class SyncTable<TEntity, TKey>");
        content.Should().Contain("public sealed class SyncOrderDirectSyncSource");
        content.Should().Contain("public sealed class SyncOrderSyncTable");
        content.Should().Contain("public sealed class JournalingSyncOrderRepository");
        content.Should().Contain("public sealed class SyncOrderLineDirectSyncSource");
        content.Should().Contain("public sealed class SyncOrderLineSyncTable");
        content.Should().Contain("public sealed class JournalingSyncOrderLineRepository");
        content.Should().Contain("AddGeneratedSyncSupport");
    }

    /// <summary>
    /// 記述子の SQL がサーバー＝SQL Server クォート・ローカル＝SQLite クォートで別々に組み立てられる。
    /// </summary>
    /// <remarks>
    /// 同期は 1 スコープで 2 方言を扱う唯一の生成物で、テンプレートの方言変数（<c>quote_open</c>）は片方しか運べない。
    /// 片方のクォートで両方を書いてしまっても C# のコンパイルは通り、実行時に初めて落ちるため名指しで固定する。
    /// </remarks>
    [Fact(DisplayName = "差分 SQL はサーバー側 [ ]・ローカル側 \" \" で別々にクォートされる")]
    public void SyncSql_UsesPerDialectQuoting()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        // サーバー側（SQL Server）: 昇順・上限つき・NULL アンカー/上限を「制約なし」として外す形。
        // 除外列（attachment）を持つ sync_orders は明示列挙になり、その列だけが落ちる
        // ＝「除外列は行の転送に載らない」という意味論はこの列挙で成立している。
        content
            .Should()
            .Contain(
                "\"SELECT TOP (@batchSize) [order_id], [customer_name], [row_ver] FROM [sync_orders] "
                    + "WHERE (@anchor IS NULL OR [row_ver] > CAST(@anchor AS binary(8))) "
                    + "AND (@ceiling IS NULL OR [row_ver] < CAST(@ceiling AS binary(8))) "
                    + "ORDER BY [row_ver]\""
            );

        // 除外列を持たないテーブルは従来どおり "*"（列挙は除外があるときだけの切り替え）
        content.Should().Contain("\"SELECT TOP (@batchSize) * FROM [sync_order_lines] ");
        content.Should().Contain("\"SELECT [order_id] FROM [sync_orders]\"");
        content.Should().Contain("\"SELECT MIN_ACTIVE_ROWVERSION()\"");

        // ローカル側（SQLite）: ミラー列の MAX からアンカーを導出する
        content.Should().Contain("\"SELECT MAX(\\\"row_ver\\\") FROM \\\"sync_orders\\\"\"");
        content.Should().Contain("\"SELECT \\\"order_id\\\" FROM \\\"sync_orders\\\"\"");
        content
            .Should()
            .Contain(
                "\"SELECT \\\"order_id\\\" FROM \\\"sync_orders\\\" WHERE \\\"order_id\\\" IN (@keys)\""
            );
    }

    /// <summary>
    /// DI 登録は FK トポロジカル順（親→子）でテーブルを並べる（エンジンはこの順で適用・逆順で削除する）。
    /// </summary>
    [Fact(
        DisplayName = "DI 登録の ISyncTable は FK 順（親 sync_orders → 子 sync_order_lines）で並ぶ"
    )]
    public void SyncTables_AreRegisteredInForeignKeyOrder()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        var parentIndex = content.IndexOf("new SyncOrderSyncTable(", StringComparison.Ordinal);
        var childIndex = content.IndexOf("new SyncOrderLineSyncTable(", StringComparison.Ordinal);

        parentIndex.Should().BeGreaterThan(-1);
        childIndex.Should().BeGreaterThan(-1);
        parentIndex
            .Should()
            .BeLessThan(
                childIndex,
                "ダウンロードの適用は親→子でなければ子行が存在しない親を参照して FK 制約に触れる"
            );
    }

    /// <summary>
    /// ジャーナル記録デコレータが全書き込み入口（Insert / Update / Delete / BulkInsert / Save×2）を覆う。
    /// </summary>
    /// <remarks>
    /// 保存フック（<c>ISaveHook</c>）はグラフ保存でしか発火せず直接 CRUD を素通しするため、
    /// デコレータ方式を採った理由そのものがこの網羅性にある。1 つでも抜けるとオフライン編集が黙って失われる。
    /// </remarks>
    [Fact(DisplayName = "デコレータは全書き込み入口でジャーナルへ記録する（対の経路監査）")]
    public void JournalingDecorator_CoversEveryWriteEntryPoint()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var decorator = ExtractClass(
            string.Concat(files.Values),
            "public sealed class JournalingSyncOrderRepository"
        );

        // 直接 CRUD と一括追加は「アップサート」として記録する
        decorator.Should().Contain("public async Task InsertAsync(");
        decorator.Should().Contain("public async Task<bool> UpdateAsync(");
        decorator.Should().Contain("public async Task<int> BulkInsertAsync(");

        // 削除は「行が消える前に読む」ことでミラー版を捕まえ、版ガード付きで再生できるようにする
        decorator.Should().Contain("public async Task<bool> DeleteAsync(");
        decorator
            .Should()
            .Contain("var existing = await inner.GetByIdAsync(id, cancellationToken)");
        decorator.Should().Contain("SyncJournalOperation.Delete");

        // グラフ保存（単一・複数）は RowState で記録内容を切り替える
        decorator.Should().Contain("await RecordSaveAsync(entity, cancellationToken)");
        decorator.Should().Contain("if (entity.RowState == RowState.Removed)");

        // 読み取り経路は素通し（記録しない）
        decorator.Should().Contain("public SqlQuery<SyncOrderEntity> Query() => inner.Query();");
        decorator
            .Should()
            .Contain("public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(");
    }

    /// <summary>ループ防止の抑制フラグを、エンジンの書き込み経路とジャーナル記録の双方が参照する。</summary>
    [Fact(DisplayName = "同期エンジンの書き込みは SyncSession でジャーナル記録を抑制する")]
    public void SyncEngine_SuppressesJournalingForItsOwnWrites()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        content.Should().Contain("public static class SyncSession");
        content.Should().Contain("if (SyncSession.IsSuppressed)");
        content.Should().Contain("using (SyncSession.Suppress())");
    }

    /// <summary>同期対象テーブルの一覧が Info 診断で FK 順のまま通知される。</summary>
    [Fact(DisplayName = "同期対象テーブルの一覧が Info 診断で通知される")]
    public void SyncSupport_ReportsTargetTablesAsInfo()
    {
        var (_, diagnostics) = Generate(Diagram(), SyncOptions());

        // 文言は UI 言語に追従するため、内容で特定する。両テーブルを列名なしで名指しする Info はこれだけ
        // （行バージョン統一は row_ver を、除外列の 2 つは attachment / Attachment を伴う）。
        var info = diagnostics
            .Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("sync_order_lines")
                && !d.Message.Contains("row_ver")
            )
            .Subject;

        info.Message.Should().Contain("sync_orders");
        info.Message.IndexOf("sync_orders", StringComparison.Ordinal)
            .Should()
            .BeLessThan(info.Message.IndexOf("sync_order_lines", StringComparison.Ordinal));
    }

    /// <summary>方言構成がサーバー＋ローカルの 2 方言でないときは生成時エラーになる。</summary>
    [Theory(DisplayName = "同期支援は sqlserver + sqlite の 2 方言でなければ生成時エラー")]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public void SyncSupport_RequiresBothDialects(string dialect)
    {
        var (_, diagnostics) = Generate(
            Diagram(),
            SyncOptions() with
            {
                RepositoryDialects = [dialect],
            }
        );

        diagnostics
            .Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains("sqlserver")
                && d.Message.Contains("sqlite")
            );
    }

    /// <summary>QuickER 版 Repository の実装を生成しない構成では生成時エラーになる。</summary>
    [Fact(
        DisplayName = "同期支援は QuickER 版 Repository の実装生成を前提とする（未生成はエラー）"
    )]
    public void SyncSupport_RequiresRepositoryImplementations()
    {
        var (_, diagnostics) = Generate(
            Diagram(),
            SyncOptions() with
            {
                GenerateRepositories = false,
                GenerateInMemoryRepositories = true,
            }
        );

        diagnostics
            .Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains("GenerateRepositories")
            );
    }

    /// <summary>行バージョン列を持つテーブルが 1 つも無い図では生成時エラーになる。</summary>
    [Fact(DisplayName = "行バージョン列を持つテーブルが無ければ同期支援は生成時エラー")]
    public void SyncSupport_RequiresAtLeastOneRowVersionTable()
    {
        var diagram = Diagram();

        foreach (var entity in diagram.Entities)
        {
            entity.Columns.RemoveAll(column => column.DataType == "rowversion");
        }

        var (_, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics
            .Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error && d.Message.Contains("rowversion")
            );
    }

    /// <summary>
    /// 無制限バイナリ列の除外との併用は<b>許可</b>され、運ばれない列は Info 診断で名指しされる。
    /// </summary>
    /// <remarks>
    /// かつては生成時エラーで拒否していた組合せ（「ストリーミングアクセサはデコレータから見えない」）を、
    /// デコレータが Write アクセサも包むようにして解消した。エラーが復活していないことと、代わりの通知が
    /// 出ていることの両方を名指しする（片方だけでは「黙って通る」への退行に気づけない）。
    /// </remarks>
    [Fact(
        DisplayName = "同期支援と無制限バイナリ列の除外は併用でき、運ばれない列が Info 診断に出る"
    )]
    public void SyncSupport_WithUnboundedBinaryExclusion_ReportsInfoInsteadOfError()
    {
        var (_, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        diagnostics.Should().NotContain(d => d.Message.Contains("ExcludeUnboundedBinaryColumns"));

        var info = diagnostics
            .Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("IncludeUnboundedBinary")
            )
            .Subject;

        info.Message.Should().Contain("sync_orders");
        info.Message.Should().Contain("Attachment");
        info.Message.Should().NotContain("sync_order_lines", "除外列を持たないテーブルは挙げない");
    }

    /// <summary>
    /// 同期対象テーブルに除外列が 1 つも無ければ、除外オプションが ON でも告知は出ない。
    /// </summary>
    /// <remarks>
    /// 「知らせるべき乖離が実在するときだけ知らせる」＝除外列を持つのが同期対象外のテーブルだけ、という
    /// 構成でも黙っていること（さもないと同期に無関係な列で通知が形骸化する）。
    /// </remarks>
    [Fact(DisplayName = "同期対象テーブルに除外列が無ければ同期の除外列 Info は出ない")]
    public void SyncSupport_WithoutExcludedColumnsInSyncTables_EmitsNoBinaryInfo()
    {
        var diagram = Diagram();
        diagram
            .Entities.Single(entity => entity.TableName == "sync_orders")
            .Columns.RemoveAll(column => column.Name == "attachment");

        var (_, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        diagnostics.Should().NotContain(d => d.Message.Contains("IncludeUnboundedBinary"));
    }

    /// <summary>
    /// 除外列があるとき、blob の単独編集（Write アクセサ）もジャーナルへ記録される。
    /// </summary>
    /// <remarks>
    /// blob だけを差し替える編集は Insert / Update / Save / Delete のいずれも通らないため、デコレータが
    /// Write アクセサを包まなければオフライン編集として永久に検出されない。読み取り（Read アクセサ）は
    /// 素通しであることも同時に固定する（読みを記録すると、取得しただけの行が送り返される）。
    /// </remarks>
    [Fact(DisplayName = "ジャーナル記録デコレータは除外列の Write アクセサも journal-first で包む")]
    public void SyncDecorator_JournalsUnboundedBinaryWrites()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var decorator = ExtractClass(
            string.Concat(files.Values),
            "public sealed class JournalingSyncOrderRepository("
        );

        // 読みは素通し
        decorator
            .Should()
            .Contain("=> inner.ReadAttachmentAsync(id, destination, cancellationToken);");

        // 書きは journal-first（記録 → 実書き込み）
        decorator.Should().Contain("public async Task<bool> WriteAttachmentAsync(");
        var record = decorator.IndexOf("SyncJournalOperation.Upsert", StringComparison.Ordinal);
        var write = decorator.IndexOf(
            "await inner.WriteAttachmentAsync(",
            StringComparison.Ordinal
        );
        record.Should().BeLessThan(write, "意図を先に記録してから業務書き込みを行う");
    }

    /// <summary>
    /// 除外列のコピー面（<c>ISyncBinaryColumns</c>）が、ローカル・直結サーバー・HTTP サーバーの 3 箇所へ出る。
    /// </summary>
    /// <remarks>
    /// エンジンはこの面しか見ないため、3 実装のどれか 1 つが欠けると「その転送経路でだけ blob が運ばれない」
    /// という、型検査にもドリフトにも出ない非対称になる。
    /// </remarks>
    [Fact(DisplayName = "除外列のコピー面はローカル・直結・HTTP の 3 実装として出力される")]
    public void SyncBinaryColumns_AreImplementedOnAllThreeSurfaces()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        // ローカル（記述子）: 基底の LocalBinaryColumns を差し替え、列名は ISyncTable 側と同じプロパティ
        content
            .Should()
            .Contain("protected override ISyncBinaryColumns<int>? LocalBinaryColumns => this;");
        content
            .Should()
            .Contain("public override IReadOnlyList<string> UnboundedBinaryColumnNames =>");
        content
            .Should()
            .Contain("localRepository.ReadAttachmentAsync(id, destination, cancellationToken)");

        // 直結サーバー: サーバー側リポジトリのアクセサ
        content.Should().Contain("public ISyncBinaryColumns<int>? BinaryColumns => this;");
        content
            .Should()
            .Contain(
                "serverRepository.WriteAttachmentAsync(id, source, length, cancellationToken)"
            );

        // HTTP サーバー: 既存のバイナリエンドポイント（基底のヘルパー）へ委譲
        content
            .Should()
            .Contain("public override ISyncBinaryColumns<int>? BinaryColumns => this;");
        content
            .Should()
            .Contain("DownloadUnboundedBinaryColumnAsync(\"Attachment\", id, destination,");
    }

    /// <summary>
    /// 除外列を持たない図では、コピー面も洗い替えの損失ガードの材料も 1 つも出ない。
    /// </summary>
    /// <remarks>両アームの名指し（片方だけ見ると「常に出る」への退行が承認される）。</remarks>
    [Fact(DisplayName = "除外列が無ければ ISyncBinaryColumns の実装は 1 つも出ない")]
    public void SyncBinaryColumns_AreAbsentWithoutExcludedColumns()
    {
        var (files, _) = Generate(
            Diagram(),
            SyncOptions() with
            {
                ExcludeUnboundedBinaryColumns = false,
            }
        );
        var content = string.Concat(files.Values);

        // 固定エンジン側の宣言（インターフェイス・基底の受け口）は常にある
        content.Should().Contain("public interface ISyncBinaryColumns<TKey>");

        // per-entity の実装は 1 つも無い
        content.Should().NotContain("LocalBinaryColumns => this");
        content.Should().NotContain("BinaryColumns => this");
        content.Should().NotContain("ReadUnboundedBinaryAsync(\n");
        content.Should().NotContain("WriteAttachmentAsync");
    }

    /// <summary>指定の宣言行から次のトップレベル型宣言までを 1 クラス分のテキストとして切り出す</summary>
    private static string ExtractClass(string content, string declaration)
    {
        var start = content.IndexOf(declaration, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{declaration}' が生成物に含まれていること");

        var next = content.IndexOf(
            "\r\n/// <summary>",
            start + declaration.Length,
            StringComparison.Ordinal
        );

        return next < 0 ? content[start..] : content[start..next];
    }
}
