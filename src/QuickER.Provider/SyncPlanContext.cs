using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// テーブル再構築（rebuild）方言で、live スキーマと選択差分から合成計画を組み立てるための入力。
/// </summary>
/// <remarks>
/// rebuild は「DB 現状（live）＋選択された差分のみ」を合成した定義でテーブルを作り直す。図の定義を直接
/// 使うと未選択の変更が紛れ込みデータ破壊を招くため、live スキーマ（＝差分計算に用いたもの）を必須入力とする。
/// </remarks>
public sealed class SyncPlanContext
{
    /// <summary>取込の結果から、計画の入力（live のスキーマと図の外の情報）を作る</summary>
    /// <remarks>
    /// 取込の結果から計画へ渡す項目（表・リレーション・補助オブジェクト・<c>CREATE TABLE</c> の原文・列の既定値・取込の警告・ビューの判定）は
    /// ここで 1 回だけ写す。呼び出し側（同期の画面・同期計画の総当たり）ごとに写すと、項目を足したときに写し忘れた側だけが
    /// 黙ってその入力の無い計画を作る（警告・除外が画面とテストで割れる）。
    /// </remarks>
    /// <param name="live">DB の取込の結果</param>
    public static SyncPlanContext FromImport(SchemaImportResult live)
    {
        ArgumentNullException.ThrowIfNull(live);

        return new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
            AuxiliaryObjects = live.AuxiliaryObjects,
            TableCreateSql = live.TableCreateSql,
            ColumnDefaults = live.ColumnDefaults,
            ImportWarnings = live.Warnings,
            LiveDependentCheck = live.LiveDependentCheck,
            RebuildBlockers = live.RebuildBlockers,
        };
    }

    /// <summary>DB から取得した現在のエンティティ（合成の土台）。</summary>
    public IReadOnlyList<Entity> LiveEntities { get; init; } = [];

    /// <summary>DB から取得した現在のリレーション（既存 FK 集合の復元に用いる）。</summary>
    public IReadOnlyList<Relationship> LiveRelationships { get; init; } = [];

    /// <summary>DB から取得した補助オブジェクト（再構築で温存するインデックス・トリガー）。</summary>
    /// <remarks>
    /// 一意制約は意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本のため、ここには含まれない
    /// （<see cref="LiveEntities"/> から合成する）。
    /// </remarks>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>
    /// DB から取得したテーブルの <c>CREATE TABLE</c> 文全文（テーブル名がキー・大文字小文字非依存）。
    /// </summary>
    /// <remarks>
    /// 合成には使わない（合成の正本はあくまで <see cref="LiveEntities"/>）。再構築で失われる属性
    /// （列の属性・制約の句・テーブルオプション。一覧は <see cref="TableRebuildAttributeDetector"/>）を検出して
    /// 警告するためだけに参照する。取得できない方言では空のまま＝警告も出ない。
    /// </remarks>
    public IReadOnlyDictionary<string, string> TableCreateSql { get; init; } =
        new Dictionary<string, string>();

    /// <summary>DB から取得した列の既定値（取込の結果＝<see cref="SchemaImportResult.ColumnDefaults"/>）。</summary>
    /// <remarks>
    /// 合成には使わない（図は既定値を持たない）。既定値のある列の型の種類の変更を事前に警告するためだけに参照する
    /// （<see cref="SyncPlanWarningKind.DefaultOnColumnTypeKindChange"/>）。
    /// </remarks>
    public IReadOnlyList<SchemaColumnDefault> ColumnDefaults { get; init; } = [];

    /// <summary>DB の取込の警告（取込の結果＝<see cref="SchemaImportResult.Warnings"/>）。</summary>
    /// <remarks>
    /// 合成には使わない。取込が図へ入れずに捨てた表の中の物（<see cref="SchemaImportWarningKinds.RebuildLosesTableContent"/>）がある表を、
    /// テーブル再構築の方言で作り直さないためだけに参照する（<see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/>）。
    /// </remarks>
    public IReadOnlyList<SchemaImportWarning> ImportWarnings { get; init; } = [];

    /// <summary>
    /// DB のビュー・トリガーが同期後の表の形でも読める・発火できるかを試す入口（取込の結果＝<see cref="SchemaImportResult.LiveDependentCheck"/>）
    /// </summary>
    /// <remarks>
    /// 合成には使わない。ビュー・トリガーが参照する表の列を消す・足す変更と表の削除のうち、ビューを読めなくするもの・トリガーを発火できなく
    /// するものを外すためだけに参照する（<see cref="SyncPlanWarningKind.TableChangeBreaksDependentView"/>・
    /// <see cref="SyncPlanWarningKind.TableChangeBreaksDependentTrigger"/>）。<c>null</c> なら何もしない。
    /// </remarks>
    public ILiveDependentCheck? LiveDependentCheck { get; init; }

    /// <summary>テーブル再構築の作り直しを QuickER の接続では必ず止める DB の物（取込の結果＝<see cref="SchemaImportResult.RebuildBlockers"/>）</summary>
    /// <remarks>
    /// 合成には使わない。DB の外で登録した照合順序・関数を使う索引・仮想の生成列がある DB で、作り直しの項目を外すためだけに参照する
    /// （<see cref="SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition"/>）。空なら何もしない。
    /// </remarks>
    public IReadOnlyList<TableRebuildBlocker> RebuildBlockers { get; init; } = [];
}
