using System.IO;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.Settings;

namespace QuickER.Tests.Settings;

/// <summary>
/// <see cref="JsonSettingsStore{TSettings}"/> の読み取り失敗の扱い（保存の抑止・破損ファイルの退避）を
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// このストアの保存はすべて「読み込み → 変更 → 書き込み」で、読めなかったときに既定値へ黙って
/// フォールバックすると、その既定値が直後の保存で確定して利用者の設定が丸ごと消える
/// （表示言語・更新チェックの可否・コード生成の出力先など）。ここで押さえるのは
/// 「読めなかったときは書かない」「読めるようになったら自動的に書ける」の 2 点。
/// </para>
/// <para>
/// 読み取り失敗は、別のハンドルで <see cref="FileShare.None"/> のまま開いておくことで決定的に作る
/// （時間・並行に依存しない）。
/// </para>
/// </remarks>
public class JsonSettingsStoreTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>一時保存先フォルダを作成する</summary>
    public JsonSettingsStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "QuickERTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    /// <summary>一時保存先フォルダを削除する</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // テスト後の後始末はベストエフォートとする
        }
    }

    /// <summary>検証用の設定型（実在の設定型に依存せず「消えると困る値」を 1 つ持つ）</summary>
    public sealed class ProbeSettings
    {
        /// <summary>利用者が選んだ値（読み取り失敗で既定へ戻ると黙って失われるもの）</summary>
        public string Language { get; set; } = string.Empty;
    }

    /// <summary>一時フォルダへ保存するテスト用ストアを生成する</summary>
    private JsonSettingsStore<ProbeSettings> CreateStore() => new("probe-settings.json", _folder);

    /// <summary>ファイルが無いだけなら既定値で読め、保存も普通にできることを検証する</summary>
    /// <remarks>初回起動の経路。ここが抑止側へ倒れると、設定を一度も保存できなくなる。</remarks>
    [Fact(DisplayName = "Load: ファイルが無ければ既定値で読み、保存も抑止しない")]
    public void Load_MissingFile_UsesDefaultsAndAllowsSave()
    {
        var store = CreateStore();

        var loaded = store.Load();

        loaded.Language.Should().BeEmpty();
        store.LastLoadUnavailable.Should().BeFalse();
        store.LastLoadCorrupt.Should().BeFalse();

        store.Save(new ProbeSettings { Language = "ja" });

        store.Load().Language.Should().Be("ja");
    }

    /// <summary>読み取れなかったときは既定値で起動しつつ、保存を抑止することを検証する</summary>
    /// <remarks>
    /// 抑止しないと、既定値（＝読めなかった中身ではない）がそのまま保存先へ焼き付き、
    /// 保存されていた設定が全部消える。ファイルの中身が操作前のままであることまで確かめる。
    /// </remarks>
    [Fact(DisplayName = "Load/Save: 読み取れなかったら既定値で起動し保存を抑止する")]
    public void Save_AfterUnavailableLoad_DoesNotOverwriteFile()
    {
        var store = CreateStore();
        store.Save(new ProbeSettings { Language = "ja" });
        var original = File.ReadAllText(store.SettingsPath);

        using (new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = store.Load();

            loaded.Language.Should().BeEmpty("読めなかったので既定値で起動する");
            store.LastLoadUnavailable.Should().BeTrue();

            store.Save(loaded);
        }

        File.ReadAllText(store.SettingsPath)
            .Should()
            .Be(original, "読めなかった内容を既定値で塗り潰さない");
        store.Load().Language.Should().Be("ja");
    }

    /// <summary>読み取りが再び成功すれば保存の抑止が解けることを検証する（自己修復）</summary>
    /// <remarks>
    /// 抑止が解けないと、一度の一時的な失敗以降その設定を二度と保存できなくなる
    /// （利用者にとっては「設定が保存されないアプリ」になる）。
    /// </remarks>
    [Fact(DisplayName = "Save: 次の Load が成功すれば抑止は解ける")]
    public void Save_AfterUnavailableThenSuccessfulLoad_WritesAgain()
    {
        var store = CreateStore();
        store.Save(new ProbeSettings { Language = "ja" });

        using (new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Load();
        }

        store.LastLoadUnavailable.Should().BeTrue("まだ Load し直していない");

        var reloaded = store.Load();

        store.LastLoadUnavailable.Should().BeFalse("読めたので抑止は解ける");
        reloaded.Language = "en";
        store.Save(reloaded);

        store.Load().Language.Should().Be("en");
    }

    /// <summary>壊れた JSON は既定値で読み、印だけを立てることを検証する</summary>
    [Fact(DisplayName = "Load: 壊れた JSON は既定値で読み保存は抑止しない")]
    public void Load_CorruptJson_UsesDefaultsWithoutSuppressingSave()
    {
        var store = CreateStore();
        File.WriteAllText(store.SettingsPath, "{ これは JSON ではない");

        var loaded = store.Load();

        loaded.Language.Should().BeEmpty();
        store.LastLoadCorrupt.Should().BeTrue();
        store.LastLoadUnavailable.Should().BeFalse("読めてはいるので保存は抑止しない");
    }

    /// <summary>壊れた JSON は最初の保存で 1 回だけ退避され、2 回目は上書きされないことを検証する</summary>
    /// <remarks>
    /// 残したいのは「最後に正しく読めていた状態にいちばん近いもの」＝最も古い退避。
    /// 毎回上書きすると、既定値で書き直したあとの無害なファイルへ入れ替わって手掛かりが消える。
    /// </remarks>
    [Fact(DisplayName = "Save: 壊れた JSON は最初の 1 回だけ .corrupt へ退避する")]
    public void Save_CorruptJson_BacksUpOnlyOnce()
    {
        var store = CreateStore();
        File.WriteAllText(store.SettingsPath, "壊れた 1 回目");

        store.Load();
        store.Save(new ProbeSettings { Language = "ja" });

        File.Exists(store.CorruptBackupPath).Should().BeTrue();
        File.ReadAllText(store.CorruptBackupPath).Should().Be("壊れた 1 回目");
        store.Load().Language.Should().Be("ja", "壊れたファイルは正常な内容で置き換わる");

        // 2 度目の破損（別の内容）でも、最初の退避は上書きしない
        File.WriteAllText(store.SettingsPath, "壊れた 2 回目");
        store.Load();
        store.Save(new ProbeSettings { Language = "en" });

        File.ReadAllText(store.CorruptBackupPath)
            .Should()
            .Be("壊れた 1 回目", "最も古い退避を残す");
    }

    /// <summary>正常に読めたファイルは退避されないことを検証する</summary>
    [Fact(DisplayName = "Save: 正常に読めたファイルは .corrupt を作らない")]
    public void Save_HealthyFile_DoesNotCreateBackup()
    {
        var store = CreateStore();
        store.Save(new ProbeSettings { Language = "ja" });

        var loaded = store.Load();
        loaded.Language = "en";
        store.Save(loaded);

        File.Exists(store.CorruptBackupPath).Should().BeFalse();
    }

    /// <summary>インポート・エクスポートの意味論が変わっていないことを検証する</summary>
    /// <remarks>
    /// <see cref="JsonSettingsStore{TSettings}.TryLoadFrom"/> は利用者の明示操作なので、
    /// 失敗を既定値で隠さず <c>null</c> を返す（呼び出し側がエラー表示を判断する）。
    /// <see cref="JsonSettingsStore{TSettings}.SaveTo"/> は既定ファイルを読まないため抑止の対象外。
    /// </remarks>
    [Fact(DisplayName = "TryLoadFrom/SaveTo: 抑止の影響を受けず従来どおり動く")]
    public void ImportExport_IsUnaffectedBySuppression()
    {
        var store = CreateStore();
        var exportPath = Path.Combine(_folder, "exported.json");

        using (
            new FileStream(
                Path.Combine(_folder, "probe-settings.json"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            store.Load();
            store.LastLoadUnavailable.Should().BeTrue();

            // 抑止中でもエクスポートは別ファイルなので通る
            store.SaveTo(exportPath, new ProbeSettings { Language = "ja" });
        }

        store.TryLoadFrom(exportPath)!.Language.Should().Be("ja");
        store.TryLoadFrom(Path.Combine(_folder, "absent.json")).Should().BeNull();
    }

    /// <summary>入れ子と辞書を持つ検証用の設定型（未知のキーの引き継ぎ範囲を確かめるためのもの）</summary>
    public sealed class NestedProbeSettings
    {
        /// <summary>トップレベルの既知の値</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>入れ子のオブジェクト（型が知っているが、中には知らないキーが入り得る）</summary>
        public ViewProbeSettings View { get; set; } = new();

        /// <summary>辞書型のプロパティ（JSON ではオブジェクトとして出るが、中へ再帰してはいけない）</summary>
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary><see cref="NestedProbeSettings"/> の入れ子側</summary>
    public sealed class ViewProbeSettings
    {
        /// <summary>入れ子の既知の値</summary>
        public bool IsCompact { get; set; }
    }

    /// <summary>入れ子と辞書を持つテスト用ストアを生成する</summary>
    private JsonSettingsStore<NestedProbeSettings> CreateNestedStore() =>
        new("probe-settings.json", _folder);

    /// <summary>設定ファイルを直接書き、読み書きの対象となる JSON を用意する</summary>
    private void WriteRawSettings(string json) =>
        File.WriteAllText(Path.Combine(_folder, "probe-settings.json"), json);

    /// <summary>保存後の設定ファイルを JSON オブジェクトとして読み出す</summary>
    private JsonObject ReadWrittenSettings() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, "probe-settings.json")))!.AsObject();

    /// <summary>型が知らないトップレベルのキーが、保存後もファイルに残ることを検証する</summary>
    /// <remarks>
    /// 設定ファイルには版番号も「新しすぎるなら拒否する」仕組みも無いので、引き継がないと
    /// 新しい版の QuickER が足したキーを、古い版で 1 度保存しただけで失う。
    /// </remarks>
    [Fact(DisplayName = "Save: 型が知らないトップレベルのキーを引き継ぐ")]
    public void Save_UnknownTopLevelKey_IsPreserved()
    {
        var store = CreateNestedStore();
        WriteRawSettings("""{"language":"ja","futureOption":123}""");

        var loaded = store.Load();
        loaded.Language = "en";
        store.Save(loaded);

        var written = ReadWrittenSettings();

        written["futureOption"]!.GetValue<int>().Should().Be(123, "型が知らないキーは残す");
        written["language"]!.GetValue<string>().Should().Be("en");
    }

    /// <summary>入れ子のオブジェクトの中にある未知のキーも引き継がれることを検証する</summary>
    /// <remarks>
    /// 実際の設定型（GuiAppSettings 等）は入れ子が深く、増えるキーの多くは入れ子の中に入る。
    /// トップレベルだけ引き継いでも実質的にはほとんど守れない。
    /// </remarks>
    [Fact(DisplayName = "Save: 入れ子のオブジェクトの中の未知のキーも引き継ぐ")]
    public void Save_UnknownNestedKey_IsPreserved()
    {
        var store = CreateNestedStore();
        WriteRawSettings("""{"language":"ja","view":{"isCompact":true,"futureNested":"x"}}""");

        var loaded = store.Load();
        loaded.View.IsCompact.Should().BeTrue();
        loaded.View.IsCompact = false;
        store.Save(loaded);

        var view = ReadWrittenSettings()["view"]!.AsObject();

        view["futureNested"]!.GetValue<string>().Should().Be("x", "入れ子の未知のキーも残す");
        view["isCompact"]!.GetValue<bool>().Should().BeFalse("既知の値は新しい値で書き換わる");
    }

    /// <summary>未知のキーの引き継ぎが、既知のキーの書き換えを邪魔しないことを検証する</summary>
    /// <remarks>
    /// 引き継ぎはディスク側の値を勝たせる処理ではない。既知のキーは必ず「いま保存しようとしている値」になる。
    /// </remarks>
    [Fact(DisplayName = "Save: 既知のキーは引き継ぎに邪魔されず新しい値で書き換わる")]
    public void Save_KnownKeys_AreOverwrittenWithNewValues()
    {
        var store = CreateNestedStore();
        WriteRawSettings(
            """{"language":"ja","view":{"isCompact":true},"tags":{"a":"1"},"futureOption":1}"""
        );

        var loaded = store.Load();
        loaded.Language = "en";
        loaded.View.IsCompact = false;
        loaded.Tags["a"] = "2";
        store.Save(loaded);

        var written = ReadWrittenSettings();

        written["language"]!.GetValue<string>().Should().Be("en");
        written["view"]!.AsObject()["isCompact"]!.GetValue<bool>().Should().BeFalse();
        written["tags"]!.AsObject()["a"]!.GetValue<string>().Should().Be("2");
    }

    /// <summary>辞書型のプロパティから利用者が消したエントリが復活しないことを検証する</summary>
    /// <remarks>
    /// 辞書は JSON ではオブジェクトとして出るため、既知のキーをシリアライズ結果から求めて
    /// 「オブジェクトなら中へ再帰する」と判定すると、消したエントリが未知のキーに見えて復活する。
    /// 既知のキーは型のメタデータ（JsonTypeInfo.Properties）から求め、辞書は値ごと置き換えること。
    /// </remarks>
    [Fact(DisplayName = "Save: 辞書から消したエントリは復活しない")]
    public void Save_RemovedDictionaryEntry_DoesNotComeBack()
    {
        var store = CreateNestedStore();
        WriteRawSettings("""{"language":"ja","tags":{"a":"1","b":"2"}}""");

        var loaded = store.Load();
        loaded.Tags.Should().ContainKeys("a", "b");
        loaded.Tags.Remove("b");
        store.Save(loaded);

        var tags = ReadWrittenSettings()["tags"]!.AsObject();

        tags.Should().ContainSingle("消したエントリを未知のキーとして復活させない");
        tags["a"]!.GetValue<string>().Should().Be("1");
    }

    /// <summary>保存の直前の読み直しが失敗したときは、何も書かないことを検証する</summary>
    /// <remarks>
    /// <see cref="JsonSettingsStore{TSettings}.LastLoadUnavailable"/> による抑止とは別の経路。
    /// 「読み込みは成功したが、保存の時点では読めない」ときに読めないまま上書きすると、
    /// そこにあった未知のキーを黙って捨てることになり、この引き継ぎが塞ぐ穴そのものを開ける。
    /// </remarks>
    [Fact(DisplayName = "Save: 保存直前の読み直しが失敗したら何も書かない")]
    public void Save_WhenRereadIsUnavailable_WritesNothing()
    {
        var store = CreateNestedStore();
        WriteRawSettings("""{"language":"ja","futureOption":123}""");

        var loaded = store.Load();
        store.LastLoadUnavailable.Should().BeFalse("読み込み自体は成功している");
        var original = File.ReadAllText(store.SettingsPath);
        loaded.Language = "en";

        using (new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => store.Save(loaded);

            act.Should().NotThrow("読めないときは書かずに諦める");
        }

        File.ReadAllText(store.SettingsPath)
            .Should()
            .Be(original, "読めないまま上書きして未知のキーを捨てない");
    }

    /// <summary>ファイルが無いときは引き継ぐ相手がいないので、普通に書けることを検証する</summary>
    [Fact(DisplayName = "Save: ファイルが無ければ引き継がずに普通に書く")]
    public void Save_WhenFileIsMissing_WritesSerializedSettings()
    {
        var store = CreateNestedStore();

        store.Save(new NestedProbeSettings { Language = "ja" });

        var written = ReadWrittenSettings();

        written["language"]!.GetValue<string>().Should().Be("ja");
        written.Should().HaveCount(3, "型が知っているキーだけが出る（language / view / tags）");
    }

    /// <summary>壊れた JSON は引き継がずに書き直し、退避も従来どおり行われることを検証する</summary>
    /// <remarks>
    /// 壊れているファイルからはどれが「型が知らないキー」なのかを決められない。引き継ぎを諦めて
    /// 正常な内容で書き直す（＝従来の挙動）。<c>.corrupt</c> への退避はそれとは独立に効く。
    /// </remarks>
    [Fact(DisplayName = "Save: 壊れた JSON は引き継がずに書き直し .corrupt へ退避する")]
    public void Save_CorruptJson_WritesWithoutMergeAndStillBacksUp()
    {
        var store = CreateNestedStore();
        const string Broken = """{"language":"ja","futureOption":""";
        WriteRawSettings(Broken);

        store.Load();
        store.LastLoadCorrupt.Should().BeTrue();
        store.Save(new NestedProbeSettings { Language = "en" });

        var written = ReadWrittenSettings();

        written["language"]!.GetValue<string>().Should().Be("en");
        written.Should().HaveCount(3, "壊れたファイルからは何も引き継がない");
        File.ReadAllText(store.CorruptBackupPath).Should().Be(Broken, "退避は従来どおり効く");
    }
}
