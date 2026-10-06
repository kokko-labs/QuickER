using QuickER.Model;

namespace QuickER.Provider.MySql;

/// <summary>
/// MySQL の外部キー参照アクション句を組み立てるヘルパー（DDL 生成と同期スクリプトが共有する）。
/// </summary>
/// <remarks>
/// InnoDB は <c>ON DELETE SET DEFAULT</c> / <c>ON UPDATE SET DEFAULT</c> を宣言として受け付けて保存するが、
/// 効かせない（親を消すと外部キーエラーになる＝実際の動きは <c>NO ACTION</c>）。宣言どおりに動くと誤解させないよう、
/// <c>SET DEFAULT</c> は句を出さずに <c>NO ACTION</c>（既定＝句なし）として書き、注意コメントを添える。
/// 差分の比較も同じく畳む（<see cref="SyncDialectCapabilities.SupportsSetDefaultAction"/>）。
/// </remarks>
public static class MySqlReferentialAction
{
    /// <summary>SET DEFAULT を省いたことを告げる注意コメント（英語＝生成 SQL の固定文の正本）</summary>
    public const string SetDefaultNote =
        "-- Note: MySQL (InnoDB) does not support SET DEFAULT; using NO ACTION";

    /// <summary><c>ON DELETE</c> / <c>ON UPDATE</c> 句を組み立てる（<c>SET DEFAULT</c> は句を出さない）</summary>
    public static string BuildClause(Relationship? relationship) =>
        relationship is null
            ? string.Empty
            : ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
                Effective(relationship.OnDelete),
                Effective(relationship.OnUpdate)
            );

    /// <summary>リレーションが SET DEFAULT を使っていて、注意コメントを添えるべきか</summary>
    public static bool UsesSetDefault(Relationship? relationship) =>
        relationship is not null
        && (
            relationship.OnDelete == ForeignKeyReferentialAction.SetDefault
            || relationship.OnUpdate == ForeignKeyReferentialAction.SetDefault
        );

    /// <summary>MySQL で実際に効く参照アクション（SET DEFAULT は NO ACTION）</summary>
    private static ForeignKeyReferentialAction Effective(ForeignKeyReferentialAction action) =>
        action == ForeignKeyReferentialAction.SetDefault
            ? ForeignKeyReferentialAction.NoAction
            : action;
}
