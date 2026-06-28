namespace QuickER.Documents;

/// <summary>
/// エンティティの視覚（レイアウト）情報。
/// </summary>
/// <remarks>
/// 意味モデル <see cref="QuickER.Model.Entity"/> から分離し、保存文書
/// （<see cref="DiagramDocument"/>）のサイドカーとして保持する。CLI・生成器・エクスポータは参照しない。
/// </remarks>
public sealed class EntityLayout
{
    /// <summary>見出し帯の背景色の既定値（淡い青）</summary>
    public const string DefaultTitleBackgroundColor = "#DCEBFF";

    /// <summary>キャンバス上の X 座標 (px)</summary>
    public double X { get; set; }

    /// <summary>キャンバス上の Y 座標 (px)</summary>
    public double Y { get; set; }

    /// <summary>エンティティカードの横幅 (px)</summary>
    public double Width { get; set; } = 200;

    /// <summary>見出し帯に表示する背景色（<c>#RRGGBB</c> 形式）</summary>
    public string TitleBackgroundColor { get; set; } = DefaultTitleBackgroundColor;
}
