using System.IO;
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
}
