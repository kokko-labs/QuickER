using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 決まった行が EF Core のモデルの探針（<see cref="CodegenProbe.EfCoreModel"/>）へ追加で表明する、モデルの事実 1 つ
/// </summary>
/// <remarks>
/// エンティティ型はモデルの中の CLR 型の名前（<c>OrderEntity</c> など）で引く（名前空間はオプションで変わるので見ない）。
/// </remarks>
internal abstract record EfCoreModelFact
{
    /// <summary>エンティティ型がモデルにある</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact Present(string entityClass) => new PresentFact(entityClass);

    /// <summary>エンティティ型がモデルに無い（主キーの無い表の除外）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact Absent(string entityClass) => new AbsentFact(entityClass);

    /// <summary>エンティティ型がナビゲーションを 1 つも持たない（除外した型への自動発見の経路が無い）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact NoNavigations(string entityClass) =>
        new NoNavigationsFact(entityClass);

    /// <summary>エンティティ型の外部キーがちょうど 1 つで、その結合先の親のキー（と外部キーの列・削除の振る舞い）が期待どおり</summary>
    /// <param name="entityClass">子のエンティティの型名</param>
    /// <param name="principalProperties">親のキーのプロパティ名（並び順どおり。null なら見ない）</param>
    /// <param name="principalIsPrimaryKey">親のキーが主キーか（null なら見ない）</param>
    /// <param name="dependentProperties">外部キーのプロパティ名（null なら見ない）</param>
    /// <param name="deleteBehavior">削除の振る舞い（null なら見ない）</param>
    /// <param name="dependentToPrincipal">子から親へのナビゲーションの名前（null なら見ない・空文字列ならナビゲーションが無い）</param>
    /// <param name="principalToDependent">親から子へのナビゲーションの名前（null なら見ない・空文字列ならナビゲーションが無い）</param>
    public static EfCoreModelFact SingleForeignKey(
        string entityClass,
        IReadOnlyList<string>? principalProperties = null,
        bool? principalIsPrimaryKey = null,
        IReadOnlyList<string>? dependentProperties = null,
        DeleteBehavior? deleteBehavior = null,
        string? dependentToPrincipal = null,
        string? principalToDependent = null
    ) =>
        new SingleForeignKeyFact(
            entityClass,
            principalProperties,
            principalIsPrimaryKey,
            dependentProperties,
            deleteBehavior,
            dependentToPrincipal,
            principalToDependent
        );

    /// <summary>
    /// エンティティ型のプロパティがモデルにあり、指定した項目が期待どおり（null の項目は見ない）
    /// </summary>
    /// <param name="entityClass">エンティティの型名</param>
    /// <param name="property">プロパティ名</param>
    /// <param name="column">列名（リレーショナルの <c>GetColumnName()</c>）</param>
    /// <param name="required">必須か（<c>!IsNullable</c>）</param>
    /// <param name="maxLength">最大長（0 なら「指定なし」を期待する）</param>
    /// <param name="unicode">Unicode か（<c>IsUnicode()</c>）</param>
    /// <param name="unicodeUnspecified"><c>IsUnicode()</c> が未指定（null）か（true なら未指定を期待する。<paramref name="unicode"/> とは併用しない）</param>
    /// <param name="precision">精度</param>
    /// <param name="scale">スケール</param>
    /// <param name="valueGenerated">値の生成</param>
    /// <param name="concurrencyToken">並行トークンか</param>
    /// <param name="providerClrType">値の変換器のプロバイダ側の CLR 型（変換器が無いことを期待するなら <see cref="NoConverter"/>）</param>
    public static EfCoreModelFact Property(
        string entityClass,
        string property,
        string? column = null,
        bool? required = null,
        int? maxLength = null,
        bool? unicode = null,
        int? precision = null,
        int? scale = null,
        ValueGenerated? valueGenerated = null,
        bool? concurrencyToken = null,
        Type? providerClrType = null,
        bool unicodeUnspecified = false
    ) =>
        new PropertyFact(
            entityClass,
            property,
            column,
            required,
            maxLength,
            unicode,
            precision,
            scale,
            valueGenerated,
            concurrencyToken,
            providerClrType,
            unicodeUnspecified
        );

