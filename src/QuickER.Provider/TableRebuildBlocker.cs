namespace QuickER.Provider;

/// <summary>
/// テーブル再構築の作り直しを、同期の実行器の接続では必ず止める DB の物 1 つ（DB の外＝アプリで登録した照合順序・関数を使う索引・
/// 仮想の生成列。取込が持ち帰る図の外の情報）
/// </summary>
/// <remarks>
/// <para>
/// 再構築は表を作り直して索引を原文で作り直し、写すときに列を読む。QuickER の接続（実行器・取込）はアプリが登録した照合順序・関数を
/// 持たないので、それを使う索引は作り直せず、それを使う仮想の生成列は読めず、同期全体が巻き戻る。さらに、照合順序を使う索引が 1 つでも
/// あると、作り直しの RENAME がスキーマ全体を読み直すときに断られ、どの表の作り直しも巻き戻る。計画はそうした作り直しの項目を外して
/// 名指しする（<see cref="SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition"/>）。
/// </para>
/// <para>
/// 埋めるのは SQLite の取込だけ（<see cref="SchemaImportResult.RebuildBlockers"/>）。判定は SQL もエラーの文も解析しない（方言の実装の remarks）。
/// </para>
/// </remarks>
/// <param name="Table">物を持つ表（DB の綴り）</param>
/// <param name="Detail">
/// 作り直しを外した表の警告に並べる名指し（索引は <c>INDEX 名前 COLLATE 照合順序</c>・<c>INDEX 名前 FUNCTION</c>、ほかの表の作り直しを
/// 止める索引は <c>INDEX 名前 ON 表 COLLATE 照合順序</c>、仮想の生成列は <c>GENERATED 列 FUNCTION</c>）
/// </param>
/// <param name="BlocksOtherTables">
/// <c>true</c> なら <paramref name="Table"/> 以外のすべての表の作り直しを止める（照合順序を使う索引＝大域）。<c>false</c> なら
/// <paramref name="Table"/> の作り直しだけを止める（局所）
/// </param>
public sealed record TableRebuildBlocker(string Table, string Detail, bool BlocksOtherTables);
