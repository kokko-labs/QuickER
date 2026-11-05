using QuickER.Services;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>図の写しの面（往復で比べる情報の種類）</summary>
/// <remarks>
/// 面は 2 種類ある。<see cref="FacetKinds.IsExistence"/> の面は「その要素があるか」を鍵の集合で比べ、
/// 残りの属性の面は両方にある要素の値だけを比べる（要素の欠けを属性の違いとして二重に数えない）。
/// <see cref="Structure"/>（表と列そのもの）の欠け・余りは、告知も宣言も理由にならない（注入や取りこぼしで表・列が増減した）。
/// </remarks>
internal enum Facet
{
    /// <summary>表と列そのもの（存在）。欠け・余りは常に失敗</summary>
    Structure,

    /// <summary>表名の綴り</summary>
    TableName,

    /// <summary>列名の綴り</summary>
    ColumnName,

    /// <summary>列の並び</summary>
    ColumnOrder,

    /// <summary>列の型の表記</summary>
    Type,

    /// <summary>列の NULL 許容</summary>
    Nullability,

    /// <summary>主キーの印（存在）</summary>
    PrimaryKey,

    /// <summary>主キーの実効順</summary>
    PrimaryKeyOrder,

    /// <summary>表の説明</summary>
    TableDescription,

    /// <summary>列の説明</summary>
    ColumnDescription,

    /// <summary>表のメモ</summary>
    Memo,

    /// <summary>一意制約（存在・構成列の集合で識別）</summary>
    UniqueSet,

    /// <summary>一意制約の構成列の並び</summary>
    UniqueOrder,

    /// <summary>一意制約の名前（元の図で名前が付いていたもの。未設定は <c>-</c>）</summary>
    UniqueName,

    /// <summary>元の図で名前の無かった一意制約に、取込で名前が付いた</summary>
    UniqueNameFilled,

    /// <summary>多対多でないリレーション（存在）</summary>
    Relationship,

    /// <summary>多対多のリレーション（存在）</summary>
    ManyToMany,

    /// <summary>元の図で列ペアの無かった多対多でないリレーションが、取込で消えた</summary>
    PairlessRelationship,

    /// <summary>元の図で列ペアが 2 組以上だった多対多でないリレーション＝複合外部キーが、取込で消えた（その子の列の印の消失を含む）</summary>
    CompositeRelationship,

    /// <summary>リレーションの種別</summary>
    RelationshipType,

    /// <summary>リレーションの列ペア</summary>
    ColumnPairs,

    /// <summary>リレーションの制約名（元の図で名前が付いていたもの。未設定は <c>-</c>）</summary>
    ConstraintName,

    /// <summary>元の図で名前の無かったリレーションに、取込で名前が付いた</summary>
    ConstraintNameFilled,

    /// <summary>参照アクション（ON DELETE / ON UPDATE）</summary>
    ReferentialAction,

    /// <summary>外部キーの印（存在）</summary>
    ForeignKeyMarker,

    /// <summary>元の図でどのリレーションの子側の列でもない列の外部キーの印が、取込で消えた</summary>
    ForeignKeyMarkerOutsideRelationship,

    /// <summary>元の図でリレーションの子側の列なのに印の無かった列に、取込が外部キーの印を付けた</summary>
    ForeignKeyMarkerDerived,

    /// <summary>元の図で列の無かった表に、取込が列を補った（補った列の主キーの印・列の並びの違いを含む）</summary>
    PlaceholderColumn,

    /// <summary>計算列の印（存在）</summary>
    Computed,

    /// <summary>主キーでない列の採番の印（存在）</summary>
    IdentityNonKey,

    /// <summary>主キーの列の採番の印（存在）</summary>
    IdentityKey,

    /// <summary>名前付きクエリ（存在）</summary>
    Query,

    /// <summary>名前・型の改行・制御文字が空白へ畳まれた違いだけ（ほかの文字は同じ）</summary>
    NameSanitized,

    /// <summary>説明・メモの改行・制御文字が空白へ畳まれた違いだけ（ほかの文字は同じ）</summary>
    DescriptionLineBreaks,

    /// <summary>元の図で NULL 許容だった主キーの列が、取込で NOT NULL になった違いだけ</summary>
    PrimaryKeyNullability,

    /// <summary>多対多のリレーションの列ペアの違いだけ（多対多の列ペアは外部キーでない）</summary>
    ManyToManyPairs,

    /// <summary>元の図で引けない列を指す列ペアを持っていたリレーションの違いだけ（列ペア・リレーション・子の列の印）</summary>
    BrokenColumnPairs,
}

