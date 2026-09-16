using System.IO;
using AwesomeAssertions;
using QuickER.Documents;

namespace QuickER.Tests.Document;

/// <summary>
/// <see cref="JsonStorageService.TryLoad"/> の失敗分類を検証するテストクラス。
/// </summary>
/// <remarks>
/// 逆直列化とルートキーの評価が try の外にあると、そこで出た例外が種別へ畳まれず呼び出し側の
/// 外側 catch まで素通りする（＝どの経路も「読めなかった」以上のことを言えない）。
/// とくに JSON のキー重複は <see cref="System.Text.Json.Nodes.JsonNode"/> の遅延評価のため
/// <c>JsonNode.Parse</c> では出ず、ルートキーへ最初に触れた時点で
/// <see cref="ArgumentException"/> として現れる。
/// </remarks>
public sealed class JsonStorageServiceTryLoadClassificationTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-loadclass-" + Guid.NewGuid().ToString("N")
    );

    public JsonStorageServiceTryLoadClassificationTests() => Directory.CreateDirectory(_folder);

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
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>生の JSON テキストをファイルへ書き出してパスを返す</summary>
    private string WriteJson(string json)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>ルートにキーが重複する JSON が InvalidJson へ分類されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: ルートのキー重複は InvalidJson へ分類される")]
    public void TryLoad_DuplicateRootKeys_IsInvalidJson()
    {
        var path = WriteJson("{ \"Version\": 1, \"Version\": 1, \"Schema\": {} }");

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.InvalidJson);
        exception.Should().NotBeNull("原因を呼び出し側へ持ち帰る");
    }

    /// <summary>プロパティの型が合わない JSON が InvalidJson へ分類されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: プロパティの型不一致は InvalidJson へ分類される")]
    public void TryLoad_TypeMismatch_IsInvalidJson()
    {
        var path = WriteJson("{ \"Version\": 1, \"Schema\": { \"Entities\": 5 } }");

        JsonStorageService
            .TryLoad(path, out var document, out var error, out var exception)
            .Should()
            .BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.InvalidJson);
        exception.Should().NotBeNull();
    }

    /// <summary>Version が数値でない JSON が InvalidJson へ分類されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: Version が数値でない文書は InvalidJson へ分類される")]
    public void TryLoad_NonNumericVersion_IsInvalidJson()
    {
        var path = WriteJson("{ \"Version\": \"abc\", \"Schema\": {} }");

        JsonStorageService.TryLoad(path, out var document, out var error, out _).Should().BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.InvalidJson);
    }

    /// <summary>Version が 0・負数の文書は保存形式として拒否されることを検証する</summary>
    [Theory(DisplayName = "TryLoad: Version が 1 未満の文書は NotDiagramDocument として拒否する")]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryLoad_VersionBelowOne_IsNotDiagramDocument(int version)
    {
        var path = WriteJson($"{{ \"Version\": {version}, \"Schema\": {{}} }}");

        JsonStorageService.TryLoad(path, out var document, out var error, out _).Should().BeFalse();

        document.Should().BeNull();
        error.Should().Be(DocumentLoadError.NotDiagramDocument);
    }

    /// <summary>構文として壊れた JSON が従来どおり InvalidJson へ分類されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: 壊れた JSON は InvalidJson へ分類される")]
    public void TryLoad_BrokenJson_IsInvalidJson()
    {
        var path = WriteJson("{ \"Version\": 1, ");

        JsonStorageService
            .TryLoad(path, out _, out var error, out var exception)
            .Should()
            .BeFalse();

        error.Should().Be(DocumentLoadError.InvalidJson);
        exception.Should().NotBeNull();
    }

    /// <summary>ER 図ではない JSON が従来どおり NotDiagramDocument へ分類されることを検証する</summary>
    [Fact(DisplayName = "TryLoad: ER 図でない JSON は NotDiagramDocument へ分類される")]
    public void TryLoad_UnrelatedJson_IsNotDiagramDocument()
    {
        var path = WriteJson("{ \"name\": \"package\", \"version\": \"1.0.0\" }");

        JsonStorageService
            .TryLoad(path, out _, out var error, out var exception)
            .Should()
            .BeFalse();

        error.Should().Be(DocumentLoadError.NotDiagramDocument);
        exception.Should().BeNull("形式検証で弾いた失敗は原因例外を持たない");
    }
}
