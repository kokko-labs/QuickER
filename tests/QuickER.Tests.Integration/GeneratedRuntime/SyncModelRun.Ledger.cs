using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 台帳「端末の持ち分」と、実行 1 回ごとの照合（署名は <c>5/</c> の族）。
/// </summary>
/// <remarks>
/// <para>
/// 台帳と照合 1〜3、照合 4 のモデルの観測は、書き込みの手の履歴・実行の前後の端末とサーバーの DB の内容・実行の公開の結果
/// （競合・<see cref="SyncLocalRowChange"/>）だけから作る。端末の同期の状態の面（<see cref="IModelDeviceSyncState"/>）・
/// 端末の状態の表は読まない（エンジンの自己申告と一緒に間違えないため）。照合 4 が状態の面から読むのは、比べる相手の
/// <see cref="ModelPendingRow.ServerKnowsRow"/> と、比べる行を選ぶための「端末に行が無く削除の未送信がある行」だけ。
/// </para>
/// <para>
/// 持ち分は（端末・表・キー）→ その端末が自分で書いた最後の内容（削除は null）。品目の blob は行と別の品目として同じ規則で
/// 持つ（blob は書いた列だけを送る）。blob の品目は、その品目の行がある限り読み出しにある（空の blob〔長さ 0〕と NULL の blob も
/// 内容のある品目で、削除ではない）。blob の内容が削除（null）になるのは行の削除だけなので、削除の blob の持ち分は行の持ち分に
/// 従う：その品目の行の持ち分が下りたら、どの経路でも一緒に下ろす。台帳の見張りとして、削除の blob の持ち分があるのに、その品目の
/// 行の持ち分が削除でない（無い、または内容がある）状態を <c>5/ledger-invariant:model_items:payload</c> で名指しする（台帳を変える
/// 手・実行の終わりに確かめる）。
/// </para>
/// <list type="bullet">
///   <item>立てる：記録つきの書き込みの手の前後の差分（割り込みの書き込みも同じ経路）。blob の書き込みは行の持ち分も立てる
///   （blob の書き込みも行の変更として記録され、エンジンは行を丸ごと送る）。追跡しない行への最初の記録つきの書き込みは、行と blob の両方を立てる（挿入として全列を送る）。
///   実行で立てるのは 1 つの形だけ：その実行でその端末が、サーバーに無かった品目の行（行の持ち分あり）を入れ、blob がまだサーバーに
///   届いていないとき、端末の blob を持ち分として立てる（挿入は全列を送る）。</item>
///   <item>成功した記録つきの書き込みの手（失敗・注入した失敗・フックの飛ばしの無い手）は、内容が変わらなくても、手が名指しする
///   1 行（blob の書き込みは行と blob の品目）の持ち分を今の内容で立てる（記録は内容に依らず残り、エンジンが送る）。</item>
///   <item>置き換える：記録しない書き込みは、持ち分があるキーの内容だけを置き換え、無いキーには立てない。</item>
///   <item>下ろす：(1) その端末の実行（ダウンロードだけの実行を除く）の後、その実行の範囲の表で端末の行とサーバーの行が同じ内容に
///   なった（届いた・戻された）。例外で終わった実行でも、下ろすのは端末の行とサーバーの行が同じ内容になったときだけ（届かずに
///   例外になった行の持ち分は残る）。同じ端末がその品目の内容のある（削除でない）blob の持ち分を持つ間は、行が持ち分の内容で
///   同じになった（届いた）なら、その品目の行の持ち分を下ろさない（blob の書き込みは行も送る。blob の持ち分が下りた実行の
///   終わりに、行も同じ内容なら一緒に下ろす）。行が持ち分と違う内容で同じになった（戻された）なら、行の持ち分だけを
///   下ろす（blob は端末の内容のまま届いていないので、blob の持ち分は残す）。その実行がそのキー（blob の品目はその行）の競合を
///   報告したときは、同じ内容になっても下ろさない（版で比べるので内容が同じでも競合になり、Collect は記録を残す）。
///   (2) 実行が <see cref="SyncLocalRowChange"/> でそのキーを報告した。(3) 照合 3 が判定した（内容が端末からもサーバーからも
///   消えた）。(4) 行が「あった」と言えない持ち分
///   （立てたときに端末に行が無く、その後のどの実行の後にも端末とサーバーの両方に同じ内容の行があると観測していない）が削除で
///   終わった（差し引き何も書いていない。blob の品目も行と一緒に下ろす）。「あった」の決め方は <see cref="Holding"/> の
///   Existed。(5) 実行の間の割り込みの書き込みで、書き手がサーバーの他者のとき、書く直前のサーバーのそのキーの内容が、ある端末の
///   そのキーの持ち分の内容と同じなら（届いた後の上書き）、その端末のその持ち分を下ろす（blob の品目も同じ規則）。</item>
///   <item>控え「記録が残っているかもしれないキー」（端末・表・キー。行のキー）：持ち分を下ろしたが、エンジンの記録は残りうるキー。
///   立てるのは、下ろす規則 (4) で下ろしたとき（差し引きは何も書いていないが、挿入と削除の記録は残る）と、例外で終わった実行で
///   下ろす規則 (1) が下ろしたとき（エンジンが記録を送る前に止まっていることがある）。外すのは、その端末の、例外の無い、
///   ダウンロードだけでない実行（洗い替えを含む）で、その表が範囲にあり、そのキーの競合が報告されず、その表が打ち切られなかった
///   とき・その実行が LocalRowChanges でそのキーを報告したとき。照合 8 だけが使う。</item>
///   <item>控え「例外で終わった実行が持ち込んだ行」（端末・表・キー。行のキー）：例外で終わったその端末の実行の前に端末に行が無く、
///   後にサーバーと同じ内容の行があり、その実行でその行の blob の読み出しの失敗が当たったキー（実行の前後の DB と、失敗の注入の
///   控え <see cref="SyncModelFaults.BlobReadHitsSince"/> で決める。行を書いた後・受理記録を書く前に blob のコピーの失敗で例外に
///   なる窓の行だけで、例外の前に普通に降りた行は入れない）。外すのは、例外の無いその端末の実行の間に端末のその
///   行の内容が変わったとき（降ろし直し・読み直し）・その端末の洗い替え（例外なし・範囲の表）・その実行が LocalRowChanges で
///   そのキーを報告したとき。照合 3 だけが使う。</item>
/// </list>
/// <list type="number">
///   <item><c>5/stale-overwrite:{表}</c>：ダウンロード以外の実行で、サーバーのキーの内容が実行の前後で変わり、後の内容が
///   実行の前の端末の内容と同じ（どちらも行が無い場合を含む）なのに、端末がそのキーの持ち分を持たず、実行の間にそのキーへ
///   書いた手も無い。外すのは、端末が持ち分（削除）を持つ行を親に辿れる子の行（サーバーのカスケード）と、実行の前のサーバーに
///   その品目の行が無く端末がその行の持ち分を持つ blob（サーバーに無い行の挿入・作り直しは全列を送る）。宣言が偽のエンジンで、
///   そのキー（blob の品目はその行）が、例外で終わった ServerWins の実行が戻した行の控えにあれば、違反にせず別の種類
///   （<see cref="ResendAfterThrownServerWinsCheck"/>）で数える。持ち越しを宣言しないエンジンでは、例外で終わった ServerWins の
///   実行が戻した行の記録が残り、後の実行がその行を端末の編集として送る。控えは、宣言が偽のエンジンの例外で終わった ServerWins の
///   実行（SyncAsync・単独の UploadAsync）の始めに持ち分があり、実行の後に範囲の表で端末とサーバーが持ち分と違う同じ内容になった
///   キーのうち、行を書いた後で例外になる窓（照合 3 の数えるだけの窓と同じ：その実行でその行の blob の読み出しの失敗が当たった／
///   SyncAsync で、端末が最後に見た後にほかの書き手がサーバーのその行を変えていた）に当たるものに立て、その表を範囲にした、例外の無い、その端末の実行（ダウンロードだけの実行を除く）の
///   後に端末とサーバーのその行が同じ内容になったら外す（記録つきの書き込みでは外さない）。宣言が真のエンジンは控えを使わない。
///   乱数の種で 1 回以上確かめる照合には入れない。</item>
///   <item><c>5/carry:overwrote:{表}</c>・<c>5/carry:edit-lost:{表}</c>：ServerWins の実行（ダウンロード以外）の始めに持ち分が
///   あり、実行の後も端末の行がサーバーと違い、実行の間に端末の行が変わらず、そのキーの競合も報告されなかった行を、戻せなかった
///   行の決定とする（<see cref="IModelDeviceSync.CarriesServerWinsDecision"/> に依らず作る）。宣言が真のエンジンだけ、生きている
///   決定の内容をサーバーへ書くことを違反にし、果たされた決定をその行の消失なし・根拠のない競合の期待から外す。宣言が偽の
///   エンジンは同じ形を数えるだけで、期待に触れない。決定の後の編集が報告なしに端末からもサーバーからも消えるのは、宣言に依らず
///   違反。宣言が真のエンジンでは決定を行の単位で持つ：行の決定が決定の後のその行の編集で外れたら同じ行の blob の決定も外し、
///   決定の後に端末が記録つきの書き込みで blob を書いた品目は行の決定も外し（エンジンは後の書き込みが blob だけでも行を blob と
///   一緒に送る。blob の内容では決めず、エンジンが写した blob では外さない）、
///   実行の間に行（blob を除く列）が変わった品目には blob の決定を作らない。宣言が偽のエンジンでは台帳の作り方を変えない。
///   例外で終わった ServerWins の実行は決定を作らない。その実行の後の決定の扱いは、エンジンが持ち越しを宣言するときに、
///   そのエンジンの振る舞いに合わせて決める。</item>
///   <item><c>5/local-discard:{表}</c>：実行の始めに持ち分があり、実行の後にその内容が端末にもサーバーにも無く、その消え方が
///   許されていない。許されるのは、その実行がそのキーを <see cref="SyncLocalRowChange"/> で報告した・Force の洗い替え・単独の
///   UploadAsync の ServerWins でその表が範囲（SyncAsync の ServerWins は報告で決める。docs の LocalRowChanges の説明＝実行が
///   変更を捨てたローカルの行を名指しし、入れるのは SyncAsync だけ）・実行の間にそのキーへ書いた手・（宣言が真のエンジン）
///   果たされた決定。競合の報告だけでは許さない（docs の「競合」の節＝Collect は競合を報告し、ローカルの行はその場に残る）。
///   戻された（端末の行とサーバーの行が持ち分と違う同じ内容になった）形は、例外で終わった実行・ダウンロードだけの実行でも判定する
///   （実行の前後の DB で分かる）。それ以外の消え方は、例外で終わり結果の一覧を運ばなかった実行では判定しない。ただし例外で
///   終わった SyncAsync が一覧を運ばないときは、空の一覧として判定する（docs の LocalRowChanges の説明＝SyncAsync が例外で
///   終わっても、それまでに変えた行は例外の Data に入る。運ばないのは変えた行が無いとき）。例外で終わった ServerWins の同期が戻した行は、持ち越しを宣言しない
///   エンジンでは一覧に載らないことがあるので、宣言が偽のエンジンで、例外で終わった ServerWins の SyncAsync が範囲の表の行を報告
///   なしに戻した形のうち、行を書いた後で例外になる 2 つの窓（決着の側の blob のコピーの失敗・ダウンロードの側のほかの書き手の
///   変更）に当たるものは、違反にせず別の種類（<see cref="LocalDiscardUnreportedInThrownServerWinsCheck"/>）で数える（窓に当たらない
///   形・宣言が真のエンジン・例外で終わっていない実行は違反）。例外で終わった ServerWins の実行（ダウンロード以外）が
///   戻せなかった行（決定と同じ条件）も控える。宣言が偽のエンジンで、実行の始めにそのキーが戻せなかった行（決定か、例外で
///   終わった実行の行）だった形は、違反にせず別の種類（<see cref="LocalDiscardAfterUnreportedServerWinsCheck"/>）で数える。
///   宣言が真のエンジンで、例外で終わった実行の行だった形も、違反にせず別の種類
///   （<see cref="LocalDiscardAfterThrownServerWinsCheck"/>）で数える。どちらも消失なしの期待には触れない。宣言が偽のエンジンで、
///   控え「例外で終わった実行が持ち込んだ行」にあるキーの削除の持ち分が報告なしに消えた（端末に行が戻った）形も、違反にせず別の
///   種類（<see cref="LocalDiscardOfDeleteOnRowBroughtInByThrownRunCheck"/>）で数える。持ち込んだ行はミラー版も受理記録も
///   持たないので、その削除の記録はサーバーに一度も届いていない行の削除として黙って捨てられ、後の同期がサーバーの行を戻す。
///   宣言が真のエンジン・削除でない持ち分・控えに無いキーは違反のまま。</item>
///   <item><c>5/facet:server-knows:{表}</c>：状態の面の「サーバーが知っているか」を、モデルの観測と比べる。観測は、端末の実行の
///   後にそのキーの行が端末とサーバーの両方にあって内容（blob を除く列）が同じなら知っている、未送信を片付けうる実行（ダウンロード
///   だけでない同期と洗い替え）が例外なく終わった後に、その実行の範囲の表で両方に無く端末がそのキーの持ち分を持たなければ知らない、
///   それ以外は前の答えを保つ。例外で終わった実行の後の観測・実行の間に書かれたキーの観測で決まった答えと、
///   エンジンの記録を直接作る・変える手が触れたキーは、決められないとして数える。</item>
///   <item><c>5/unreported-overwrite:{表}</c>：版で守る実行（Versioned で Collect か ServerWins の、ダウンロード以外の同期）が、
///   サーバーのキーを実行の前の端末の内容（どちらも行が無い場合を含む。blob が行ごと消えた形は行の品目が見る）へ変え、端末がその
///   キーの持ち分を持ち、実行の間にそのキーへ割り込みの書き込みが無く、そのキーを最後に変えた書き手がその端末でなく、その番号が
///   「端末がそのキーのサーバーの内容を最後に見た番号」より後で、その実行がそのキー（blob の品目はその行）の競合を報告していない。
///   後勝ち・LocalWins の実行（上書きが約束どおり）・ダウンロードだけの実行・洗い替えは対象の外。台帳の通し番号は書き込みの手と
///   実行の終わりごとに 1 つ進める。サーバーのキーを最後に変えた書き手は、サーバーの他者の書き込みの手（初期データを含む）ならその
///   番号でサーバーの他者、端末の実行の間に変わったキーならその実行の終わりの番号でその端末（割り込みの書き込みが変えたキーは
///   割り込みの書き手のまま）。端末がキーを最後に見た番号は、その端末の実行（ダウンロードだけ・洗い替え・例外で終わった実行と、
///   最初の形を含む）の終わりに、端末とサーバーのそのキーの内容が同じ（どちらも無いを含む）ならその番号。
///   外す形は 2 つ。(a) 実行の前のサーバーに行が無く、端末の持ち分が「行があった」と言えない：ほかの書き手が消したキーへの新しい
///   行の挿入で、上書きではない。「行があった」持ち分（一度サーバーとそろえた行をほかの書き手が消し、端末の編集が競合なしに
///   作り直す形）は違反のまま。(b) blob の品目は、その blob の持ち分を書いたときに端末がその品目の行を最後に見ていた番号も、
///   blob を見たに数える：行の版はほかの書き手の blob の書き込みを含むので、その版の行を降ろした後に書いた blob は新しい版に
///   基づく編集。blob を書いた後で行を見ても数えない（その blob は古い編集のままで、上書きに当たる）。端末がその品目の行を
///   自分の内容でサーバーへ届けた実行（blob の送信だけが失敗した）の番号も、同じく blob を見たに数える（勝ち負けは行の単位で
///   決まり、残った blob は届けた行の続き。届けた後にほかの書き手が書いた内容の上書きは、上書きに当たる）。宣言が偽のエンジンで、
///   そのキーが例外で終わった ServerWins の実行の戻せなかった行（決定と同じ条件）なら、違反にせず別の種類
///   （<see cref="UnreportedOverwriteAfterThrownServerWinsCheck"/>）で数える。</item>
///   <item><c>5/out-of-scope-write:{表}</c>：実行が、その範囲の外の表（除外した表・Versioned のときの版なしの表）のサーバーの行
///   （blob の品目も）を変えた。ダウンロードだけの実行と洗い替えは、サーバーのどの行を変えても違反。出典と前提（サーバーの外部キーは
///   すべて NO ACTION）は <see cref="CheckServerWritesInScope"/>。</item>
///   <item><c>5/serverwins-wrote:{表}</c>：Versioned で ServerWins の実行（ダウンロード以外）が、サーバーの行を変えた。出典は
///   <see cref="CheckServerWritesInScope"/>。</item>
///   <item><c>5/unowned-send:{表}</c>：実行が、追加・更新の競合か DiscardedByServerWins を報告したキーに、実行の始めに端末の
///   持ち分が無かった。実行の始めの控え「記録が残っているかもしれないキー」にあるキーは持ち分ありとみなす（端末が一度も
///   書いていないキーの報告は違反のまま）。実行の間の割り込みのキーと、エンジンの記録を直接作る・変える手が触れたキーは外す。
///   例外で終わった ServerWins の実行が戻した行の控えにあるキーは、照合 1 と同じく別の種類で数える。</item>
/// </list>
/// </remarks>
internal sealed partial class SyncModelRun
{
    /// <summary>照合の種類：古い内容でサーバーの変更を上書きしていないか（確かめた＝実行がサーバーを端末の前の内容へ変えた）</summary>
    public const string StaleOverwriteCheck = "stale-overwrite";

