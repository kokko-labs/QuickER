using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>DB のビュー・トリガー・索引（図の外の物）が、同期後の表の形でも読める・発火できる・作り直せるかを試す</summary>
/// <remarks>
/// <para>
/// 取込はビューもトリガーも索引も図に入れないので、計画は図からはそれらを知らない。テーブル再構築の方言（SQLite）でそれらが参照する
/// 列・表を同期が消すと、ビューは読めなくなり、トリガーは発火したときに断られるようになり（作り直しはトリガーの本文を検証しない）、
/// 作り直す表の索引は作り直しの途中で作れずに同期が巻き戻る。計画の段でその変更を外すために、取込がこの入口を
/// <see cref="SchemaImportResult.LiveDependentCheck"/> で持ち帰り、計画が <see cref="SyncPlanContext.LiveDependentCheck"/> で使う。
/// 実装は方言が持つ（SQL を解析せず、DB 自身に試させる）。持ち帰らない方言では <c>null</c> のまま＝計画は何もしない。
/// </para>
/// <para>
/// ビューが読めるかは、表の列の名前の集合と表の有無だけで決まる（型・主キー・NOT NULL・列順・制約の変化ではビューは壊れない）ので、
/// 入力は表ごとの同期後の列名だけにする。トリガーも、作り直しで壊れるのは同じ変化だけ（型の変更では壊れない）。索引を壊すのは、
/// 索引が読む列（列の並び・式・部分索引の <c>WHERE</c>）を消す変化だけ。ただしトリガーは表のキーにも頼る（<c>ON CONFLICT</c> の的）ので、
/// 実装は残る列でできたキーを置き換えの表へ写す。同じ同期が主キー・一意制約を変える形は試さない（実行器の同期の前後の比較が巻き戻して
/// 名指しする）。
/// </para>
/// </remarks>
public interface ILiveDependentCheck
{
    /// <summary>
    /// 同期前は読めて（発火できて・作れて）、同期後の表の形では読めなくなるビュー・発火できなくなるトリガー・作り直せなくなる索引の名前を返す
    /// </summary>
    /// <param name="tablesAfterSync">
    /// 列の名前の集合が変わる live の表 → 同期後の列名（<c>null</c> は表の削除）。ここに無い表は同期の前後で変わらないとみなす
    /// </param>
    /// <returns>
    /// 読めなくなるビュー・発火できなくなるトリガー・作り直せなくなる索引の名前（それぞれ序数の昇順）。試せなかった表・ビュー・トリガー・
    /// 索引（同期の前から写しで作れない物）は数えない（実行器が最後に拾う）。消した表のトリガー・索引は表と一緒に消えるので数えない
    /// </returns>
    LiveDependentBreaks DependentsBrokenBy(
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync
    );
}

/// <summary>
/// 同期後の表の形で読めなくなるビュー・発火できなくなるトリガー・作り直せなくなる索引（<see cref="ILiveDependentCheck.DependentsBrokenBy"/> の答え）
/// </summary>
/// <param name="Views">読めなくなるビューの名前（序数の昇順）</param>
/// <param name="Triggers">発火できなくなるトリガーの名前（序数の昇順）</param>
/// <param name="Indexes">作り直せなくなる索引の名前（序数の昇順）</param>
public sealed record LiveDependentBreaks(
    IReadOnlyList<string> Views,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Indexes
)
{
    /// <summary>どれも無い</summary>
    public bool IsEmpty => Views.Count == 0 && Triggers.Count == 0 && Indexes.Count == 0;
}
