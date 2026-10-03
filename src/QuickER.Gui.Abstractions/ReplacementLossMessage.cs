using System.Text;

namespace QuickER.Gui.Abstractions;

/// <summary>取込で図を置き換えるときに失うものを述べる文言（呼び出し側の resx から渡す書式の組）</summary>
/// <param name="BrokenQueriesFormat">削除されるクエリの一覧を述べる書式（<c>{0}</c> に名前の一覧が入る）</param>
/// <param name="DescriptionOverwriteFormat">説明の上書き件数を述べる書式（<c>{0}</c> に件数が入る）</param>
/// <param name="MoreItemsFormat">一覧の上限を超えた分を示す書式（<c>{0}</c> に省略件数が入る）</param>
public sealed record ReplacementLossTexts(
    string BrokenQueriesFormat,
    string DescriptionOverwriteFormat,
    string MoreItemsFormat
);

/// <summary>取込による図の置き換えの確認文へ、失うものの内訳（削除されるクエリ・説明の上書き件数）を付ける</summary>
/// <remarks>
/// DB 取込・C# コードの取込・Excel 定義書のマージ取込が共有する。確認を出すかどうかの判定は
/// 言語中立の <c>QuickER.Provider.DiagramReplacementCheck</c> が持ち、ここは文言の組み立てだけを持つ
/// （このアセンブリは Provider を参照せず、文言も持たない＝名前の一覧と件数、書式だけを受け取る）。
/// クエリ名の一覧は標準のメッセージボックスへ載るため、<see cref="DialogItemList"/> の上限で畳む。
/// </remarks>
public static class ReplacementLossMessage
{
    /// <summary>確認文の後ろへ、失うものがあればその内訳を空行で区切って付けた文を返す</summary>
    /// <param name="message">確認文（「置き換えます。よろしいですか？」等）</param>
    /// <param name="brokenQueryNames">取込で削除されるクエリの名前（無ければ空）</param>
    /// <param name="descriptionOverwriteCount">取込値で書き換わる説明の件数（無ければ 0）</param>
    /// <param name="texts">内訳の書式</param>
    public static string Build(
        string message,
        IReadOnlyList<string> brokenQueryNames,
        int descriptionOverwriteCount,
        ReplacementLossTexts texts
    )
    {
        var builder = new StringBuilder(message);

        if (brokenQueryNames.Count > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    texts.BrokenQueriesFormat,
                    DialogItemList.Format(
                        brokenQueryNames.Select(name => "- " + name).ToList(),
                        texts.MoreItemsFormat
                    )
                );
        }

        if (descriptionOverwriteCount > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(texts.DescriptionOverwriteFormat, descriptionOverwriteCount);
        }

        return builder.ToString();
    }
}
