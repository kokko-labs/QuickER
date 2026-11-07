using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>
    /// TryLoad の失敗分類
    /// </summary>
    /// <remarks>
    /// 逆直列化とルートキーの評価が try の外にあると、そこで出た例外が種別へ畳まれず呼び出し側の
    /// 外側 catch まで素通りする（＝どの経路も「読めなかった」以上のことを言えない）。
    /// とくに JSON のキー重複は <see cref="System.Text.Json.Nodes.JsonNode"/> の遅延評価のため
    /// <c>JsonNode.Parse</c> では出ず、ルートキーへ最初に触れた時点で
    /// <see cref="ArgumentException"/> として現れる。
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> TryLoadClassificationRows() =>
        [
            // 前置きを何も置かない＝対象のパスにファイルが無い（DocumentLoadError.ReadFailed の XmlDoc「不在・アクセス不可などの IO エラー」）
            Row(
                "tryload-missing-file-read-failed",
                "TryLoad: ファイルが無ければ ReadFailed へ分類し、原因の例外を持ち帰り、読み取ったバイト列は null",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.ReadFailed,
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Exception.Should()
                            .BeAssignableTo<FileNotFoundException>("原因を呼び出し側へ持ち帰る");
                        o.RawContents.Should()
                            .BeNull("読み取り自体に失敗したときだけ null（TryLoad の XmlDoc）");
                        File.Exists(o.Path).Should().BeFalse("読み込みはファイルを作らない");
                    },
                }
            ),
            // 特性の固定の側面：例外の型（JsonStorageService.cs:135 の Load は File.ReadAllText と逆直列化の例外をそのまま出す。
            // XmlDoc は「自分が書いたファイルを読み戻す」用途と言うだけで、読めないときの型を定めない）
            Row(
                "load-missing-file-throws",
                "特性の固定（文書に定めが無い今の振る舞い）：Load はファイルが無ければ FileNotFoundException を投げる（TryLoad のように分類しない）",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Threw,
                    Expect = o =>
                    {
                        o.Thrown.Should().BeAssignableTo<FileNotFoundException>();
                        File.Exists(o.Path).Should().BeFalse("読み込みはファイルを作らない");
                    },
                }
            ),
            Row(
                "load-invalid-json-throws",
                "特性の固定（文書に定めが無い今の振る舞い）：Load は JSON として読めない内容に JsonException を投げ、ファイルを変えない",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Threw,
                    Json = "{ \"Version\": 1, \"Schema\": ",
                    Expect = o =>
                    {
                        o.Thrown.Should().BeAssignableTo<System.Text.Json.JsonException>();
                        o.BytesAfter.Should()
                            .Equal(o.BytesBefore, "読み込みはファイルを書き換えない");
                    },
                }
            ),
            Row(
                "tryload-duplicate-root-keys-invalid-json",
                "TryLoad: ルートのキー重複は InvalidJson へ分類される",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = "{ \"Version\": 1, \"Version\": 1, \"Schema\": {} }",
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.InvalidJson);
                        o.Exception.Should().NotBeNull("原因を呼び出し側へ持ち帰る");
                    },
                }
            ),
            // 拒否するのはルートの重複だけ（どちらの値を図として採るか決まらない）。解析を 1 回にまとめても、
            // 入れ子の扱いを「文字列から逆直列化していたとき」から変えないことの固定。
            Row(
                "tryload-duplicate-nested-keys-last-one-wins",
                "TryLoad: 入れ子のキー重複は後勝ちで読む",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.Loaded,
                    Json =
                        "{ \"Version\": 1, \"Schema\": { \"TargetDbms\": \"sqlserver\", \"TargetDbms\": \"postgresql\" } }",
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.None);
                        o.Loaded!.Schema.TargetDbms.Should().Be("postgresql");
                    },
                }
            ),
            Row(
                "tryload-type-mismatch-invalid-json",
                "TryLoad: プロパティの型不一致は InvalidJson へ分類される",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = "{ \"Version\": 1, \"Schema\": { \"Entities\": 5 } }",
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.InvalidJson);
                        o.Exception.Should().NotBeNull();
                    },
                }
            ),
            Row(
                "tryload-non-numeric-version-invalid-json",
                "TryLoad: Version が数値でない文書は InvalidJson へ分類される",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = "{ \"Version\": \"abc\", \"Schema\": {} }",
                    Expect = o =>
                    {
                        o.Loaded.Should().BeNull();
                        o.Error.Should().Be(DocumentLoadError.InvalidJson);
                    },
                }
            ),
            VersionBelowOneRow("tryload-version-zero-not-diagram-document", "Version が 0", "0"),
            VersionBelowOneRow(
                "tryload-version-negative-not-diagram-document",
                "Version が -1",
                "-1"
            ),
            Row(
                "tryload-broken-json-invalid-json",
                "TryLoad: 壊れた JSON は InvalidJson へ分類される",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = "{ \"Version\": 1, ",
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.InvalidJson);
                        o.Exception.Should().NotBeNull();
                    },
                }
            ),
            Row(
                "tryload-unrelated-json-not-diagram-document",
                "TryLoad: ER 図でない JSON は NotDiagramDocument へ分類される",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.NotDiagramDocument,
                    Json = "{ \"name\": \"package\", \"version\": \"1.0.0\" }",
                    Expect = o =>
                    {
                        o.Error.Should().Be(DocumentLoadError.NotDiagramDocument);
                        o.Exception.Should().BeNull("形式検証で弾いた失敗は原因例外を持たない");
                    },
                }
            ),
        ];

    /// <summary>Version が 0・負数の文書が保存形式として拒否される行</summary>
    private static ScenarioRow<FormatCase> VersionBelowOneRow(
        string id,
        string shape,
        string version
    ) =>
        Row(
            id,
            $"TryLoad: Version が 1 未満の文書は NotDiagramDocument として拒否する（{shape}）",
            new FormatCase
            {
                Stage = FormatStage.TryLoad,
                Result = FormatResult.NotDiagramDocument,
                Json = $"{{ \"Version\": {version}, \"Schema\": {{}} }}",
                Expect = o =>
                {
                    o.Loaded.Should().BeNull();
                    o.Error.Should().Be(DocumentLoadError.NotDiagramDocument);
                },
            }
        );
}