    /// <summary><see cref="Property"/> の <c>providerClrType</c> で「値の変換器が無い」を期待する印</summary>
    public static Type NoConverter { get; } = typeof(NoConverterMarker);

    /// <summary>エンティティ型にそのプロパティが無い（基底のメンバーを無視する構成など）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    /// <param name="property">プロパティ名</param>
    public static EfCoreModelFact NoProperty(string entityClass, string property) =>
        new NoPropertyFact(entityClass, property);

    /// <summary>エンティティ型の主キーのプロパティが、並びも含めて期待どおり</summary>
    /// <param name="entityClass">エンティティの型名</param>
    /// <param name="properties">主キーのプロパティ名（並び順どおり）</param>
    public static EfCoreModelFact PrimaryKey(
        string entityClass,
        IReadOnlyList<string> properties
    ) => new PrimaryKeyFact(entityClass, properties);

    /// <summary>エンティティ型の表名（とスキーマ）が期待どおり</summary>
    /// <param name="entityClass">エンティティの型名</param>
    /// <param name="table">表名</param>
    /// <param name="schema">スキーマ（null ならスキーマ無しを期待する）</param>
    public static EfCoreModelFact Table(string entityClass, string table, string? schema = null) =>
        new TableFact(entityClass, table, schema);

    /// <summary>
    /// エンティティ型の索引のうち、プロパティの並びが <paramref name="properties"/> のものがちょうど 1 つあり、一意かが <paramref name="unique"/>
    /// （索引の総数も <paramref name="totalIndexes"/> を指定すれば見る）
    /// </summary>
    /// <param name="entityClass">エンティティの型名</param>
    /// <param name="properties">索引のプロパティ名（並び順どおり）</param>
    /// <param name="unique">一意か</param>
    /// <param name="totalIndexes">エンティティ型の索引の総数（null なら見ない）</param>
    public static EfCoreModelFact Index(
        string entityClass,
        IReadOnlyList<string> properties,
        bool unique = true,
        int? totalIndexes = null
    ) => new IndexFact(entityClass, properties, unique, totalIndexes);

    /// <summary>エンティティ型が索引を 1 つも持たない（一意制約の無い表に索引が出ない）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact NoIndexes(string entityClass) => new NoIndexesFact(entityClass);

    /// <summary>モデルと照合する（問題を返す）</summary>
    /// <param name="model">組んだモデル</param>
    public abstract IEnumerable<string> Check(IModel model);

    /// <summary>モデルからエンティティ型を CLR 型の名前で引く（無ければ null）</summary>
    /// <param name="model">モデル</param>
    /// <param name="entityClass">エンティティの型名</param>
    public static IEntityType? Find(IModel model, string entityClass) =>
        model.GetEntityTypes().SingleOrDefault(type => type.ClrType.Name == entityClass);

