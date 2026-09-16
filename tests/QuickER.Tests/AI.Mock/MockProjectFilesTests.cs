using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 成果物探索の共有ヘルパー <see cref="MockProjectFiles"/> を検証するテストクラス。
/// </summary>
/// <remarks>
/// 素の <c>Directory.EnumerateFiles(..., AllDirectories)</c> は権限のないサブフォルダで例外を投げ、
/// ビルド中間物も無関係な同居ファイルも数えてしまう。3 つの性質（アクセス拒否を飛ばす・obj/bin を数えない・
/// 範囲は起点フォルダ配下だけ）をここで固定する。
/// </remarks>
public class MockProjectFilesTests
{
    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Write(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>ビルド中間物（obj / bin）配下のファイルを数えないことを検証する</summary>
    [Fact(DisplayName = "obj / bin 配下は列挙から除外する")]
    public void Enumerate_ExcludesBuildOutputDirectories()
    {
        var root = NewTempFolder();

        try
        {
            Write(Path.Combine(root, "Components", "Home.razor"));
            Write(Path.Combine(root, "obj", "Debug", "Generated.razor"));
            Write(Path.Combine(root, "bin", "Debug", "Copied.razor"));

            var files = MockProjectFiles.Enumerate(root, "*.razor").ToList();

            files.Should().ContainSingle();
            files[0].Should().EndWith("Home.razor");
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>除外は起点直下の obj / bin だけで、深い階層の同名フォルダは数えることを検証する</summary>
    /// <remarks>
    /// 最終ビルドの前に消すのは <c>{プロジェクト}/obj</c>・<c>{プロジェクト}/bin</c> の 2 つだけなので、
    /// 数えない範囲もそこへ揃える。途中の階層まで名前で弾くと、生成物が正当に作った
    /// <c>Components/bin/</c> のようなフォルダごと成果物検証から消える。
    /// </remarks>
    [Fact(DisplayName = "除外は起点直下の obj / bin だけ")]
    public void Enumerate_ExcludesOnlyTopLevelBuildOutputDirectories()
    {
        var root = NewTempFolder();

        try
        {
            Write(Path.Combine(root, "obj", "Debug", "Generated.razor"));
            Write(Path.Combine(root, "Components", "bin", "Nested.razor"));

            var files = MockProjectFiles.Enumerate(root, "*.razor").ToList();

            files.Should().ContainSingle("起点直下の obj だけを除外する");
            files[0].Should().EndWith("Nested.razor");
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>探索範囲が起点フォルダ配下に限られる（親フォルダの同名ファイルを拾わない）ことを検証する</summary>
    [Fact(DisplayName = "探索範囲は起点フォルダ配下に限られる")]
    public void HasAny_DoesNotLookOutsideRoot()
    {
        var outputDirectory = NewTempFolder();

        try
        {
            // 出力フォルダ直下（プロジェクトフォルダの外）にだけ csproj を置く
            Write(Path.Combine(outputDirectory, "Unrelated.csproj"));
            var projectDirectory = Path.Combine(outputDirectory, "AcmeMock");
            Directory.CreateDirectory(projectDirectory);

            MockProjectFiles.HasAny(projectDirectory, "*.csproj").Should().BeFalse();

            Write(Path.Combine(projectDirectory, "AcmeMock.csproj"));

            MockProjectFiles.HasAny(projectDirectory, "*.csproj").Should().BeTrue();
        }
        finally
        {
            Cleanup(outputDirectory);
        }
    }

    /// <summary>起点フォルダが無い・空文字でも例外にならず false／空になることを検証する</summary>
    [Fact(DisplayName = "起点フォルダが無ければ空を返す")]
    public void Enumerate_MissingRoot_ReturnsEmpty()
    {
        MockProjectFiles.Enumerate(string.Empty, "*.cs").Should().BeEmpty();
        MockProjectFiles
            .Enumerate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "*.cs")
            .Should()
            .BeEmpty();
        MockProjectFiles.HasAny("   ", "*.cs").Should().BeFalse();
    }

    /// <summary>
    /// アクセス拒否のサブフォルダがあっても例外にならず、読めるファイルだけを返すことを検証する。
    /// </summary>
    /// <remarks>
    /// 現在のユーザーへ拒否 ACE を付けたフォルダを作る。素の再帰列挙はこの状況で
    /// <see cref="UnauthorizedAccessException"/> を投げ、呼び出し側（成果物検証・ランナー）ごと落ちる。
    /// 後片付けのために、判定後は必ず拒否 ACE を外してから削除する。
    /// </remarks>
    [Fact(DisplayName = "アクセス拒否のフォルダがあっても落ちない")]
    public void Enumerate_InaccessibleSubdirectory_DoesNotThrow()
    {
        var root = NewTempFolder();
        var denied = Path.Combine(root, "denied");
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(
            user,
            FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny
        );

        Write(Path.Combine(root, "Visible.csproj"));
        Write(Path.Combine(denied, "Hidden.csproj"));

        var info = new DirectoryInfo(denied);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        try
        {
            // 素の列挙は落ちる（＝共有ヘルパーが解決している問題が実在することの確認）
            var raw = () =>
                Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).ToList();
            raw.Should().Throw<UnauthorizedAccessException>();

            var files = MockProjectFiles.Enumerate(root, "*.csproj").ToList();

            files.Should().ContainSingle();
            files[0].Should().EndWith("Visible.csproj");
        }
        finally
        {
            var cleanupSecurity = info.GetAccessControl();
            cleanupSecurity.RemoveAccessRule(rule);
            info.SetAccessControl(cleanupSecurity);
            Cleanup(root);
        }
    }
}
