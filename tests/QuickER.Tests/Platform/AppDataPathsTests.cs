using System.IO;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// アプリ設定・作業状態と一時ファイルの置き場所（<see cref="AppDataPaths"/>）の値を固定する
/// </summary>
/// <remarks>
/// 置き場所の算出を QuickER.Platform の外で写さないことは、ソースの規則の表の <c>app-data-path-lookups</c>
/// （<see cref="TestSupport.Guards.SourceRuleTests"/>）が固定する。
/// </remarks>
public sealed class AppDataPathsTests
{
    /// <summary>アプリ設定・作業状態の保存先は %LOCALAPPDATA%\QuickER で、ローミングの配下ではない</summary>
    [Fact(
        DisplayName = "LocalRoot は %LOCALAPPDATA%\\QuickER で、ローミング（%APPDATA%）の配下ではない"
    )]
    public void LocalRoot_IsUnderLocalApplicationData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        AppDataPaths.LocalRoot.Should().Be(Path.Combine(local, "QuickER"));
        AppDataPaths
            .LocalRoot.Should()
            .NotStartWith(
                roaming + Path.DirectorySeparatorChar,
                "機械固有の作業状態と DPAPI の秘密はローミングで他端末へ複製しない"
            );
    }

    /// <summary>一時ファイルの置き場所は %TEMP%\QuickER</summary>
    [Fact(DisplayName = "TempRoot は %TEMP%\\QuickER")]
    public void TempRoot_IsUnderTempPath()
    {
        AppDataPaths.TempRoot.Should().Be(Path.Combine(Path.GetTempPath(), "QuickER"));
    }
}
