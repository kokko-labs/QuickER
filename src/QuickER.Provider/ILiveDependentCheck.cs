using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>DB のビューとトリガー（図の外の物）が、同期後の表の形でも読める・発火できるかを試す</summary>
/// <remarks>
/// <para>
/// 取込はビューもトリガーも図に入れないので、計画は図からはそれらを知らない。テーブル再構築の方言（SQLite）でビュー・トリガーが参照する
/// 列・表を同期が消すと、ビューは読めなくなり、トリガーは発火したときに断られるようになる（作り直しはトリガーの本文を検証しない）。
/// 計画の段でその変更を外すために、取込がこの入口を <see cref="SchemaImportResult.LiveDependentCheck"/> で持ち帰り、計画が
/// <see cref="SyncPlanContext.LiveDependentCheck"/> で使う。実装は方言が持つ（SQL を解析せず、DB 自身に試させる）。持ち帰らない方言では
/// <c>null</c> のまま＝計画は何もしない。
/// </para>
/// <para>
/// ビューが読めるかは、表の列の名前の集合と表の有無だけで決まる（型・主キー・NOT NULL・列順・制約の変化ではビューは壊れない）ので、
/// 入力は表ごとの同期後の列名だけにする。トリガーも、作り直しで壊れるのは同じ変化だけ（型の変更では壊れない）。ただしトリガーは表の
/// キーにも頼る（<c>ON CONFLICT</c> の的）ので、実装は残る列でできたキーを置き換えの表へ写す。同じ同期が主キー・一意制約を変える形は
/// 試さない（実行器の同期の前後の比較が巻き戻して名指しする）。
/// </para>
/// </remarks>
public interface ILiveDependentCheck
{
    /// <summary>同期前は読めて（発火できて）、同期後の表の形では読めなくなるビュー・発火できなくなるトリガーの名前を返す</summary>
    /// <param name="tablesAfterSync">
    /// 列の名前の集合が変わる live の表 → 同期後の列名（<c>null</c> は表の削除）。ここに無い表は同期の前後で変わらないとみなす
    /// </param>
    /// <returns>
    /// 読めなくなるビューと発火できなくなるトリガーの名前（それぞれ序数の昇順）。試せなかった表・ビュー・トリガーは数えない
    /// （実行器の前後の比較が最後に拾う）。消した表のトリガーは表と一緒に消えるので数えない
    /// </returns>
    LiveDependentBreaks DependentsBrokenBy(
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync
    );
}

/// <summary>同期後の表の形で読めなくなるビューと発火できなくなるトリガー（<see cref="ILiveDependentCheck.DependentsBrokenBy"/> の答え）</summary>
/// <param name="Views">読めなくなるビューの名前（序数の昇順）</param>
/// <param name="Triggers">発火できなくなるトリガーの名前（序数の昇順）</param>
public sealed record LiveDependentBreaks(
    IReadOnlyList<string> Views,
    IReadOnlyList<string> Triggers
)
{
    /// <summary>どちらも無い</summary>
    public bool IsEmpty => Views.Count == 0 && Triggers.Count == 0;
}
