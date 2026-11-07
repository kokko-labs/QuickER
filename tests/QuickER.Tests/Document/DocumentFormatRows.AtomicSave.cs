using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>原子的な保存（SaveAtomic の新規・置換・失敗・Save との一致、SaveNew の新規・既存の拒否）</summary>
    private static IEnumerable<ScenarioRow<FormatCase>> AtomicSaveRows() =>
        [
            Row(
                "saveatomic-new-file-created-without-temporary-leftover",
                "SaveAtomic: 保存先が無ければ新規作成し .tmp を残さない",
                new FormatCase
                {
                    Stage = FormatStage.SaveAtomic,
                    Result = FormatResult.Written,
                    Document = () => BuildDocument("Customer", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        File.Exists(o.Path).Should().BeTrue();
                        o.Leftovers.Should().BeEmpty("一時ファイルは差し替え後に残らない");
                        JsonStorageService
                            .Load(o.Path)
                            .Schema.Entities.Should()
                            .ContainSingle()
                            .Which.TableName.Should()
                            .Be("Customer");
                    },
                }
            ),
            Row(
                "saveatomic-existing-file-replaced-without-temporary-leftover",
                "SaveAtomic: 既存ファイルを置換し .tmp を残さない",
                new FormatCase
                {
                    Stage = FormatStage.SaveAtomic,
                    Result = FormatResult.Written,
                    Prior = () => BuildDocument("Old", DiagramDocument.CurrentVersion),
                    Document = () => BuildDocument("New", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        o.Leftovers.Should().BeEmpty("一時ファイルは差し替え後に残らない");
                        JsonStorageService
                            .Load(o.Path)
                            .Schema.Entities.Should()
                            .ContainSingle()
                            .Which.TableName.Should()
                            .Be("New");
                    },
                }
            ),
            // 保存先と同名のディレクトリを置いて本体への差し替え（File.Move）を確実に失敗させる。
            // 失敗のたびに .tmp が残ると、次回保存の差し替え対象と紛らわしくなる。
            Row(
                "saveatomic-replace-failure-removes-temporary-and-throws",
                "SaveAtomic: 差し替え失敗時は .tmp を残さず例外を投げる",
                new FormatCase
                {
                    Stage = FormatStage.SaveAtomic,
                    Result = FormatResult.Threw,
                    Directory = true,
                    Document = () => BuildDocument("Customer", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        // 保存先がディレクトリの場合、File.Move(overwrite:true) は UnauthorizedAccessException を
                        // 投げる（OS/ランタイム依存）ため、どちらの型でも「伝播していること」を検証する
                        var thrown = o
                            .Thrown.Should()
                            .NotBeNull("差し替えの失敗を握り潰さない")
                            .And.Subject!;
                        (thrown is IOException or UnauthorizedAccessException)
                            .Should()
                            .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
                        o.Leftovers.Should().BeEmpty("失敗時に一時ファイルを残さない");
                    },
                }
            ),
            // 往復は SaveAtomic で書いてから読む。書けなければ読み戻しへ進まず、SaveAtomic の失敗をそのまま伝える
            // （SaveAtomic の XmlDoc：書き込みは AtomicFile へ委譲＝失敗で既存の内容を壊さない）
            Row(
                "roundtrip-unwritable-destination-throws-without-temporary",
                "往復: 保存先に書けなければ例外を伝え、読み戻さず、一時ファイルを残さず、保存先を変えない",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Threw,
                    Directory = true,
                    Document = () => BuildDocument("Customer", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        var thrown = o
                            .Thrown.Should()
                            .NotBeNull("書き込みの失敗を握り潰さない")
                            .And.Subject!;
                        (thrown is IOException or UnauthorizedAccessException)
                            .Should()
                            .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
                        o.Loaded.Should().BeNull("書けなかった図を読み戻さない");
                        o.Returned.Should().BeNull();
                        o.Leftovers.Should().BeEmpty("失敗時に一時ファイルを残さない");
                        System
                            .IO.Directory.Exists(o.Path)
                            .Should()
                            .BeTrue("保存先のフォルダはそのまま");
                    },
                }
            ),
            // 元のテストは同じ図を 2 回 SaveAtomic で別々のファイルへ書いて比べている（比べ方を写す）
            Row(
                "saveatomic-output-identical-to-another-saveatomic-output",
                "SaveAtomic: 出力 JSON は Save と同一",
                new FormatCase
                {
                    Stage = FormatStage.SaveAtomic,
                    Result = FormatResult.Written,
                    Document = () => BuildDocument("Customer", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        var plainPath = Path.Combine(o.Folder, "plain.json");
                        JsonStorageService.SaveAtomic(
                            plainPath,
                            BuildDocument("Customer", DiagramDocument.CurrentVersion)
                        );

                        o.FileText.Should().Be(File.ReadAllText(plainPath));
                    },
                }
            ),
            Row(
                "savenew-new-file-writes-same-json-as-saveatomic",
                "SaveNew: 保存先が無ければ SaveAtomic と同じ内容で新規作成し .tmp を残さない",
                new FormatCase
                {
                    Stage = FormatStage.SaveNew,
                    Result = FormatResult.Written,
                    Document = () => BuildDocument("Customer", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        o.FileText.Should().Be(JsonStorageService.ToJson(o.Written!));
                        o.Leftovers.Should().BeEmpty("一時ファイルは置いた後に残らない");
                    },
                }
            ),
            // MCP の create_diagram は存在を確かめてから SaveNew で書く。その隙間に別のプロセスが作った図を上書きしない。
            Row(
                "savenew-existing-file-throws-and-keeps-existing-diagram",
                "SaveNew: 保存先が既にあれば置き換えずに IOException を投げ、既存の図を残す",
                new FormatCase
                {
                    Stage = FormatStage.SaveNew,
                    Result = FormatResult.Threw,
                    Prior = () => BuildDocument("Existing", DiagramDocument.CurrentVersion),
                    Document = () => BuildDocument("Replacement", DiagramDocument.CurrentVersion),
                    Expect = o =>
                    {
                        o.Thrown.Should()
                            .BeAssignableTo<IOException>("新規作成は既存の図を置き換えない");
                        o.BytesAfter.Should().Equal(o.BytesBefore);
                        o.Leftovers.Should().BeEmpty("失敗時に一時ファイルを残さない");
                    },
                }
            ),
        ];

    /// <summary>指定のテーブル名・フォーマットバージョンでエンティティ 1 件の保存文書を組み立てる</summary>
    /// <remarks>エンティティ Id は固定にして、出力を比較できるようにする</remarks>
    private static DiagramDocument BuildDocument(string tableName, int version) =>
        new()
        {
            Version = version,
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities =
                {
                    new Entity
                    {
                        Id = new Guid("22222222-0000-0000-0000-000000000001"),
                        TableName = tableName,
                    },
                },
            },
        };
}
