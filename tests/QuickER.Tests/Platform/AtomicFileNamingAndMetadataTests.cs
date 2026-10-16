using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="AtomicFile"/> の一時ファイルの名前（長い名前の保存先・残骸の判定）と、置き換えで保存先の属性・ACL が残ることを固定する
/// </summary>
/// <remarks>
/// 一時ファイルの名前は保存先の名前に 37 文字を足すので、そのまま足すと 218 文字を超える名前の保存先では 255 文字の上限を超え、
/// 保存先そのものは作れるのに書き込みが必ず失敗する。<see cref="File.Replace(string, string, string?)"/> の代わりの
/// <see cref="File.Move(string, string, bool)"/> は、保存先の属性と ACL を引き継がない。
/// </remarks>
public sealed class AtomicFileNamingAndMetadataTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "atomic-naming-" + Guid.NewGuid().ToString("N")
    );

    public AtomicFileNamingAndMetadataTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    [Theory(
        DisplayName = "一時ファイルの名前は、保存先の名前の長さに依らず 1 区間の上限に収まり、同じフォルダに置かれる"
    )]
    [InlineData(1)]
    [InlineData(218)]
    [InlineData(219)]
    [InlineData(255)]
    public void TemporaryPathFor_FitsSegmentLimit(int length)
    {
        var path = Path.Combine(_folder, new string('a', length));

        var temporary = AtomicFile.TemporaryPathFor(path);

        Path.GetFileName(temporary).Length.Should().BeLessThanOrEqualTo(255);
        Path.GetDirectoryName(temporary).Should().Be(_folder);
        AtomicFile.IsTemporaryFileName(Path.GetFileName(temporary)).Should().BeTrue();
        Path.GetFileName(temporary).Should().StartWith(new string('a', Math.Min(length, 218)));
    }

    [Fact(DisplayName = "一時ファイルの名前を切るとき、サロゲートペアの途中で切らない")]
    public void TemporaryPathFor_DoesNotSplitSurrogatePair()
    {
        // 218 文字目と 219 文字目がサロゲートペアになる名前
        var name = new string('a', 217) + "\U0001F600" + new string('b', 30);

        var temporaryName = Path.GetFileName(
            AtomicFile.TemporaryPathFor(Path.Combine(_folder, name))
        );

        temporaryName.Should().StartWith(new string('a', 217) + ".");
        temporaryName.Length.Should().BeLessThanOrEqualTo(255);
    }

    [Theory(DisplayName = "255 文字までの名前の保存先へ、書き込みが成功し一時ファイルを残さない")]
    [InlineData(219)]
    [InlineData(255)]
    public void WriteAllText_LongFileName_Succeeds(int length)
    {
        var path = Path.Combine(_folder, new string('x', length - 5) + ".json");

        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");

        File.ReadAllText(path).Should().Be("second");
        Directory.GetFiles(_folder).Should().ContainSingle();
    }

    [Theory(DisplayName = "一時ファイルの形かどうかの判定")]
    [InlineData("a.json.0123456789abcdef0123456789abcdef.tmp", true)]
    [InlineData("x.0123456789abcdef0123456789abcdef.TMP", true)]
    [InlineData("a.json.tmp", false)]
    [InlineData("a.json.0123456789ABCDEF0123456789ABCDEF.tmp", false)]
    [InlineData("a.json.0123456789abcdef0123456789abcdeg.tmp", false)]
    [InlineData("a.json-0123456789abcdef0123456789abcdef.tmp", false)]
    [InlineData(".0123456789abcdef0123456789abcdef.tmp", false)]
    [InlineData("index.html", false)]
    public void IsTemporaryFileName_RecognizesOnlyTheTemporaryForm(string name, bool expected)
    {
        AtomicFile.IsTemporaryFileName(name).Should().Be(expected);
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
