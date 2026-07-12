namespace QuickER.AI.UI;

/// <summary>
/// モデル候補 1 件。固定カタログ由来（削除不可）か、× ボタンで個別削除できる履歴由来かを
/// <see cref="IsRemovable"/> で見分ける（API キー接続・Codex 接続のモデル ComboBox で共用）。
/// </summary>
/// <param name="Name">モデル名</param>
/// <param name="IsRemovable">履歴由来（× で削除可能）なら true。固定カタログ候補は false</param>
public sealed record ModelCandidate(string Name, bool IsRemovable)
{
    /// <summary>編集可能 ComboBox の項目選択時に Text（＝モデル名の双方向バインド）へ入る値</summary>
    public override string ToString() => Name;
}
