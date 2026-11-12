using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの乱数の種が作る世界の形（端末の数と、端末ごとの最初の形）。種の番号から決め、乱数は引かない。
/// </summary>
/// <param name="Devices">世界の端末</param>
/// <param name="Preloads">端末ごとの最初の形（<paramref name="Devices"/> と同じ並び）</param>
/// <remarks>
/// <para>
/// 既存の種の範囲（<see cref="WideSeedBase"/> より小さい種）は端末 1・2 が空から始める世界のまま。<see cref="WideSeedBase"/>
/// から始まる広い世界の種は端末 1〜3 の世界にし、端末 i の最初の形を <c>Kinds[(種 / 4 + i) % 4]</c> で回す（種の組＝種を 4 で
/// 割った余りと独立に回し、モード×ポリシー〔商の 6 の余り〕とは、続いた 48 種でどの組にもすべての形が出る）。範囲を分けるので、既存の種の操作の列と種の指紋は変わらない。
/// </para>
/// <para>
/// 端末は 3 台まで。同期のエンジンが分岐に読むのは、その端末の手元の記録（ジャーナル・ミラー版・受理記録・blob のコピー
/// 待ち）と、サーバーの行と版だけで、サーバーは端末ごとの状態を持たない（サーバーの差分ソースの入口は再開点・上限・件数・
/// キーだけを受け取り、書き込みは素のリポジトリへ行く。生成されるランタイムに端末の識別子は無い）。ほかの端末は、サーバーの
/// 行と版を変える書き手としてだけ見える。2 台では、どの端末から見てもほかの端末は 1 台だけで、ほかの端末どうしのアップロードが
/// 重なる形が無い。3 台でどの端末にもほかの端末が 2 台そろい、その 2 台の書き込みが重なる形が生まれる。4 台目でエンジンが
/// 新しく読むものは無い。
/// </para>
/// </remarks>
internal sealed record SyncModelWorldShape(
    IReadOnlyList<ModelActor> Devices,
    IReadOnlyList<ModelPreload> Preloads
)
{
    /// <summary>
    /// 広い世界の種の始まり（24 の倍数。種の組〔種を 4 で割った余り〕とモード×ポリシー〔種を 4 で割った商の 6 の余り〕の
    /// 配り方が既存の種と同じに、続いた 24 種で全部の組を出す）
    /// </summary>
    public const int WideSeedBase = 120_000;

    /// <summary>
    /// 追跡しない行（<see cref="ModelPreload.Untracked"/>）のキーの帯：端末のキーの範囲の基点にこの値を足した先から使う
    /// （生成器の新しいキーは基点の 1 から数え、1 つの種でここまで届かない）
    /// </summary>
    public const int UntrackedKeyOffset = 800;

    /// <summary>広い世界の端末の最初の形を回す順</summary>
    public static IReadOnlyList<ModelPreload> Kinds { get; } =
    [ModelPreload.None, ModelPreload.Refresh, ModelPreload.Journaled, ModelPreload.Untracked];

    /// <summary>端末 1・2</summary>
    public static IReadOnlyList<ModelActor> TwoDevices { get; } =
    [ModelActor.Device1, ModelActor.Device2];

    /// <summary>端末 1〜3</summary>
    public static IReadOnlyList<ModelActor> ThreeDevices { get; } =
    [ModelActor.Device1, ModelActor.Device2, ModelActor.Device3];

    /// <summary>端末 1・2 が空から始める世界（既存の種・決定的なシナリオ・網羅の行）</summary>
    public static SyncModelWorldShape Default { get; } =
        new(TwoDevices, [ModelPreload.None, ModelPreload.None]);

    /// <summary>種が広い世界の種か</summary>
    /// <param name="seed">種</param>
    public static bool IsWide(int seed) => seed >= WideSeedBase;

    /// <summary>種の世界の形</summary>
    /// <param name="seed">種</param>
    public static SyncModelWorldShape For(int seed) =>
        IsWide(seed)
            ? new SyncModelWorldShape(
                ThreeDevices,
                [.. ThreeDevices.Select((_, i) => Kinds[(seed / 4 + i) % Kinds.Count])]
            )
            : Default;

    /// <summary>書き手（世界の端末とサーバーの他者）</summary>
    public IEnumerable<ModelActor> Actors => [.. Devices, ModelActor.Server];

    /// <summary>端末の最初の形（サーバーの他者は <see cref="ModelPreload.None"/>）</summary>
    /// <param name="actor">書き手</param>
    public ModelPreload PreloadOf(ModelActor actor)
    {
        var index = Devices.ToList().IndexOf(actor);

        return index < 0 ? ModelPreload.None : Preloads[index];
    }

    /// <summary>追跡しない行のキーの帯のキーか（端末のキーの範囲の基点から <see cref="UntrackedKeyOffset"/> より先）</summary>
    /// <param name="id">フォルダー・品目のキー</param>
    public static bool IsUntrackedKey(int id) =>
        id is > 1000 and < 5000 && id / 1000 != 3 && id % 1000 > UntrackedKeyOffset;
}

/// <summary>端末の最初の形（1 歩目の前に、端末がどう行を持つか）</summary>
internal enum ModelPreload
{
    /// <summary>空から始め、後勝ちの静かな同期 1 回で初期データへそろえる</summary>
    None,

    /// <summary>空から始め、洗い替え（blob を運ぶ）1 回で初期データを作る（docs：洗い替えの用途の初回構築）</summary>
    Refresh,

    /// <summary>
    /// 最初の同期の前に、Repository 越しに自分の行を書く（ジャーナルに記録される通常のオフライン編集。一部は初期データの
    /// キー・一意な値とぶつける）。最初の同期は乱数の列が選ぶ
    /// </summary>
    Journaled,

    /// <summary>
    /// 最初の同期の前に、Repository を通らずに自分の行を入れる（記録されない・ミラー版も無い・サーバーに無いキーだけ。
    /// docs：Repository を通らない書き込みは追跡されず、上がらず、削除伝搬で消える）。最初の同期は乱数の列が選ぶ
    /// </summary>
    Untracked,
}

/// <summary>端末 1 台の最初の形と、その中身</summary>
/// <param name="Device">端末</param>
/// <param name="Kind">最初の形</param>
/// <param name="Writes">
/// <see cref="ModelPreload.Journaled"/> は Repository 越しに書く手、<see cref="ModelPreload.Untracked"/> は Repository を通らずに
/// 入れる行（追加の手だけ）。ほかの形は空
/// </param>
internal sealed record SyncModelPreload(
    ModelActor Device,
    ModelPreload Kind,
    IReadOnlyList<ModelWriteOp> Writes
);
