using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の、Before のフックが新しい行のキーを決める保存を軸から組むシナリオ（<c>BackendParityScenarios.HookAssignedKeys</c>
/// ＝<c>n2-hook-assigned-keys-{型}-{数}-{置き場所}-{経路}</c>）が作らない組の宣言（QuickER.Tests にもリンクで入るので、Integration の型を
/// cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class BackendParityHookAssignedKeysNotGenerated
{
    private const string Parity = "`GeneratedRuntime/Parity`";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "BackendParityHookAssignedKeys",
            [ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "Parity")],
            [
                NotGenerated.Accepted(
                    "single-save-several-new-roots",
                    "単数の SaveAsync で、根の新しい顧客を 2 つ以上保存する組",
                    "単数のグラフ保存は根を 1 つだけ取る（生成の Repository の SaveAsync(entity, …)）。根の新しい顧客を 2 つ以上にするのは複数形の SaveAsync で、軸の保存の経路の水準 save-many が作る",
                    Parity
                ),
                NotGenerated.Accepted(
                    "one-row-different-parents",
                    "新しい行が 1 つで、置き場所が別の親の組",
                    "1 つの行の親は 1 つなので、別の親は新しい行が 2 つ以上の組にしか無い",
                    Parity
                ),
                NotGenerated.Accepted(
                    "new-customers-as-referred-children",
                    "新しい顧客を、既存の顧客の紹介先（顧客の自己参照の子のコレクション）として足す置き場所",
                    "グラフ保存は子の行を根の行と同じ追跡の腕（新しい行を Added で追跡へ入れる TrackNode）へ通し、腕は行の型と親子の道に依らない。同じ型の新しい行が 2 つ以上の形は根の顧客の複数形の保存が作り、置き場所の軸（同じ親・別の親）は注文と明細の水準で交差させる",
                    Parity
                ),
                NotGenerated.Accepted(
                    "contacts",
                    "連絡先（顧客を 5 つの外部キーで参照する子）の新しい行のキーをフックが決める組",
                    "連絡先の新しい行も注文・明細と同じ追跡の腕を通る。連絡先が持つ外部キーの参照アクションの違いはキーの決め方の分岐を変えないので、型の軸は根の顧客・注文・明細の 3 つにする",
                    Parity
                ),
            ]
        );
}
