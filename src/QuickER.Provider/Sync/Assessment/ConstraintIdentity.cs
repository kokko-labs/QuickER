using QuickER.Model;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>計画の文が外す・付ける制約の種類</summary>
internal enum ConstraintKind
{
    /// <summary>外部キー</summary>
    ForeignKey,

    /// <summary>一意制約</summary>
    UniqueConstraint,

    /// <summary>主キー</summary>
    PrimaryKey,
}

/// <summary>
/// 計画の文が外す・付ける制約の身元（外す文と付け直す文を組み、警告で名指しする単位）
/// </summary>
/// <remarks>
/// <para>
/// 外部キーは子の表と実効名、一意制約は表と実効名（<see cref="UniqueConstraintNaming.Resolve"/>）、主キーは表で決まる（主キーの名前は
/// <c>PRIMARY KEY</c>）。同じ身元かは <see cref="Key"/> の序数一致で決める（表と名前は <see cref="SchemaNames.Key"/> で畳む。主キーは
/// 表だけを見る）。
/// </para>
/// <para><see cref="Table"/> と <see cref="Name"/> は名指しに使う綴り（表は保存する形）。</para>
/// </remarks>
/// <param name="Kind">制約の種類</param>
/// <param name="Table">表（外部キーは子の表）</param>
/// <param name="Name">実効名</param>
internal sealed record ConstraintIdentity(ConstraintKind Kind, string Table, string Name)
{
    /// <summary>主キーの名指しの名前</summary>
    public const string PrimaryKeyName = "PRIMARY KEY";

    /// <summary>同じ身元を見分ける照合キー</summary>
    public string Key =>
        Kind switch
        {
            ConstraintKind.ForeignKey => "FK"
                + SignatureText.Part(SchemaNames.Key(Table))
                + SignatureText.Part(SchemaNames.Key(Name)),
            ConstraintKind.UniqueConstraint => "UQ"
                + SignatureText.Part(SchemaNames.Key(Table))
                + SignatureText.Part(SchemaNames.Key(Name)),
            _ => "PK" + SignatureText.Part(SchemaNames.Key(Table)),
        };
}