    /// <summary>照合の種類：ServerWins の実行が戻せなかった行の決定を作った（違反は数えない）</summary>
    public const string CarryHeldCheck = "carry-held";

    /// <summary>
    /// 照合の種類：生きている決定の内容を後の実行がサーバーへ書いた（宣言が真のエンジンだけ違反。偽のエンジンは数えるだけ）
    /// </summary>
    public const string CarryOverwroteCheck = "carry-overwrote";

    /// <summary>照合の種類：決定が果たされた（後の実行で端末の行がサーバーの行になった。違反は数えない）</summary>
    public const string CarryFulfilledCheck = "carry-fulfilled";

    /// <summary>照合の種類：決定の後の編集が端末からもサーバーからも消えた</summary>
    public const string CarryEditLostCheck = "carry-edit-lost";

    /// <summary>照合の種類：持ち分の内容が端末からもサーバーからも消えた</summary>
    public const string LocalDiscardCheck = "local-discard";

    /// <summary>
    /// 照合の種類：宣言が偽のエンジンで、戻せなかった行の決定があったキーの持ち分の内容が、後の実行で報告なしに消えた（違反は
    /// 数えない）
    /// </summary>
    public const string LocalDiscardAfterUnreportedServerWinsCheck =
        "local-discard-after-unreported-serverwins";

    /// <summary>
    /// 照合の種類：宣言が真のエンジンで、例外で終わった ServerWins の実行が戻せなかった行の持ち分の内容が、後の実行で報告なしに
    /// 消えた（違反は数えない）。例外で送る前に止まった行が、後で普通に届いた（消えた）形も含む粗い目安
    /// </summary>
    public const string LocalDiscardAfterThrownServerWinsCheck =
        "local-discard-after-thrown-serverwins";

    /// <summary>
    /// 照合の種類：版で守る実行が、端末の持ち分の内容で、端末が最後に見た後にほかの書き手がサーバーへ書いた内容を、競合を報告せずに
    /// 上書きした（確かめた＝実行が端末の持ち分の内容をサーバーへ書いた件数）
    /// </summary>
    public const string UnreportedOverwriteCheck = "unreported-overwrite";

    /// <summary>
    /// 照合の種類：宣言が偽のエンジンで、例外で終わった ServerWins の実行が戻せなかった内容による、報告の無い上書き（違反は数えない）。
    /// 例外で送る前に止まった行が、後で普通に届いた形も含む粗い目安
    /// </summary>
    public const string UnreportedOverwriteAfterThrownServerWinsCheck =
        "unreported-overwrite-after-thrown-serverwins";

    /// <summary>
    /// 照合の種類：実行がその範囲の外のサーバーの行を変えていないか（確かめた＝実行 1 回。違反は範囲の外の行を変えた実行）
    /// </summary>
    public const string OutOfScopeWriteCheck = "out-of-scope-write";

    /// <summary>
    /// 照合の種類：Versioned＋ServerWins の実行がサーバーを変えていないか（確かめた＝その実行 1 回。違反はサーバーを変えた実行）
    /// </summary>
    public const string ServerWinsWroteCheck = "serverwins-wrote";

    /// <summary>
    /// 照合の種類：実行が報告した追加・更新の競合と DiscardedByServerWins のキーに、端末の持ち分があったか（確かめた＝報告
    /// 1 件。違反は持ち分が無かった報告）
    /// </summary>
    public const string UnownedSendCheck = "unowned-send";

    /// <summary>
    /// 照合の種類：宣言が偽のエンジンで、例外で終わった ServerWins の SyncAsync が範囲の表の行を報告なしに戻した（違反は数えない）。
    /// 持ち越しを宣言しないエンジンが、例外で終わった ServerWins の同期で報告なしに行を戻すのは、行を書いた後で例外になる 2 つの
    /// 場合だけ：(a) 決着の側＝捨てる記録の行をサーバーから読み直して書いた後、その品目の blob のコピーが失敗した（この実行でこの
    /// 端末のその品目の BlobDownload の失敗が当たったことを、失敗の注入の控え <see cref="SyncModelFaults.BlobReadHitsSince"/> で見る）、
    /// (b) ダウンロードの側＝端末がそのキーを最後に見た後にほかの書き手がサーバーのその行を変えていて、ダウンロードがその行を書いた
    /// 後で、決着の前に例外になった（台帳の、サーバーのそのキーを最後に変えた書き手と番号・端末が最後に見た番号の、実行の始めの値で
    /// 見る）。どちらでもない形は違反
    /// </summary>
    public const string LocalDiscardUnreportedInThrownServerWinsCheck =
        "local-discard-unreported-in-thrown-serverwins";

    /// <summary>
    /// 照合の種類：宣言が偽のエンジンで、例外で終わった ServerWins の実行が戻した行を、その端末の後の実行が端末の編集として
    /// 送った（照合 1 の <c>5/stale-overwrite</c> と照合 8 の <c>5/unowned-send</c> に当たる形。違反は数えない）。戻した行の記録が
    /// 残るので、後の実行がその行（戻した古い写し）を送る
    /// </summary>
    public const string ResendAfterThrownServerWinsCheck = "resend-after-thrown-serverwins";

    /// <summary>
    /// 照合の種類：宣言が偽のエンジンで、例外で終わった実行が持ち込んだ行の削除の持ち分が、後の実行で報告なしに消えた（端末に行が
    /// 戻った。違反は数えない）。持ち込んだ行はミラー版も受理記録も持たないので、その削除の記録はサーバーに一度も届いていない行の
    /// 削除として黙って捨てられ、後の実行がサーバーの行を端末へ戻す
    /// </summary>
    public const string LocalDiscardOfDeleteOnRowBroughtInByThrownRunCheck =
        "local-discard-of-delete-on-row-brought-in-by-thrown-run";

    /// <summary>照合の種類：持ち分の内容が消えたが、例外で終わり結果を運ばなかった実行なので判定しなかった（違反は数えない）</summary>
    public const string LocalDiscardUnjudgedCheck = "local-discard-unjudged";

    /// <summary>照合の種類：状態の面の「サーバーが知っているか」とモデルの観測を比べた（決められないものを除く）</summary>
    public const string FacetServerKnowsCheck = "facet-server-knows";

    /// <summary>照合の種類：状態の面と比べようとしたが、モデルの答えを決められなかった（違反は数えない）</summary>
    public const string FacetServerKnowsUndeterminedCheck = "facet-server-knows-undetermined";

    /// <summary>
    /// 照合の種類：台帳の見張り（内容が削除の blob の持ち分の品目の行の持ち分が削除でない）が破れた（違反だけを数える）
    /// </summary>
    public const string LedgerInvariantCheck = "ledger-invariant";

