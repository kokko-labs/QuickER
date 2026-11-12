using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の乱数の保存の生成器（<c>BackendParitySqlServerSaveGenerator</c>）が作らない形の宣言
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class BackendParitySqlServerSaveNotGenerated
{
    private const string Cases = "`BackendParitySqlServerRuntimeTests`";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "BackendParitySqlServerSaveGenerator",
            [ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "ParitySqlServerSaves")],
            [
                NotGenerated.Accepted(
                    "delete-gone-row",
                    "他者が消した行の削除",
                    "行の無い削除の戻り値は実装先で割れることを受け入れている（docs）。次元表の網羅しない組にも受け入れとして載る",
                    "`GeneratedRuntime/Parity`（行 `crud-update-missing-and-delete-twice`）"
                ),
                NotGenerated.Accepted(
                    "referenced-code-change",
                    "部品に参照された code の変更と、部品を持つ行の他者による削除",
                    "外部キーで保存の全体が断られ、ほかの行を比べなくなる。参照された代替キーの更新は決まった形のケースが受け持つ。生成器は、code を変える kit の部品を必ず付け替え（後回しの kit の部品だけ）、1 段目で code を手放す kit には部品を持たせない（first-stage-releaser-parts）",
                    Cases
                        + " の行 `alternate-key-referenced-update` / `alternate-key-referenced-graph-save`"
                ),
                NotGenerated.Accepted(
                    "inserted-taker-before-releaser",
                    "挿入する行（新しい kit・insertWhenUpdateMissing で挿入へ切り替わる消えた kit）を、まだ code を持つ手放す行より前に渡し、その code を取る保存",
                    "実装先で結果が割れることを受け入れている（docs のグラフ保存の書く順と EF Core モードの代替キーの段落：QuickER 版はグラフの順に書いて挿入が一意制約に断られ、EF Core 版は変わった代替キーを先に書いて通す）。生成器は挿入する取る行を手放す行の直後へ移す",
                    Cases
                        + " の行 `alternate-key-new-takes-released-taker-first` / `alternate-key-gone-insert-takes-released-taker-first`"
                ),
                NotGenerated.Accepted(
                    "first-stage-releaser-parts",
                    "1 段目で code を手放す kit（code を変える・手放す code を取る・入れ替える kit）の部品の付け替え・単独の削除・残す形",
                    "付け替えると、EF Core 版は代替キーを先に書いて外部キーに断られ、QuickER 版は付け替え先を先に渡せば通る（docs のグラフ保存の書く順と EF Core モードの代替キーの段落に書いた受け入れた差・H2-D1）。単独で消すか残すと、どの実装先でも参照された code の変更として断られる。次元表の網羅しない組（H2-D1）にも単独の削除が受け入れとして載る",
                    Cases
                        + " の行 `alternate-key-part-moved-off-released-taker-first` / `alternate-key-part-moved-off-released-releaser-first`"
                ),
                NotGenerated.Accepted(
                    "gone-part-in-refused-save",
                    "どの実装先でも断られる順（値を取る kit が、その値をまだ持つ kit より前に書かれるか、持つ kit を Before のフックが飛ばすか、code を入れ替える kit がある）を含む保存に、他者が消した部品を置く形",
                    "QuickER 版はグラフの順で前にある部品の競合（NotFound）を、EF Core 版は先に書く代替キーの断りを報告する（docs に書いた受け入れた差・H2-D2）。どちらの保存も何も書かず、順を直すまで通らない。入れ替える kit への他者が消した部品は、次元表の網羅しない組（H2-D2）にも受け入れとして載る",
                    Cases + " の行 `alternate-key-gone-part-then-taker-first`"
                ),
                NotGenerated.Accepted(
                    "gone-part-delete",
                    "他者が消した部品の単独の削除と、消す kit のカスケードで消す部品の他者による削除",
                    "行の無い削除の戻り値は実装先で割れることを受け入れている（kits の行の delete-gone-row と同じ・docs）。次元表の網羅しない組（F4）にも受け入れとして載る",
                    "`GeneratedRuntime/Parity`（行 `crud-update-missing-and-delete-twice`）"
                ),
                NotGenerated.Accepted(
                    "part-stale",
                    "部品を読んだ後に他者が更新する（別の kit へ付け替える）形",
                    "kit_parts は行バージョンを持たないので、他者の更新はどの実装先でも版の照合に当たらず、保存は後勝ちで書く。外部キーの元の値を読む経路は、消す kit から付け替える部品が乱数の保存で通る",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "skipped-kitless-target",
                    "Before のフックが飛ばす kit のうち、code を変えるか新しい kit を、付け替える部品・新しい部品の書く先にする形",
                    "飛ばした kit は書かれないので、部品の外部キーがどの実装先でも断られて保存の全体が止まり、ほかの行を比べなくなる。次元表の網羅しない組（H2）にも受け入れとして載る。code を変えない kit を飛ばす形と、飛ばす後回しの kit の部品の単独の削除は作る",
                    Cases + " の行 `hook-skip-parent-child-fk`"
                ),
                NotGenerated.Accepted(
                    "part-to-taker-of-its-code",
                    "消す kit か code を手放す kit の部品を、その code を取る kit へ外部キーの値のまま付け替える形",
                    "元の持ち主が値を手放すか消える間も部品の参照が残るので、どの実装先でも断られる（docs のグラフ保存の「1 回では保存できない形」を、新しい行を更新する行に置き換えた形。3 実装先とも断ることを実測で確かめた）",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
