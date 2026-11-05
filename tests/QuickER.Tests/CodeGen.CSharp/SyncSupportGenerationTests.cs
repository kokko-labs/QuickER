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
    /// 同期支援 ON × リモートサービス ON では、同期専用エンドポイントと HTTP 差分ソースが両側に出る。
    /// </summary>
    /// <remarks>
    /// エンドポイントは同期対象テーブル<b>ごと</b>に張られなければならない（1 つでも漏れるとそのテーブルだけ
    /// 同期できず、しかもコンパイルは通る）。ルート名は既存のリモート面と同じ規則で、クライアントとサーバーが
    /// 同じ定数（<c>RemoteSyncOperations</c>）から組み立てることで食い違いようがない形にしている。
    /// </remarks>
    // 残す理由（P3-4b）：経路の文字列の組み立て（補間の形と区切りの `/`＝書式）。構造の表明（エンドポイントの張り方・定数の参照・HTTP 差分ソースの登録）は行 code-sync-remote-services-emit-endpoints-and-http-sources へ移した
    [Fact(
        DisplayName = "同期支援 × リモートサービスで同期専用エンドポイントと HTTP 差分ソースが出る"
    )]
    public void SyncSupportWithRemoteServices_EmitsEndpointsAndHttpSources()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var server = files[SyncFixtureDefinition.RemoteServerOutputFileName];

        // 経路はクライアントと共有の定数から組み立てる（片側だけ変えても食い違わない）
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Ceiling}\"");
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Changes}\"");
        server.Should().Contain("$\"{entityRoute}/{RemoteSyncOperations.Keys}\"");
    }

    /// <summary>
    /// 同期エンドポイントは、マップ時に差分ソースの登録を同期対象テーブルごとに検査する。
    /// </summary>
    /// <remarks>
    /// ハンドラはリクエスト時に解決するため、登録漏れは「正常起動・CRUD 全部 200・同期だけ不透明な 500」に
    /// なる。検査はテーブルを 1 つでも取りこぼすとそのテーブルだけ黙って抜けるので、対象ごとに 1 件ずつ
    /// 名指しで表明する（呼び出し側は <c>MapGeneratedRemoteEndpoints</c> の 1 行だけ）。
    /// </remarks>
    // 残す理由（P3-4b）：例外メッセージの文言（例外の文言）。構造の表明（検査の呼び出し・順・全テーブルぶんの表）は行 code-sync-endpoints-check-source-registration-at-mapping-time へ移した
    [Fact(DisplayName = "同期エンドポイントはマップ時に差分ソースの登録を全テーブル分検査する")]
    public void SyncEndpoints_CheckSourceRegistrationAtMappingTime()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var server = files[SyncFixtureDefinition.RemoteServerOutputFileName];

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
    // 残す理由（P3-4b）：SQL Server の SQL の断片（[dbo].[sync.orders] の分割クォート）。SQLite 側の SQL は行 exec-sync-dotted-table-name-local-sql-quotes-at-first-dot（別 DB を dbo として付けた SQLite で実行）へ移した
    [Fact(DisplayName = "ドット付きテーブル名の同期 SQL は最初のドットだけで分割クォートされる")]
    public void SyncSql_DottedTableName_QuotesAtFirstDotOnly()
    {
        var diagram = Diagram();
        diagram.Entities.First(e => e.TableName == "sync_orders").TableName = "dbo.sync.orders";

        var (files, diagnostics) = Generate(diagram, SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);

        var content = string.Concat(files.Values);

        content.Should().Contain("FROM [dbo].[sync.orders]");
        content.Should().NotContain("[dbo].[sync].[orders]");
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
    /// <c>TimeSpan</c> は time の列で作れるので、<c>CodegenCaseTests</c> の行 <c>probe-sync-key-text-timespan-key</c>
    /// （探針 SyncKeyText）へ移した。
    /// </remarks>
    [Theory(
        DisplayName = "TimeOnly・DateOnly の主キーの同期キーはコンパイルでき、文字列化が往復する"
    )]
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
    /// ジャーナル記録デコレータが全書き込み入口（Insert / Update / Delete / BulkInsert / Save×2）を覆う。
    /// </summary>
    /// <remarks>
    /// 保存フック（<c>ISaveHook</c>）はグラフ保存でしか発火せず直接 CRUD を素通しするため、
    /// デコレータ方式を採った理由そのものがこの網羅性にある。1 つでも抜けるとオフライン編集が黙って失われる。
    /// </remarks>
    // 残す理由（P3-4b）：SyncGraphRecorder の RowState の比較の向き（== Removed と != Unchanged＝式本体）。構造の表明（全書き込み入口の記録・読み取りの素通し・削除前の読み・per-type の委譲・走査）は行 code-sync-journaling-decorator-covers-every-write-entry-point へ移した
    [Fact(DisplayName = "デコレータは全書き込み入口でジャーナルへ記録する（対の経路監査）")]
    public void JournalingDecorator_CoversEveryWriteEntryPoint()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());

        // RowState の分岐は SyncGraphRecorder 側にある（保存側の決定手順のミラー）
        var recorder = ExtractClass(
            string.Concat(files.Values),
            "public sealed class SyncGraphRecorder"
        );
        recorder.Should().Contain("if (entity.RowState == RowState.Removed)");
        recorder.Should().Contain("if (entity.RowState != RowState.Unchanged");
    }

    /// <summary>ループ防止の抑制フラグを、エンジンの書き込み経路とジャーナル記録の双方が参照する。</summary>
    // 残す理由（P3-4b）：抑制の文の形（`if (SyncSession.IsSuppressed)` の判定と `using (SyncSession.Suppress())` の範囲＝式本体）。構造の表明（SyncSession の宣言・エンジンとジャーナル記録の双方の参照）は行 code-sync-session-suppresses-journaling-for-engine-writes へ移した
    [Fact(DisplayName = "同期エンジンの書き込みは SyncSession でジャーナル記録を抑制する")]
    public void SyncEngine_SuppressesJournalingForItsOwnWrites()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        content.Should().Contain("if (SyncSession.IsSuppressed)");
        content.Should().Contain("using (SyncSession.Suppress())");
    }

    // 同期対象テーブルの Info 診断（FK 順）・方言構成が 2 方言でないときのエラー・Repository 実装を前提とするエラー・
    // 同期可能テーブル 0 件のエラーと OFF のときの非発動・無制限バイナリ列の Info は、決まった行（CodegenCaseTests）
    // diag-sync-target-tables-info-in-foreign-key-order / diag-sync-requires-both-dialects-sqlserver・-sqlite /
    // diag-sync-requires-repository-implementations / diag-sync-requires-an-eligible-table・diag-sync-off-allows-no-eligible-table /
    // diag-sync-unbounded-binary-exclusion-reports-info-instead-of-error・diag-sync-no-excluded-column-in-sync-tables-no-binary-info

    /// <summary>
    /// 混在図（版あり 2＋版なし 1）の版なしテーブルは、後勝ち専用の生成形になる。
    /// </summary>
    /// <remarks>
    /// 名指しする形＝①記述子は <c>VersionlessSyncTable</c> 派生（版・アンカー系メンバーなし）②直結ソースは
    /// キー順ページング（<c>GetFirstPageAsync</c>／<c>GetPageAfterAsync</c>）で ceiling は null・change stream は
    /// <c>NotSupportedException</c> ③デコレータの Delete は事前読みなしで null 版を記録 ④サーバーの同期
    /// エンドポイントは版なしテーブルだけ <c>MapVersionlessSyncEndpoints</c>（SyncPage＋SyncKeys）を張る。
    /// </remarks>
    // 残す理由（P3-4b）：SQL Server の SQL の断片（版なしの記述子のキー順ページング SQL・版ありのソースの MIN_ACTIVE_ROWVERSION）。構造の表明（記述子の版・登録・ソース・デコレータの形）は行 code-sync-mixed-diagram-versionless-shapes へ移した（エンドポイントの選別は行 code-sync-versionless-endpoint-map-with-versionless-table へ任せた）
    [Fact(
        DisplayName = "混在図の版なしテーブルは後勝ち専用の生成形（記述子・ソース・エンドポイント）になる"
    )]
    public void MixedDiagram_EmitsVersionlessShapes()
    {
        var (files, diagnostics) = Generate(Diagram(), SyncOptions());

        diagnostics.Should().NotContain(d => d.Severity == GenerationDiagnosticSeverity.Error);
        var content = string.Concat(files.Values);

        // 走査の形（版あり＝版の昇順／版なし＝キー順ページング）は固定側が持ち、記述子はその方言 SQL を運ぶ
        ExtractDescriptor(content, "SyncNoteEntity", "SyncNote")
            .Should()
            .Contain("WHERE [note_id] > @afterKey ORDER BY [note_id]");
        ExtractClass(content, "public sealed class DirectSyncSource<TEntity, TKey>")
            .Should()
            .Contain("MIN_ACTIVE_ROWVERSION()");
    }

    /// <summary>
    /// 除外列のコピー面（<c>ISyncBinaryColumns</c>）が、ローカル・直結サーバー・HTTP サーバーの 3 箇所へ出る。
    /// </summary>
    /// <remarks>
    /// エンジンはこの面しか見ないため、3 実装のどれか 1 つが欠けると「その転送経路でだけ blob が運ばれない」
    /// という、型検査にもドリフトにも出ない非対称になる。
    /// </remarks>
    // 残す理由（P3-4b）：HTTP サーバー側の BinaryColumns の条件式（`Count == 0 ? null : this`＝式本体）。構造の表明（記述子の列名・アクセサ・ローカルと直結の面・HTTP の委譲）は行 code-sync-binary-columns-implemented-on-three-surfaces へ移した
    [Fact(DisplayName = "除外列のコピー面はローカル・直結・HTTP の 3 実装として出力される")]
    public void SyncBinaryColumns_AreImplementedOnAllThreeSurfaces()
    {
        var (files, _) = Generate(Diagram(), SyncOptions());
        var content = string.Concat(files.Values);

        // HTTP サーバー：除外列が無ければコピー面を持たず、あれば自分自身がその面になる
        ExtractClass(content, "public sealed class HttpSyncServerSource<TEntity, TKey>")
            .Should()
            .Contain("_descriptor.UnboundedBinaryColumnNames.Count == 0 ? null : this;");
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
}
