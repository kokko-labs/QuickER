using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP の ER 図操作ツールが図ファイルをどう読み書きするか（書き直さない・読み込みで粘る・作成で上書きしない・
/// 読めない理由を正しく言う）を確かめる
/// </summary>
public sealed class DocumentErDiagramFileHandlingTests : IDisposable
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownCaseLedger Ledger = new("DocumentErDiagramFileHandlingTests", []);

    /// <summary>変更の無い呼び出しの既知の失敗（直す前の現状）。直したら外す</summary>
    private static readonly KnownCaseLedger NoOpLedger = new(
        "DocumentErDiagramFileHandlingTests",
        []
    );

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-file-handling-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramFileHandlingTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    [Fact(
        DisplayName = "何も変えない呼び出しは、成功を返しても図のファイルを書き直さない（内容も更新日時も変わらない）"
    )]
    public void NoOpCalls_DoNotRewriteTheFile()
    {
        var file = Path.Combine(_dir, "diagram.json");
        var cases = new (string Id, string Tool, JsonObject Args)[]
        {
            (
                "set_entity_property.same-description",
                "set_entity_property",
                new JsonObject { ["table_name"] = "Customer", ["description"] = string.Empty }
            ),
            (
                "set_column_property.same-data-type",
                "set_column_property",
                new JsonObject
                {
                    ["table_name"] = "Customer",
                    ["column_name"] = "Name",
                    ["data_type"] = "nvarchar(50)",
                }
            ),
            (
                "set_column_property.nullable-on-primary-key",
                "set_column_property",
                new JsonObject
                {
                    ["table_name"] = "Customer",
                    ["column_name"] = "Id",
                    ["is_nullable"] = true,
                }
            ),
            (
                "set_unique_constraint.same-definition",
                "set_unique_constraint",
                new JsonObject
                {
                    ["table_name"] = "Customer",
                    ["columns"] = new JsonArray("Email"),
                    ["name"] = "UQ_Customer_Email",
                }
            ),
            (
                "set_query.same-definition",
                "set_query",
                new JsonObject
                {
                    ["table_name"] = "Order",
                    ["query_name"] = "ByCustomer",
                    ["returns"] = "list",
                    ["implementation"] = "dsl",
                    ["condition"] = "CustomerId = @customerId",
                    ["parameters"] = new JsonArray(
                        new JsonObject { ["name"] = "customerId", ["source_column"] = "CustomerId" }
                    ),
                }
            ),
        };
        var problems = new List<string>();

        foreach (var (id, tool, args) in cases)
        {
            NoOpLedger.Run(
                id,
                () =>
                {
                    WriteSeed(file);
                    var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    File.SetLastWriteTimeUtc(file, stamp);
                    var before = File.ReadAllBytes(file);

                    var (result, success) = DocumentErDiagramToolHost.Execute(
                        tool,
                        file,
                        JsonSerializer.SerializeToElement(args)
                    );

                    if (!success)
                    {
                        throw new Xunit.Sdk.XunitException($"呼び出しが失敗した: {result}");
                    }

                    if (
                        File.GetLastWriteTimeUtc(file) != stamp
                        || !File.ReadAllBytes(file).AsSpan().SequenceEqual(before)
                    )
                    {
                        throw new Xunit.Sdk.XunitException($"書き直した: {result}");
                    }
                },
                problems
            );
        }

        KnownCaseLedger.ThrowIfAny(problems);
    }

    [Fact(
        DisplayName = "別のプロセスが保存の最中で読めない図は、すぐに諦めず差し替えと同じだけ粘ってから失敗を返す"
    )]
    public void LockedFile_IsRetriedBeforeGivingUp() =>
        Ledger.Run("LockedFile_IsRetriedBeforeGivingUp", LockedFile_IsRetriedBeforeGivingUpCore);

    private void LockedFile_IsRetriedBeforeGivingUpCore()
    {
        var file = Path.Combine(_dir, "locked.json");
        WriteSeed(file);

        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);

        // 測る前に同じ失敗を 1 回通す（初回の JIT と例外の文言の読み込みには時間がかかり、粘らない実装でも
        // 下限を越えてしまう）
        JsonStorageService.TryLoad(file, out _, out _, out _);

        // 測るのは図の読み込みの共通の口（ツールも GUI の開く経路もここを通る）。ツール越しに測ると、並列に走る
        // ほかの呼び出しがプロセス共通のロック（DiagramFileGate）を握っている間の待ちまで入り、粘らない実装でも
        // 下限を越えてしまう
        var watch = Stopwatch.StartNew();
        var loaded = JsonStorageService.TryLoad(file, out _, out var error, out _);
        watch.Stop();

        loaded.Should().BeFalse();
        error.Should().Be(DocumentLoadError.ReadFailed);

        // 判定は時間で行う（AtomicFileTests の読み取りのリトライと同じ下限。粘らない実装は即座に戻る）
        watch
            .ElapsedMilliseconds.Should()
            .BeGreaterThanOrEqualTo(60, "別のプロセスの保存と重なった一瞬で諦めない");
    }

    [Fact(
        DisplayName = "create_diagram は、存在を確かめてから書くまでの間に現れたファイルを上書きしない（作成は既存を置き換えない書き込みで行う）"
    )]
    public void CreateDiagram_WritesWithoutReplacing() =>
        Ledger.Run(
            "CreateDiagram_WritesWithoutReplacing",
            CreateDiagram_WritesWithoutReplacingCore
        );

    private void CreateDiagram_WritesWithoutReplacingCore()
    {
        // 存在の確認と書き込みの間の競合は外から決定的に作れないので、書き込みの手段をソースで確かめる。
        // 置き換える書き込み（SaveAtomic）へ戻すと、その隙間に別のプロセスが作った図を空の図で消す
        var source = File.ReadAllText(
            Path.Combine(
                NeutralResxFiles.FindRepositoryRoot(),
                "src",
                "QuickER.Mcp.Tools",
                "DocumentErDiagramToolHost.cs"
            )
        );
        var start = source.IndexOf(
            "private static (string, bool) CreateDiagram(",
            StringComparison.Ordinal
        );
        var end = source.IndexOf(
            "// ---------------- get_diagram_summary",
            start,
            StringComparison.Ordinal
        );
        var body = source[start..end];

        body.Should().NotContain("SaveAtomic(", "作成で既存のファイルを置き換えない");
    }

    [Fact(
        DisplayName = "新しい版で保存され、この版では読めない値を含む図は「JSON ではない」と言わず、新しい版だと伝える"
    )]
    public void NewerFormatThatCannotBeRead_IsReportedAsNewerFormat() =>
        Ledger.Run(
            "NewerFormatThatCannotBeRead_IsReportedAsNewerFormat",
            NewerFormatThatCannotBeRead_IsReportedAsNewerFormatCore
        );

    private void NewerFormatThatCannotBeRead_IsReportedAsNewerFormatCore()
    {
        var file = Path.Combine(_dir, "newer.json");
        WriteSeed(file);
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["Version"] = 2;
        json["Schema"]!["Relationships"]![0]!["Type"] = "SomeFutureType";
        File.WriteAllText(file, json.ToJsonString());

        var (result, success) = DocumentErDiagramToolHost.Execute(
            "get_diagram_summary",
            file,
            JsonSerializer.SerializeToElement(new { })
        );

        success.Should().BeFalse();
        result.Should().NotContain("is not valid JSON", "壊れたファイルではない");
        result.Should().Contain("newer", "新しい版の QuickER で保存されたと分かる");
    }

    [Fact(
        DisplayName = "Version が 1 より小さい図は「Version が無い」と言わず、版の値が正しくないと伝える"
    )]
    public void VersionBelowOne_IsReportedWithItsValue() =>
        Ledger.Run(
            "VersionBelowOne_IsReportedWithItsValue",
            VersionBelowOne_IsReportedWithItsValueCore
        );

    private void VersionBelowOne_IsReportedWithItsValueCore()
    {
        var file = Path.Combine(_dir, "zero.json");
        WriteSeed(file);
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["Version"] = 0;
        File.WriteAllText(file, json.ToJsonString());

        var (result, success) = DocumentErDiagramToolHost.Execute(
            "get_diagram_summary",
            file,
            JsonSerializer.SerializeToElement(new { })
        );

        success.Should().BeFalse();
        result.Should().Contain("version 0", "持っている版の値を名指しする");
    }

    /// <summary>引数の総当たりと同じ種の図を書く</summary>
    private static void WriteSeed(string file) =>
        JsonStorageService.SaveAtomic(
            file,
            new DiagramDocument
            {
                Version = DiagramDocument.CurrentVersion,
                Schema = ToolArgumentFuzzSeed.BuildDiagram(),
            }
        );
}
