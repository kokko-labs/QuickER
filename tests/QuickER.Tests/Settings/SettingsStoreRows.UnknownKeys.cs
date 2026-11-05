using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Settings.SettingsOpKind;

namespace QuickER.Tests.Settings;

internal static partial class SettingsStoreRows
{
    /// <summary>未知のキーの引き継ぎ（トップレベル・入れ子・既知のキーの上書き・辞書・読み直せないとき・ファイル無し・壊れた JSON）</summary>
    private static IEnumerable<ScenarioRow<SettingsScenario>> UnknownKeyRows() =>
        [
            // 設定ファイルには版番号も「新しすぎるなら拒否する」仕組みも無いので、引き継がないと
            // 新しい版の QuickER が足したキーを、古い版で 1 度保存しただけで失う。
            Row(
                "save-unknown-top-level-key-preserved",
                "Save: 型が知らないトップレベルのキーを引き継ぐ",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = """{"language":"ja","futureOption":123}""",
                },
                Step<NestedProbeSettings>(Load),
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Language = "en",
                    Expect = o =>
                    {
                        var written = o.FileJson;

                        written["futureOption"]!
                            .GetValue<int>()
                            .Should()
                            .Be(123, "型が知らないキーは残す");
                        written["language"]!.GetValue<string>().Should().Be("en");
                    },
                }
            ),
            // 実際の設定型（GuiAppSettings 等）は入れ子が深く、増えるキーの多くは入れ子の中に入る。
            Row(
                "save-unknown-nested-key-preserved",
                "Save: 入れ子のオブジェクトの中の未知のキーも引き継ぐ",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = """{"language":"ja","view":{"isCompact":true,"futureNested":"x"}}""",
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.View.IsCompact.Should().BeTrue(),
                },
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.View.IsCompact = false,
                    Expect = o =>
                    {
                        var view = o.FileJson["view"]!.AsObject();

                        view["futureNested"]!
                            .GetValue<string>()
                            .Should()
                            .Be("x", "入れ子の未知のキーも残す");
                        view["isCompact"]!
                            .GetValue<bool>()
                            .Should()
                            .BeFalse("既知の値は新しい値で書き換わる");
                    },
                }
            ),
            // 引き継ぎはディスク側の値を勝たせる処理ではない。既知のキーは必ず「いま保存しようとしている値」になる。
            Row(
                "save-known-keys-overwritten-with-new-values",
                "Save: 既知のキーは引き継ぎに邪魔されず新しい値で書き換わる",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text =
                        """{"language":"ja","view":{"isCompact":true},"tags":{"a":"1"},"futureOption":1}""",
                },
                Step<NestedProbeSettings>(Load),
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded =>
                    {
                        loaded.Language = "en";
                        loaded.View.IsCompact = false;
                        loaded.Tags["a"] = "2";
                    },
                    Expect = o =>
                    {
                        var written = o.FileJson;

                        written["language"]!.GetValue<string>().Should().Be("en");
                        written["view"]!.AsObject()["isCompact"]!
                            .GetValue<bool>()
                            .Should()
                            .BeFalse();
                        written["tags"]!.AsObject()["a"]!.GetValue<string>().Should().Be("2");
                    },
                }
            ),
            // 辞書は JSON ではオブジェクトとして出るため、既知のキーをシリアライズ結果から求めて
            // 「オブジェクトなら中へ再帰する」と判定すると、消したエントリが未知のキーに見えて復活する。
            Row(
                "save-removed-dictionary-entry-does-not-come-back",
                "Save: 辞書から消したエントリは復活しない",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = """{"language":"ja","tags":{"a":"1","b":"2"}}""",
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Tags.Should().ContainKeys("a", "b"),
                },
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Tags.Remove("b"),
                    Expect = o =>
                    {
                        var tags = o.FileJson["tags"]!.AsObject();

                        tags.Should().ContainSingle("消したエントリを未知のキーとして復活させない");
                        tags["a"]!.GetValue<string>().Should().Be("1");
                    },
                }
            ),
            // LastLoadUnavailable による抑止とは別の経路。「読み込みは成功したが、保存の時点では読めない」ときに
            // 読めないまま上書きすると、そこにあった未知のキーを黙って捨てることになる。
            Row(
                "save-when-reread-is-unavailable-writes-nothing",
                "Save: 保存直前の読み直しが失敗したら何も書かない",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = """{"language":"ja","futureOption":123}""",
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o =>
                    {
                        o.Store.LastLoadUnavailable.Should().BeFalse("読み込み自体は成功している");
                        o.Memo["original"] = o.FileText;
                    },
                },
                Step<NestedProbeSettings>(LockFile),
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Language = "en",
                    Expect = o => o.Thrown.Should().BeNull("読めないときは書かずに諦める"),
                },
                Step<NestedProbeSettings>(Save) with
                {
                    Expect = o => o.Saved.Should().BeFalse("書かずに戻ったことを呼び出し側へ返す"),
                },
                Step<NestedProbeSettings>(UnlockFile) with
                {
                    Expect = o =>
                        o.FileText.Should()
                            .Be(
                                (string?)o.Memo["original"],
                                "読めないまま上書きして未知のキーを捨てない"
                            ),
                }
            ),
            Row(
                "save-when-file-is-missing-writes-serialized-settings",
                "Save: ファイルが無ければ引き継がずに普通に書く",
                Step<NestedProbeSettings>(Save) with
                {
                    Make = () => new NestedProbeSettings { Language = "ja" },
                    Expect = o =>
                    {
                        var written = o.FileJson;

                        written["language"]!.GetValue<string>().Should().Be("ja");
                        written
                            .Should()
                            .HaveCount(3, "型が知っているキーだけが出る（language / view / tags）");
                    },
                }
            ),
            // 壊れているファイルからはどれが「型が知らないキー」なのかを決められない。引き継ぎを諦めて
            // 正常な内容で書き直す。.corrupt への退避はそれとは独立に効く。
            Row(
                "save-corrupt-json-writes-without-merge-and-still-backs-up",
                "Save: 壊れた JSON は引き継がずに書き直し .corrupt へ退避する",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = BrokenSettingsJson,
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o => o.Store.LastLoadCorrupt.Should().BeTrue(),
                },
                Step<NestedProbeSettings>(Save) with
                {
                    Make = () => new NestedProbeSettings { Language = "en" },
                    Expect = o =>
                    {
                        var written = o.FileJson;

                        written["language"]!.GetValue<string>().Should().Be("en");
                        written.Should().HaveCount(3, "壊れたファイルからは何も引き継がない");
                        o.BackupText.Should().Be(BrokenSettingsJson, "退避は従来どおり効く");
                    },
                }
            ),
        ];

    /// <summary>途中で切れた設定 JSON</summary>
    private const string BrokenSettingsJson = """{"language":"ja","futureOption":""";
}
