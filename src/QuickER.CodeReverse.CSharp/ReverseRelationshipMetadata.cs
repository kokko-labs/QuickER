using QuickER.Model;

namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// コード上で <b>明示されていた</b> 外部キーメタデータ（<c>[NavigationReference]</c> の名前付き引数）。
/// </summary>
/// <remarks>
/// <para>
/// 各フィールドは「コードに書かれていなかった＝未指定」を <c>null</c> で表す。
/// <see cref="Relationship.OnDelete"/> / <see cref="Relationship.OnUpdate"/> は列挙体の既定値
/// （<see cref="ForeignKeyReferentialAction.NoAction"/>）を持つため、リレーション本体だけでは
/// 「コードが NoAction と言っている」と「コードが何も言っていない」を区別できない。
/// この区別が要るのは GUI マージの温存判断（<see cref="ReverseMergePostProcessor"/>）だけなので、
/// リレーション本体とは別の側チャネルとして <see cref="CodeReverseResult.RelationshipMetadata"/> で運ぶ。
/// </para>
/// <para>
/// 解釈できない参照アクション（未知トークン）は「未指定」として扱う（値を主張できないため。
/// 解析側は非致命の警告を出し、リレーション本体は既定値のままになる）。
/// </para>
/// </remarks>
/// <param name="ConstraintName">コードで指定されていた外部キー制約名（未指定は <c>null</c>）</param>
/// <param name="OnDelete">コードで指定されていた親行削除時の参照アクション（未指定・解釈不能は <c>null</c>）</param>
/// <param name="OnUpdate">コードで指定されていた親キー更新時の参照アクション（未指定・解釈不能は <c>null</c>）</param>
public sealed record ReverseRelationshipMetadata(
    string? ConstraintName,
    ForeignKeyReferentialAction? OnDelete,
    ForeignKeyReferentialAction? OnUpdate
)
{
    /// <summary>1 つでも明示されたフィールドがあるか（すべて未指定なら <c>false</c>）</summary>
    public bool HasAnySpecified =>
        ConstraintName is not null || OnDelete is not null || OnUpdate is not null;
}
