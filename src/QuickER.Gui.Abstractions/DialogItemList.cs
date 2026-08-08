namespace QuickER.Gui.Abstractions;

/// <summary>ダイアログ本文へ項目一覧を載せるときの整形ヘルパ</summary>
/// <remarks>
/// <para>
/// 確認・通知ダイアログへ「対象の一覧」を載せる箇所（型変換できなかった列、取込で消える名前付きクエリなど）で
/// 共有する。件数が多いとダイアログが縦に伸び、標準の <c>MessageBox</c> はスクロールしないため
/// ボタンが画面外へ出てしまう。そこで上限件数までを並べ、超過分は「他 N 件」の 1 行へ畳む。
/// </para>
/// <para>
/// 「他 N 件」の文言は呼び出し側の resx から書式で受け取る（このアセンブリは文言を持たない）。
/// </para>
/// </remarks>
public static class DialogItemList
{
    /// <summary>一覧としてそのまま並べる上限件数（超過分は「他 N 件」へ畳む）</summary>
    public const int MaxItems = 30;

    /// <summary>項目行を上限まで並べ、超過分を「他 N 件」の行へ畳んだ本文を組み立てる</summary>
    /// <param name="lines">整形済みの項目行（箇条書きの記号などは呼び出し側で付ける）</param>
    /// <param name="moreItemsFormat">超過分を示す書式（<c>{0}</c> に省略件数が入る）</param>
    /// <returns>改行区切りの本文（項目が無い場合は空文字）</returns>
    public static string Format(IReadOnlyList<string> lines, string moreItemsFormat)
    {
        var body = string.Join(Environment.NewLine, lines.Take(MaxItems));

        return lines.Count <= MaxItems
            ? body
            : body + Environment.NewLine + string.Format(moreItemsFormat, lines.Count - MaxItems);
    }
}
