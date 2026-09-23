using QuickER.CodeGen.CSharp.Resources;
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

    /// <summary>ER 図定義から EF Core（DbContext・Fluent 構成）の生成モデルを構築する（GenerateEfCoreRepositories が OFF のときは null）</summary>
    /// <remarks>
    /// 主キーの無いテーブルは DbContext から除外する（DbSet・Fluent 構成を出さず、当該テーブルが絡む
    /// リレーション構成もスキップし、<c>modelBuilder.Ignore&lt;T&gt;()</c> を明示・Warning で名指しする）。
    /// EF Core はマップされる全エンティティ型にキーを要求するため、キーの無い型が 1 つでもモデルへ入ると
    /// DbContext 全体が初回使用時に例外になる。keyless 型（HasNoKey）はリレーションの principal に
    /// なれず keyless 型へのナビゲーションも持てないため、FK で子として参加する図では結局モデル検証
    /// 例外へ戻る＝除外が QuickER 版 Repository のスキップ（単一主キーのみ）と同じ線で対称になる。
    /// </remarks>
    private CSharpEfCoreModel? BuildEfCoreModel(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GenerateEfCoreRepositories)
        {
            return null;
        }

        var mappedEntities = diagram.Entities.Where(HasAnyPrimaryKey).ToList();
        var keylessEntities = diagram.Entities.Where(entity => !HasAnyPrimaryKey(entity)).ToList();

        foreach (var entity in keylessEntities)
        {
            diagnostics.Add(
                GenerationDiagnostic.Warning(
                    string.Format(
                        Strings.CodeGen_Warning_EfCoreKeylessTableExcluded,
                        entity.TableName
                    )
                )
            );
        }

        var dbSets = mappedEntities
            .Select(entity => new CSharpEfCoreDbSetModel
            {
                EntityClassName = _nameConverter.ToEntityClassName(entity.TableName),
                PropertyName = _nameConverter.ToDbSetName(entity.TableName),
            })
            .ToList();

        // principal（親）エンティティ ID → その親が持つリレーション構成の対応を先に組み立てる
        var relationshipsByPrincipal = BuildEfCoreRelationships(diagram);

        var entities = mappedEntities
            .Select(entity => new CSharpEfCoreEntityConfigModel
            {
                EntityClassName = _nameConverter.ToEntityClassName(entity.TableName),
                // Fluent の ToTable("...") へ C# リテラルとして埋め込むためエスケープする（[Table] と同じ規則）
                TableName = EscapeNameForCSharpString(entity.TableName),
                // 複合キーの HasKey は並びが意味を持つため、主キー列は実効順で取り出す
                KeyPropertyNames = entity
                    .GetPrimaryKeyColumnsInOrder()
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
            IgnoredEntityClassNames = keylessEntities
                .Select(entity => _nameConverter.ToEntityClassName(entity.TableName))
                .ToList(),
            IgnoredBaseMembers = EntityBaseIgnoredMembers,
        };
    }

    /// <summary>主キー列を 1 つ以上持つか（EF Core の DbContext へマップできるか）</summary>
    private static bool HasAnyPrimaryKey(Entity entity) =>
        entity.Columns.Any(column => column.IsPrimaryKey);

    /// <summary>カラム定義から EF Core のスカラープロパティ構成モデルを構築する</summary>
    private CSharpEfCorePropertyConfigModel BuildEfCorePropertyConfig(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);

        return new CSharpEfCorePropertyConfigModel
        {
            PropertyName = _nameConverter.ToPropertyName(column.Name),
            // Fluent の HasColumnName("...") へ C# リテラルとして埋め込むためエスケープする（[Column] と同じ規則）
            ColumnName = EscapeNameForCSharpString(column.Name),
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

            // 主キーの無いテーブルは DbContext から除外（Ignore）するため、そのテーブルが絡む
            // リレーションの Fluent 構成も出さない（Ignore された型を参照する構成はモデル構築で落ちる）
            if (!HasAnyPrimaryKey(source) || !HasAnyPrimaryKey(target))
            {
                continue;
            }

            // 複合外部キーは対象外（診断はナビゲーション解決側が 1 度だけ出す）
            if (relationship.ColumnPairs.Count > 1)
            {
                continue;
            }

            // 列ペアが唯一の正本（主キー・IsForeignKey フラグによる推測フォールバックは行わない。
            // ナビゲーション解決と同一規則）
            var columnPair = relationship.ColumnPairs.FirstOrDefault();
            var principalColumn = columnPair is null
                ? null
                : source.Columns.FirstOrDefault(column => column.Id == columnPair.SourceColumnId);
            var dependentColumn = columnPair is null
                ? null
                : target.Columns.FirstOrDefault(column => column.Id == columnPair.TargetColumnId);

            if (principalColumn is null || dependentColumn is null)
            {
                continue;
            }

            var isCollection = relationship.Type == RelationshipType.OneToMany;
            var isSelfReference = source.Id == target.Id;

            // 親列集合が親の主キー集合と一致しないとき（UNIQUE 列参照・複合主キーの一部参照）は
            // HasPrincipalKey を明示する。EF Core の既定は「FK は親の主キーへ結合」なので、
            // 出さないと主キーへ黙って結合される（列ペアは単一ペアのみここへ来る）
            var principalKeyColumns = source.GetPrimaryKeyColumnsInOrder();
            var referencesPrimaryKey =
                principalKeyColumns.Count == 1 && principalKeyColumns[0].Id == principalColumn.Id;

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
                PrincipalKeyPropertyNames = referencesPrimaryKey
                    ? []
                    : [_nameConverter.ToPropertyName(principalColumn.Name)],
                // 図の参照アクション → EF Core の DeleteBehavior（実挙動を観測して決めた写像）。
                // NoAction / Restrict はクライアント挙動が同一（必須関係＝保存前に例外・任意関係＝
                // 追跡中の子の FK を null 化）なので図の宣言どおり NoAction を出す。SetNull は EF が
                // 追跡中の子を、DB の ON DELETE SET NULL が未追跡の子を、同じ結論へ揃える。
                // SET DEFAULT だけは EF に対応値が無く、ClientNoAction（EF は子へ触れず DELETE だけ
                // 送る）でないと EF が子の FK を null へ更新して DB の既定値設定を横取りする
                DeleteBehavior = relationship.OnDelete switch
                {
                    ForeignKeyReferentialAction.Cascade => "Cascade",
                    ForeignKeyReferentialAction.SetNull => "SetNull",
                    ForeignKeyReferentialAction.SetDefault => "ClientNoAction",
                    _ => "NoAction",
                },
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
