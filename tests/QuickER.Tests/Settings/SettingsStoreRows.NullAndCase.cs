using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Settings.SettingsOpKind;

namespace QuickER.Tests.Settings;

internal static partial class SettingsStoreRows
{
    /// <summary>明示 null の読みと PascalCase のキーの読み・書き</summary>
    private static IEnumerable<ScenarioRow<SettingsScenario>> NullAndCaseRows() =>
        [
            // 手で編集した設定ファイルの "repositoryDialects": null がそのまま代入されると、宣言上は null に
            // ならないプロパティが null になり、読み手の側で NullReferenceException になる。
            // Load（既定の保存先）と TryLoadFrom（設定読込）の両方で確かめる。null を許すプロパティは対象外。
            Row(
                "load-explicit-null-for-non-nullable-property-keeps-default",
                "Load / TryLoadFrom: null を許さないプロパティの明示 null は既定値のまま残る",
                Step<NullProbeSettings>(WriteFile) with
                {
                    Text = """{"language":null,"note":null,"dialects":null,"view":null}""",
                },
                Step<NullProbeSettings>(Load) with
                {
                    Expect = o => AssertNullKeepsDefaults(o.Loaded!),
                },
                Step<NullProbeSettings>(TryLoadFrom) with
                {
                    Name = null,
                    Expect = o =>
                    {
                        AssertNullKeepsDefaults(o.TryLoaded!);

                        o.Store.LastLoadCorrupt.Should()
                            .BeFalse("1 つのキーの null で設定全体を壊れた扱いにしない");
                    },
                }
            ),
            // 利用側（コード生成ダイアログの対象 DB の判定）は要素の null でも落ちない比較をしている。
            // 要素の null まで扱えるようになったときに気づけるよう、いまの挙動を固定しておく。
            Row(
                "load-null-list-element-kept-as-is",
                "Load: リストの要素の null はそのまま読む",
                Step<NullProbeSettings>(WriteFile) with
                {
                    Text = """{"dialects":[null,"sqlite"]}""",
                },
                Step<NullProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Dialects.Should().Equal(null, "sqlite"),
                }
            ),
            // CLI は設定ファイルのキーの大文字小文字を区別せずに読む。GUI の「設定読込」で同じファイルを読むと
            // キーが全部捨てられて画面が既定値へ戻っていた。Load と TryLoadFrom の両方で確かめる。
            Row(
                "load-pascal-case-keys-are-read",
                "Load / TryLoadFrom: PascalCase のキーも読む",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text = """{"Language":"ja","View":{"IsCompact":true}}""",
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o => AssertPascalCaseRead(o.Loaded!),
                },
                Step<NestedProbeSettings>(TryLoadFrom) with
                {
                    Name = null,
                    Expect = o => AssertPascalCaseRead(o.TryLoaded!),
                }
            ),
            // 未知のキーの引き継ぎの照合が大文字小文字を区別すると、読み込みで既知として読んだ "Language" を
            // 未知と見なして書き出す側へ持ち越し、"language" と並んで残る。次の読み込みでは後ろにある
            // 古い値が勝ち、保存した値が戻ってしまう。
            Row(
                "save-pascal-case-file-does-not-duplicate-known-keys",
                "Save: PascalCase のキーは 1 つにまとまり、入れ子の未知のキーは引き継ぐ",
                Step<NestedProbeSettings>(WriteFile) with
                {
                    Text =
                        """{"Language":"ja","View":{"IsCompact":true,"futureNested":1},"futureTop":2}""",
                },
                Step<NestedProbeSettings>(Load),
                Step<NestedProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Language = "en",
                    Expect = o =>
                    {
                        o.Saved.Should().BeTrue();

                        var written = o.FileJson;

                        written
                            .Select(entry => entry.Key)
                            .Should()
                            .BeEquivalentTo("language", "view", "tags", "futureTop");
                        written["language"]!.GetValue<string>().Should().Be("en");
                        written["view"]!["futureNested"]!.GetValue<int>().Should().Be(1);
                    },
                },
                Step<NestedProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Language.Should().Be("en"),
                }
            ),
        ];

    /// <summary>明示 null が既定値のまま残る表明（Load と TryLoadFrom で共通）</summary>
    private static void AssertNullKeepsDefaults(NullProbeSettings loaded)
    {
        loaded.Language.Should().Be("en");
        loaded.Dialects.Should().Equal("sqlserver");
        loaded.View.IsCompact.Should().BeTrue();
        loaded.Note.Should().BeNull("null を許すプロパティの null は正当な値");
    }

    /// <summary>PascalCase のキーが読めた表明（Load と TryLoadFrom で共通）</summary>
    private static void AssertPascalCaseRead(NestedProbeSettings loaded)
    {
        loaded.Language.Should().Be("ja");
        loaded.View.IsCompact.Should().BeTrue();
    }
}
