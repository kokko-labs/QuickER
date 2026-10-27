using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに起きない失敗も赤）
/// </summary>
/// <remarks>
/// <para>
/// 各項目は、対応する指摘（三巡目レビューの summary の番号・記録の番号、22a で見つけたものは NA の番号）と、
/// 一覧から外す区切りを持つ。修正の区切りは該当する項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。
/// 区切り 22 で名前の同一性の項目（44・M2・N7・32・NA1〜NA5・取込の既知の残件）と識別子の長さの上限（NA7＝既定名は上限に収め、
/// 利用者の名前は DDL・同期の入口で断る）は直して一覧から外した。区切り 23 で NA6（XmlDoc の cref）も直し、一覧は空になった。
/// </para>
/// <para>
/// キーの 3 つめは、重複の判定の失敗（duplicate-accepted / distinct-rejected）では名前の組の違い
/// （<see cref="NameDifference"/>）、受け付けた後の性質（lookup-wrong-target・stored-form-differs・upsert-rejected など）では
/// 組のどちらかが前後に空白を持つか（Plain / WithWhitespace）。
/// </para>
/// </remarks>
public static class NameFuzzKnownFailures
{
    /// <summary>一覧（項目は <see cref="FuzzFailureKey.Known"/> で作る）</summary>
    public static KnownFailureSet Set { get; } = new("Name", []);
}