    private sealed record PresentFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model) =>
            Find(model, EntityClass) is null
                ? [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"]
                : [];
    }

    private sealed record AbsentFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model) =>
            Find(model, EntityClass) is null
                ? []
                : [$"探針（EF Core のモデル）: {EntityClass} がモデルにある（除外されるはず）"];
    }

    private sealed record NoNavigationsFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                return [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"];
            }

            var navigations = entity
                .GetNavigations()
                .Select(navigation => navigation.Name)
                .ToList();

            return navigations.Count == 0
                ? []
                :
                [
                    $"探針（EF Core のモデル）: {EntityClass} がナビゲーション [{string.Join(", ", navigations)}] を持つ",
                ];
        }
    }

    private sealed record SingleForeignKeyFact(
        string EntityClass,
        IReadOnlyList<string>? PrincipalProperties,
        bool? PrincipalIsPrimaryKey,
        IReadOnlyList<string>? DependentProperties,
        DeleteBehavior? DeleteBehavior,
        string? DependentToPrincipal,
        string? PrincipalToDependent
    ) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} がモデルに無い";
                yield break;
            }

            var foreignKeys = entity.GetForeignKeys().ToList();

            if (foreignKeys.Count != 1)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーが {foreignKeys.Count} 個（期待 1 個）";
                yield break;
            }

            var foreignKey = foreignKeys[0];
            var principal = foreignKey.PrincipalKey.Properties.Select(p => p.Name).ToList();
            var dependent = foreignKey.Properties.Select(p => p.Name).ToList();

            if (
                PrincipalProperties is not null
                && !principal.SequenceEqual(PrincipalProperties, StringComparer.Ordinal)
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの親のキーが ({string.Join(", ", principal)})（期待 ({string.Join(", ", PrincipalProperties)})）";
            }

            if (
                PrincipalIsPrimaryKey is { } isPrimaryKey
                && foreignKey.PrincipalKey.IsPrimaryKey() != isPrimaryKey
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの親のキーが主キー{(isPrimaryKey ? "でない" : "である（主キーでない親の列へ結合するはず）")}";
            }

            if (
                DependentProperties is not null
                && !dependent.SequenceEqual(DependentProperties, StringComparer.Ordinal)
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの列が ({string.Join(", ", dependent)})（期待 ({string.Join(", ", DependentProperties)})）";
            }

            if (DeleteBehavior is { } behavior && foreignKey.DeleteBehavior != behavior)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの削除の振る舞いが {foreignKey.DeleteBehavior}（期待 {behavior}）";
            }

            var toPrincipal = foreignKey.DependentToPrincipal?.Name ?? "";

            if (
                DependentToPrincipal is { } expectedToPrincipal
                && toPrincipal != expectedToPrincipal
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの子→親のナビゲーションが「{toPrincipal}」（期待「{expectedToPrincipal}」）";
            }

            var toDependent = foreignKey.PrincipalToDependent?.Name ?? "";

            if (
                PrincipalToDependent is { } expectedToDependent
                && toDependent != expectedToDependent
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの親→子のナビゲーションが「{toDependent}」（期待「{expectedToDependent}」）";
            }
        }
    }

    private sealed record PropertyFact(
        string EntityClass,
        string PropertyName,
        string? Column,
        bool? Required,
        int? MaxLength,
        bool? Unicode,
        int? Precision,
        int? Scale,
        ValueGenerated? Generated,
        bool? ConcurrencyToken,
        Type? ProviderClrType,
        bool UnicodeUnspecified
    ) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var where = $"{EntityClass}.{PropertyName}";
            var property = Find(model, EntityClass)?.FindProperty(PropertyName);

            if (property is null)
            {
                yield return $"探針（EF Core のモデル）: {where} がモデルに無い";
                yield break;
            }

            if (Column is not null && property.GetColumnName() != Column)
            {
                yield return $"探針（EF Core のモデル）: {where} の列名が {property.GetColumnName()}（期待 {Column}）";
            }

            if (Required is { } required && property.IsNullable == required)
            {
                yield return $"探針（EF Core のモデル）: {where} が{(property.IsNullable ? " NULL 許容" : "必須")}（期待 {(required ? "必須" : "NULL 許容")}）";
            }

            if (MaxLength is { } maxLength && (property.GetMaxLength() ?? 0) != maxLength)
            {
                yield return $"探針（EF Core のモデル）: {where} の最大長が {Show(property.GetMaxLength())}（期待 {(maxLength == 0 ? "(指定なし)" : Show(maxLength))}）";
            }

            if (Unicode is { } unicode && property.IsUnicode() != unicode)
            {
                yield return $"探針（EF Core のモデル）: {where} の IsUnicode が {property.IsUnicode()?.ToString() ?? "(指定なし)"}（期待 {unicode}）";
            }

            if (UnicodeUnspecified && property.IsUnicode() is not null)
            {
                yield return $"探針（EF Core のモデル）: {where} の IsUnicode が {property.IsUnicode()}（期待 (指定なし)）";
            }

            if (Precision is { } precision && property.GetPrecision() != precision)
            {
                yield return $"探針（EF Core のモデル）: {where} の精度が {Show(property.GetPrecision())}（期待 {Show(precision)}）";
            }

            if (Scale is { } scale && property.GetScale() != scale)
            {
                yield return $"探針（EF Core のモデル）: {where} のスケールが {Show(property.GetScale())}（期待 {Show(scale)}）";
            }

            if (Generated is { } generated && property.ValueGenerated != generated)
            {
                yield return $"探針（EF Core のモデル）: {where} の ValueGenerated が {property.ValueGenerated}（期待 {generated}）";
            }

            if (ConcurrencyToken is { } token && property.IsConcurrencyToken != token)
            {
                yield return $"探針（EF Core のモデル）: {where} の IsConcurrencyToken が {property.IsConcurrencyToken}（期待 {token}）";
            }

            if (ProviderClrType is { } provider)
            {
                var converter = property.GetValueConverter();

                if (provider == NoConverter)
                {
                    if (converter is not null)
                    {
                        yield return $"探針（EF Core のモデル）: {where} に値の変換器がある（{converter.ModelClrType.Name} → {converter.ProviderClrType.Name}）";
                    }
                }
                else if (converter is null)
                {
                    yield return $"探針（EF Core のモデル）: {where} に値の変換器が無い（期待 → {provider.Name}）";
                }
                else if (converter.ProviderClrType != provider)
                {
                    yield return $"探針（EF Core のモデル）: {where} の変換器のプロバイダ側の型が {converter.ProviderClrType.Name}（期待 {provider.Name}）";
                }
            }
        }

        private static string Show(int? value) =>
            value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(指定なし)";
    }

    private sealed record NoPropertyFact(string EntityClass, string PropertyName) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} がモデルに無い";
            }
            else if (entity.FindProperty(PropertyName) is not null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass}.{PropertyName} がモデルにある（無いはず）";
            }
        }
    }

    private sealed record PrimaryKeyFact(string EntityClass, IReadOnlyList<string> Properties)
        : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var key = Find(model, EntityClass)?.FindPrimaryKey();
            var actual = key?.Properties.Select(property => property.Name).ToList() ?? [];

            if (!actual.SequenceEqual(Properties, StringComparer.Ordinal))
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の主キーが ({string.Join(", ", actual)})（期待 ({string.Join(", ", Properties)})）";
            }
        }
    }

    private sealed record TableFact(string EntityClass, string TableName, string? Schema)
        : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} がモデルに無い";
                yield break;
            }

            if (entity.GetTableName() != TableName || entity.GetSchema() != Schema)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の表が {entity.GetSchema() ?? "(スキーマなし)"}.{entity.GetTableName()}（期待 {Schema ?? "(スキーマなし)"}.{TableName}）";
            }
        }
    }

    private sealed record IndexFact(
        string EntityClass,
        IReadOnlyList<string> Properties,
        bool Unique,
        int? TotalIndexes
    ) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} がモデルに無い";
                yield break;
            }

            var indexes = entity.GetIndexes().ToList();
            var matched = indexes
                .Where(index =>
                    index
                        .Properties.Select(property => property.Name)
                        .SequenceEqual(Properties, StringComparer.Ordinal)
                )
                .ToList();

            if (matched.Count != 1)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の索引 ({string.Join(", ", Properties)}) が {matched.Count} 個（期待 1 個）";
            }
            else if (matched[0].IsUnique != Unique)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の索引 ({string.Join(", ", Properties)}) が{(Unique ? "一意でない" : "一意")}";
            }

            if (TotalIndexes is { } total && indexes.Count != total)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の索引が {indexes.Count} 個（期待 {total} 個）";
            }
        }
    }

    private sealed record NoIndexesFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                return [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"];
            }

            var indexes = entity
                .GetIndexes()
                .Select(index => string.Join(", ", index.Properties.Select(p => p.Name)))
                .ToList();

            return indexes.Count == 0
                ? []
                :
                [
                    $"探針（EF Core のモデル）: {EntityClass} が索引 [{string.Join(" / ", indexes)}] を持つ",
                ];
        }
    }

    private sealed class NoConverterMarker;
}
