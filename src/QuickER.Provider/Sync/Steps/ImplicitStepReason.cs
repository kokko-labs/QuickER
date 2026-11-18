namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 計画器が、利用者の選んでいない文を計画へ足す理由
/// </summary>
internal enum ImplicitStepReason
{
    /// <summary>主キーの変更で新しく主キーに加わる NULL 許容の列を NOT NULL にする</summary>
    PrimaryKeyColumnNotNull,

    /// <summary>選んだ表・列の削除で端が消える外部キーを外す（付け直さない）</summary>
    VanishingForeignKey,

    /// <summary>選んだ列の削除で構成列が消える一意制約を外す（付け直さない）</summary>
    VanishingUniqueConstraint,

    /// <summary>
    /// 新しい主キーと同じ列集合の一意制約を外す（主キーと同じ列集合の一意制約を持てない方言。付け直さない）
    /// </summary>
    UniqueConstraintReplacedByPrimaryKey,

    /// <summary>ほかの文を流す間だけ外部キーを外して付け直す</summary>
    ForeignKeyRebuild,

    /// <summary>列の変更を流す間だけ主キーを外して同じ構成で付け直す</summary>
    PrimaryKeyRebuild,

    /// <summary>
    /// 列の変更を流す間だけ一意制約を外して付け直す（表・構成列が同じ計画で消える一意制約は外すだけ）
    /// </summary>
    UniqueConstraintRebuild,
}
