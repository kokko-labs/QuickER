using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
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

        // 差分ソースの登録検査もゲートの内側（同期の無いサーバーには検査する対象が無い）
        content.Should().NotContain("RequireSyncSources");
        content.Should().NotContain("IServiceProviderIsService");

        // HttpClient 共有の注意書きは、同期ソースの登録メソッドが在る構成でしか意味を持たない
        content.Should().NotContain("AddGeneratedHttpSyncSources");
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

        // 同期エンジンと直結経路は出る
        content.Should().Contain("public sealed class SyncEngine");
        content.Should().Contain("public sealed class DirectSyncSource<TEntity, TKey>");
        content.Should().Contain("AddGeneratedDirectSyncSources");

        // HTTP 経路（転送エンベロープ・固定基底・per-entity クライアント・DI）は 1 つも出ない
        content.Should().NotContain("RemoteSyncOperations");
        content.Should().NotContain("RemoteSyncChangesRequest");
        content.Should().NotContain("HttpSyncServerSource");
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
        main.Should().Contain("public sealed class HttpSyncServerSource<TEntity, TKey>");
        main.Should().Contain("AddHttpSource<SyncOrderEntity, int>(");
        main.Should().Contain("AddHttpSource<SyncOrderLineEntity, int>(");
        main.Should().Contain("public static IServiceCollection AddGeneratedHttpSyncSources(");

        // 転送経路の選択は DI 1 行の差でしかない（直結の登録も併存する）
        main.Should().Contain("public static IServiceCollection AddGeneratedDirectSyncSources(");
        main.Should().Contain("public static IServiceCollection AddGeneratedSyncEngine(");
    }

    /// <summary>
    /// 同期エンドポイントは、マップ時に差分ソースの登録を同期対象テーブルごとに検査する。
    /// </summary>
    /// <remarks>
    /// ハンドラはリクエスト時に解決するため、登録漏れは「正常起動・CRUD 全部 200・同期だけ不透明な 500」に
    /// なる。検査はテーブルを 1 つでも取りこぼすとそのテーブルだけ黙って抜けるので、対象ごとに 1 件ずつ
    /// 名指しで表明する（呼び出し側は <c>MapGeneratedRemoteEndpoints</c> の 1 行だけ）。
    /// </remarks>
    [Fact(DisplayName = "同期エンドポイントはマップ時に差分ソースの登録を全テーブル分検査する")]
    public void SyncEndpoints_CheckSourceRegistrationAtMappingTime()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var server = files[SyncFixtureDefinition.RemoteServerOutputFileName];

        // マップ側は 1 行（エンドポイントを張る前に検査する）
        server.Should().Contain("RequireSyncSources(endpoints.ServiceProvider);");

        // 検査は「登録の有無だけを問う面」で行う＝実体を作らずスコープにも触れない
        server
            .Should()
            .Contain("services.GetService<IServiceProviderIsService>() is not { } registrations");

        // 検査そのものは表を 1 周するだけ（per-entity に増えるのは表の行だけ）
        server.Should().Contain("foreach (var (service, name) in SyncSourceServices)");
        server.Should().Contain("if (!registrations.IsService(service))");

        // 同期対象テーブルごとに 1 行（漏れたテーブルは検査から静かに抜ける）。
        // 報告名は型から導かず書き下ろす＝メッセージがコードと同じ綴り（Int32 ではなく int）になる
        server
            .Should()
            .Contain(
                "(typeof(ISyncServerSource<SyncOrderEntity, int>), "
                    + "\"ISyncServerSource<SyncOrderEntity, int>\"),"
            );
        server
            .Should()
            .Contain(
                "(typeof(ISyncServerSource<SyncOrderLineEntity, int>), "
                    + "\"ISyncServerSource<SyncOrderLineEntity, int>\"),"
            );

        // 例外は「何をすべきか」まで書く（クライアント側 fail-fast と同水準）
        server.Should().Contain("Call AddGeneratedDirectSyncSources on the server's service");
    }

    /// <summary>
    /// リモートリポジトリの HttpClient 共有の注意書きが、同期ソース側と対称に両オーバーロードへ出る。
    /// </summary>
    /// <remarks>
    /// ベースアドレス版どうしは同じアドレスでも HttpClient を共有しない（別ホルダ）。この注意は従来
    /// 同期ソース側にしか書かれておらず、リモートリポジトリ側だけを読んだ利用者には共有されているように見えた。
    /// </remarks>
    [Fact(DisplayName = "HttpClient 共有の注意書きがリモートリポジトリ側の両オーバーロードに出る")]
    public void HttpRemoteRepositories_DocumentClientSharingWithSyncSources()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());

        var main = files[SyncFixtureDefinition.OutputFileName];

        // 非 keyed 版と keyed 版の 2 箇所（片側だけ直すと読んだ順で結論が変わる）
        main.Should()
            .Contain("The client is this registration's own, and passing the same base address to");
        main.Should()
            .Contain(
                "The key keeps this client apart from the one <c>AddGeneratedHttpSyncSources</c> builds as well"
            );
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
        content.Should().Contain("public sealed class SyncTable<TEntity, TKey>");
        content.Should().Contain("public sealed class DirectSyncSource<TEntity, TKey>");
        content.Should().Contain("public static class GeneratedSyncTables");
        content
            .Should()
            .Contain(
                "public static SyncTableDescriptor<SyncOrderEntity, int> SyncOrder { get; } ="
            );
        content
            .Should()
            .Contain(
                "public static SyncTableDescriptor<SyncOrderLineEntity, int> SyncOrderLine { get; } ="
            );
        content.Should().Contain("public sealed class JournalingSyncOrderRepository");
        content.Should().Contain("public sealed class JournalingSyncOrderLineRepository");
        content.Should().Contain("AddGeneratedSyncSupport");

        // テーブルごとのクラスはもう出ない（記述子 1 つが同じ役目を果たす）
        content.Should().NotContain("class SyncOrderSyncTable");
        content.Should().NotContain("class SyncOrderDirectSyncSource");
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
        // 計算列（summary）は逆に載る＝行としては転送され、ローカルへ書かれるかどうかは INSERT 側の除外が決める。
        content
            .Should()
            .Contain(
                "\"SELECT TOP (@batchSize) [order_id], [customer_name], [row_ver], [summary] FROM [sync_orders] "
                    + "WHERE (@anchor IS NULL OR [row_ver] > CAST(@anchor AS binary(8))) "
                    + "AND (@ceiling IS NULL OR [row_ver] < CAST(@ceiling AS binary(8))) "
                    + "ORDER BY [row_ver]\""
            );

        // 除外列を持たないテーブルは "*"（列挙は除外があるときだけの切り替え）
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
    /// ドット付きテーブル名の同期 SQL が最初のドットだけで分割クォートされることを検証する
    /// （DDL・生成 CRUD の <c>EntitySaveMetadata.QuoteTableName</c> と同じ規則）。
    /// </summary>
    /// <remarks>
    /// 全ドット分割にすると <c>dbo.sync.orders</c> のような名前で <c>[dbo].[sync].[orders]</c> という
    /// 存在しないオブジェクトを指し、DDL・CRUD が読み書きするテーブルと同期 SQL が食い違う。
    /// </remarks>
    [Fact(DisplayName = "ドット付きテーブル名の同期 SQL は最初のドットだけで分割クォートされる")]
    public void SyncSql_DottedTableName_QuotesAtFirstDotOnly()
    {
        var diagram = Diagram();
        diagram.Entities.First(e => e.TableName == "sync_orders").TableName = "dbo.sync.orders";

        var (files, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);

        content.Should().Contain("FROM [dbo].[sync.orders]");
        content.Should().Contain("FROM \\\"dbo\\\".\\\"sync.orders\\\"");
        content.Should().NotContain("[dbo].[sync].[orders]");
    }

    /// <summary>
    /// decimal 主キーのジャーナルキー文字列化がスケールを正規化（G29＝末尾ゼロなし）することを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server は宣言スケールへ揃えて返し（decimal(10,2) の 1.5 は 1.50）、SQLite は書き込んだ綴りを
    /// 保存する（1.5 のまま＝実測）。素の ToString だと同じ行のキー文字列がサーバーとローカルで食い違い、
    /// 削除伝搬・ack・pending の序数照合がすべて外れる。
    /// </remarks>
    [Fact(DisplayName = "decimal 主キーのキー文字列化はスケールを正規化する（G29）")]
    public void SyncKeyText_DecimalPrimaryKey_NormalizesScale()
    {
        var diagram = Diagram();
        diagram
            .Entities.First(e => e.TableName == "sync_orders")
            .Columns.First(c => c.Name == "order_id")
            .DataType = "decimal(10,2)";

        var (files, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);

        content.Should().Contain(".ToString(\"G29\", CultureInfo.InvariantCulture)");
    }

    /// <summary>
    /// 主キーが <c>TimeSpan</c> / <c>TimeOnly</c> / <c>DateOnly</c> のテーブルでも同期支援がコンパイルでき、生成されたキーの文字列化が
    /// 往復することを、生成→実コンパイル→記述子の <c>FormatKey</c> / <c>ParseKey</c> の実行で検証する
    /// </summary>
    /// <remarks>
    /// <c>TimeSpan</c> は書式を取る <c>ToString(IFormatProvider)</c> を持たず、既定の形ではコンパイルできなかった。<c>TimeOnly</c> の
    /// 既定の形は秒を落として往復しなかった。どちらも不変の往復書式（"c" / "O"）で書く。<c>DateOnly</c> は既定の形
    /// （"MM/dd/yyyy"）で往復するので変えない（変えると、更新前に記録した未送信のジャーナルのキーが読めなくなる）。
    /// 5 方言の型マッパーは <c>TimeOnly</c> / <c>DateOnly</c> を出さないので、列の型はこのテストで直接与える。
    /// </remarks>
    [Theory(
        DisplayName = "TimeSpan・TimeOnly・DateOnly の主キーの同期キーはコンパイルでき、文字列化が往復する"
    )]
    [InlineData("TimeSpan")]
    [InlineData("TimeOnly")]
    [InlineData("DateOnly")]
    public void SyncKeyText_TimeLikePrimaryKeys_RoundTrip(string keyTypeName)
    {
        var diagram = Diagram();
        var keyColumnIds = new[]
        {
            diagram
                .Entities.First(e => e.TableName == "sync_orders")
                .Columns.First(c => c.Name == "order_id")
                .Id,
            diagram
                .Entities.First(e => e.TableName == "sync_order_lines")
                .Columns.First(c => c.Name == "order_id")
                .Id,
        };
        var (primary, byDialect) = SyncFixtureDefinition.ResolveColumnTypes(diagram);

        IReadOnlyDictionary<Guid, CSharpTypeInfo> WithKeyType(
            IReadOnlyDictionary<Guid, CSharpTypeInfo> types
        )
        {
            var replaced = types.ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (var id in keyColumnIds)
            {
                replaced[id] = replaced[id] with
                {
                    TypeName = keyTypeName,
                    IsReferenceType = false,
                };
            }

            return replaced;
        }

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            WithKeyType(primary),
            byDialect.ToDictionary(
                pair => pair.Key,
                pair => WithKeyType(pair.Value),
                StringComparer.OrdinalIgnoreCase
            ),
            SyncOptions() with
            {
                GenerateRemoteServices = false,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.SyncKey.{keyTypeName}.{Guid.NewGuid():N}"
        );
        var descriptor = assembly
            .GetTypes()
            .Single(type => type.Name == "GeneratedSyncTables")
            .GetProperty("SyncOrder")!
            .GetValue(null)!;
        var format = (Delegate)descriptor.GetType().GetProperty("FormatKey")!.GetValue(descriptor)!;
        var parse = (Delegate)descriptor.GetType().GetProperty("ParseKey")!.GetValue(descriptor)!;

        object[] samples = keyTypeName switch
        {
            "TimeSpan" =>
            [
                new TimeSpan(2, 13, 45, 30, 123).Add(TimeSpan.FromTicks(4)),
                TimeSpan.FromHours(-3),
                TimeSpan.Zero,
            ],
            "TimeOnly" =>
            [
                new TimeOnly(13, 45, 30, 123).Add(TimeSpan.FromTicks(4)),
                TimeOnly.MinValue,
            ],
            _ => [new DateOnly(2026, 10, 7), DateOnly.MaxValue],
        };

        foreach (var sample in samples)
        {
            var text = (string)format.DynamicInvoke(sample)!;
            parse.DynamicInvoke(text).Should().Be(sample, $"'{text}' は元の値へ戻るべき");
        }

        if (keyTypeName == "DateOnly")
        {
            ((string)format.DynamicInvoke(new DateOnly(2026, 10, 7))!)
                .Should()
                .Be(
                    "10/07/2026",
                    "DateOnly の文字列化は従来の形のまま（未送信のジャーナルを読めるように）"
                );
        }
    }

    /// <summary>
    /// 循環参照のある図でも、循環の外のテーブルは親の後ろへ並ぶ（行き詰まったら循環に属するテーブルを宣言順で 1 つずつ先に出す）
    /// </summary>
    /// <remarks>
    /// 宣言順 comments, users, teams で users ⇄ teams が循環し、comments が users を参照する図。行き詰まった時点で残りを宣言順に
    /// 全部出していた頃は Comment → User → Team の順になり、子の comments が親の users より前へ出ていた。
    /// </remarks>
    [Fact(DisplayName = "循環参照のある図でも循環の外の子は親の後ろへ並ぶ")]
    public void SyncTables_CycleDoesNotPullOutsideChildBeforeParent()
    {
        static Entity Table(string name, out Column id)
        {
            id = new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
            return new Entity { TableName = name, Columns = [id] };
        }

        static Column AddForeignKey(Entity entity, string name)
        {
            var column = new Column { Name = name, DataType = "int" };
            entity.Columns.Add(column);
            return column;
        }

        var comments = Table("comments", out _);
        var users = Table("users", out var userId);
        var teams = Table("teams", out var teamId);
        var commentUser = AddForeignKey(comments, "user_id");
        var userTeam = AddForeignKey(users, "team_id");
        var teamLead = AddForeignKey(teams, "lead_user_id");

        Relationship Reference(Entity parent, Column parentKey, Entity child, Column childKey) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new RelationshipColumnPair(parentKey.Id, childKey.Id)],
            };

        var diagram = new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = [comments, users, teams],
            Relationships =
            [
                Reference(users, userId, comments, commentUser),
                Reference(teams, teamId, users, userTeam),
                Reference(users, userId, teams, teamLead),
            ],
        };

        var (files, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);
        int IndexOf(string entityName) =>
            content.IndexOf(
                $"AddSyncTable<{entityName}Entity, int, I{entityName}Repository>(",
                StringComparison.Ordinal
            );

        IndexOf("User").Should().BeGreaterThan(-1);
        IndexOf("User").Should().BeLessThan(IndexOf("Comment"), "comments は users の子");
        IndexOf("Comment").Should().BeLessThan(IndexOf("Team"), "循環の残りは宣言順");
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

        var parentIndex = content.IndexOf(
            "AddSyncTable<SyncOrderEntity, int, ISyncOrderRepository>(",
            StringComparison.Ordinal
        );
        var childIndex = content.IndexOf(
            "AddSyncTable<SyncOrderLineEntity, int, ISyncOrderLineRepository>(",
            StringComparison.Ordinal
        );

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
        var content = string.Concat(files.Values);

        // 書き込み入口の網羅は汎用基底（第 10 次 A-2 で集約）が持つ:
        // 直接 CRUD と一括追加は「アップサート」として記録し、削除は記録フック経由・グラフ保存は
        // カスケード閉包の記録（SyncGraphRecorder）を先に呼ぶ。どの入口も記録と業務書き込みを
        // RecordAndWriteAsync（ローカルの書き込みロックの内側で 1 組）へ渡す
        var journalingBase = ExtractClass(
            content,
            "public abstract class JournalingRepositoryBase<TEntity, TKey>"
        );
        journalingBase.Should().Contain("public async Task InsertAsync(");
        journalingBase.Should().Contain("public Task<bool> UpdateAsync(");
        journalingBase.Should().Contain("public Task<int> BulkInsertAsync(");
        journalingBase.Should().Contain("public Task<bool> DeleteAsync(");
        journalingBase.Should().Contain("GraphRecorder.RecordSaveAsync(");
        journalingBase.Should().Contain("GraphRecorder.CollectSave(");
        CountOccurrences(journalingBase, "return RecordAndWriteAsync(")
            .Should()
            .Be(4, "Update・BulkInsert・SaveAsync×2 はロックの内側で記録と書き込みを 1 組にする");
        journalingBase.Should().Contain("await RecordAndWriteAsync(", "Insert も同じ");
        journalingBase.Should().Contain("Journal.Gate.WriteExclusiveAsync(");

        // 読み取り経路は素通し（記録しない）
        journalingBase.Should().Contain("public SqlQuery<TEntity> Query() => Inner.Query();");

        // 削除は「行が消える前に読む」ことでミラー版を捕まえ、版ガード付きで再生できるようにする
        // （版ありサブクラス基底の担当）
        var versionedBase = ExtractClass(
            content,
            "public abstract class JournalingRepository<TEntity, TKey>"
        );
        versionedBase
            .Should()
            .Contain("var existing = await Inner.GetByIdAsync(id, cancellationToken)");
        versionedBase.Should().Contain("SyncJournalOperation.Delete");

        // per-type はテーブルの記述子と共有レコーダを基底へ渡すだけ（契約固有メンバの転送を除く）
        var decorator = ExtractClass(content, "public sealed class JournalingSyncOrderRepository");
        decorator.Should().Contain(": JournalingRepository<SyncOrderEntity, int>");
        decorator.Should().Contain("GeneratedSyncTables.SyncOrder,");
        decorator.Should().Contain("GeneratedSyncTables.GraphRecorder");

        // テーブル名・キー整形・グラフ記録の委譲は汎用コアが記述子から引く（per-type には出ない）
        decorator.Should().NotContain("protected override string TableName =>");
        decorator.Should().NotContain("RecordSaveAsync(");

        // 重複事前チェックはランタイム共通面の member なので、素通しは汎用コア側が 1 回だけ持つ
        decorator
            .Should()
            .NotContain("public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(");
        ExtractClass(content, "public abstract class JournalingRepositoryBase<TEntity, TKey>")
            .Should()
            .Contain("public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(");

        // RowState の分岐と子の走査は SyncGraphRecorder 側にある（保存側の決定手順のミラー）。
        // 走査は保存側と同じ 1 つの列挙（EntityBase.EnumerateCascadeChildren）で、テーブルごとに
        // 生成された再帰メソッドではない＝「同じ走査の 2 実装」がそもそも存在しない
        var recorder = ExtractClass(
            string.Concat(files.Values),
            "public sealed class SyncGraphRecorder"
        );
        recorder.Should().Contain("if (entity.RowState == RowState.Removed)");
        recorder.Should().Contain("if (entity.RowState != RowState.Unchanged");
        recorder.Should().Contain("foreach (var child in entity.EnumerateCascadeChildren())");
        recorder.Should().Contain("SyncJournalOperation.Delete");
        recorder.Should().NotContain("SyncOrderEntity");
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

    /// <summary>
    /// 混在図（版あり 2＋版なし 1）の版なしテーブルは、後勝ち専用の生成形になる。
    /// </summary>
    /// <remarks>
    /// 名指しする形＝①記述子は <c>VersionlessSyncTable</c> 派生（版・アンカー系メンバーなし）②直結ソースは
    /// キー順ページング（<c>GetFirstPageAsync</c>／<c>GetPageAfterAsync</c>）で ceiling は null・change stream は
    /// <c>NotSupportedException</c> ③デコレータの Delete は事前読みなしで null 版を記録 ④サーバーの同期
    /// エンドポイントは版なしテーブルだけ <c>MapVersionlessSyncEndpoints</c>（SyncPage＋SyncKeys）を張る。
    /// </remarks>
    [Fact(
        DisplayName = "混在図の版なしテーブルは後勝ち専用の生成形（記述子・ソース・エンドポイント）になる"
    )]
    public void MixedDiagram_EmitsVersionlessShapes()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        var content = string.Concat(files.Values);

        // ① 記述子が版の有無を宣言し、登録側がその値で固定クラスを選ぶ
        var noteDescriptor = ExtractDescriptor(content, "SyncNoteEntity", "SyncNote");
        noteDescriptor.Should().Contain("IsVersionless = true,");
        var orderDescriptor = ExtractDescriptor(content, "SyncOrderEntity", "SyncOrder");
        orderDescriptor.Should().Contain("IsVersionless = false,");
        content.Should().Contain("new VersionlessSyncTable<TEntity, TKey>(");
        content.Should().Contain("new SyncTable<TEntity, TKey>(local, localSqlExecutor, server");
        content.Should().Contain("new VersionlessDirectSyncSource<TEntity, TKey>(");

        // ② 走査の形（版あり＝版の昇順／版なし＝キー順ページング）は固定側が持ち、
        //    記述子はその方言 SQL と版アクセサの有無だけを運ぶ
        noteDescriptor.Should().Contain("WHERE [note_id] > @afterKey ORDER BY [note_id]");
        noteDescriptor.Should().NotContain("ServerChangesSql");
        noteDescriptor.Should().NotContain("ReadRowVersion");
        orderDescriptor.Should().Contain("ServerChangesSql =");
        orderDescriptor.Should().Contain("ReadRowVersion = entity => entity.RowVer,");
        orderDescriptor.Should().NotContain("ServerPageFirstSql");

        // 基底側の両アーム: 版ありは MIN_ACTIVE_ROWVERSION の ceiling・版なしはキー順ページングと null ceiling
        ExtractClass(content, "public sealed class DirectSyncSource<TEntity, TKey>")
            .Should()
            .Contain("MIN_ACTIVE_ROWVERSION()");
        var versionlessSourceBase = ExtractClass(
            content,
            "public sealed class VersionlessDirectSyncSource<TEntity, TKey>"
        );
        versionlessSourceBase.Should().Contain("GetFirstPageAsync");
        versionlessSourceBase.Should().Contain("Task.FromResult<byte[]?>(null)");

        // ③ 版なしデコレータは版なし基底（Delete は事前読みなし・null 版で記録）を継承し、
        //    版ありデコレータは版あり基底（事前読み＋ミラー版）＋ ReadRowVersion override を持つ
        var noteDecorator = ExtractClass(
            content,
            "public sealed class JournalingSyncNoteRepository"
        );
        noteDecorator.Should().Contain(": VersionlessJournalingRepository<SyncNoteEntity, int>");
        noteDecorator.Should().NotContain("ReadRowVersion");
        var orderDecorator = ExtractClass(
            content,
            "public sealed class JournalingSyncOrderRepository"
        );
        orderDecorator.Should().Contain(": JournalingRepository<SyncOrderEntity, int>");

        // ④ サーバー側エンドポイントの選別（版あり＝3 本・版なし＝SyncPage＋SyncKeys）
        var server = files[SyncFixtureDefinition.RemoteServerOutputFileName];
        server.Should().Contain("MapSyncEndpoints<SyncOrderEntity, int>(group, \"SyncOrder\");");
        server
            .Should()
            .Contain("MapVersionlessSyncEndpoints<SyncNoteEntity, int>(group, \"SyncNote\");");
        server.Should().NotContain("MapVersionlessSyncEndpoints<SyncOrderEntity");

        // Info 診断は版なしテーブルを後勝ち専用として名指しする
        diagnostics
            .Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("sync_notes")
                && (d.Message.Contains("後勝ち") || d.Message.Contains("last-write-wins"))
            );
    }

    /// <summary>
    /// 版なし専用のエンドポイントマップ（<c>MapVersionlessSyncEndpoints</c>）は、呼び出し側になる版なしテーブルが
    /// 1 つでもあるときだけ出力される（版ありだけの図ではメソッドごと消える＝死蔵コードを残さない）。
    /// </summary>
    [Fact(DisplayName = "版なし専用エンドポイントのマップは版なしテーブルが在るときだけ出力される")]
    public void VersionlessEndpointMap_EmittedOnlyWhenAVersionlessTableExists()
    {
        // 版あり＋版なしの混在図: メソッドと呼び出しの両方が出る
        var (mixedFiles, _) = Generate(Diagram(), SyncOptions());
        var mixedServer = mixedFiles[SyncFixtureDefinition.RemoteServerOutputFileName];
        mixedServer
            .Should()
            .Contain("private static void MapVersionlessSyncEndpoints<TEntity, TKey>(");
        mixedServer
            .Should()
            .Contain("MapVersionlessSyncEndpoints<SyncNoteEntity, int>(group, \"SyncNote\");");

        // 版なしテーブルを外した図: 呼び出しが無いのでメソッドも出ない（版ありのマップは従来どおり）
        var versionedOnly = Diagram();
        var removed = versionedOnly.Entities.Where(e => e.TableName == "sync_notes").ToList();
        versionedOnly.Relationships.RemoveAll(r =>
            removed.Any(e => e.Id == r.SourceEntityId || e.Id == r.TargetEntityId)
        );
        versionedOnly.Entities.RemoveAll(e => e.TableName == "sync_notes");

        var (versionedFiles, versionedDiagnostics) = Generate(versionedOnly, SyncOptions());
        versionedDiagnostics
            .Should()
            .NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var versionedServer = versionedFiles[SyncFixtureDefinition.RemoteServerOutputFileName];
        versionedServer.Should().NotContain("MapVersionlessSyncEndpoints");
        versionedServer
            .Should()
            .Contain("MapSyncEndpoints<SyncOrderEntity, int>(group, \"SyncOrder\");");
        versionedServer.Should().Contain("RemoteSyncOperations.Ceiling");
    }

    /// <summary>
    /// rowversion 列を 1 つも持たない図でも生成は成立する（全テーブルが後勝ち専用の版なしテーブルになる）。
    /// </summary>
    /// <remarks>
    /// かつては「rowversion 列を持つテーブルが 1 つも無い＝生成時エラー」だったが、版なしテーブルの後勝ち同期
    /// 対応で列の有無は「対象かどうか」でなく「モード素材」になった。全版なし図は LastWriteWins ラン専用の
    /// 生成物として有効で、記述子は VersionlessSyncTable 派生になる。
    /// </remarks>
    [Fact(DisplayName = "rowversion 列が 1 つも無い図でも同期支援は生成できる（全テーブル後勝ち）")]
    public void SyncSupport_AllowsAllVersionlessDiagram()
    {
        var diagram = Diagram();

        foreach (var entity in diagram.Entities)
        {
            entity.Columns.RemoveAll(column => column.DataType == "rowversion");
        }

        var (files, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        var content = string.Concat(files.Values);
        var descriptor = ExtractDescriptor(content, "SyncOrderEntity", "SyncOrder");
        descriptor.Should().Contain("IsVersionless = true,");
        descriptor.Should().NotContain("ServerChangesSql");
        descriptor.Should().NotContain("LocalAnchorSql");
    }

    /// <summary>
    /// 同期可能なテーブル（Repository 契約が生成される単一主キーのテーブル）が 1 つも無い図では生成時エラーになる。
    /// </summary>
    [Fact(DisplayName = "同期可能なテーブルが 1 つも無ければ同期支援は生成時エラー")]
    public void SyncSupport_RequiresAtLeastOneEligibleTable()
    {
        var (_, diagnostics) = Generate(NoEligibleTableDiagram(), SyncOptions());

        // 「何らかのエラー」ではなく当該ガードの文言そのものを名指しする
        // （別の理由で落ちても緑にならないようにする）
        diagnostics
            .Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message == Strings.CodeGen_Error_SyncSupportRequiresEligibleTables
            );
    }

    /// <summary>
    /// 同期可能なテーブルが 0 件でも、同期支援 OFF なら当該ガードは発動しない（ゲートの成功側）。
    /// </summary>
    /// <remarks>
    /// 「同期可能テーブル 0 件」は同期支援を要求したときだけの問題で、要求していない構成では
    /// 複合主キーだけの図が正当な入力。ガードが <see cref="CodeGenerationOptions.GenerateSyncSupport"/> の
    /// 内側にあることを、発動側テストと対で固定する。
    /// </remarks>
    [Fact(DisplayName = "同期支援 OFF なら同期可能テーブル 0 件でもエラーにならない")]
    public void SyncSupportOff_AllowsDiagramWithoutEligibleTables()
    {
        var (_, diagnostics) = Generate(
            NoEligibleTableDiagram(),
            SyncOptions() with
            {
                GenerateSyncSupport = false,
            }
        );

        diagnostics
            .Should()
            .NotContain(d => d.Message == Strings.CodeGen_Error_SyncSupportRequiresEligibleTables);
    }

    /// <summary>同期可能テーブル（単一主キー）が 1 つも無い図を作る（全テーブルを複合主キー化する）</summary>
    private static ErDiagram NoEligibleTableDiagram()
    {
        var diagram = Diagram();

        // 全テーブルを複合主キー化して Repository 契約の生成対象から外す＝同期可能テーブル 0 件の図を作る
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                column.IsPrimaryKey = column.DataType != "rowversion";
            }
        }

        return diagram;
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
            "public sealed class JournalingSyncOrderRepository"
        );

        // 読みは素通し
        decorator
            .Should()
            .Contain("=> _inner.ReadAttachmentAsync(id, destination, cancellationToken);");

        // 書きは journal-first（記録 → 実書き込み）を基底のヘルパーがロックの内側で行う
        decorator.Should().Contain("public Task<bool> WriteAttachmentAsync(");
        decorator.Should().Contain("RecordUnboundedBinaryWriteAsync(");
        decorator
            .Should()
            .Contain("() => _inner.WriteAttachmentAsync(id, source, length, cancellationToken)");
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

        // 列ごとのアクセサは記述子が 1 度だけ運ぶ（具象契約を知るのはこのラムダだけ）
        var descriptor = ExtractDescriptor(content, "SyncOrderEntity", "SyncOrder");
        descriptor.Should().Contain("UnboundedBinaryColumnNames = [\"Attachment\"],");
        descriptor
            .Should()
            .Contain(
                "((ISyncOrderRepository)repository).ReadAttachmentAsync(id, destination, cancellationToken)"
            );
        descriptor
            .Should()
            .Contain(
                "((ISyncOrderRepository)repository).WriteAttachmentAsync(id, source, length, cancellationToken)"
            );

        // ローカル（記述子側）: 同じ表をローカルリポジトリの上で開く
        ExtractClass(content, "public abstract class SyncTableBase<TEntity, TKey>")
            .Should()
            .Contain("new SyncRepositoryBinaryColumns<TEntity, TKey>(descriptor, local)");

        // 直結サーバー: 同じ表をサーバーリポジトリ（＝Writer）の上で開く
        ExtractClass(content, "public abstract class DirectSyncSourceBase<TEntity, TKey>")
            .Should()
            .Contain("new SyncRepositoryBinaryColumns<TEntity, TKey>(descriptor, writer)");

        // HTTP サーバー: 既存のバイナリエンドポイント（基底のヘルパー）へ委譲
        var httpSource = ExtractClass(
            content,
            "public sealed class HttpSyncServerSource<TEntity, TKey>"
        );
        httpSource
            .Should()
            .Contain("_descriptor.UnboundedBinaryColumnNames.Count == 0 ? null : this;");
        httpSource.Should().Contain("DownloadUnboundedBinaryColumnAsync(");
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

    /// <summary>
    /// 記述子は、単一列で同期対象の親の主キーを参照する外部キーを親参照として持ち、親の側は自分を参照する
    /// テーブルの名前を持つ。
    /// </summary>
    [Fact(DisplayName = "記述子は単一列の外部キーを親参照として、参照される側は子テーブル名を持つ")]
    public void Descriptors_CarryParentReferencesAndReferencingTables()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        var line = ExtractDescriptor(content, "SyncOrderLineEntity", "SyncOrderLine");
        line.Should().Contain("new SyncParentReference<SyncOrderLineEntity>(");
        line.Should().Contain("\"sync_orders\",");
        line.Should()
            .Contain(
                "entity => entity.OrderId is { } value ? value.ToString(CultureInfo.InvariantCulture) : null"
            );
        line.Should().NotContain("ReferencingTableNames");

        var order = ExtractDescriptor(content, "SyncOrderEntity", "SyncOrder");
        order.Should().NotContain("ParentReferences");
        order.Should().Contain("ReferencingTableNames = [\"sync_order_lines\", \"sync_notes\"],");
    }

    /// <summary>
    /// 自己参照は親参照に含め、複数列の外部キー・一意制約の列を参照する外部キー・同期対象でない親への外部キーは
    /// 含めない（子の行から親のキーを組み立てられない・親の記述子が無い）。被参照テーブル名は同期対象かどうかを
    /// 問わない。
    /// </summary>
    [Fact(
        DisplayName = "親参照は自己参照を含み、複数列・一意制約参照・同期対象外の親への外部キーは含めない"
    )]
    public void ParentReferences_CoverSelfReferenceAndSkipWhatCannotBeKeyed()
    {
        var diagram = Diagram();
        var order = diagram.Entities.Single(entity => entity.TableName == "sync_orders");
        var note = diagram.Entities.Single(entity => entity.TableName == "sync_notes");
        var orderKey = order.Columns.Single(column => column.IsPrimaryKey);
        var orderName = order.Columns.Single(column => column.Name == "customer_name");

        // 自己参照（NULL 許容）
        var parentOrder = new Column
        {
            Id = System.Guid.NewGuid(),
            Name = "parent_order_id",
            DataType = "int",
            IsNullable = true,
        };
        order.Columns.Add(parentOrder);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = System.Guid.NewGuid(),
                SourceEntityId = order.Id,
                TargetEntityId = order.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = { new RelationshipColumnPair(orderKey.Id, parentOrder.Id) },
            }
        );

        // 一意制約の列（主キーでない）を参照する外部キー（同じ親子の 2 本目はナビゲーション名が重なるので別テーブル）
        var tagOwner = new Column
        {
            Id = System.Guid.NewGuid(),
            Name = "owner_name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var tag = new Entity
        {
            Id = System.Guid.NewGuid(),
            TableName = "sync_tags",
            Columns =
            {
                new Column
                {
                    Id = System.Guid.NewGuid(),
                    Name = "tag_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                tagOwner,
            },
        };
        diagram.Entities.Add(tag);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = System.Guid.NewGuid(),
                SourceEntityId = order.Id,
                TargetEntityId = tag.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = { new RelationshipColumnPair(orderName.Id, tagOwner.Id) },
            }
        );

        // 同期対象でない親（複合主キー＝Repository 契約が無い）への外部キー
        var category = new Entity
        {
            Id = System.Guid.NewGuid(),
            TableName = "categories",
            Columns =
            {
                new Column
                {
                    Id = System.Guid.NewGuid(),
                    Name = "region",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column
                {
                    Id = System.Guid.NewGuid(),
                    Name = "code",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        diagram.Entities.Add(category);
        var noteRegion = new Column
        {
            Id = System.Guid.NewGuid(),
            Name = "region",
            DataType = "int",
            IsNullable = true,
        };
        var noteCode = new Column
        {
            Id = System.Guid.NewGuid(),
            Name = "code",
            DataType = "int",
            IsNullable = true,
        };
        note.Columns.Add(noteRegion);
        note.Columns.Add(noteCode);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = System.Guid.NewGuid(),
                SourceEntityId = category.Id,
                TargetEntityId = note.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                {
                    new RelationshipColumnPair(category.Columns[0].Id, noteRegion.Id),
                    new RelationshipColumnPair(category.Columns[1].Id, noteCode.Id),
                },
            }
        );

        var (files, diagnostics) = Generate(diagram, SyncOptions());
        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        var content = string.Concat(files.Values);

        var orderDescriptor = ExtractDescriptor(content, "SyncOrderEntity", "SyncOrder");
        orderDescriptor.Should().Contain("new SyncParentReference<SyncOrderEntity>(");
        orderDescriptor
            .Should()
            .Contain(
                "entity => entity.ParentOrderId is { } value ? value.ToString(CultureInfo.InvariantCulture) : null"
            );
        orderDescriptor
            .Should()
            .Contain(
                "ReferencingTableNames = [\"sync_order_lines\", \"sync_notes\", \"sync_orders\", \"sync_tags\"],"
            );

        var noteDescriptor = ExtractDescriptor(content, "SyncNoteEntity", "SyncNote");
        CountOccurrences(noteDescriptor, "new SyncParentReference<SyncNoteEntity>(")
            .Should()
            .Be(1, "主キーを単一列で参照する注文への外部キーだけ");
        noteDescriptor.Should().NotContain("\"categories\"");
        ExtractDescriptor(content, "SyncTagEntity", "SyncTag")
            .Should()
            .NotContain(
                "ParentReferences",
                "一意制約の列への外部キーからは親のキーを組み立てられない"
            );
    }

    /// <summary>1 テーブル分の記述子（<c>GeneratedSyncTables</c> の静的プロパティ初期化子）を切り出す</summary>
    private static string ExtractDescriptor(
        string content,
        string entityClassName,
        string propertyName
    )
    {
        var declaration =
            $"public static SyncTableDescriptor<{entityClassName}, int> {propertyName} {{ get; }} =";
        var start = content.IndexOf(declaration, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{declaration}' が生成物に含まれていること");

        var end = content.IndexOf("\r\n        };", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1, "記述子の初期化子が閉じていること");

        return content[start..end];
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

    /// <summary>部分文字列の出現回数を数える</summary>
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;

        for (
            var index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }
}