/// <summary>面の分類と、面と告知の種類の対応（1 か所）</summary>
internal static class FacetKinds
{
    /// <summary>名前の面と、元の図で名前が無かったときの面（比べるときに元の値で振り分ける）</summary>
    public static IReadOnlyDictionary<Facet, Facet> FilledFacets { get; } =
        new Dictionary<Facet, Facet>
        {
            [Facet.UniqueName] = Facet.UniqueNameFilled,
            [Facet.ConstraintName] = Facet.ConstraintNameFilled,
        };

    /// <summary>存在を鍵の集合で比べる面</summary>
    public static bool IsExistence(Facet facet) =>
        facet
            is Facet.Structure
                or Facet.PrimaryKey
                or Facet.UniqueSet
                or Facet.Relationship
                or Facet.ManyToMany
                or Facet.CompositeRelationship
                or Facet.PairlessRelationship
                or Facet.ForeignKeyMarker
                or Facet.ForeignKeyMarkerDerived
                or Facet.ForeignKeyMarkerOutsideRelationship
                or Facet.PlaceholderColumn
                or Facet.BrokenColumnPairs
                or Facet.Computed
                or Facet.IdentityNonKey
                or Facet.IdentityKey
                or Facet.Query;

    /// <summary>写しの値が既定（空の説明・名前なし・空の列ペア・既定の参照アクション）か</summary>
    public static bool IsDefault(string value) =>
        value is "" or "\"\"" or "-" or "del=NoAction upd=NoAction";

    /// <summary>属性の面の持ち主の存在の面（持ち主ごと消えた要素の属性は、属性の違いとして数えられない）</summary>
    public static IReadOnlyList<Facet> Owners(Facet facet) =>
        facet switch
        {
            Facet.UniqueOrder or Facet.UniqueName or Facet.UniqueNameFilled => [Facet.UniqueSet],
            Facet.RelationshipType
            or Facet.ColumnPairs
            or Facet.ConstraintName
            or Facet.ConstraintNameFilled
            or Facet.ReferentialAction =>
            [
                Facet.Relationship,
                Facet.ManyToMany,
                Facet.CompositeRelationship,
            ],
            _ => [],
        };

    /// <summary>告知の種類が名指しする面（その種類の告知が説明できる欠落）</summary>
    /// <remarks>
    /// <see cref="Facet.Structure"/> はどの種類にも対応しない（表・列の増減は告知の対象でない）。
    /// 全種類が対応を持つことは <c>FormatRoundTripTests.FacetMap_CoversEveryOmissionKind</c> が照合する。
    /// </remarks>
    public static IReadOnlyDictionary<
        ExportOmissionKind,
        IReadOnlyList<Facet>
    > OmissionFacets { get; } =
        new Dictionary<ExportOmissionKind, IReadOnlyList<Facet>>
        {
            [ExportOmissionKind.TableDescription] = [Facet.TableDescription],
            [ExportOmissionKind.TableMemo] = [Facet.Memo],
            [ExportOmissionKind.ColumnDescription] = [Facet.ColumnDescription],
            [ExportOmissionKind.ColumnNullability] = [Facet.Nullability],
            [ExportOmissionKind.CompositeUniqueConstraint] = [Facet.UniqueSet],
            [ExportOmissionKind.UniqueConstraintName] = [Facet.UniqueName],
            [ExportOmissionKind.ForeignKeyColumnPairs] = [Facet.ColumnPairs],
            [ExportOmissionKind.ReferentialAction] = [Facet.ReferentialAction],
            [ExportOmissionKind.NamedQuery] = [Facet.Query],
            [ExportOmissionKind.ColumnNameNormalized] = [Facet.ColumnName],
            [ExportOmissionKind.PrimaryKeyOrder] = [Facet.PrimaryKeyOrder],
            [ExportOmissionKind.ForeignKeyMarkerWithoutRelationship] =
            [
                Facet.ForeignKeyMarkerOutsideRelationship,
            ],
            [ExportOmissionKind.RelationshipWithoutColumns] =
            [
                Facet.Relationship,
                Facet.ManyToMany,
                Facet.CompositeRelationship,
                Facet.PairlessRelationship,
                Facet.BrokenColumnPairs,
            ],
            [ExportOmissionKind.ComputedColumn] = [Facet.Computed],
            [ExportOmissionKind.TableNameNormalized] = [Facet.TableName],
            [ExportOmissionKind.ColumnTypeNormalized] = [Facet.Type],
            [ExportOmissionKind.UniqueConstraintOnForeignKeyColumn] = [Facet.UniqueSet],
            [ExportOmissionKind.RelationshipConstraintName] = [Facet.ConstraintName],
            [ExportOmissionKind.IdentityColumn] = [Facet.IdentityNonKey, Facet.IdentityKey],
        };
}
