using System.IO;
using AwesomeAssertions;
using QuickER.Settings;

namespace QuickER.Tests.Settings;

/// <summary><see cref="AtomicFile"/> の原子的書き込み（新規作成・置換・失敗時の巻き戻り）を検証するテストクラス</summary>
/// <remarks>
/// 図ファイル・設定ファイル・接続プロファイルの 3 経路が共有する単一正本のため、
/// 「保存先を壊さない」「一時ファイルを残さない」「失敗を握り潰さない」をここで直接押さえる。
/// 各ストア側のテスト（JsonStorageServiceTests / GuiAppSettingsStoreTests /
/// SqlConnectionProfileStoreTests）は、経路ごとに委譲が繋がっていることの保証として併存させる。
/// </remarks>
public class AtomicFileTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>一時保存先フォルダを作成する</summary>
    public AtomicFileTests()
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

    /// <summary>保存先が存在しない場合に新規作成し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "WriteAllText: 保存先が無ければ新規作成し .tmp を残さない")]
    public void WriteAllText_NewFile_CreatesFileWithoutTemporaryLeftover()
    {
        var path = Path.Combine(_folder, "new.json");

        // 非 ASCII を含めて、既定の文字コード（BOM なし UTF-8）で往復できることも併せて確認する
        AtomicFile.WriteAllText(path, "{ \"名前\": \"顧客\" }");

        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Be("{ \"名前\": \"顧客\" }");
        FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
    }

    /// <summary>既存ファイルを置換し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "WriteAllText: 既存ファイルを置換し .tmp を残さない")]
    public void WriteAllText_ExistingFile_ReplacesContentWithoutTemporaryLeftover()
    {
        var path = Path.Combine(_folder, "existing.json");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        File.ReadAllText(path).Should().Be("new");
        FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
    }

    /// <summary>短い内容で上書きしても前の内容が末尾に残らない（切り詰めではなく差し替え）ことを検証する</summary>
    /// <remarks>
    /// 「一時ファイルへ書いて差し替える」実装では自明だが、うっかり保存先へ直接追記・部分上書きする
    /// 実装へ退行すると、旧内容の尻尾が残って壊れた JSON になる。その退行を検知する。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 短い内容で上書きしても旧内容が残らない")]
    public void WriteAllText_ShorterContent_LeavesNoTrailingRemnant()
    {
        var path = Path.Combine(_folder, "shrink.json");
        File.WriteAllText(path, new string('x', 4096));

        AtomicFile.WriteAllText(path, "{}");

        File.ReadAllText(path).Should().Be("{}");
    }

    /// <summary>差し替えに失敗した場合、元ファイルを無傷のまま残し、一時ファイルを掃除して例外を伝えることを検証する</summary>
    /// <remarks>
    /// 保存先を <see cref="FileShare.None"/> で開いたままにして、置換（<see cref="File.Replace"/>）と
    /// そのフォールバック（<see cref="File.Move(string, string, bool)"/>）の双方を確実に失敗させる。
    /// 原子的書き込みの本命は「失敗しても保存先が書き込み前のまま」であることなので、ここが要。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 差し替え失敗時は元ファイル無傷・.tmp なし・例外を伝播")]
    public void WriteAllText_ReplaceFailure_KeepsOriginalAndRemovesTemporary()
    {
        var path = Path.Combine(_folder, "locked.json");
        File.WriteAllText(path, "original");

        using (
            var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            var act = () => AtomicFile.WriteAllText(path, "replacement");

            // File.Replace は IOException・フォールバックの File.Move は UnauthorizedAccessException を
            // 投げる（OS/ランタイム依存）ため、どちらの型でも「伝播していること」を検証する
            var thrown = act.Should().Throw<Exception>("差し替えの失敗を握り潰さない").Which;
            (thrown is IOException or UnauthorizedAccessException)
                .Should()
                .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
            FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
        }

        File.ReadAllText(path).Should().Be("original", "失敗しても保存先は書き込み前のまま");
    }

    /// <summary>保存先がディレクトリ等で書き込めない場合も一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "WriteAllText: 保存先がディレクトリなら .tmp を残さず例外を投げる")]
    public void WriteAllText_PathIsDirectory_RemovesTemporaryAndThrows()
    {
        var path = Path.Combine(_folder, "as-directory");
        Directory.CreateDirectory(path);

        var act = () => AtomicFile.WriteAllText(path, "{}");

        act.Should().Throw<IOException>();
        FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
    }

    /// <summary>一時ファイル名に固定名 <c>{path}.tmp</c> を使わない（GUID を挟む）ことを検証する</summary>
    /// <remarks>
    /// 固定名へ退行すると、同じファイルを同時保存した 2 プロセスが同じ一時ファイルを奪い合い
    /// 「書き途中の混線した内容を本体へ差し替える」破損へ昇格する。実際にこのドリフト（1 箇所だけ
    /// 固定名のまま取り残される）が起きた経緯があるため、固定名の一時ファイルをあらかじめ置いて
    /// 「他プロセスの一時ファイルを踏まない」ことを直接確かめる。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 固定名 {path}.tmp を使わない")]
    public void WriteAllText_DoesNotUseFixedTemporaryName()
    {
        var path = Path.Combine(_folder, "guid.json");
        var fixedTemporaryPath = path + ".tmp";
        File.WriteAllText(fixedTemporaryPath, "他プロセスが使用中の一時ファイル");

        AtomicFile.WriteAllText(path, "{}");

        File.ReadAllText(path).Should().Be("{}");
        File.ReadAllText(fixedTemporaryPath)
            .Should()
            .Be("他プロセスが使用中の一時ファイル", "一時ファイル名には GUID が挟まる");
    }

    /// <summary>一時保存先フォルダに残った一時ファイル（<c>{path}.{GUID}.tmp</c>）を列挙する</summary>
    /// <remarks>一時ファイル名には GUID が挟まるため、固定名ではなくワイルドカードで探す</remarks>
    private string[] FindTemporaryLeftovers() => Directory.GetFiles(_folder, "*.tmp");
}
