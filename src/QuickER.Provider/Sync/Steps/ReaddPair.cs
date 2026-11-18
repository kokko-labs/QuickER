namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 制約を外す文と、その制約を付け直す文が共有する対の印
/// </summary>
/// <remarks>
/// 値を持たず、同じインスタンスを持つ 2 つの文が対になる（参照で比べる）。対になるのは、外部キー・一意制約の作り直しの
/// 削除と追加、外部キー・一意制約・主キーの暗黙の外し直し、主キーの変更の解除と付与。利用者が別々に選んだ削除と追加は、
/// 同じ名前でも対にしない。
/// </remarks>
internal sealed class ReaddPair;
