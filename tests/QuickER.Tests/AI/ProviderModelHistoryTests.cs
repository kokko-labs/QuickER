using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ProviderModelHistory"/> のプロバイダ別 MRU ロジック（Touch/Remove/ModelsFor・
/// キー正規化・空リストのキー削除・戻り値）を検証するテストクラス。
/// </summary>
public class ProviderModelHistoryTests
{
    /// <summary>Touch はプロバイダ別リストを自動作成して先頭挿入し、変化時 true を返すことを検証する</summary>
    [Fact(DisplayName = "Touch はプロバイダ別に先頭挿入し変化時 true を返す")]
    public void Touch_InsertsAtFrontPerProvider_ReturnsTrueOnChange()
    {
        var history = new ProviderModelHistory();

        history.Touch("ollama-launch", "a").Should().BeTrue();
        history.Touch("ollama-launch", "b").Should().BeTrue();

        history.ModelsFor("ollama-launch").Should().Equal("b", "a");
    }

    /// <summary>プロバイダキーは Trim + 小文字化で正規化され、表記ゆれでも同一履歴になることを検証する</summary>
    [Fact(DisplayName = "プロバイダキーは Trim・大文字小文字を正規化する")]
    public void ProviderKey_IsNormalized()
    {
        var history = new ProviderModelHistory();

        history.Touch("  Ollama-Launch  ", "a").Should().BeTrue();

        // 表記ゆれ（大文字・前後空白）でも同じ履歴を指す
        history.ModelsFor("ollama-launch").Should().Equal("a");
        history.ModelsFor("OLLAMA-LAUNCH").Should().Equal("a");
        history.Providers.Should().ContainSingle().Which.Key.Should().Be("ollama-launch");
    }

    /// <summary>プロバイダ別に履歴が分離されることを検証する</summary>
    [Fact(DisplayName = "履歴はプロバイダ別に分離される")]
    public void Touch_IsolatesHistoryPerProvider()
    {
        var history = new ProviderModelHistory();
        history.Touch("provider-a", "model-a");
        history.Touch("provider-b", "model-b");

        history.ModelsFor("provider-a").Should().Equal("model-a");
        history.ModelsFor("provider-b").Should().Equal("model-b");
    }

    /// <summary>未知のプロバイダの ModelsFor は空を返すことを検証する</summary>
    [Fact(DisplayName = "未知プロバイダの ModelsFor は空を返す")]
    public void ModelsFor_UnknownProvider_ReturnsEmpty()
    {
        var history = new ProviderModelHistory();

        history.ModelsFor("unknown").Should().BeEmpty();
    }

    /// <summary>空白のみのプロバイダ・モデルの Touch は false を返し、キーも作られないことを検証する</summary>
    [Fact(DisplayName = "空白プロバイダ・空白モデルの Touch は false を返す")]
    public void Touch_BlankProviderOrModel_ReturnsFalse()
    {
        var history = new ProviderModelHistory();

        history.Touch("   ", "model").Should().BeFalse();
        history.Touch("provider", "   ").Should().BeFalse();

        // 空白モデルの Touch で空リストのキーが残らない
        history.Providers.Should().BeEmpty();
    }

    /// <summary>Touch は MRU 規則（OrdinalIgnoreCase 重複排除・新表記採用・上限 20 件）に従うことを検証する</summary>
    [Fact(DisplayName = "Touch は重複排除・新表記採用・上限 20 件の MRU 規則に従う")]
    public void Touch_FollowsMruRules()
    {
        var history = new ProviderModelHistory();
        history.Touch("p", "a");
        history.Touch("p", "b");
        history.Touch("p", "A");

        history.ModelsFor("p").Should().Equal("A", "b");

        for (var i = 0; i <= 20; i++)
        {
            history.Touch("p", $"model-{i}");
        }

        history.ModelsFor("p").Should().HaveCount(20);
        history.ModelsFor("p")[0].Should().Be("model-20");
    }

    /// <summary>Remove は OrdinalIgnoreCase 一致で削除し、空になったらキーごと消えることを検証する</summary>
    [Fact(DisplayName = "Remove は大文字小文字問わず削除し空になればキーも消す")]
    public void Remove_CaseInsensitive_DropsEmptyKey()
    {
        var history = new ProviderModelHistory();
        history.Touch("p", "a");
        history.Touch("p", "b");

        history.Remove("P", "A").Should().BeTrue();
        history.ModelsFor("p").Should().Equal("b");

        history.Remove("p", "b").Should().BeTrue();

        // 空になったプロバイダのキーは辞書から消える
        history.Providers.Should().BeEmpty();
    }

    /// <summary>存在しないプロバイダ・モデルの Remove は false を返すことを検証する</summary>
    [Fact(DisplayName = "存在しない Remove は false を返す")]
    public void Remove_Missing_ReturnsFalse()
    {
        var history = new ProviderModelHistory();
        history.Touch("p", "a");

        history.Remove("unknown", "a").Should().BeFalse();
        history.Remove("p", "zzz").Should().BeFalse();
        history.ModelsFor("p").Should().Equal("a");
    }
}