    /// <summary>
    /// 端末のエンジンの宣言（<see cref="IModelDeviceSync.CarriesServerWinsDecision"/>）が決まった値のときだけ件数が出る照合の
    /// 種類と、その値（ここに無い種類はどちらの宣言でも数える）。宣言が違うエンジンでは定義から 0 件なので、空振りの守りから外す
    /// （<see cref="GuardedRunChecksFor"/>）
    /// </summary>
    public static IReadOnlyDictionary<string, bool> RunCheckCountedOnlyWhenCarrying { get; } =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            // 真のエンジンで起きれば違反（1 件以上を求める守りと両立しない）。偽のエンジンでは数えるだけの件数
            [CarryOverwroteCheck] = false,
            [LocalDiscardAfterUnreportedServerWinsCheck] = false,
            [LocalDiscardAfterThrownServerWinsCheck] = true,
            [UnreportedOverwriteAfterThrownServerWinsCheck] = false,
            [LocalDiscardUnreportedInThrownServerWinsCheck] = false,
            [ResendAfterThrownServerWinsCheck] = false,
            [LocalDiscardOfDeleteOnRowBroughtInByThrownRunCheck] = false,
        };

    /// <summary>
    /// 空振りの守りに掛ける照合の並びを、端末のエンジンの宣言から決める（<paramref name="checks"/> から、その宣言のエンジンでは
    /// 定義から件数の出ない種類＝<see cref="RunCheckCountedOnlyWhenCarrying"/> を除く。並びの順は保つ）
    /// </summary>
    /// <param name="checks">守りに掛けたい照合の並び</param>
    /// <param name="carriesServerWinsDecision">流した端末のエンジンの宣言</param>
    public static IReadOnlyList<string> GuardedRunChecksFor(
        IReadOnlyList<string> checks,
        bool carriesServerWinsDecision
    ) =>
        [
            .. checks.Where(kind =>
                !RunCheckCountedOnlyWhenCarrying.TryGetValue(kind, out var countedWhen)
                || countedWhen == carriesServerWinsDecision
            ),
        ];

    /// <summary>照合の種類（<see cref="RunChecks"/> の鍵）</summary>
    public static IReadOnlyList<string> RunCheckKinds { get; } =
    [
        StaleOverwriteCheck,
        CarryHeldCheck,
        CarryOverwroteCheck,
        CarryFulfilledCheck,
        CarryEditLostCheck,
        LocalDiscardCheck,
        LocalDiscardAfterUnreportedServerWinsCheck,
        LocalDiscardAfterThrownServerWinsCheck,
        UnreportedOverwriteCheck,
        UnreportedOverwriteAfterThrownServerWinsCheck,
        OutOfScopeWriteCheck,
        ServerWinsWroteCheck,
        UnownedSendCheck,
        LocalDiscardUnreportedInThrownServerWinsCheck,
        ResendAfterThrownServerWinsCheck,
        LocalDiscardOfDeleteOnRowBroughtInByThrownRunCheck,
        LocalDiscardUnjudgedCheck,
        FacetServerKnowsCheck,
        FacetServerKnowsUndeterminedCheck,
        LedgerInvariantCheck,
    ];

    /// <summary>照合ごとの、確かめた件数と違反の件数（空振りしていないかを出力で確かめる）</summary>
    public IReadOnlyDictionary<string, (int Checked, int Violations)> RunChecks => _runChecks;

    private readonly Dictionary<string, (int Checked, int Violations)> _runChecks = new(
        StringComparer.Ordinal
    );

    /// <summary>実行ごとの照合の違反（静止化の成否に依らず最後に足す＝根拠の違反と同じ扱い）</summary>
    private readonly List<ModelFailure> _runFailures = [];

    /// <summary>持ち分 1 つ</summary>
    /// <param name="Content">その端末が自分で書いた最後の内容（削除は null）</param>
    /// <param name="Existed">
    /// 行が「あった」か（偽のまま削除で終わった持ち分は差し引き何も書いていないので下ろす）。立てたときに端末にその行（blob の
    /// 品目は blob）があれば真。その後は、その端末の実行の後の観測で真にする（下の remarks）
    /// </param>
    /// <remarks>
    /// 実行の後に端末とサーバーの両方にそのキーの行（blob の品目はその品目の行）があり内容が同じと観測したら真にする。実行の後の
    /// 観測そのものが取れないとき（その実行の間にそのキーへ割り込みの書き込みがあった）は真にする。例外で終わった実行でも、実行の
    /// 後の端末とサーバーの DB は読めるので、観測で決める（同じ内容の行が両方にあれば真・無ければ偽のまま）。版なしの表（ラベル）は
    /// 削除を無条件に送るので、実行をまたいだら真にする
    /// </remarks>
    private readonly record struct Holding(string? Content, bool Existed);

    /// <summary>台帳「端末の持ち分」：（端末・表・キー）→ 持ち分</summary>
    private readonly Dictionary<(ModelActor Device, string Table, string Key), Holding> _holdings =
    [];

    /// <summary>いま流している実行の間に、割り込みの書き込みが変えたキー（書き手を問わない）</summary>
    private readonly HashSet<(string Table, string Key)> _writtenDuringRun = [];

    /// <summary>割り込みの書き込みを流している間（書き込みが変えたキーを <see cref="_writtenDuringRun"/> へ入れる）</summary>
    private bool _interrupting;

    /// <summary>
    /// ServerWins の実行が戻せなかった行の決定：（端末・表・キー）→ その実行の直後の端末の内容。宣言に依らず作る（偽の
    /// エンジンでは数えるのと、決定の後の編集が消えないかを見るのにだけ使う）
    /// </summary>
    private readonly Dictionary<(ModelActor Device, string Table, string Key), string?> _carried =
    [];

    /// <summary>
    /// 品目の行の決定を作った後に、その端末が記録つきの書き込みで blob を書いた品目：（端末・品目の表・キー）。宣言が真の
    /// エンジンは決定を行の単位で持つので、決定の後に端末が blob を書いた行の行の決定を外すのに使う
    /// （<see cref="CheckCarried"/>）。端末の blob の持ち分を立てる所（<see cref="NoteWrite"/>）で足し、行の決定を作るときに
    /// 消す。blob の内容では決めない（エンジンがサーバーの blob を端末へ写しても、端末の書き込みではないので決定は外れない）
    /// </summary>
    private readonly HashSet<(ModelActor Device, string Table, string Key)> _blobWrittenAfterHeld =
    [];

    /// <summary>
    /// 例外で終わった ServerWins の実行（ダウンロード以外）が戻せなかった行：（端末・表・キー）→ その実行の直後の端末の内容。
    /// 決定は作らず、後の実行の照合 3 で数えるだけに使う（持ち分が無くなったら外す）
    /// </summary>
    private readonly Dictionary<
        (ModelActor Device, string Table, string Key),
        string?
    > _thrownServerWins = [];

    /// <summary>
    /// 宣言が偽のエンジンで、例外で終わった ServerWins の実行（ダウンロード以外）が戻した行：（端末・表・キー。blob の品目は
    /// その行のキー）。実行の始めに持ち分があり、実行の後に端末とサーバーが持ち分と違う同じ内容になったキーのうち、行を書いた後で
    /// 例外になる窓（その実行でその行の blob の読み出しの失敗が当たった／SyncAsync で、端末が最後に見た後にほかの書き手が
    /// サーバーのその行を変えていた）に当たるもの。その端末の後の実行の
    /// 照合 1・8 が数えるだけに使う。その表を範囲にした、例外の無い、その端末の実行（ダウンロードだけの実行を除く）の後に、端末と
    /// サーバーのその行が同じ内容になったら外す。記録つきの書き込みでは外さない（残った古い記録と一緒に送られる）
    /// </summary>
    private readonly HashSet<(
        ModelActor Device,
        string Table,
        string Key
    )> _restoredByThrownServerWins = [];

    /// <summary>
    /// 記録が残っているかもしれないキー：（端末・表・キー。行のキー）。持ち分を下ろしたが、エンジンの記録は残りうるキー（下ろす
    /// 規則 (4) で下ろした・例外で終わった実行で下ろす規則 (1) が下ろした）。照合 8 だけが「持ち分あり」とみなすのに使う
    /// </summary>
    private readonly HashSet<(ModelActor Device, string Table, string Key)> _mayHoldRecord = [];

    /// <summary>
    /// 例外で終わった実行が持ち込んだ行：（端末・表・キー。行のキー）。例外で終わったその端末の実行の前に端末に行が無く、後に
    /// サーバーと同じ内容の行があり、その実行でその行の blob の読み出しの失敗が当たったキー。照合 3 が数えるだけに使う
    /// </summary>
    private readonly HashSet<(ModelActor Device, string Table, string Key)> _broughtInByThrownRun =
    [];

    /// <summary>果たされた決定（宣言が真のエンジンだけ。根拠のない競合の判定から外す）</summary>
    private readonly HashSet<(ModelActor Device, string Table, string Key)> _carryFulfilled = [];

    /// <summary>
    /// モデルの観測による「サーバーが知っているか」：（端末・表・キー）→ 答えと、その答えを決められないか（例外で終わった
    /// 実行の後の観測・実行の間に書かれたキーの観測）
    /// </summary>
    private readonly Dictionary<
        (ModelActor Device, string Table, string Key),
        (bool Known, bool Undetermined)
    > _serverKnows = [];

    /// <summary>
    /// エンジンの記録を直接作る・変える手が触れた（端末・表・キー）。表が null なら端末の全部の表、キーが null なら表の全部の
    /// キー（照合 4 だけが決められないとして数える）
    /// </summary>
    private readonly HashSet<(ModelActor Device, string? Table, string? Key)> _recordTouched = [];

    /// <summary>台帳の通し番号（書き込みの手と実行の終わりごとに 1 つ進める）</summary>
    private long _sequence;

    /// <summary>
    /// サーバーのそのキー（blob の品目は <see cref="PayloadTable"/>）を最後に変えた書き手と通し番号（サーバーの他者の書き込みの手は
    /// その手の番号で <see cref="ModelActor.Server"/>、端末の実行の間に変わったキーはその実行の終わりの番号でその端末。実行の間の
    /// 割り込みの書き込みが変えたキーは割り込みの書き手のまま）
    /// </summary>
    private readonly Dictionary<
        (string Table, string Key),
        (ModelActor Writer, long Sequence)
    > _serverLastWriter = [];

    /// <summary>
    /// 端末がそのキーのサーバーの内容を最後に見た通し番号（端末の実行の終わりに、端末とサーバーのそのキーの内容が同じ〔どちらも
    /// 無いを含む〕ならその番号。一度も見ていなければ無い＝0）
    /// </summary>
    private readonly Dictionary<(ModelActor Device, string Table, string Key), long> _lastSeen = [];

    /// <summary>
    /// 持ち分の内容を最後に書いたときに、端末がその行（blob の品目はその品目の行）のサーバーの内容を最後に見ていた番号
    /// （端末・表・キー。持ち分が無いキーの値は使わない）
    /// </summary>
    private readonly Dictionary<
        (ModelActor Device, string Table, string Key),
        long
    > _rowSeenWhenHeld = [];

    /// <summary>実行 1 回の照合の入力のうち、実行の前に控えるもの</summary>
    /// <param name="Device">端末</param>
    /// <param name="Description">実行の手（違反の文に載せる）</param>
    /// <param name="WritesServer">サーバーへ書く実行か（ダウンロード・洗い替えは書かない）</param>
    /// <param name="ServerWinsRun">ローカルの変更を捨ててよいと頼んだ実行か（ServerWins のダウンロード以外の実行）</param>
    /// <param name="ForceRefresh">未送信を捨ててよいと頼んだ洗い替えか</param>
    /// <param name="SyncKind">SyncAsync（同期）の実行か（単独の UploadAsync・DownloadAsync・洗い替えは違う）</param>
    /// <param name="VersionedServerWins">Versioned で ServerWins の、ダウンロード以外の同期か（サーバーを変えない実行）</param>
    /// <param name="GuardsVersions">
    /// 版で守ってサーバーへ書く実行か（Versioned で Collect か ServerWins の、ダウンロード以外の同期。後勝ち・LocalWins・
    /// ダウンロードだけ・洗い替えは違う）
    /// </param>
    /// <param name="LowersOnMatch">
    /// 端末の行とサーバーの行が同じ内容になった持ち分を下ろす実行か（届ける実行と、端末の行をサーバーの行で置き換える洗い替え。
    /// ダウンロードだけの実行は下ろさない）
    /// </param>
    /// <param name="InScope">その実行の範囲の表か</param>
    /// <param name="ServerBeforeState">実行の前のサーバー</param>
    /// <param name="ServerBefore">実行の前のサーバーの内容</param>
    /// <param name="LocalBefore">実行の前の端末の内容</param>
    /// <param name="Holdings">実行の始めの、その端末の持ち分の内容</param>
    /// <param name="Carried">実行の始めに、その端末の戻せなかった行の決定があったキー</param>
    /// <param name="ThrownServerWins">
    /// 実行の始めに、例外で終わった ServerWins の実行が戻せなかった行で、端末の行がその実行の直後のままのキー
    /// </param>
    private sealed record RunFrame(
        ModelActor Device,
        string Description,
        bool WritesServer,
        bool ServerWinsRun,
        bool ForceRefresh,
        bool SyncKind,
        bool VersionedServerWins,
        bool GuardsVersions,
        bool LowersOnMatch,
        Func<string, bool> InScope,
        ModelDbState ServerBeforeState,
        IReadOnlyDictionary<(string Table, string Key), string> ServerBefore,
        IReadOnlyDictionary<(string Table, string Key), string> LocalBefore,
        IReadOnlyDictionary<(string Table, string Key), string?> Holdings,
        IReadOnlySet<(string Table, string Key)> Carried,
        IReadOnlySet<(string Table, string Key)> ThrownServerWins
    )
    {
        /// <summary>実行の始めの、BlobDownload の失敗の控えの位置（<see cref="SyncModelFaults.BlobReadHitsSince"/>）</summary>
        public int BlobReadHitIndex { get; init; }

        /// <summary>洗い替えか（サーバーへ書かず、端末の行をサーバーの行で置き換える実行）</summary>
        public bool IsRefresh => !WritesServer && LowersOnMatch;

        /// <summary>実行の前の端末の外部キーの違反の集合（外部キーの表明は、実行の後に新しく出来た違反だけを数える）</summary>
        public IReadOnlySet<ForeignKeyViolation>? ForeignKeyViolationsBefore { get; init; }
    }

    /// <summary>
    /// 品目の blob を行と別の品目として台帳と照合に載せるときの表の名前（blob は書いた列だけを送るので、行と同じ規則で
    /// 別に見る）
    /// </summary>
    private const string PayloadTable = "model_items:payload";

    /// <summary>台帳の品目の行のキー（blob の品目は、その品目の行のキー）</summary>
    private static (string Table, string Key) RowKey((string Table, string Key) key) =>
        key.Table == PayloadTable ? (ModelTables.Items, key.Key) : key;

    /// <summary>状態の内容（表・キー → 行の正規の文字列。品目の blob は <see cref="PayloadTable"/> の品目として別に持つ）</summary>
    private static Dictionary<(string Table, string Key), string> Contents(ModelDbState state)
    {
        var contents = new Dictionary<(string Table, string Key), string>();

        foreach (var table in ModelTables.All)
        {
            foreach (var (key, row) in state.Rows(table))
            {
                contents[(table, key)] = row;
            }
        }

        foreach (var (key, hex) in state.Payloads())
        {
            contents[(PayloadTable, key)] = hex;
        }

        return contents;
    }

    /// <summary>
    /// 照合が読む DB の状態（端末は、決まった形の行が一時的に消した表を空として読む。サーバーは世界の読み方のまま）
    /// </summary>
    private Task<ModelDbState> ReadLedgerStateAsync(ModelActor actor) =>
        actor == ModelActor.Server
            ? _world.ReadStateAsync(actor, _ct)
            : ModelDbState.ReadAsync(
                _world.Device(actor).ConnectionString,
                _ct,
                missingTablesAreEmpty: true
            );

    /// <summary>
    /// 書き込みの手の前後の差分を台帳へ反映する（記録つきなら変わったキーの持ち分を立て、記録しないなら持ち分があるキーだけ
    /// 内容を置き換える）。割り込みの書き込みなら、変わったキーを実行の間に書かれたキーへ入れる（書き手を問わない）
    /// </summary>
    /// <param name="actor">書き手</param>
    /// <param name="before">書き込みの前の書き手の DB</param>
    /// <param name="after">書き込みの後の書き手の DB</param>
    /// <param name="tracked">記録つきの書き込みか</param>
    /// <remarks>追跡しない行の印を消費する <see cref="RecordChanges"/> より前に呼ぶ</remarks>
    /// <param name="what">書き込みの手（台帳の見張りの違反の文に載せる）</param>
    /// <param name="succeeded">
    /// 成功した記録つきの書き込みの手（失敗した手・注入した失敗・フックが飛ばした手は null）。内容が変わらなくても、手が名指しする
    /// 1 行（blob の書き込みは行と blob の品目）の持ち分を今の内容で立てる（記録は内容に依らず残り、エンジンが送る）。グラフ保存が
    /// ほかの行を書いても、名指しの行だけを立てる（ほかの行は内容が変わった分を差分が拾う）
    /// </param>
    private void NoteWrite(
        ModelActor actor,
        ModelDbState before,
        ModelDbState after,
        bool tracked,
        string what,
        ModelWriteOp? succeeded = null
    )
    {
        var was = Contents(before);
        var now = Contents(after);
        var changed = was
            .Keys.Union(now.Keys)
            .Where(key =>
                !string.Equals(
                    was.GetValueOrDefault(key),
                    now.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
            )
            .ToList();

        var sequence = ++_sequence;

        if (_interrupting)
        {
            _writtenDuringRun.UnionWith(changed);
        }

        // サーバーの他者の書き込みの手が変えたキーは、この手の番号でサーバーの他者が最後に変えた
        if (actor == ModelActor.Server)
        {
            foreach (var key in changed)
            {
                _serverLastWriter[key] = (ModelActor.Server, sequence);
            }

            // 割り込みの書き込みが、端末の持ち分の内容と同じサーバーの内容を上書きした（届いた後の上書き）：その持ち分を下ろす
            if (_interrupting)
            {
                ForgetHoldingsOverwrittenAfterDelivery(changed, was, what);
            }

            return;
        }

        var held = new HashSet<(string Table, string Key)>();

        foreach (var key in changed)
        {
            if (!tracked)
            {
                if (_holdings.ContainsKey((actor, key.Table, key.Key)))
                {
                    held.Add(key);
                }

                continue;
            }

            held.Add(key);
            var row = RowKey(key);

            // blob の書き込みは行も送る・追跡しない行への最初の書き込みは全列を送る
            if (key.Table == PayloadTable || _untrackedPreload.Contains(row))
            {
                held.Add(row);

                if (row.Table == ModelTables.Items)
                {
                    held.Add((PayloadTable, row.Key));
                }
            }
        }

        // 成功した記録つきの手は、内容が変わらなくても名指しの行の持ち分を立てる
        if (tracked && succeeded is not null)
        {
            held.Add((succeeded.Table, succeeded.KeyText));

            if (succeeded is WritePayloadOp)
            {
                held.Add((PayloadTable, succeeded.KeyText));
            }
        }

        foreach (var key in held)
        {
            var existed = _holdings.TryGetValue((actor, key.Table, key.Key), out var previous)
                ? previous.Existed
                : was.ContainsKey(key);
            _holdings[(actor, key.Table, key.Key)] = new(now.GetValueOrDefault(key), existed);
            _rowSeenWhenHeld[(actor, key.Table, key.Key)] = _lastSeen.GetValueOrDefault(
                (actor, RowKey(key).Table, RowKey(key).Key)
            );

            // 行の決定がある品目の blob を、端末が記録つきの書き込みで書いた（決定の後の端末の書き込み）
            if (
                tracked
                && key.Table == PayloadTable
                && _carried.ContainsKey((actor, ModelTables.Items, key.Key))
            )
            {
                _blobWrittenAfterHeld.Add((actor, ModelTables.Items, key.Key));
            }
        }

        // 立てたときに端末に行が無く、実行を挟まずに削除で終わった持ち分は、差し引き何も書いていない（blob の品目も一緒に下ろす）
        foreach (var key in held.Where(k => k.Table != PayloadTable))
        {
            if (
                _holdings.TryGetValue((actor, key.Table, key.Key), out var holding)
                && holding is { Content: null, Existed: false }
            )
            {
                // 持ち分は下ろすが、エンジンには挿入と削除の記録が残りうる
                _mayHoldRecord.Add((actor, key.Table, key.Key));
                ForgetHolding(actor, key);
            }
        }

        ForgetDeletedBlobsWithoutRow(actor);
        CheckLedgerInvariant(actor, what);
    }

    /// <summary>
    /// 下ろす (5)：サーバーの他者の割り込みの書き込みが変えたキー（blob の品目も同じ規則）で、書く直前のサーバーの内容が、ある端末の
    /// そのキーの持ち分の内容と同じなら（端末の書き込みが届いた後の上書き）、その端末のその持ち分を下ろす
    /// </summary>
    /// <param name="changed">割り込みの書き込みが変えたキー</param>
    /// <param name="was">書く直前のサーバーの内容</param>
    /// <param name="what">書き込みの手（台帳の見張りの違反の文に載せる）</param>
    private void ForgetHoldingsOverwrittenAfterDelivery(
        IReadOnlyList<(string Table, string Key)> changed,
        IReadOnlyDictionary<(string Table, string Key), string> was,
        string what
    )
    {
        var keys = changed.ToHashSet();
        var delivered = _holdings
            .Where(p =>
                keys.Contains((p.Key.Table, p.Key.Key))
                && string.Equals(
                    p.Value.Content,
                    was.GetValueOrDefault((p.Key.Table, p.Key.Key)),
                    StringComparison.Ordinal
                )
            )
            .Select(p => p.Key)
            .ToList();

        foreach (var key in delivered)
        {
            _holdings.Remove(key);
        }

        foreach (var device in delivered.Select(k => k.Device).Distinct())
        {
            ForgetDeletedBlobsWithoutRow(device);
            CheckLedgerInvariant(device, what);
        }
    }

    /// <summary>
    /// 品目の行の持ち分が下りた端末の、内容が削除の blob の持ち分を下ろす（削除の blob の持ち分は行の削除からしか立たないので、
    /// 行の持ち分に従う。行の持ち分がどの経路で下りても一緒に下ろす）
    /// </summary>
    private void ForgetDeletedBlobsWithoutRow(ModelActor device)
    {
        foreach (
            var key in _holdings
                .Where(p =>
                    p.Key.Device == device
                    && p.Key.Table == PayloadTable
                    && p.Value.Content is null
                    && !_holdings.ContainsKey((device, ModelTables.Items, p.Key.Key))
                )
                .Select(p => p.Key)
                .ToList()
        )
        {
            _holdings.Remove(key);
        }
    }

    /// <summary>
    /// 台帳の見張り：内容が削除の blob の持ち分があれば、その品目の行の持ち分も削除でなければならない（行の削除からしか立たない）。
    /// 破れていれば <c>5/ledger-invariant:model_items:payload</c> で名指しする
    /// </summary>
    /// <param name="device">端末</param>
    /// <param name="what">台帳を変えた直前の手か実行</param>
    private void CheckLedgerInvariant(ModelActor device, string what)
    {
        foreach (
            var (key, blob) in _holdings
                .Where(p =>
                    p.Key.Device == device && p.Key.Table == PayloadTable && p.Value.Content is null
                )
                .ToList()
        )
        {
            var hasRow = _holdings.TryGetValue((device, ModelTables.Items, key.Key), out var row);

            if (hasRow && row.Content is null)
            {
                continue;
            }

            Tally(LedgerInvariantCheck, violation: true);
            _runFailures.Add(
                new(
                    $"5/ledger-invariant:{PayloadTable}",
                    $"{device} {ModelTables.Items}/{key.Key}: the blob holding is (deleted) but the row holding is "
                        + (hasRow ? row.Content : "(none)")
                        + $", after {what}"
                )
            );
        }
    }

    /// <summary>行の持ち分を下ろす（品目の行なら blob の品目も）</summary>
    private void ForgetHolding(ModelActor device, (string Table, string Key) row)
    {
        _holdings.Remove((device, row.Table, row.Key));

        if (row.Table == ModelTables.Items)
        {
            _holdings.Remove((device, PayloadTable, row.Key));
        }
    }

    /// <summary>エンジンの記録を直接作る・変える手が触れたキーを控える（照合 4 だけが決められないとして数える）</summary>
    private void NoteRecordTouched(ModelActor device, string? table, string? key) =>
        _recordTouched.Add((device, table, key));

    /// <summary>同期の実行の照合の入力を控える（状態を渡せばそれを使い、無ければ読む）</summary>
    private Task<RunFrame> BeginSyncRunAsync(ModelSyncOp op, RunSnapshot? before) =>
        BeginRunAsync(
            op.Device,
            op.Describe(),
            writesServer: op.Kind != ModelSyncKind.Download,
            serverWinsRun: op.Policy == SyncConflictPolicy.ServerWins
                && op.Kind != ModelSyncKind.Download,
            forceRefresh: false,
            syncKind: op.Kind == ModelSyncKind.Sync,
            versionedServerWins: op
                is { Mode: SyncMode.Versioned, Policy: SyncConflictPolicy.ServerWins }
                && op.Kind != ModelSyncKind.Download,
            guardsVersions: op is { Mode: SyncMode.Versioned }
                && op.Policy is SyncConflictPolicy.Collect or SyncConflictPolicy.ServerWins
                && op.Kind != ModelSyncKind.Download,
            lowersOnMatch: op.Kind != ModelSyncKind.Download,
            table =>
                !op.Excluded.Contains(table)
                && !(op.Mode == SyncMode.Versioned && table == ModelTables.Labels),
            before?.Server,
            before?.Local
        );

    /// <summary>洗い替えの実行の照合の入力を控える</summary>
    private Task<RunFrame> BeginRefreshRunAsync(
        ModelOp op,
        ModelActor device,
        SyncRefreshOptions options
    ) =>
        BeginRunAsync(
            device,
            op.Describe(),
            writesServer: false,
            serverWinsRun: false,
            forceRefresh: options.Force,
            syncKind: false,
            versionedServerWins: false,
            guardsVersions: false,
            lowersOnMatch: true,
            table => InRefreshScope(table, options),
            null,
            null
        );

    private async Task<RunFrame> BeginRunAsync(
        ModelActor device,
        string description,
        bool writesServer,
        bool serverWinsRun,
        bool forceRefresh,
        bool syncKind,
        bool versionedServerWins,
        bool guardsVersions,
        bool lowersOnMatch,
        Func<string, bool> inScope,
        ModelDbState? server,
        ModelDbState? local
    )
    {
        _writtenDuringRun.Clear();
        server ??= await ReadLedgerStateAsync(ModelActor.Server);
        local ??= await ReadLedgerStateAsync(device);
        var localContents = Contents(local);

        return new(
            device,
            description,
            writesServer,
            serverWinsRun,
            forceRefresh,
            syncKind,
            versionedServerWins,
            guardsVersions,
            lowersOnMatch,
            inScope,
            server,
            Contents(server),
            localContents,
            _holdings
                .Where(p => p.Key.Device == device)
                .ToDictionary(p => (p.Key.Table, p.Key.Key), p => p.Value.Content),
            _carried.Keys.Where(k => k.Device == device).Select(k => (k.Table, k.Key)).ToHashSet(),
            _thrownServerWins
                .Where(p =>
                    p.Key.Device == device
                    && string.Equals(
                        localContents.GetValueOrDefault((p.Key.Table, p.Key.Key)),
                        p.Value,
                        StringComparison.Ordinal
                    )
                )
                .Select(p => (p.Key.Table, p.Key.Key))
                .ToHashSet()
        )
        {
            BlobReadHitIndex = _world.Faults.BlobReadHitCount,
            ForeignKeyViolationsBefore = await ReadForeignKeyViolationsAsync(device),
        };
    }

    /// <summary>
    /// 実行 1 回の照合（実行の直後に確定する）。台帳を下ろし、「サーバーが知っているか」の観測を更新する
    /// </summary>
    /// <param name="frame">実行の前に控えた入力</param>
    /// <param name="conflicts">実行が報告した競合（例外で終わった実行は空）</param>
    /// <param name="changes">
    /// 実行が報告した、自分から変えたローカルの行（例外で終わり結果を運ばなかった実行は null＝持ち分の消失を判定しない）
    /// </param>
    /// <param name="threw">実行が例外で終わったか</param>
    /// <param name="truncated">実行がダウンロードを打ち切った表（結果を返した同期だけ。ほかは null）</param>
    private async Task EndRunAsync(
        RunFrame frame,
        IReadOnlyList<SyncConflict> conflicts,
        IReadOnlyList<SyncLocalRowChange>? changes,
        bool threw,
        IReadOnlyCollection<string>? truncated = null
    )
    {
        // 例外で終わった SyncAsync が一覧を運ばないときは、空の一覧として判定する。docs の LocalRowChanges の説明
        // （SyncAsync が例外で終わっても、それまでに変えた行は例外の Data に入る）から、運ばないのは変えた行が無いときと
        // 読む。単独の UploadAsync・DownloadAsync は一覧を入れない（同じ説明）ので null のまま
        if (threw && changes is null && frame.SyncKind)
        {
            changes = [];
        }

        var device = frame.Device;
        var serverAfterState = await ReadLedgerStateAsync(ModelActor.Server);
        var localAfterState = await ReadLedgerStateAsync(device);
        var serverAfter = Contents(serverAfterState);
        var localAfter = Contents(localAfterState);

        // 外部キーの表明（例外で終わった実行・洗い替えも同じ）：実行の前に無かった違反だけを数える
        await CheckNewForeignKeyViolationsAsync(
            device,
            frame.ForeignKeyViolationsBefore,
            frame.Description
        );
        var carries = _world.Device(device).Sync.CarriesServerWinsDecision;
        var conflicted = conflicts
            .Select(c => (c.TableName, CanonicalKey(c.TableName, c.KeyText)))
            .ToHashSet();
        var reported = (changes ?? [])
            .Select(c => (c.TableName, CanonicalKey(c.TableName, c.KeyText)))
            .ToHashSet();

        CheckStaleOverwrite(frame, serverAfter, carries);
        CheckUnreportedOverwrite(frame, serverAfter, conflicted, carries);
        CheckServerWritesInScope(frame, serverAfter);
        CheckUnownedSends(frame, conflicts, changes, carries);

        // 照合 3 の窓の判定が使う、実行の始めの値（この実行の分を台帳へ足す前）：端末がそのキーを最後に見た後に、ほかの書き手が
        // サーバーのその行を変えていたキーと、この実行でこの端末の blob の読み出しの失敗が当たった品目
        var movedSinceSeen = frame
            .Holdings.Keys.Where(key =>
                _serverLastWriter.TryGetValue(key, out var last)
                && last.Writer != device
                && last.Sequence > _lastSeen.GetValueOrDefault((device, key.Table, key.Key))
            )
            .ToHashSet();
        var blobReadFailed = _world
            .Faults.BlobReadHitsSince(frame.BlobReadHitIndex)
            .Where(hit => hit.Device == device)
            .Select(hit => (ModelTables.Items, hit.Key))
            .ToHashSet();
        NoteServerWritesAndSeen(frame, serverAfter, localAfter);

        var (editLost, fulfilled) = CheckCarried(
            frame,
            serverAfter,
            localAfter,
            conflicted,
            reported,
            changes is not null,
            carries
        );
        CheckLocalDiscard(
            frame,
            serverAfter,
            localAfter,
            threw,
            reported,
            changes is not null,
            editLost,
            fulfilled,
            carries,
            movedSinceSeen,
            blobReadFailed
        );

        if (!threw && frame.ServerWinsRun)
        {
            HoldServerWinsDecisions(frame, serverAfter, localAfter, conflicted, carries);
        }

        ObserveServerKnows(
            device,
            serverAfterState,
            localAfterState,
            threw,
            forgets: frame.LowersOnMatch && !threw,
            frame.InScope
        );

        // 実行が自分から変えたと報告した行の持ち分は下ろす
        foreach (var row in reported)
        {
            ForgetHolding(device, row);
        }

        // サーバーに無かった品目の行を、行の持ち分を持つ端末がこの実行で入れた：挿入は全列を送るので、blob も送るべき内容に
        // なる。blob がまだ届いていなければ（送信の失敗）、端末の blob を持ち分として立て、届くまで残す
        if (frame.WritesServer)
        {
            foreach (
                var row in frame.Holdings.Keys.Where(k =>
                    k.Table == ModelTables.Items
                    && !frame.ServerBefore.ContainsKey(k)
                    && serverAfter.ContainsKey(k)
                )
            )
            {
                var blob = (PayloadTable, row.Key);

                if (
                    localAfter.TryGetValue(blob, out var localBlob)
                    && !string.Equals(
                        localBlob,
                        serverAfter.GetValueOrDefault(blob),
                        StringComparison.Ordinal
                    )
                    && !_holdings.ContainsKey((device, PayloadTable, row.Key))
                )
                {
                    _holdings[(device, PayloadTable, row.Key)] = new(localBlob, Existed: true);
                    _rowSeenWhenHeld[(device, PayloadTable, row.Key)] = _lastSeen.GetValueOrDefault(
                        (device, row.Table, row.Key)
                    );
                }
            }
        }

        // 下ろす規則 (1) で持ち分を下ろす。例外で終わった実行では、エンジンが記録を送る前に止まっていて記録が残りうるので、
        // 記録が残っているかもしれないキーとして控える
        void LowerOnMatch((ModelActor Device, string Table, string Key) key)
        {
            _holdings.Remove(key);

            if (threw)
            {
                var lowered = RowKey((key.Table, key.Key));
                _mayHoldRecord.Add((device, lowered.Table, lowered.Key));
            }
        }

        // blob の持ち分を行の持ち分より先に見る（blob の持ち分がある間は行の持ち分を下ろさない）
        foreach (
            var key in _holdings
                .Keys.Where(k => k.Device == device)
                .OrderBy(k => k.Table == PayloadTable ? 0 : 1)
                .ToList()
        )
        {
            var held = (key.Table, key.Key);

            // その実行の範囲の表で、端末の行とサーバーの行が同じ内容になった持ち分は下ろす（届いた・戻された）。ダウンロードだけの
            // 実行は届けず、未送信の行を戻さないので下ろさない。その実行がそのキー（blob の品目はその行）の競合を報告したなら
            // 下ろさない（版で比べるので内容が同じでも競合になり、Collect は記録を残す）
            if (
                frame.LowersOnMatch
                && frame.InScope(RowKey(held).Table)
                && !conflicted.Contains(RowKey(held))
                && string.Equals(
                    localAfter.GetValueOrDefault(held),
                    serverAfter.GetValueOrDefault(held),
                    StringComparison.Ordinal
                )
            )
            {
                var blobPending =
                    key.Table == ModelTables.Items
                    && _holdings.TryGetValue((device, PayloadTable, key.Key), out var blob)
                    && blob.Content is not null;

                if (!blobPending)
                {
                    LowerOnMatch(key);
                    continue;
                }

                // 同じ端末がその品目の内容のある（削除でない）blob の持ち分をまだ持つ：行が持ち分の内容で同じになった（届いた）
                // なら、行の持ち分を残す（blob の書き込みは行も送る）。持ち分と違う内容で同じになった（戻された）なら、行の
                // 持ち分だけを下ろす（blob は端末の内容のまま届いていないので、blob の持ち分は残す）
                if (
                    !string.Equals(
                        localAfter.GetValueOrDefault(held),
                        _holdings[key].Content,
                        StringComparison.Ordinal
                    )
                )
                {
                    LowerOnMatch(key);
                    continue;
                }
            }

            // 実行の後に、端末とサーバーの両方にその行（blob の品目はその品目の行）があり内容が同じと観測したら、行は「あった」
            // （その後の削除は差し引きで消えない）。観測が取れない（実行の間にそのキーへ割り込みの書き込みがあった）ときと、削除を
            // 無条件に送る版なしの表は、真にする
            var row = RowKey(held);

            if (
                WrittenDuringRun(held)
                || row.Table == ModelTables.Labels
                || (
                    localAfter.TryGetValue(row, out var localRow)
                    && string.Equals(
                        localRow,
                        serverAfter.GetValueOrDefault(row),
                        StringComparison.Ordinal
                    )
                )
            )
            {
                _holdings[key] = _holdings[key] with { Existed = true };
            }
        }

        // 例外で終わった ServerWins の実行（ダウンロード以外）の、範囲の表の戻せなかった行を控える（決定と同じ条件。決定は作らない）
        if (threw && frame.ServerWinsRun)
        {
            HoldThrownServerWinsRows(frame, serverAfter, localAfter, conflicted);
        }

        NoteRestoredByThrownServerWins(
            frame,
            serverAfter,
            localAfter,
            threw,
            carries,
            movedSinceSeen,
            blobReadFailed
        );
        NoteRecordAndBroughtInControls(
            frame,
            serverAfter,
            localAfter,
            threw,
            conflicted,
            reported,
            truncated ?? [],
            blobReadFailed
        );

        // 行の持ち分が下りた品目の、削除の blob の持ち分も下ろす（照合 3・報告・同じ内容のどの経路で行が下りても）
        ForgetDeletedBlobsWithoutRow(device);
        CheckLedgerInvariant(device, frame.Description);

        // 持ち分が無くなった行は控えから外す
        foreach (
            var key in _thrownServerWins
                .Keys.Where(k =>
                    k.Device == device && !_holdings.ContainsKey((device, k.Table, k.Key))
                )
                .ToList()
        )
        {
            _thrownServerWins.Remove(key);
        }
    }

    /// <summary>
    /// 記録が残っているかもしれないキー（<see cref="_mayHoldRecord"/>）を外し、例外で終わった実行が持ち込んだ行
    /// （<see cref="_broughtInByThrownRun"/>）を立てて外す（実行の前後の DB・実行の結果・失敗の注入の控えで決める）
    /// </summary>
    /// <param name="frame">実行の前に控えた入力</param>
    /// <param name="serverAfter">実行の後のサーバーの内容</param>
    /// <param name="localAfter">実行の後の端末の内容</param>
    /// <param name="threw">実行が例外で終わったか</param>
    /// <param name="conflicted">実行が競合を報告したキー</param>
    /// <param name="reported">実行が LocalRowChanges で報告したキー</param>
    /// <param name="truncated">実行がダウンロードを打ち切った表</param>
    /// <param name="blobReadFailed">この実行でこの端末の blob の読み出しの失敗が当たった行</param>
    private void NoteRecordAndBroughtInControls(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        bool threw,
        HashSet<(string, string)> conflicted,
        HashSet<(string, string)> reported,
        IReadOnlyCollection<string> truncated,
        HashSet<(string Table, string Key)> blobReadFailed
    )
    {
        var device = frame.Device;

        // 記録が残っているかもしれないキーを外す：例外の無い、ダウンロードだけでない実行（洗い替えを含む）で、その表が範囲に
        // あり、そのキーの競合が報告されず、その表が打ち切られなかった／その実行が LocalRowChanges でそのキーを報告した
        _mayHoldRecord.RemoveWhere(k =>
            k.Device == device
            && (
                reported.Contains((k.Table, k.Key))
                || (
                    !threw
                    && frame.LowersOnMatch
                    && frame.InScope(k.Table)
                    && !conflicted.Contains((k.Table, k.Key))
                    && !truncated.Contains(k.Table)
                )
            )
        );

        // 例外で終わった実行が持ち込んだ行を立てる：実行の前に端末に行が無く、後にサーバーと同じ内容の行があり、この実行で
        // その行の blob の読み出しの失敗が当たったキー（行を書いた後・受理記録を書く前に、blob のコピーの失敗で例外になる窓。
        // 例外の前のバッチで普通に降りた行は版を持つので入れない）
        if (threw)
        {
            foreach (var (key, row) in localAfter)
            {
                if (
                    key.Table != PayloadTable
                    && blobReadFailed.Contains(key)
                    && !frame.LocalBefore.ContainsKey(key)
                    && string.Equals(
                        row,
                        serverAfter.GetValueOrDefault(key),
                        StringComparison.Ordinal
                    )
                )
                {
                    _broughtInByThrownRun.Add((device, key.Table, key.Key));
                }
            }
        }

        // 外す：例外の無いその端末の実行の間に端末のその行の内容が変わった（降ろし直し・読み直し）・その端末の洗い替え（例外
        // なし・範囲の表）／その実行が LocalRowChanges でそのキーを報告した
        _broughtInByThrownRun.RemoveWhere(k =>
            k.Device == device
            && (
                reported.Contains((k.Table, k.Key))
                || (
                    !threw
                    && (
                        (frame.IsRefresh && frame.InScope(k.Table))
                        || !string.Equals(
                            frame.LocalBefore.GetValueOrDefault((k.Table, k.Key)),
                            localAfter.GetValueOrDefault((k.Table, k.Key)),
                            StringComparison.Ordinal
                        )
                    )
                )
            )
        );
    }

    /// <summary>
    /// 例外で終わった ServerWins の実行が戻した行の控え（<see cref="_restoredByThrownServerWins"/>）を、実行の前後の DB と
    /// 窓の判定（blob の読み出しの失敗・ほかの書き手の変更）で立てて外す。宣言が真のエンジンは控えを使わない
    /// </summary>
    /// <param name="frame">実行の前に控えた入力</param>
    /// <param name="serverAfter">実行の後のサーバーの内容</param>
    /// <param name="localAfter">実行の後の端末の内容</param>
    /// <param name="threw">実行が例外で終わったか</param>
    /// <param name="carries">エンジンが持ち越しを宣言するか</param>
    /// <param name="movedSinceSeen">
    /// 実行の始めに、端末がそのキーを最後に見た後で、ほかの書き手がサーバーのその行を変えていた持ち分のキー
    /// </param>
    /// <param name="blobReadFailed">この実行でこの端末の blob の読み出しの失敗が当たった行</param>
    private void NoteRestoredByThrownServerWins(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        bool threw,
        bool carries,
        HashSet<(string Table, string Key)> movedSinceSeen,
        HashSet<(string Table, string Key)> blobReadFailed
    )
    {
        var device = frame.Device;

        // 外す：その表を範囲にした、例外の無い、ダウンロードだけでない実行の後に、端末とサーバーのその行が同じ内容になった
        if (!threw && frame.LowersOnMatch)
        {
            _restoredByThrownServerWins.RemoveWhere(k =>
                k.Device == device
                && frame.InScope(k.Table)
                && string.Equals(
                    localAfter.GetValueOrDefault((k.Table, k.Key)),
                    serverAfter.GetValueOrDefault((k.Table, k.Key)),
                    StringComparison.Ordinal
                )
            );
        }

        if (!threw || !frame.ServerWinsRun || carries)
        {
            return;
        }

        // 立てる：実行の始めに持ち分があり、範囲の表で、実行の後に端末とサーバーが持ち分と違う同じ内容になったキーのうち、
        // 行を書いた後で例外になる窓に当たるもの（照合 3 の数えるだけの窓と同じ）：この実行でその行の blob の読み出しの失敗が
        // 当たった／SyncAsync で、端末が最後に見た後にほかの書き手がサーバーのその行を変えていた
        foreach (var (key, held) in frame.Holdings)
        {
            var localNow = localAfter.GetValueOrDefault(key);

            if (
                frame.InScope(RowKey(key).Table)
                && (
                    blobReadFailed.Contains(RowKey(key))
                    || (frame.SyncKind && movedSinceSeen.Contains(key))
                )
                && string.Equals(
                    localNow,
                    serverAfter.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                && !string.Equals(localNow, held, StringComparison.Ordinal)
            )
            {
                var row = RowKey(key);
                _restoredByThrownServerWins.Add((device, row.Table, row.Key));
            }
        }
    }

    /// <summary>
    /// 例外で終わった ServerWins の実行の後の、戻せなかった行を控える：実行の始めに持ち分があり、実行の後も端末の行がサーバーと
    /// 違い、実行の間に端末の行が変わっておらず、そのキーの競合が報告されなかった（範囲の表だけ）。決定と違い、後の実行の照合
    /// 3 で数えるだけに使う
    /// </summary>
    private void HoldThrownServerWinsRows(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        HashSet<(string, string)> conflicted
    )
    {
        foreach (var key in frame.Holdings.Keys)
        {
            var localNow = localAfter.GetValueOrDefault(key);

            if (
                !frame.InScope(RowKey(key).Table)
                || string.Equals(
                    localNow,
                    serverAfter.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    localNow,
                    frame.LocalBefore.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || conflicted.Contains(RowKey(key))
                || !_holdings.ContainsKey((frame.Device, key.Table, key.Key))
            )
            {
                continue;
            }

            _thrownServerWins[(frame.Device, key.Table, key.Key)] = localNow;
        }
    }

    /// <summary>
    /// 照合 1：実行がサーバーのキーを実行の前の端末の内容へ変えたのに、端末がそのキーの持ち分を持たない
    /// （<c>5/stale-overwrite:{表}</c>）
    /// </summary>
    private void CheckStaleOverwrite(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        bool carries
    )
    {
        if (!frame.WritesServer)
        {
            return;
        }

        foreach (var key in frame.ServerBefore.Keys.Union(serverAfter.Keys).ToList())
        {
            var before = frame.ServerBefore.GetValueOrDefault(key);
            var after = serverAfter.GetValueOrDefault(key);

            // 変わっていない・変わった先が実行の前の端末の内容でない（誰も書いていない内容は 3/invented が見る）・blob が
            // 行ごと消えた（行の品目が見る）
            if (
                string.Equals(before, after, StringComparison.Ordinal)
                || !string.Equals(
                    after,
                    frame.LocalBefore.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || (key.Table == PayloadTable && after is null)
                || WrittenDuringRun(key)
            )
            {
                continue;
            }

            var row = RowKey(key);
            var violation =
                !frame.Holdings.ContainsKey(key)
                // 端末が届けた親の削除にサーバーの外部キーのカスケードが従った子の行
                && !(
                    after is null
                    && frame.Holdings.Any(h =>
                        h.Value is null
                        && h.Key.Table != PayloadTable
                        && IsUnder(
                            frame.ServerBeforeState,
                            row.Table,
                            row.Key,
                            h.Key.Table,
                            h.Key.Key
                        )
                    )
                )
                // サーバーに無い行の挿入・作り直しは全列を送る（実行の前のサーバーにその品目の行が無く、端末が行の持ち分を持つ）
                && !(
                    key.Table == PayloadTable
                    && !frame.ServerBefore.ContainsKey(row)
                    && frame.Holdings.ContainsKey(row)
                );

            // 宣言が偽のエンジンで、例外で終わった ServerWins の実行が戻した行（blob の品目は行の控え）の送信は、数えるだけ
            var resend =
                violation
                && !carries
                && _restoredByThrownServerWins.Contains((frame.Device, row.Table, row.Key));
            Tally(StaleOverwriteCheck, violation && !resend);

            if (resend)
            {
                Tally(ResendAfterThrownServerWinsCheck, violation: false);
            }

            if (!violation || resend)
            {
                continue;
            }

            _runFailures.Add(
                new(
                    $"5/stale-overwrite:{key.Table}",
                    $"{frame.Device} {key.Table}/{key.Key}: the server row changed from {before ?? "(none)"} "
                        + $"to {after ?? "(none)"} (the device's content before the run) during {frame.Description}, "
                        + "but the device holds no write of its own on it"
                )
            );
        }
    }

    /// <summary>
    /// 照合 6・7：実行がサーバーへ書いてよい行だけを書いたか（<c>5/out-of-scope-write:{表}</c>・<c>5/serverwins-wrote:{表}</c>）。
    /// 実行の間に割り込みの書き込みが変えたキーは外す。両方に当たる書き込みは両方の署名を出す（別の約束）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 照合 6：実行は、その範囲の外の表（除外した表・Versioned のときの版なしの表）のサーバーの行（blob の品目も）を変えない。
    /// ダウンロードだけの実行と洗い替えは、サーバーのどの行も変えない。出典は docs/code-generation.ja.md の「版なしテーブルと
    /// 後勝ちモード」（既定（Versioned）のランは、版なしテーブルに一切触れません。ダウンロードもアップロードも削除伝搬もしません）と、
    /// 同じ節の <c>SyncOptions.ExcludedEntityTypes</c> の説明（今回のランから外すだけ）。前提として、モデルの図のサーバーの外部キーは
    /// すべて NO ACTION なので、サーバー側のカスケードで範囲の外の行は変わらない（<see cref="EnsureNoActionReferences"/> が確かめる）。
    /// </para>
    /// <para>
    /// 照合 7：Versioned で ServerWins の実行（ダウンロード以外）は、サーバーのどの行も変えない。出典は docs/code-generation.ja.md の
    /// 「競合」の節のポリシーの表の <c>ServerWins</c> の行（<c>Versioned</c> のランは何も送らず、範囲のテーブルの未送信の変更を
    /// 全部捨てて、その各行をサーバーの行で戻す）。後勝ち＋ServerWins は対象の外（同じ行が、<c>LastWriteWins</c> のランはいつも
    /// どおり送ると書く）。
    /// </para>
    /// </remarks>
    private void CheckServerWritesInScope(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter
    )
    {
        var changed = frame
            .ServerBefore.Keys.Union(serverAfter.Keys)
            .Where(key =>
                !WrittenDuringRun(key)
                && !string.Equals(
                    frame.ServerBefore.GetValueOrDefault(key),
                    serverAfter.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
            )
            .ToList();
        var outOfScope = changed
            .Where(key => !frame.WritesServer || !frame.InScope(RowKey(key).Table))
            .ToList();
        Tally(OutOfScopeWriteCheck, violation: outOfScope.Count > 0);

        foreach (var key in outOfScope)
        {
            _runFailures.Add(
                new(
                    $"5/out-of-scope-write:{key.Table}",
                    $"{frame.Device} {key.Table}/{key.Key}: {frame.Description} changed the server row from "
                        + $"{frame.ServerBefore.GetValueOrDefault(key) ?? "(none)"} to "
                        + $"{serverAfter.GetValueOrDefault(key) ?? "(none)"}, outside the run's scope"
                )
            );
        }

        if (!frame.VersionedServerWins)
        {
            return;
        }

        Tally(ServerWinsWroteCheck, violation: changed.Count > 0);

        foreach (var key in changed)
        {
            _runFailures.Add(
                new(
                    $"5/serverwins-wrote:{key.Table}",
                    $"{frame.Device} {key.Table}/{key.Key}: {frame.Description} changed the server row from "
                        + $"{frame.ServerBefore.GetValueOrDefault(key) ?? "(none)"} to "
                        + $"{serverAfter.GetValueOrDefault(key) ?? "(none)"}, though a Versioned ServerWins run does not write"
                )
            );
        }
    }

    /// <summary>
    /// 照合 8：実行が報告した追加・更新の競合（削除の競合は外す）と DiscardedByServerWins の行のキーに、実行の始めに端末の持ち分
    /// （行か blob の品目）があったか（<c>5/unowned-send:{表}</c>）。実行の間の割り込みのキーと、エンジンの記録を直接作る・変える手が
    /// 触れたキー（エンジンの外から記録を作った形で、端末の書き込みではない）は外す
    /// </summary>
    private void CheckUnownedSends(
        RunFrame frame,
        IReadOnlyList<SyncConflict> conflicts,
        IReadOnlyList<SyncLocalRowChange>? changes,
        bool carries
    )
    {
        var sent = conflicts
            .Where(c => c.Operation == SyncOperation.Upsert)
            .Select(c =>
                (c.TableName, CanonicalKey(c.TableName, c.KeyText), $"conflict {c.Reason}")
            )
            .Concat(
                (changes ?? [])
                    .Where(c => c.Reason == SyncLocalRowChangeReason.DiscardedByServerWins)
                    .Select(c =>
                        (
                            c.TableName,
                            CanonicalKey(c.TableName, c.KeyText),
                            $"local row change {c.Reason}"
                        )
                    )
            )
            .Distinct()
            .ToList();

        foreach (var (table, key, what) in sent)
        {
            if (WrittenDuringRun((table, key)) || RecordTouched(frame.Device, table, key))
            {
                continue;
            }

            // 持ち分か、記録が残っているかもしれないキーの控え（実行の始めの値＝外すのは実行の終わり）があれば持ち分あり
            var owned =
                frame.Holdings.ContainsKey((table, key))
                || (table == ModelTables.Items && frame.Holdings.ContainsKey((PayloadTable, key)))
                || _mayHoldRecord.Contains((frame.Device, table, key));

            // 宣言が偽のエンジンで、例外で終わった ServerWins の実行が戻した行の送信は、数えるだけ
            var resend =
                !owned
                && !carries
                && _restoredByThrownServerWins.Contains((frame.Device, table, key));
            Tally(UnownedSendCheck, violation: !owned && !resend);

            if (resend)
            {
                Tally(ResendAfterThrownServerWinsCheck, violation: false);
            }

            if (owned || resend)
            {
                continue;
            }

            _runFailures.Add(
                new(
                    $"5/unowned-send:{table}",
                    $"{frame.Device} {table}/{key}: {frame.Description} reported {what} for a row the device "
                        + "held no write of its own on at the start of the run"
                )
            );
        }
    }

    /// <summary>エンジンの記録を直接作る・変える手がそのキーに触れたか（端末の全部の表・その表の全部のキーの印を含む）</summary>
    private bool RecordTouched(ModelActor device, string table, string key) =>
        _recordTouched.Contains((device, null, null))
        || _recordTouched.Contains((device, table, null))
        || _recordTouched.Contains((device, table, key));

    /// <summary>
    /// 照合 6 の前提（モデルの図のサーバーの外部キーはすべて NO ACTION）を確かめる。図の全部のリレーションの削除・更新の動作が
    /// NO ACTION でなければ、名指しの例外で止める
    /// </summary>
    private static readonly Lazy<bool> NoActionReferences = new(() =>
    {
        foreach (
            var diagram in new[]
            {
                SyncModelFixtureDefinition.Build(),
                SyncModelFixtureDefinition.BuildSqliteMirror(),
            }
        )
        {
            foreach (var relationship in diagram.Relationships)
            {
                if (
                    relationship.OnDelete != ForeignKeyReferentialAction.NoAction
                    || relationship.OnUpdate != ForeignKeyReferentialAction.NoAction
                )
                {
                    throw new InvalidOperationException(
                        $"The out-of-scope write check assumes every foreign key of the model diagram is NO ACTION, "
                            + $"but {relationship.ConstraintName} has ON DELETE {relationship.OnDelete} and "
                            + $"ON UPDATE {relationship.OnUpdate}."
                    );
                }
            }
        }

        return true;
    });

    /// <summary>照合 6 の前提を確かめる（プロセスで 1 回）</summary>
    private static void EnsureNoActionReferences() => _ = NoActionReferences.Value;

    /// <summary>
    /// 照合 5：版で守る実行が、端末の持ち分の内容で、端末がそのキーのサーバーの内容を最後に見た後にほかの書き手がサーバーへ書いた
    /// 内容を上書きし、そのキーの競合を報告しなかった（<c>5/unreported-overwrite:{表}</c>）
    /// </summary>
    private void CheckUnreportedOverwrite(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        HashSet<(string, string)> conflicted,
        bool carries
    )
    {
        if (!frame.GuardsVersions)
        {
            return;
        }

        foreach (var key in frame.ServerBefore.Keys.Union(serverAfter.Keys).ToList())
        {
            var before = frame.ServerBefore.GetValueOrDefault(key);
            var after = serverAfter.GetValueOrDefault(key);

            // 実行がサーバーのキーを、実行の前の端末の内容（端末の持ち分）へ変えた（blob が行ごと消えた形は行の品目が見る）
            if (
                string.Equals(before, after, StringComparison.Ordinal)
                || !string.Equals(
                    after,
                    frame.LocalBefore.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || (key.Table == PayloadTable && after is null)
                || WrittenDuringRun(key)
                || !frame.Holdings.ContainsKey(key)
            )
            {
                continue;
            }

            // ほかの書き手が、端末が最後に見た後にサーバーへ書いた内容を上書きし、そのキーの競合を報告していない
            var seen = SeenForOverwrite(frame.Device, key);

            // サーバーに行が無く、端末の持ち分が「行があった」と言えない（端末が作ってから一度もサーバーと同じ内容でそろって
            // いない）なら、ほかの書き手が消したキーへの新しい行の挿入で、上書きではない
            var insertsOwnRow =
                before is null
                && _holdings.TryGetValue((frame.Device, key.Table, key.Key), out var holding)
                && !holding.Existed;
            var overwrote =
                !insertsOwnRow
                && _serverLastWriter.TryGetValue(key, out var last)
                && last.Writer != frame.Device
                && last.Sequence > seen
                && !conflicted.Contains(RowKey(key));

            // 宣言が偽のエンジンで、例外で終わった ServerWins の実行が戻せなかった内容による上書きは、数えるだけ
            var countOnly = overwrote && !carries && frame.ThrownServerWins.Contains(key);
            Tally(UnreportedOverwriteCheck, violation: overwrote && !countOnly);

            if (countOnly)
            {
                Tally(UnreportedOverwriteAfterThrownServerWinsCheck, violation: false);
                continue;
            }

            if (!overwrote)
            {
                continue;
            }

            _runFailures.Add(
                new(
                    $"5/unreported-overwrite:{key.Table}",
                    $"{frame.Device} {key.Table}/{key.Key}: {frame.Description} overwrote {before ?? "(none)"}, "
                        + $"written by {_serverLastWriter[key].Writer} at #{_serverLastWriter[key].Sequence} after "
                        + $"the device last saw the key at #{seen}, with its own {after ?? "(deleted)"} and reported "
                        + "no conflict"
                )
            );
        }
    }

    /// <summary>
    /// 照合 5 が使う「端末がそのキーのサーバーの内容を最後に見た番号」。blob の品目は、その blob の持ち分を書いたときに端末が
    /// その品目の行を最後に見ていた番号も、見たに数える（行の版はほかの書き手の blob の書き込みを含むので、その版の行を降ろした
    /// 後に書いた blob は、新しい版に基づく編集。blob を書いた後で行を見ても数えない＝その blob は古い編集のまま）
    /// </summary>
    private long SeenForOverwrite(ModelActor device, (string Table, string Key) key)
    {
        var seen = _lastSeen.GetValueOrDefault((device, key.Table, key.Key));

        return key.Table == PayloadTable
            ? Math.Max(seen, _rowSeenWhenHeld.GetValueOrDefault((device, key.Table, key.Key)))
            : seen;
    }

    /// <summary>
    /// 実行の終わりの通し番号で、その実行の間にサーバーで変わったキーを、その端末が最後に変えたとし（割り込みの書き込みが変えた
    /// キーは割り込みの書き手のまま）、端末とサーバーで内容が同じ（どちらも無いを含む）キーを、その端末が最後に見たとする
    /// </summary>
    private void NoteServerWritesAndSeen(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter
    )
    {
        var sequence = ++_sequence;

        foreach (var key in frame.ServerBefore.Keys.Union(serverAfter.Keys))
        {
            if (
                !WrittenDuringRun(key)
                && !string.Equals(
                    frame.ServerBefore.GetValueOrDefault(key),
                    serverAfter.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
            )
            {
                _serverLastWriter[key] = (frame.Device, sequence);

                // 端末がこの実行で品目の行を自分の内容でサーバーへ届け、blob の持ち分がまだ残る（blob の送信が失敗した）：
                // 勝ち負けは行の単位で決まるので、残った blob は届けた行の続き。届けた番号を、その blob が基づく行の番号にする
                if (
                    key.Table == ModelTables.Items
                    && serverAfter.TryGetValue(key, out var delivered)
                    && string.Equals(
                        delivered,
                        localAfter.GetValueOrDefault(key),
                        StringComparison.Ordinal
                    )
                    && _holdings.TryGetValue((frame.Device, PayloadTable, key.Key), out var blob)
                    && blob.Content is not null
                )
                {
                    _rowSeenWhenHeld[(frame.Device, PayloadTable, key.Key)] = sequence;
                }
            }
        }

        NoteSeen(frame.Device, serverAfter, localAfter, sequence);
    }

    /// <summary>端末とサーバーで内容が同じ（どちらも無いを含む）キーを、その通し番号で端末が見たとする</summary>
    private void NoteSeen(
        ModelActor device,
        IReadOnlyDictionary<(string Table, string Key), string> server,
        IReadOnlyDictionary<(string Table, string Key), string> local,
        long sequence
    )
    {
        foreach (
            var key in server
                .Keys.Union(local.Keys)
                .Union(_serverLastWriter.Keys)
                .Union(_lastSeen.Keys.Where(k => k.Device == device).Select(k => (k.Table, k.Key)))
                .ToList()
        )
        {
            if (
                string.Equals(
                    server.GetValueOrDefault(key),
                    local.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
            )
            {
                _lastSeen[(device, key.Table, key.Key)] = sequence;
            }
        }
    }

    /// <summary>
    /// 最初の形の後の台帳を始める：初期データ（サーバーの他者の書き込み）を最初の番号でサーバーの他者が最後に変えたとし、続く番号で
    /// 各端末が、端末とサーバーで内容が同じキーを見たとする
    /// </summary>
    private async Task StartLedgerAsync()
    {
        EnsureNoActionReferences();

        var initial = ++_sequence;

        foreach (var key in Contents(_initial).Keys)
        {
            _serverLastWriter.TryAdd(key, (ModelActor.Server, initial));
        }

        var seen = ++_sequence;
        var server = Contents(await ReadLedgerStateAsync(ModelActor.Server));

        foreach (var device in _world.Devices)
        {
            NoteSeen(
                device.Actor,
                server,
                Contents(await ReadLedgerStateAsync(device.Actor)),
                seen
            );
        }
    }

    /// <summary>
    /// 照合 2：ServerWins の実行が戻せなかった行の決定を、後の実行で照らす（決定の後の編集の消失・生きている決定の内容の
    /// 書き込み・果たされた決定）。
    /// </summary>
    /// <returns>決定の後の編集が消えたキー（照合 3 で重ねて名指ししない）と、この実行で果たされた決定のキー</returns>
    private (
        HashSet<(string Table, string Key)> EditLost,
        HashSet<(string Table, string Key)> Fulfilled
    ) CheckCarried(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        HashSet<(string, string)> conflicted,
        HashSet<(string, string)> reported,
        bool judged,
        bool carries
    )
    {
        var editLost = new HashSet<(string Table, string Key)>();
        var fulfilled = new HashSet<(string Table, string Key)>();
        var entries = _carried.Where(p => p.Key.Device == frame.Device).ToList();

        // 宣言が真のエンジンは決定を行の単位で持ち越す：行の決定が決定の後のその行の編集で外れたら、同じ行の blob の決定も
        // 外す（編集した行はもう決定の行ではない）。宣言が偽のエンジンは数えるだけなので、台帳の作り方を変えない
        var editedRows = carries
            ? entries
                .Where(p =>
                    p.Key.Table != PayloadTable
                    && !string.Equals(
                        frame.LocalBefore.GetValueOrDefault((p.Key.Table, p.Key.Key)),
                        p.Value,
                        StringComparison.Ordinal
                    )
                )
                .Select(p => (p.Key.Table, p.Key.Key))
                .ToHashSet()
            : [];

        foreach (var (entry, decided) in entries)
        {
            var key = (entry.Table, entry.Key);
            var localBefore = frame.LocalBefore.GetValueOrDefault(key);
            var localNow = localAfter.GetValueOrDefault(key);
            var serverBefore = frame.ServerBefore.GetValueOrDefault(key);
            var serverNow = serverAfter.GetValueOrDefault(key);

            // 行の決定が編集で外れた行の blob の決定（blob 自身の編集は下の枝が見る）
            if (
                entry.Table == PayloadTable
                && editedRows.Contains(RowKey(key))
                && string.Equals(localBefore, decided, StringComparison.Ordinal)
            )
            {
                _carried.Remove(entry);
                continue;
            }

            // 逆向き：決定の後に端末が blob を書いた品目の行の決定（宣言が真のエンジンは、後の書き込みが blob だけでも決定を
            // 行ごと外し、行を blob と一緒に送る。サーバーが知らない行の blob は、行を送らなければ届かない）。端末の書き込み
            // だけを数え、エンジンが写した blob では外さない。blob 自身の編集が消えないかは、blob の決定の枝と照合 3 が見る
            if (
                carries
                && entry.Table == ModelTables.Items
                && string.Equals(localBefore, decided, StringComparison.Ordinal)
                && _blobWrittenAfterHeld.Contains(entry)
            )
            {
                _carried.Remove(entry);
                continue;
            }

            // 決定の後に編集された：決定は消える。その編集は報告なしに消えてはならない（宣言に依らず違反）
            if (!string.Equals(localBefore, decided, StringComparison.Ordinal))
            {
                _carried.Remove(entry);

                if (
                    !judged
                    || WrittenDuringRun(key)
                    || string.Equals(localNow, localBefore, StringComparison.Ordinal)
                    || string.Equals(serverNow, localBefore, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                editLost.Add(key);
                var exempt = Discardable(frame, key, conflicted, reported);
                Tally(CarryEditLostCheck, violation: !exempt);

                if (!exempt)
                {
                    _runFailures.Add(
                        new(
                            $"5/carry:edit-lost:{entry.Table}",
                            $"{frame.Device} {entry.Table}/{entry.Key}: edited to {localBefore ?? "(deleted)"} after "
                                + $"the held ServerWins decision ({decided ?? "(deleted)"}), the edit is neither on the "
                                + $"device ({localNow ?? "(none)"}) nor on the server ({serverNow ?? "(none)"}) after "
                                + $"{frame.Description}, with no conflict or local row change reported"
                        )
                    );
                }

                continue;
            }

            // 生きている決定の内容を、この実行がサーバーへ書いた（実行の前にほかの書き手が同じ内容にしたのは数えない）
            if (
                frame.WritesServer
                && string.Equals(serverNow, decided, StringComparison.Ordinal)
                && !string.Equals(serverBefore, decided, StringComparison.Ordinal)
                && !WrittenDuringRun(key)
            )
            {
                _carried.Remove(entry);
                Tally(CarryOverwroteCheck, violation: carries);

                if (carries)
                {
                    _runFailures.Add(
                        new(
                            $"5/carry:overwrote:{entry.Table}",
                            $"{frame.Device} {entry.Table}/{entry.Key}: {frame.Description} wrote the content the "
                                + $"held ServerWins decision discards ({decided ?? "(deleted)"}) to the server"
                        )
                    );
                }

                continue;
            }

            // 果たされた：端末の行がサーバーの行になった
            if (string.Equals(localNow, serverNow, StringComparison.Ordinal))
            {
                _carried.Remove(entry);
                fulfilled.Add(key);
                Tally(CarryFulfilledCheck, violation: false);

                // 宣言が真のエンジンだけ、その行の期待から端末の書き込みを外す（消失なし・根拠のない競合）
                if (carries)
                {
                    var row = RowKey(key);
                    _carryFulfilled.Add((frame.Device, row.Table, row.Key));

                    if (!_conflictsTo.TryGetValue(row, out var set))
                    {
                        _conflictsTo[row] = set = [];
                    }

                    set.Add(frame.Device);
                }

                continue;
            }

            // エンジンが端末の行を決定の内容でもサーバーの内容でもないものへ変えた：同じ決定ではなくなった
            if (!string.Equals(localNow, decided, StringComparison.Ordinal))
            {
                _carried.Remove(entry);
            }
        }

        return (editLost, fulfilled);
    }

    /// <summary>
    /// 照合 3：実行の始めの持ち分の内容が、実行の後に端末にもサーバーにも無い（<c>5/local-discard:{表}</c>）。判定した持ち分は
    /// 下ろす（同じ消失を次の実行で重ねて名指ししない）
    /// </summary>
    private void CheckLocalDiscard(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        bool threw,
        HashSet<(string, string)> reported,
        bool judged,
        HashSet<(string Table, string Key)> editLost,
        HashSet<(string Table, string Key)> fulfilled,
        bool carries,
        HashSet<(string Table, string Key)> movedSinceSeen,
        HashSet<(string Table, string Key)> blobReadFailed
    )
    {
        var judgedRows = new HashSet<(string Table, string Key)>();

        // 行の持ち分を blob の持ち分より先に見る
        foreach (
            var (key, held) in frame.Holdings.OrderBy(p => p.Key.Table == PayloadTable ? 1 : 0)
        )
        {
            var localNow = localAfter.GetValueOrDefault(key);
            var serverNow = serverAfter.GetValueOrDefault(key);

            if (
                WrittenDuringRun(key)
                || string.Equals(localNow, held, StringComparison.Ordinal)
                || string.Equals(serverNow, held, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (editLost.Contains(key))
            {
                _holdings.Remove((frame.Device, key.Table, key.Key));
                continue;
            }

            // 戻された（端末の行とサーバーの行が、持ち分と違う同じ内容になった）は、結果が無くても実行の前後の DB で分かる。
            // 戻してよい実行（その実行がそのキーを報告した・Force の洗い替え・単独の UploadAsync の ServerWins でその表が範囲）
            // でなければ違反。SyncAsync の ServerWins は報告で決めるので、一覧を運ばなかった例外の実行は判定しない
            var restored = string.Equals(localNow, serverNow, StringComparison.Ordinal);
            var allowed = MayDiscard(frame, key, reported);

            if (!allowed && (restored ? !judged && frame.ServerWinsRun && frame.SyncKind : !judged))
            {
                Tally(LocalDiscardUnjudgedCheck, violation: false);
                continue;
            }

            _holdings.Remove((frame.Device, key.Table, key.Key));

            // 行の持ち分と同じ実行で消えた blob の持ち分は、行の持ち分が名指しする（1 つの形に 1 つの署名）
            if (key.Table == PayloadTable && judgedRows.Contains(RowKey(key)))
            {
                continue;
            }

            if (key.Table != PayloadTable)
            {
                judgedRows.Add(key);
            }

            if (allowed || (carries && fulfilled.Contains(key)))
            {
                Tally(LocalDiscardCheck, violation: false);
                continue;
            }

            // 宣言が偽のエンジンで、例外で終わった ServerWins の SyncAsync が範囲の表の行を報告なしに戻した形は、行を書いた後で
            // 例外になる 2 つの窓のどちらかに当たるときだけ数える：(a) 決着の側＝この実行でこの端末のその品目の blob の読み出しが
            // 失敗した（読み直した行を書いた後の blob のコピー）、(b) ダウンロードの側＝端末がそのキーを最後に見た後に、ほかの
            // 書き手がサーバーのその行を変えていた（ダウンロードが行を書いた後で、決着の前に例外）
            if (
                !carries
                && restored
                && threw
                && frame.ServerWinsRun
                && frame.SyncKind
                && frame.InScope(RowKey(key).Table)
                && (blobReadFailed.Contains(RowKey(key)) || movedSinceSeen.Contains(key))
            )
            {
                Tally(LocalDiscardUnreportedInThrownServerWinsCheck, violation: false);
                continue;
            }

            // 宣言が偽のエンジンで、例外で終わった実行が持ち込んだ行の削除の持ち分が、報告なしに消えた（端末に行が戻った）形は、
            // 数えるだけ
            if (
                !carries
                && held is null
                && localNow is not null
                && _broughtInByThrownRun.Contains((frame.Device, key.Table, key.Key))
            )
            {
                Tally(LocalDiscardOfDeleteOnRowBroughtInByThrownRunCheck, violation: false);
                continue;
            }

            // 宣言が偽のエンジンで、実行の始めに戻せなかった行（決定か、例外で終わった ServerWins の実行の行）だったキーは、
            // 別の種類で数えるだけ
            if (!carries && (frame.Carried.Contains(key) || frame.ThrownServerWins.Contains(key)))
            {
                Tally(LocalDiscardAfterUnreportedServerWinsCheck, violation: false);
                continue;
            }

            // 宣言が真のエンジンで、例外で終わった ServerWins の実行の行だったキーは、別の種類で数えるだけ（その実行の後の扱いは
            // エンジンの振る舞いに合わせて決める）
            if (carries && frame.ThrownServerWins.Contains(key))
            {
                Tally(LocalDiscardAfterThrownServerWinsCheck, violation: false);
                continue;
            }

            Tally(LocalDiscardCheck, violation: true);
            _runFailures.Add(
                new(
                    $"5/local-discard:{key.Table}",
                    $"{frame.Device} {key.Table}/{key.Key}: its own write {held ?? "(deleted)"} is neither on the "
                        + $"device ({localNow ?? "(none)"}) nor on the server ({serverNow ?? "(none)"}) after "
                        + $"{frame.Description}, with no conflict or local row change reported"
                )
            );
        }
    }

    /// <summary>
    /// 照合 3 で、持ち分の内容が端末から消えてよい実行か：その実行がそのキー（blob の品目はその行）を
    /// <see cref="SyncLocalRowChange"/> で報告した・Force の洗い替え・単独の UploadAsync の ServerWins でその表が範囲にある。
    /// 競合の報告だけでは免除しない（docs の「競合」の節：Collect は競合を報告し、ローカルの行はその場に残る）。SyncAsync の
    /// ServerWins は、範囲の表というだけでは免除せず報告で決める（docs の LocalRowChanges の説明：実行が変更を捨てた
    /// ローカルの行を名指しし、入れるのは SyncAsync だけ）
    /// </summary>
    private static bool MayDiscard(
        RunFrame frame,
        (string Table, string Key) key,
        HashSet<(string, string)> reported
    ) =>
        reported.Contains(RowKey(key))
        || frame.ForceRefresh
        || (frame.ServerWinsRun && !frame.SyncKind && frame.InScope(RowKey(key).Table));

    /// <summary>
    /// 端末の内容が消えてよい実行か（そのキーの競合か自分から変えた行が報告された・ServerWins の実行でその表が範囲にある・
    /// Force の洗い替え）。決定の後の編集の消失（照合 2）が使う
    /// </summary>
    private static bool Discardable(
        RunFrame frame,
        (string Table, string Key) key,
        HashSet<(string, string)> conflicted,
        HashSet<(string, string)> reported
    ) =>
        conflicted.Contains(RowKey(key))
        || reported.Contains(RowKey(key))
        || (frame.ServerWinsRun && frame.InScope(RowKey(key).Table))
        || frame.ForceRefresh;

    /// <summary>実行の間に割り込みの書き込みがそのキー（blob の品目はその行も）を変えたか</summary>
    private bool WrittenDuringRun((string Table, string Key) key) =>
        _writtenDuringRun.Contains(key) || _writtenDuringRun.Contains(RowKey(key));

    /// <summary>
    /// ServerWins の実行の後、戻せなかった行の決定を作る：実行の始めに持ち分があり、実行の後も端末の行がサーバーと違い、
    /// 実行の間に端末の行が変わっておらず、そのキーの競合が報告されなかった（範囲の表だけ）。宣言が真のエンジンは決定を
    /// 行の単位で持つので、実行の間にその行（blob を除く列）が変わった品目の blob の決定も作らない
    /// </summary>
    private void HoldServerWinsDecisions(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter,
        HashSet<(string, string)> conflicted,
        bool carries
    )
    {
        foreach (var key in frame.Holdings.Keys)
        {
            var localNow = localAfter.GetValueOrDefault(key);

            if (
                carries
                && key.Table == PayloadTable
                && !string.Equals(
                    localAfter.GetValueOrDefault(RowKey(key)),
                    frame.LocalBefore.GetValueOrDefault(RowKey(key)),
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                !frame.InScope(RowKey(key).Table)
                || string.Equals(
                    localNow,
                    serverAfter.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    localNow,
                    frame.LocalBefore.GetValueOrDefault(key),
                    StringComparison.Ordinal
                )
                || conflicted.Contains(RowKey(key))
                || _carried.ContainsKey((frame.Device, key.Table, key.Key))
            )
            {
                continue;
            }

            _carried[(frame.Device, key.Table, key.Key)] = localNow;

            // 新しい決定の前の blob の書き込みは、この決定の後の書き込みではない
            _blobWrittenAfterHeld.Remove((frame.Device, key.Table, key.Key));

            Tally(CarryHeldCheck, violation: false);
        }
    }

    /// <summary>
    /// 端末の実行の後に、「サーバーが知っているか」のモデルの観測を更新する：そのキーの行が端末とサーバーの両方にあって内容
    /// （blob を除く列）が同じなら知っている（届けたか降ろした）。両方に無く、端末がそのキーの持ち分を持たなければ知らない
    /// （削除が届いた・削除伝搬で消えた）。ただし「知らない」へ戻すのは、未送信を片付けうる実行（ダウンロードだけでない同期と
    /// 洗い替え）が例外なく終わった後の、その実行の範囲の表だけ。それ以外は前の答えを保つ。例外で終わった実行の後の観測と、
    /// 実行の間に書かれたキーの観測は、決められないとして控える
    /// </summary>
    /// <param name="device">端末</param>
    /// <param name="server">実行の後のサーバー</param>
    /// <param name="local">実行の後の端末</param>
    /// <param name="threw">実行が例外で終わったか</param>
    /// <param name="forgets">両方に行が無いキーを「知らない」へ戻してよい実行か</param>
    /// <param name="inScope">その実行の範囲の表か（「知らない」へ戻すのは範囲の表だけ）</param>
    private void ObserveServerKnows(
        ModelActor device,
        ModelDbState server,
        ModelDbState local,
        bool threw,
        bool forgets,
        Func<string, bool> inScope
    )
    {
        foreach (var table in ModelTables.All.Where(t => t != ModelTables.Labels))
        {
            // 「知らない」へ倒すのは、その実行の範囲の表だけ（範囲の外の表の未送信は、その実行では片付かない）
            var forgetsTable = forgets && inScope(table);
            var serverRows = server.Rows(table);
            var localRows = local.Rows(table);
            var keys = serverRows
                .Keys.Concat(localRows.Keys)
                .Concat(
                    _serverKnows
                        .Keys.Where(k => k.Device == device && k.Table == table)
                        .Select(k => k.Key)
                )
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var key in keys)
            {
                var s = serverRows.GetValueOrDefault(key);
                var l = localRows.GetValueOrDefault(key);
                var undetermined = threw || _writtenDuringRun.Contains((table, key));

                if (s is not null && string.Equals(s, l, StringComparison.Ordinal))
                {
                    _serverKnows[(device, table, key)] = (true, undetermined);
                }
                else if (
                    forgetsTable
                    && s is null
                    && l is null
                    && !_holdings.ContainsKey((device, table, key))
                )
                {
                    _serverKnows[(device, table, key)] = (false, undetermined);
                }
                else if (_writtenDuringRun.Contains((table, key)))
                {
                    _serverKnows[(device, table, key)] = (
                        _serverKnows.GetValueOrDefault((device, table, key)).Known,
                        true
                    );
                }
            }
        }
    }

    /// <summary>
    /// 照合 4：状態の面の「サーバーが知っているか」を、モデルの観測と比べる（<c>5/facet:server-knows:{表}</c>）。比べるのは
    /// 判定役がその答えを読む行（端末に行が無く削除の未送信がある行）だけ。版なしの表のモデルの答えは常に真。一度も観測して
    /// いないキーは知らない。モデルの答えを決められないキーと、エンジンの記録を直接作る・変える手が触れたキーは、決められないと
    /// して数える
    /// </summary>
    private void CompareServerKnows(
        ModelActor device,
        ModelDbState local,
        IReadOnlyList<PendingGroup> pending
    )
    {
        foreach (var row in pending)
        {
            if (!row.HasDelete || local.Row(row.Table, row.Key) is not null)
            {
                continue;
            }

            var (expected, undetermined) =
                row.Table == ModelTables.Labels ? (true, false)
                : _serverKnows.TryGetValue((device, row.Table, row.Key), out var known) ? known
                : (false, false);

            if (
                undetermined
                || _recordTouched.Contains((device, null, null))
                || _recordTouched.Contains((device, row.Table, null))
                || _recordTouched.Contains((device, row.Table, row.Key))
            )
            {
                Tally(FacetServerKnowsUndeterminedCheck, violation: false);
                continue;
            }

            var violation = expected != row.ServerKnows;
            Tally(FacetServerKnowsCheck, violation);

            if (violation)
            {
                _runFailures.Add(
                    new(
                        $"5/facet:server-knows:{row.Table}",
                        $"{device} {row.Table}/{row.Key}: the device state says the server "
                            + $"{(row.ServerKnows ? "knows" : "does not know")} the row, the model observed that it "
                            + $"{(expected ? "does" : "does not")}"
                    )
                );
            }
        }
    }

    /// <summary>例外が運ぶ、実行が自分から変えたローカルの行（運ばなければ null）</summary>
    private static IReadOnlyList<SyncLocalRowChange>? ThrownLocalRowChanges(Exception ex) =>
        ex.Data[SyncEngine.LocalRowChangesDataKey] as IReadOnlyList<SyncLocalRowChange>;

    private void Tally(string kind, bool violation)
    {
        var (count, violations) = _runChecks.GetValueOrDefault(kind);
        _runChecks[kind] = (count + 1, violations + (violation ? 1 : 0));
    }
}
