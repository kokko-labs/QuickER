using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>DB のビュー（図の外の物）が、同期後の表の形でも読めるかを試す</summary>
/// <remarks>
/// <para>
/// 取込はビューを図に入れないので、計画は図からはビューを知らない。テーブル再構築の方言（SQLite）でビューが読む列・表を同期が消すと
/// ビューが読めなくなり、実行器が同期の前後の比較で見つけて巻き戻す。計画の段でその変更を外すために、取込がこの入口を
/// <see cref="SchemaImportResult.LiveViewCheck"/> で持ち帰り、計画が <see cref="SyncPlanContext.LiveViewCheck"/> で使う。
/// 実装は方言が持つ（ビューの SQL を解析せず、DB 自身に試させる）。持ち帰らない方言では <c>null</c> のまま＝計画は何もしない。
/// </para>
/// <para>
/// ビューが読めるかは、表の列の名前の集合と表の有無だけで決まる（型・主キー・NOT NULL・列順・制約の変化ではビューは壊れない）ので、
/// 入力は表ごとの同期後の列名だけにする。
/// </para>
/// </remarks>
public interface ILiveViewCheck
{
    /// <summary>同期前は読めて、同期後の表の形では読めなくなるビューの名前を返す</summary>
    /// <param name="tablesAfterSync">
    /// 列の名前の集合が変わる live の表 → 同期後の列名（<c>null</c> は表の削除）。ここに無い表は同期の前後で変わらないとみなす
    /// </param>
    /// <returns>読めなくなるビューの名前（序数の昇順）。試せなかった表・ビューは数えない（実行器の前後の比較が最後に拾う）</returns>
    IReadOnlyList<string> ViewsBrokenBy(
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync
    );
}
