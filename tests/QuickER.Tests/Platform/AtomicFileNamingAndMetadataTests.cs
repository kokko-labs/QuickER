using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="AtomicFile"/> の置き換えで保存先の隠し属性・ACL が残ることを固定するテストクラス
/// </summary>
/// <remarks>
/// 一時ファイルの名前の行は <c>Platform/OsBoundary/AtomicFileRows.Names.cs</c> へ移した。ここに残すのは属性と ACL（OS の振る舞い）。
/// <see cref="File.Replace(string, string, string?)"/> の代わりの <see cref="File.Move(string, string, bool)"/> は、保存先の属性と ACL を引き継がない。
/// </remarks>
public sealed class AtomicFileNamingAndMetadataTests : IDisposable
{
    /// <summary>テストごとの一時フォルダ（破棄で中身ごと消す）</summary>
    private readonly TestTempFolder _temp = new();

    private string _folder => _temp.Path;

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            _temp.Dispose();
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    [Fact(DisplayName = "隠し属性の保存先を置き換えても、隠し属性は残る")]
    public void WriteAllText_HiddenTarget_KeepsHidden()
    {
        var path = Path.Combine(_folder, "hidden.json");
        File.WriteAllText(path, "old");
        File.SetAttributes(path, FileAttributes.Hidden);

        AtomicFile.WriteAllText(path, "new");

        File.ReadAllText(path).Should().Be("new");
        File.GetAttributes(path).Should().HaveFlag(FileAttributes.Hidden);
        Directory.GetFiles(_folder).Should().ContainSingle();
    }

    [Fact(DisplayName = "置き換えの代わりの移動でも、保存先の属性と ACL は移した先へ写る")]
    public void MoveOverPreservingMetadata_KeepsAttributesAndAcl()
    {
        var path = Path.Combine(_folder, "target.json");
        File.WriteAllText(path, "old");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(
            new FileSystemAccessRule(everyone, FileSystemRights.ReadData, AccessControlType.Allow)
        );
        new FileInfo(path).SetAccessControl(security);
        var temporary = AtomicFile.TemporaryPathFor(path);
        File.WriteAllText(temporary, "new");

        AtomicFile.MoveOverPreservingMetadata(temporary, path);

        File.ReadAllText(path).Should().Be("new");
        File.GetAttributes(path).Should().HaveFlag(FileAttributes.Hidden);
        File.GetAttributes(path).Should().HaveFlag(FileAttributes.System);
        new FileInfo(path)
            .GetAccessControl()
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                typeof(SecurityIdentifier)
            )
            .OfType<FileSystemAccessRule>()
            .Should()
            .Contain(rule =>
                rule.IdentityReference.Equals(everyone)
                && rule.FileSystemRights.HasFlag(FileSystemRights.ReadData)
                && rule.AccessControlType == AccessControlType.Allow
            );
        File.Exists(temporary).Should().BeFalse();
    }
}
