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

    /// <summary>
    /// 中を列挙できない reparse フォルダがあっても、列挙は例外にならず、ほかのファイルを返すことを検証する
    /// （素の再帰列挙は <see cref="IOException"/> で止まり、成果物検証がランごと落ちていた）。
    /// </summary>
    [Fact(
        DisplayName = "中を列挙できない reparse フォルダがあっても HasAny / Enumerate は落ちない"
    )]
    public void Enumerate_UnlistableReparseFolder_DoesNotThrow()
    {
        var root = NewTempFolder();

        try
        {
            Write(Path.Combine(root, "AcmeMock.csproj"));
            Write(Path.Combine(root, "Components", "Home.razor"));
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(cache);
            ReparsePointTestHelper.MakeUnlistableReparseDirectory(cache);

            // 素の列挙は落ちる（＝共有ヘルパーが解決している問題が実在することの確認）
            var raw = () =>
                Directory
                    .EnumerateFiles(
                        root,
                        "*.razor",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                        }
                    )
                    .ToList();
            raw.Should().Throw<IOException>();

            MockProjectFiles.HasAny(root, "*.csproj").Should().BeTrue();
            MockProjectFiles.HasAny(root, "*.xaml").Should().BeFalse();
            MockProjectFiles
                .Enumerate(root, "*.razor")
                .Should()
                .ContainSingle()
                .Which.Should()
                .EndWith("Home.razor");
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(root);
        }
    }

    /// <summary>
    /// ジャンクションの先は数えないことを検証する（成果物はプロジェクトフォルダの中に実在するものだけ。
    /// 辿ると、外を指すジャンクション 1 つで出力フォルダの外を走査し、外のファイルで成果物検証が通る）。
    /// </summary>
    [Fact(DisplayName = "ジャンクションの先のファイルは数えない")]
    public void Enumerate_DoesNotFollowJunctions()
    {
        var root = NewTempFolder();
        var outside = NewTempFolder();

        try
        {
            Write(Path.Combine(outside, "Outside.razor"));
            ReparsePointTestHelper.CreateJunction(Path.Combine(root, "linked"), outside);

            MockProjectFiles.HasAny(root, "*.razor").Should().BeFalse();
            File.Exists(Path.Combine(outside, "Outside.razor")).Should().BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(root);
            ReparsePointTestHelper.DeleteTree(outside);
        }
    }

    /// <summary>
    /// リンクでない reparse フォルダ（OneDrive の Files On-Demand と同じ形）の中は数えることを検証する
    /// （属性ビットだけで止めると、そこへ書かれた成果物が見えなくなる）。
    /// </summary>
    [Fact(DisplayName = "リンクでない reparse フォルダの中は数える")]
    public void Enumerate_TraversesNonLinkReparseFolder()
    {
        var root = NewTempFolder();

        try
        {
            var cloud = Path.Combine(root, "Components");
            Write(Path.Combine(cloud, "Home.razor"));
            ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(cloud);

            MockProjectFiles.HasAny(root, "*.razor").Should().BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(root);
        }
    }

    /// <summary>
    /// リンクの判定は <see cref="FileSystemInfo.LinkTarget"/> で行い、reparse point の属性ビットでは決めない
    /// （OneDrive の Files On-Demand のようなリンクでない reparse フォルダは中身を読み書きできる）。
    /// </summary>
    [Fact(
        DisplayName = "IsLink はジャンクションとシンボリックリンクだけを真にし、リンクでない reparse フォルダは偽"
    )]
    public void IsLink_DistinguishesLinksFromOtherReparsePoints()
    {
        var root = NewTempFolder();
        var outside = NewTempFolder();

        try
        {
            var plain = Path.Combine(root, "plain");
            Directory.CreateDirectory(plain);

            var junction = Path.Combine(root, "junction");
            ReparsePointTestHelper.CreateJunction(junction, outside);

            var nonLink = Path.Combine(root, "cloud");
            Directory.CreateDirectory(nonLink);
            Write(Path.Combine(nonLink, "inside.txt"));
            ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(nonLink);

            var fileTarget = Path.Combine(outside, "target.txt");
            Write(fileTarget);
            var fileLink = Path.Combine(root, "link.txt");
            File.CreateSymbolicLink(fileLink, fileTarget);

            MockProjectFiles.IsLink(new DirectoryInfo(plain)).Should().BeFalse();
            MockProjectFiles.IsLink(new DirectoryInfo(junction)).Should().BeTrue();
            MockProjectFiles.IsLink(new DirectoryInfo(nonLink)).Should().BeFalse();
            MockProjectFiles.IsLink(new FileInfo(fileLink)).Should().BeTrue();
            MockProjectFiles.IsLink(new FileInfo(fileTarget)).Should().BeFalse();

            // 属性ビットだけで見ると、リンクでない reparse フォルダもリンクと区別できない
            new DirectoryInfo(nonLink)
                .Attributes.HasFlag(FileAttributes.ReparsePoint)
                .Should()
                .BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(root);
            ReparsePointTestHelper.DeleteTree(outside);
        }
    }
}
