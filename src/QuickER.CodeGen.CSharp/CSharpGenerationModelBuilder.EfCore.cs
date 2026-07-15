using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// EF Core 用コード（DbContext と OnModelCreating の Fluent 構成）の生成モデルを構築する部分。
/// </summary>
/// <remarks>
/// 生成される DbContext は既存 Entity をそのまま既存スキーマへ接続する用途（方言非依存・1 本）。
/// スキーマ作成（HasColumnType の焼き込み・Migrations）は範囲外で、列名・必須・最大長・精度・
/// 行バージョン・VO 変換・リレーションのみを構成する。リレーションは principal（親）側にまとめて構成する。
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>EntityBase の永続化対象外メンバー（get/set 可能な公開プロパティ）。Fluent の Ignore で除外する</summary>
    /// <remarks>
    /// EF Core は get-only プロパティ（IsAdded / HasChanges 等）を既定でマップしないが、RowState は get/set のため
    /// マップ対象になり得る。get-only 派生フラグも意図を明示するため併せて Ignore する
    /// </remarks>
    private static readonly IReadOnlyList<string> EntityBaseIgnoredMembers =
    [
        "RowState",
        "IsAdded",
        "IsUpdated",
        "IsRemoved",
        "HasChanges",
    ];

    /// <summary>ER 図定義から EF Core（DbContext・Fluent 構成）の生成モデルを構築する（GenerateEfCore が OFF のときは null）</summary>
    private CSharpEfCoreModel? BuildEfCoreModel(ErDiagram diagram, CodeGenerationOptions options)
    {
        if (!options.GenerateEfCore)
        {
            return null;
        }

        var dbSets = diagram
            .Entities.Select(entity => new CSharpEfCoreDbSetModel
            {
                EntityClassName = _nameConverter.ToEntityClassName(entity.TableName),
                PropertyName = _nameConverter.ToDbSetName(entity.TableName),
            })
            .ToList();

        // principal（親）エンティティ ID → その親が持つリレーション構成の対応を先に組み立てる
        var relationshipsByPrincipal = BuildEfCoreRelationships(diagram);

        var entities = diagram
            .Entities.Select(entity => new CSharpEfCoreEntityConfigModel
            {
                EntityClassName = _nameConverter.ToEntityClassName(entity.TableName),
                TableName = entity.TableName,
                KeyPropertyNames = entity
                    .Columns.Where(column => column.IsPrimaryKey)
                    .Select(column => _nameConverter.ToPropertyName(column.Name))
                    .ToList(),
                Properties = entity.Columns.Select(BuildEfCorePropertyConfig).ToList(),
                Relationships = relationshipsByPrincipal.TryGetValue(
                    entity.Id,
                    out var relationships
                )
                    ? relationships
                    : [],
            })
            .ToList();

        return new CSharpEfCoreModel
        {
            DbSets = dbSets,
            Entities = entities,
            IgnoredBaseMembers = EntityBaseIgnoredMembers,
        };
    }

    /// <summary>カラム定義から EF Core のスカラープロパティ構成モデルを構築する</summary>
    private CSharpEfCorePropertyConfigModel BuildEfCorePropertyConfig(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);

        return new CSharpEfCorePropertyConfigModel
        {
            PropertyName = _nameConverter.ToPropertyName(column.Name),
            ColumnName = column.Name,
            IsRequired = !column.IsNullable,
            // VO は最大長・桁数を VO 内部で検証するため Fluent には出さない（Entity の [MaxLength] 抑制と同じ方針）
            MaxLength = valueObject is not null ? null : typeInfo.MaxLength,
            Precision = valueObject is not null ? null : typeInfo.Precision,
            Scale = valueObject is not null ? null : typeInfo.Scale,
            IsRowVersion = typeInfo.IsRowVersion,
            IsValueObject = valueObject is not null,
            ValueObjectClassName = valueObject?.ClassName ?? string.Empty,
        };
    }

    /// <summary>全リレーションを走査し、principal（親）エンティティ ID ごとの EF Core リレーション構成を解決する</summary>
    /// <remarks>
    /// 多対多・参照先/キー不明のリレーションは <see cref="ResolveAllNavigations"/> と同様にスキップする
    /// （そこで警告は既に追加済みのため、ここでは診断を重複させない）。1 対多・1 対 1 のみを構成する
    /// </remarks>
    private Dictionary<Guid, List<CSharpEfCoreRelationshipConfigModel>> BuildEfCoreRelationships(
        ErDiagram diagram
    )
    {
        var result = new Dictionary<Guid, List<CSharpEfCoreRelationshipConfigModel>>();

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var source = diagram.Entities.FirstOrDefault(item =>
                item.Id == relationship.SourceEntityId
            );
            var target = diagram.Entities.FirstOrDefault(item =>
                item.Id == relationship.TargetEntityId
            );

            if (source is null || target is null)
            {
                continue;
            }

            // 親（source）は参照先カラム＝主キー、子（target）は外部キー列にフォールバックする（ナビゲーション解決と同一規則）
            var sourceColumn = relationship.SourceColumnId is null
                ? null
                : source.Columns.FirstOrDefault(column =>
                    column.Id == relationship.SourceColumnId.Value
                );
            var targetColumn = relationship.TargetColumnId is null
                ? null
                : target.Columns.FirstOrDefault(column =>
                    column.Id == relationship.TargetColumnId.Value
                );
            var principalColumn =
                sourceColumn ?? source.Columns.FirstOrDefault(column => column.IsPrimaryKey);
            var dependentColumn =
                targetColumn ?? target.Columns.FirstOrDefault(column => column.IsForeignKey);

            if (principalColumn is null || dependentColumn is null)
            {
                continue;
            }

            var isCollection = relationship.Type == RelationshipType.OneToMany;
            var isSelfReference = source.Id == target.Id;

            var config = new CSharpEfCoreRelationshipConfigModel
            {
                DependentClassName = _nameConverter.ToEntityClassName(target.TableName),
                // 親側ナビゲーション名: 1 対多は子の複数形、1 対 1 は子の単数形
                PrincipalNavigationName = _nameConverter.ToNavigationName(
                    target.TableName,
                    collection: isCollection
                ),
                // 子側ナビゲーション名: 親への単一参照。ただし自己参照では子（親参照）ナビゲーションを
                // 生成しない（ResolveAllNavigations と同じ規則）ため空にし、テンプレートで WithOne() を無引数にする
                DependentNavigationName = isSelfReference
                    ? string.Empty
                    : _nameConverter.ToNavigationName(source.TableName, collection: false),
                IsCollection = isCollection,
                ForeignKeyPropertyNames = [_nameConverter.ToPropertyName(dependentColumn.Name)],
                // カスケード削除の有無はモデルの OnDelete に従う（Cascade のときのみ連鎖削除）
                CascadeDelete = relationship.OnDelete == ForeignKeyReferentialAction.Cascade,
            };

            if (!result.TryGetValue(source.Id, out var list))
            {
                list = new List<CSharpEfCoreRelationshipConfigModel>();
                result[source.Id] = list;
            }

            list.Add(config);
        }

        return result;
    }
}
