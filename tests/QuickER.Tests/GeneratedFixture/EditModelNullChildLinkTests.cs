using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// アクセサが null を返す子コレクション登録（<c>AddChildren</c>）の許容を、コミット済みフィクスチャ
/// （<c>InMemoryFixture.g.cs</c> の NodeEditModel＋テスト側 partial）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則: 子コレクションのアクセサが null を返す間、そのリンクは空として扱われる（単一子の
/// <c>ForSingle</c> と同じ規則＝検証・エラー収集・受理・変更判定のどれも NRE にならない）。partial で
/// 遅延初期化のコレクションを登録する利用者コードが、初期化前の検証で落ちないための対称性。
/// 対象を NodeEditModel にしているのは、この partial（Ghost リンク）が他のテストの検証対象へ
/// 影響しないようにするため（Customer / Order / BlankProbe は他スイートが使う）。
/// </remarks>
public sealed class EditModelNullChildLinkTests
{
    [Fact]
    public void アクセサがnullを返す子コレクション登録は空として扱われる()
    {
        var model = new NodeEditModel();

        // 必須列（node_id / label）を満たしておく＝残る検証対象は子リンクだけ
        model.BindingNodeId = "1";
        model.BindingLabel = "probe";

        // RegisterExtraChildren（下の partial）が null アクセサの子リンクを登録している
        model.Validate().Should().BeTrue("null のリンクは検証対象なしとして成立する");
        model.HasGraphChanges(includeChildren: true).Should().BeTrue("自身は Added＝変更あり");
        model.CollectErrors().Should().BeEmpty();
        var acceptChanges = () => model.AcceptChanges(includeChildren: true);
        acceptChanges.Should().NotThrow("受理も null リンクを素通りする");
    }
}

/// <summary>null を返す子コレクションアクセサを登録するテスト用 partial（上のテスト専用・null リンクは空として無害）。</summary>
public partial class NodeEditModel
{
    /// <inheritdoc />
    protected override void RegisterExtraChildren() =>
        AddChildren<NodeEditModel>("Ghost", () => null!);
}
