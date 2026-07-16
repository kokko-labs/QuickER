using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>ER 図定義からテンプレート用の C# コード生成モデルを構築するビルダー</summary>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>列名（正規化キー）→ 値オブジェクト生成モデルの対応。GenerateValueObjects が OFF のときは空で、VO 化しない</summary>
    private IReadOnlyDictionary<string, CSharpValueObjectModel> _valueObjects =
        new Dictionary<string, CSharpValueObjectModel>();

    /// <summary>テーブル名・カラム名を C# 識別子へ変換するコンバーター</summary>
    private readonly CSharpNameConverter _nameConverter = new();

    /// <summary>カラム ID → 解決済み C# 型情報。Build 呼び出し時に外部（SQL Server プロバイダ等）から受け取る。
    /// 生成器自体は DB 非依存で、SQL 型のマッピングは行わない</summary>
    private IReadOnlyDictionary<Guid, CSharpTypeInfo> _columnTypes =
        new Dictionary<Guid, CSharpTypeInfo>();

    /// <summary>ER 図定義とオプションから生成モデル全体を構築する</summary>
    /// <param name="diagnostics">生成中に検出した警告などを蓄積する出力先</param>
    /// <remarks>
    /// ナビゲーションは全エンティティ分を一度だけ解決する。以前は Entity / EditModel / Mapper の
    /// 各構築で個別に <see cref="ResolveNavigations"/> を呼んでいたため、同一リレーションの警告が
    /// （エンティティ数 × パス数）回重複していた。解決結果を共有して重複と再計算を防ぐ。
    /// また生成対象として無効なクラス群は構築自体を行わない。
    /// </remarks>
    public CSharpGenerationModel Build(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, CSharpTypeInfo>? queryParameterTypes = null
    )
    {
        _columnTypes = columnTypes;
        _queryTokenTypes =
            queryParameterTypes
            ?? new Dictionary<string, CSharpTypeInfo>(StringComparer.OrdinalIgnoreCase);
        _queriesByEntity = diagram
            .Queries.GroupBy(query => query.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());
        _queryDtoNames.Clear();

        // 参照先エンティティが存在しないクエリ定義（削除済みエンティティの残骸等）は警告してスキップする
        var entityIds = diagram.Entities.Select(entity => entity.Id).ToHashSet();

        foreach (var orphan in diagram.Queries.Where(query => !entityIds.Contains(query.EntityId)))
        {
            diagnostics.Add(
                Warning(string.Format(Strings.CodeGen_Query_UnknownEntity, orphan.Name))
            );
        }

        var navigationsByEntity = ResolveAllNavigations(diagram, diagnostics);
        _valueObjects = BuildValueObjects(diagram, options, diagnostics);

        return new CSharpGenerationModel
        {
            NamespaceName = string.IsNullOrWhiteSpace(options.RootNamespace)
                ? "Generated"
                : options.RootNamespace.Trim(),
            // Entity は全カテゴリの前提のため常に生成する
            EntityClasses = diagram
                .Entities.Select(entity =>
                    BuildEntityClass(entity, navigationsByEntity[entity.Id], diagnostics)
                )
                .ToList(),
            EditModelClasses = options.GenerateEditModels
                ? diagram
                    .Entities.Select(entity =>
                        BuildEditModelClass(entity, navigationsByEntity[entity.Id], diagnostics)
                    )
                    .ToList()
                : [],
            MapperClasses = options.GenerateMappers
                ? diagram
                    .Entities.Select(entity =>
                        BuildMapperClass(entity, navigationsByEntity[entity.Id])
                    )
                    .ToList()
                : [],
            // 共通契約（インターフェイス群・SqlQuery・メタデータ等）と各エンティティ用リポジトリインターフェイスは、
            // QuickER の SQL Server 実装（GenerateRepositories）・EF Core 実装（GenerateEfCore）・インメモリ実装
            // （GenerateInMemoryRepositories）のいずれかが有効なら必要になる。
            // EF Core・インメモリ単独出力でも各 Repository が I{Entity}Repository を実装するため、モデルを構築しておく
            RepositoryClasses = options.GeneratesRepositoryContract
                ? diagram
                    .Entities.Select(entity => BuildRepositoryClass(entity, options, diagnostics))
                    .Where(model => model is not null)
                    .Cast<CSharpRepositoryModel>()
                    .ToList()
                : [],
            ValueObjectClasses = _valueObjects
                .Values.OrderBy(vo => vo.ClassName, StringComparer.Ordinal)
                .ToList(),
            EfCore = BuildEfCoreModel(diagram, options),
        };
    }

    /// <summary>エンティティ定義と解決済みナビゲーションからエンティティクラスの生成モデルを構築する</summary>
    private CSharpClassModel BuildEntityClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var className = _nameConverter.ToEntityClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildProperty).ToList();

        // 列由来プロパティ名が生成する静的メンバー（DisplayName / CustomizeDisplayName）と衝突する場合は、
        // そのエンティティのみ両メンバーを省略する（生成は完走・警告診断を出す）。
        var hasDisplayNameCollision = properties.Any(property =>
            EntityDisplayNameReservedMembers.Contains(property.PropertyName)
        );

        if (hasDisplayNameCollision)
        {
            diagnostics.Add(
                Warning(
                    string.Format(
                        Strings.CodeGen_Warning_EntityDisplayNameCollision,
                        className,
                        entity.TableName
                    )
                )
            );
        }

        return new CSharpClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            // [DbTableMeta(Description = "...")] へ C# リテラルとして埋め込むためエスケープする（未エスケープだと " や \ でコンパイル不能になる）
            Description = EscapeForCSharpString(entity.Description),
            DescriptionXmlDoc = EscapeForXmlDocSummary(entity.Description),
            // 既定表示名: Description があればそれ、無ければクラス名。C# リテラルへエスケープして埋め込む
            DisplayName = string.IsNullOrWhiteSpace(entity.Description)
                ? className
                : EscapeForCSharpString(entity.Description),
            HasDisplayNameCollision = hasDisplayNameCollision,
            Properties = properties,
            Navigations = navigations.Select(BuildEntityNavigation).ToList(),
        };
    }

    /// <summary>Entity が生成する静的表示名メンバー名（列由来プロパティ名がこれらと一致すると衝突とみなす）</summary>
    private static readonly HashSet<string> EntityDisplayNameReservedMembers = new(
        StringComparer.Ordinal
    )
    {
        "DisplayName",
        "CustomizeDisplayName",
    };

    /// <summary>EditModel が生成する表示名解決ヘルパ名（列由来プロパティ名がこれらと一致すると衝突とみなす）</summary>
    private static readonly HashSet<string> EditModelDisplayNameReservedMembers = new(
        StringComparer.Ordinal
    )
    {
        "GetDisplayName",
        "CustomizePropertyDisplayName",
    };

    /// <summary>エンティティ定義と解決済みナビゲーションから EditModel クラスの生成モデルを構築する</summary>
    private CSharpEditModelClassModel BuildEditModelClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var className = _nameConverter.ToEditModelClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildEditModelProperty).ToList();
        var navigationModels = navigations.Select(BuildEditModelNavigation).ToList();

        // 列由来プロパティ名（確定値・バインディング両方）が表示名解決ヘルパ（GetDisplayName /
        // CustomizePropertyDisplayName）と衝突する場合は、この EditModel のみ表示名機構を省略し、
        // 検証メッセージを従来どおりプロパティ名で構築する（生成は完走・警告診断を出す）。
        var hasDisplayNameCollision = properties.Any(property =>
            EditModelDisplayNameReservedMembers.Contains(property.PropertyName)
            || EditModelDisplayNameReservedMembers.Contains(property.BindingPropertyName)
        );

        if (hasDisplayNameCollision)
        {
            diagnostics.Add(
                Warning(
                    string.Format(
                        Strings.CodeGen_Warning_EditModelDisplayNameCollision,
                        className,
                        entity.TableName
                    )
                )
            );
        }

        // 各プロパティの検証メッセージへ渡す表示名式を解決する（衝突時は従来どおり nameof(Prop)）
        properties = properties
            .Select(property =>
                property with
                {
                    DisplayNameExpression = ResolveEditModelDisplayNameExpression(
                        property,
                        hasDisplayNameCollision
                    ),
                }
            )
            .ToList();

        return new CSharpEditModelClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            DescriptionXmlDoc = EscapeForXmlDocSummary(entity.Description),
            Properties = properties,
            Navigations = navigationModels,
            HasCascadeNavigations = navigationModels.Any(navigation => navigation.Cascade),
            TypedParentModelTypeName = ResolveTypedParentModelTypeName(className, navigationModels),
            HasDisplayNameCollision = hasDisplayNameCollision,
            HasNonValueObjectProperty = properties.Any(property => !property.IsValueObject),
        };
    }

    /// <summary>
    /// EditModel プロパティの検証メッセージへ渡す表示名の C# 式を解決する。
    /// VO 有効時は VO の静的 <c>DisplayName</c> を参照（VO 側の Customize 上書きが自動反映）、
    /// VO 無効時は EditModel 内蔵の既定表示名＋<c>CustomizePropertyDisplayName</c> フック経由（<c>GetDisplayName</c> ヘルパ）。
    /// 表示名衝突（<paramref name="hasCollision"/>）のときは従来どおり <c>nameof(Prop)</c> を返す（後方互換）。
    /// </summary>
    private string ResolveEditModelDisplayNameExpression(
        CSharpEditModelPropertyModel property,
        bool hasCollision
    )
    {
        // VO 有効時は VO 側の DisplayName を参照する（衝突は EditModel ヘルパの話で VO には無関係のため優先）
        if (property.IsValueObject)
        {
            return $"{property.ValueObjectClassName}.DisplayName";
        }

        // 衝突時はヘルパを生成しないため、従来どおりプロパティ名を渡す（メッセージは変わらない）
        if (hasCollision)
        {
            return $"nameof({property.PropertyName})";
        }

        // 既定表示名: Description があればそれ、無ければプロパティ名（メッセージの後方互換）
        return $"GetDisplayName(nameof({property.PropertyName}), \"{property.DisplayName}\")";
    }

    /// <summary>型付き ParentModel を生成できる場合の親 EditModel 型名を解決する（親候補型がちょうど 1 つのときのみ）</summary>
    /// <remarks>
    /// 親候補は (1) 親参照ナビ（IsParentReference）の参照先型、(2) 自己参照（自身をカスケード子に持つ）の場合の自分自身の型。
    /// 複数 FK などで親候補型が 2 つ以上になる場合は型付き版を生成せず、基底の EditModelBase? のみとする。
    /// </remarks>
    private static string? ResolveTypedParentModelTypeName(
        string className,
        IReadOnlyList<CSharpNavigationModel> navigationModels
    )
    {
        var parentTypeNames = navigationModels
            .Where(navigation => navigation.IsParentReference)
            .Select(navigation => navigation.TypeName)
            .ToHashSet(StringComparer.Ordinal);

        // 自己参照（自分自身をカスケード子＝コレクション/単一参照で持つ）場合、親候補に自分自身を加える
        if (
            navigationModels.Any(navigation =>
                navigation.Cascade
                && !navigation.IsParentReference
                && string.Equals(navigation.TypeName, className, StringComparison.Ordinal)
            )
        )
        {
            parentTypeNames.Add(className);
        }

        return parentTypeNames.Count == 1 ? parentTypeNames.First() : null;
    }

    /// <summary>エンティティ定義と解決済みナビゲーションから Entity ↔ EditModel 変換 Mapper の生成モデルを構築する</summary>
    private CSharpMapperModel BuildMapperClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations
    )
    {
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var editModelClassName = _nameConverter.ToEditModelClassName(entity.TableName);
        var mapperClassName = _nameConverter.ToMapperClassName(entity.TableName);

        var scalarProperties = entity
            .Columns.Select(column =>
            {
                var property = BuildProperty(column);
                var editModelProperty = BuildEditModelProperty(column);
                // VO は ToString() が型ごとの表現（binary は Base64）を返す。さらに非 NULL の VO 列は
                // = null! 初期化のためロード前は null になり得るので、必須でも null 条件付きで ToString する。
                var isValueObject = ResolveValueObject(column) is not null;
                return new CSharpMappingPropertyPair
                {
                    PropertyName = property.PropertyName,
                    EntityTypeName = property.TypeName,
                    EditModelTypeName = editModelProperty.TypeName,
                    EditModelIsNullable = editModelProperty.IsNullable,
                    IsBinary = editModelProperty.IsBinary,
                    LoadBindingExpression = isValueObject
                        ? $"entity.{property.PropertyName}?.ToString() ?? string.Empty"
                        : BuildMapperBindingExpression(
                            property.TypeName,
                            editModelProperty.IsBinary,
                            property.PropertyName
                        ),
                    BindingPropertyName = "Binding" + property.PropertyName,
                };
            })
            .ToList();

        return new CSharpMapperModel
        {
            ClassName = mapperClassName,
            EntityClassName = entityClassName,
            EditModelClassName = editModelClassName,
            ScalarProperties = scalarProperties,
            NavigationProperties = navigations.Select(BuildMapperNavigation).ToList(),
        };
    }

    /// <summary>エンティティ定義から Repository の生成モデルを構築する</summary>
    /// <returns>単一主キーを持たないテーブルは対象外として null を返す</returns>
    private CSharpRepositoryModel? BuildRepositoryClass(
        Entity entity,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var keyColumn = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

        // Repository は単一主キーを前提とするため、複合・主キーなしのテーブルはスキップする
        if (keyColumn.Count != 1)
        {
            diagnostics.Add(
                Warning(
                    string.Format(Strings.CodeGen_Warning_RepositorySingleKeyOnly, entity.TableName)
                )
            );
            return null;
        }

        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var repositoryName = entityClassName.EndsWith("Entity", StringComparison.Ordinal)
            ? entityClassName[..^"Entity".Length]
            : entityClassName;
        var keyTypeName = BuildProperty(keyColumn[0]).TypeName.TrimEnd('?');
        var queryBlocks = BuildQueryBlocks(entity, repositoryName, options, diagnostics);
        var binaryStreamBlocks = BuildBinaryStreamBlocks(
            entity,
            entityClassName,
            repositoryName,
            keyTypeName,
            options
        );

        return new CSharpRepositoryModel
        {
            InterfaceName = $"I{repositoryName}Repository",
            RemoteInterfaceName = $"I{repositoryName}RemoteRepository",
            ClassName = $"{repositoryName}Repository",
            EntityClassName = entityClassName,
            KeyTypeName = keyTypeName,
            QueryInterfaceBlock = queryBlocks.InterfaceBlock,
            QuerySharedImplBlock = queryBlocks.SharedImplBlock,
            QueryImplBlocksByDialect = queryBlocks.ImplBlocksByDialect,
            QueryDtoBlock = queryBlocks.DtoBlock,
            RemoteClientClassName = $"Http{repositoryName}RemoteRepository",
            RemoteRouteName = repositoryName,
            QueryRemoteClientBlock = queryBlocks.RemoteClientBlock,
            QueryRemoteServerBlock = queryBlocks.RemoteServerBlock,
            QueryRemoteServerRecordsBlock = queryBlocks.RemoteServerRecordsBlock,
            BinaryStreamContractBlock = binaryStreamBlocks.ContractBlock,
            BinaryStreamThinImplBlock = binaryStreamBlocks.ThinImplBlock,
            BinaryStreamEfImplBlock = binaryStreamBlocks.EfImplBlock,
            BinaryStreamFileExtensionsBlock = binaryStreamBlocks.FileExtensionsBlock,
            BinaryStreamRemoteClientBlock = binaryStreamBlocks.RemoteClientBlock,
            BinaryStreamRemoteServerBlock = binaryStreamBlocks.RemoteServerBlock,
        };
    }

    /// <summary>カラム定義からエンティティのスカラープロパティ生成モデルを構築する</summary>
    private CSharpPropertyModel BuildProperty(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);

        // 値オブジェクト生成時は列の C# 型を VO 型へ置き換える（VO は参照型）。
        var typeName = valueObject?.ClassName ?? typeInfo.TypeName;
        var isReferenceType = valueObject is not null || typeInfo.IsReferenceType;

        // NULL 許容列は型へ ? を付与する。値型は Nullable<T>、参照型（string / byte[] / VO）は nullable 注釈となる。
        // 非 NULL の string / byte[] は ? を付けず、後段の初期化子で空既定値を与えて CS8618 を回避する
        if (column.IsNullable)
        {
            typeName += "?";
        }

        return new CSharpPropertyModel
        {
            PropertyName = _nameConverter.ToPropertyName(column.Name),
            ColumnName = column.Name,
            TypeName = typeName,
            IsNullable = column.IsNullable,
            IsReferenceType = isReferenceType,
            IsPrimaryKey = column.IsPrimaryKey,
            IsForeignKey = column.IsForeignKey,
            // VO は [MaxLength] を出さない（長さ検証は VO 内部で行う。非 string 型に [MaxLength] を付けない安全策にもなる）
            MaxLength = valueObject is not null ? null : typeInfo.MaxLength,
            // [SqlColumnType] の Precision/Scale に載せる DB カラムのメタ情報。VO 抑制なしの生値。decimal は precision 指定時に scale 既定 0
            FacetPrecision = typeInfo.Precision,
            FacetScale = typeInfo.Precision is not null ? typeInfo.Scale ?? 0 : null,
            // SQL パラメータ型明示化（[SqlColumnType]）用。VO 有無に関わらず DB 由来の生値を載せる（束縛は素値へ開いてから）
            SqlDbTypeName = typeInfo.SqlDbTypeName,
            SqlDeclaredLength = typeInfo.SqlDeclaredLength,
            // 無制限バイナリ列のマーカー（[UnboundedBinaryColumn] 付与判定用）。付与可否はテンプレート側の
            // グローバル変数（exclude_unbounded_binary）で制御するため、ここでは options 条件を掛けず常時転記する
            IsUnboundedBinary = typeInfo.IsUnboundedBinary,
            // store-generated 列（rowversion / timestamp 等）のマーカー（[StoreGeneratedColumn] 付与判定用）。
            // バグ修正のため付与はオプション非依存・常時（is_row_version 自体が判定材料）
            IsRowVersion = typeInfo.IsRowVersion,
            // DB 定義メタ属性（[DbColumnMeta]）用。方言中立トークンと列の説明（型解決とは独立にモデルから引く）
            CanonicalTypeToken = typeInfo.CanonicalTypeToken,
            // [DbColumnMeta(..., Description = "...")] へ C# リテラルとして埋め込むためエスケープする（未エスケープだと " や \ でコンパイル不能になる）
            Description = EscapeForCSharpString(column.Description),
            DescriptionXmlDoc = EscapeForXmlDocSummary(column.Description),
            // 非 NULL の VO は妥当な空既定値を作れないため null! でロード前提を表明（NULL 許容 VO は初期化不要）
            Initializer = valueObject is not null
                ? (column.IsNullable ? string.Empty : " = null!;")
                : BuildEntityInitializer(typeName, column.IsNullable),
            // インメモリ Repository のシーダー用の決定的サンプル値式
            SampleValueExpression = BuildSampleValueExpression(column, typeInfo, valueObject),
        };
    }

    /// <summary>
    /// インメモリ Repository の決定的シーダーで、指定カラムへ代入する値の C# 式を構築する。
    /// </summary>
    /// <remarks>
    /// シーダーは各エンティティ 3 件（ループ変数 <c>index</c> = 1..3）を親→子の順で投入する。
    /// 主キー（int/long 等）は <c>index</c>、string 主キーは <c>"{TABLE}-00n"</c>、FK は親キーの実在値（<c>index</c>）を指す。
    /// NULL 可列は 3 件中 1 件（<c>index == 3</c>）を null にする。値オブジェクトは <c>Create</c> で包む。型ベースの固定値で決定的。
    /// </remarks>
    private string BuildSampleValueExpression(
        Column column,
        CSharpTypeInfo typeInfo,
        CSharpValueObjectModel? valueObject
    )
    {
        // 値オブジェクトは内包値を作ってから Create で包む（GuidKey は無引数生成の代わりに決定的 GUID を渡す）
        if (valueObject is not null)
        {
            var innerExpr = valueObject.IsGuidKey
                ? SampleGuidLiteral() + ".ToString()"
                : BuildSampleScalarExpression(
                    column,
                    valueObject.ValueTypeName,
                    valueObject.MaxLength
                );
            var created = $"{valueObject.ClassName}.Create({innerExpr})";

            // NULL 可 VO 列は 3 件中 1 件を null にする（確定値型は VoClass?）
            return column.IsNullable ? $"index == 3 ? null : {created}" : created;
        }

        var baseType = typeInfo.TypeName.TrimEnd('?');
        var scalar = BuildSampleScalarExpression(column, baseType, typeInfo.MaxLength);

        // NULL 可列は 3 件中 1 件（index == 3）を null にする（プロパティ型は T?・target-typed conditional で解決）
        return column.IsNullable ? $"index == 3 ? null : {scalar}" : scalar;
    }

    /// <summary>型・主キー/外部キー・列名から、非 NULL のスカラーサンプル値式を構築する</summary>
    private string BuildSampleScalarExpression(Column column, string baseType, int? maxLength)
    {
        // 主キー・外部キーは決定的な行番号（index）で表現し、親子の FK 整合を保つ
        var isKey = column.IsPrimaryKey || column.IsForeignKey;

        switch (baseType)
        {
            case "int":
                return isKey ? "index" : "index * 10";

            case "long":
                return isKey ? "(long)index" : "index * 10L";

            case "short":
                return isKey ? "(short)index" : "(short)(index * 10)";

            case "byte":
                return "(byte)index";

            case "decimal":
                return "index * 100.50m";

            case "double":
                return "index * 100.5d";

            case "float":
                return "index * 100.5f";

            case "bool":
                return "index % 2 == 1";

            case "System.Guid":
            case "Guid":
                return SampleGuidLiteral();

            case "System.DateTime":
            case "DateTime":
                return "new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)";

            case "System.DateTimeOffset":
            case "DateTimeOffset":
                return "new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index)";

            case "System.TimeSpan":
            case "TimeSpan":
                return "TimeSpan.FromHours(index)";

            case "byte[]":
                return "new byte[] { (byte)index, (byte)(index + 1), (byte)(index + 2) }";

            case "string":
                return BuildSampleStringExpression(column, maxLength);

            default:
                // enum など未知の値型は既定値へフォールバックする（決定的・コンパイル可能）
                return $"default({baseType})";
        }
    }

    /// <summary>string 列のサンプル値式を構築する（主キーは "{TABLE}-00n"・それ以外は "{列名} n"。MaxLength で切り詰め）</summary>
    private string BuildSampleStringExpression(Column column, int? maxLength)
    {
        string expression;

        if (column.IsPrimaryKey)
        {
            // string 主キーは "{TABLE}-001" 形式（テーブル名は大文字化）
            var prefix = EscapeForCSharpString(column.Name.ToUpperInvariant());
            expression = $"$\"{prefix}-00{{index}}\"";
        }
        else
        {
            var label = EscapeForCSharpString(column.Name);
            expression = $"$\"{label} {{index}}\"";
        }

        // MaxLength があれば宣言長を超えないよう安全側で切り詰める（決定的・実行時例外を避ける）
        if (maxLength is > 0)
        {
            return $"({expression}).Length > {maxLength} ? ({expression})[..{maxLength}] : ({expression})";
        }

        return expression;
    }

    /// <summary>決定的な GUID リテラル式を返す（index を末尾に埋め込み 3 件を区別する）</summary>
    private static string SampleGuidLiteral() =>
        "new Guid($\"00000000-0000-0000-0000-00000000000{index}\")";

    /// <summary>カラム定義から EditModel のプロパティ生成モデルを構築する</summary>
    /// <remarks>
    /// EditModel は入力途中の不正値も保持するため、値型・文字列・バイナリは原則 NULL 許容とし、
    /// 確定値プロパティと UI バインディング用文字列プロパティの両方の情報を組み立てる
    /// </remarks>
    private CSharpEditModelPropertyModel BuildEditModelProperty(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);
        if (valueObject is not null)
        {
            return BuildValueObjectEditModelProperty(column, valueObject);
        }

        var typeName = typeInfo.TypeName;

        var isBytes = typeName == "byte[]";
        var editModelIsNullable =
            column.IsNullable || !typeInfo.IsReferenceType || typeName == "string" || isBytes;

        // 値型・参照型を問わず、EditModel が NULL 許容にする列は型名へ ? を付ける（入力途中の未確定値を保持するため）
        if (editModelIsNullable)
        {
            typeName += "?";
        }

        var propertyName = _nameConverter.ToPropertyName(column.Name);
        var fieldName = ToFieldName(propertyName);
        var bindingPropertyName = "Binding" + propertyName;
        var bindingFieldName = ToFieldName(bindingPropertyName);

        isBytes = typeName == "byte[]" || typeName == "byte[]?";
        var needsParse = !typeInfo.IsReferenceType && !isBytes;
        var parseTypeName = needsParse ? typeName.TrimEnd('?') : string.Empty;

        // 参照型・NULL 許容の値型は初期化子なし（null 始まり）、それ以外の非 NULL 値型のみ default で初期化する
        var fieldInitializer =
            typeInfo.IsReferenceType || editModelIsNullable ? string.Empty : "default";

        var bindingFieldInitializer = "string.Empty";

        return new CSharpEditModelPropertyModel
        {
            PropertyName = propertyName,
            // 既定表示名: Description があればそれ、無ければプロパティ名（メッセージの後方互換）
            DisplayName = string.IsNullOrWhiteSpace(column.Description)
                ? propertyName
                : EscapeForCSharpString(column.Description),
            DescriptionXmlDoc = EscapeForXmlDocSummary(column.Description),
            TypeName = typeName,
            FieldName = fieldName,
            BindingPropertyName = bindingPropertyName,
            BindingFieldName = bindingFieldName,
            NeedsParse = needsParse,
            ParseTypeName = parseTypeName,
            FieldInitializer = fieldInitializer,
            BindingFieldInitializer = bindingFieldInitializer,
            IsNullable = editModelIsNullable,
            IsReferenceType = typeInfo.IsReferenceType,
            IsBinary = isBytes,
            // Entity 側が非 NULL（必須）で EditModel 側は入力途中を許容して NULL 許容にした項目を必須とみなす
            IsRequired = editModelIsNullable && !column.IsNullable,
            RevertBindingExpression = BuildBindingExpression(propertyName, isBytes),
        };
    }

    /// <summary>確定値から UI バインディング文字列へ戻す式を生成する（バイナリは Base64 化する）</summary>
    private static string BuildBindingExpression(string propertyName, bool isBinary)
    {
        if (isBinary)
        {
            return $"{propertyName} is null ? string.Empty : Convert.ToBase64String({propertyName})";
        }

        return $"{propertyName}?.ToString() ?? string.Empty";
    }

    /// <summary>非 NULL の string / byte[] プロパティに対する空既定値の初期化子を生成する</summary>
    private static string BuildEntityInitializer(string typeName, bool isNullable)
    {
        if (typeName == "string" && !isNullable)
        {
            return " = string.Empty;";
        }

        if (typeName == "byte[]" && !isNullable)
        {
            return " = Array.Empty<byte>();";
        }

        return string.Empty;
    }

    /// <summary>Mapper 内で Entity プロパティを EditModel のバインディング文字列へ変換する式を生成する</summary>
    private static string BuildMapperBindingExpression(
        string entityTypeName,
        bool isBinary,
        string propertyName
    )
    {
        if (isBinary)
        {
            return $"entity.{propertyName} is null ? string.Empty : Convert.ToBase64String(entity.{propertyName})";
        }

        if (entityTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            return $"entity.{propertyName}?.ToString() ?? string.Empty";
        }

        return $"entity.{propertyName}.ToString() ?? string.Empty";
    }

    /// <summary>解決済みナビゲーション情報からエンティティのナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpNavigationModel BuildEntityNavigation(NavigationInfo nav)
    {
        var targetEntityTypeName = _nameConverter.ToEntityClassName(nav.TargetTableName);
        return new CSharpNavigationModel
        {
            PropertyName = nav.PropertyName,
            TypeName = targetEntityTypeName,
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            IsParentReference = nav.IsParentReference,
            // 子方向（親参照でない）ナビゲーションのみカスケード保存/削除の対象とする
            Cascade = !nav.IsParentReference,
            DisplayTypeName = nav.IsCollection
                ? $"ICollection<{targetEntityTypeName}>"
                : (nav.IsNullable ? targetEntityTypeName + "?" : targetEntityTypeName),
            Initializer = nav.IsCollection
                ? $" = new List<{targetEntityTypeName}>();"
                : (nav.IsNullable ? string.Empty : " = null!;"),
            PrincipalTableName = nav.PrincipalTableName,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentTableName = nav.DependentTableName,
            DependentColumnName = nav.DependentColumnName,
        };
    }

    /// <summary>解決済みナビゲーション情報から EditModel のナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpNavigationModel BuildEditModelNavigation(NavigationInfo nav)
    {
        var targetEditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName);
        return new CSharpNavigationModel
        {
            PropertyName = nav.PropertyName,
            TypeName = targetEditModelTypeName,
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            IsParentReference = nav.IsParentReference,
            Cascade = !nav.IsParentReference,
            DisplayTypeName = nav.IsCollection
                ? $"EditModelCollection<{targetEditModelTypeName}>"
                : (nav.IsNullable ? targetEditModelTypeName + "?" : targetEditModelTypeName),
            // カスケード子（親→子）のみバッキングフィールドで所有者リンクを張るため、フィールド名を用意する
            FieldName = nav.IsParentReference ? string.Empty : ToFieldName(nav.PropertyName),
            Initializer = nav.IsCollection
                ? $" = new EditModelCollection<{targetEditModelTypeName}>();"
                : (nav.IsNullable ? string.Empty : " = null!;"),
            PrincipalTableName = nav.PrincipalTableName,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentTableName = nav.DependentTableName,
            DependentColumnName = nav.DependentColumnName,
        };
    }

    /// <summary>解決済みナビゲーション情報から Mapper が扱うナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpMapperNavigationModel BuildMapperNavigation(NavigationInfo nav) =>
        new()
        {
            PropertyName = nav.PropertyName,
            EditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName),
            MapperClassName = _nameConverter.ToMapperClassName(nav.TargetTableName),
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            // 子方向（親参照でない）のみカスケード変換の対象とし、親をたどる無限再帰を防ぐ
            IsCascade = !nav.IsParentReference,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentColumnName = nav.DependentColumnName,
        };

    /// <summary>ナビゲーション解決の中間結果（生成側の表現に依存しない情報）</summary>
    private sealed record NavigationInfo(
        string PropertyName,
        string TargetTableName,
        bool IsCollection,
        bool IsNullable,
        bool IsParentReference,
        string PrincipalTableName,
        string PrincipalColumnName,
        string DependentTableName,
        string DependentColumnName
    );

    /// <summary>全リレーションを一度だけ走査し、エンティティ ID ごとのナビゲーション情報を解決する</summary>
    /// <remarks>
    /// 多対多や参照先・キーが解決できないリレーションは警告を出して生成対象外とする。
    /// 警告はリレーション単位で 1 回だけ追加されるため、従来のような重複は発生しない。
    /// </remarks>
    private Dictionary<Guid, List<NavigationInfo>> ResolveAllNavigations(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var navigationsByEntity = diagram.Entities.ToDictionary(
            entity => entity.Id,
            _ => new List<NavigationInfo>()
        );

        foreach (var relationship in diagram.Relationships)
        {
            // 多対多は中間テーブルを介する設計のため C# 生成では直接ナビゲーションを作らない
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                diagnostics.Add(
                    Warning(
                        string.Format(Strings.CodeGen_Warning_ManyToManySkipped, relationship.Id)
                    )
                );
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
                diagnostics.Add(
                    Warning(
                        string.Format(
                            Strings.CodeGen_Warning_RelationshipTargetNotFound,
                            relationship.Id
                        )
                    )
                );
                continue;
            }

            // 明示指定の列を優先し、無ければ principal は主キー、dependent は外部キー列にフォールバックする
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
                diagnostics.Add(
                    Warning(
                        string.Format(
                            Strings.CodeGen_Warning_RelationshipKeyUnknown,
                            relationship.Id
                        )
                    )
                );
                continue;
            }

            var isCollection = relationship.Type == RelationshipType.OneToMany;

            // source 側（親）は子へのナビゲーション（1 対多なら collection）を持つ
            navigationsByEntity[source.Id]
                .Add(
                    new NavigationInfo(
                        PropertyName: _nameConverter.ToNavigationName(
                            target.TableName,
                            collection: isCollection
                        ),
                        TargetTableName: target.TableName,
                        IsCollection: isCollection,
                        IsNullable: false,
                        IsParentReference: false,
                        PrincipalTableName: source.TableName,
                        PrincipalColumnName: principalColumn.Name,
                        DependentTableName: target.TableName,
                        DependentColumnName: dependentColumn.Name
                    )
                );

            // target 側（子）は親への単一参照ナビゲーションを持つ。
            // 自己参照（source == target）の場合は従来どおり子側ナビゲーションのみとし、重複を避ける
            if (target.Id != source.Id)
            {
                navigationsByEntity[target.Id]
                    .Add(
                        new NavigationInfo(
                            PropertyName: _nameConverter.ToNavigationName(
                                source.TableName,
                                collection: false
                            ),
                            TargetTableName: source.TableName,
                            IsCollection: false,
                            IsNullable: dependentColumn.IsNullable,
                            IsParentReference: true,
                            PrincipalTableName: source.TableName,
                            PrincipalColumnName: principalColumn.Name,
                            DependentTableName: target.TableName,
                            DependentColumnName: dependentColumn.Name
                        )
                    );
            }
        }

        return navigationsByEntity;
    }

    /// <summary>プロパティ名から先頭小文字・アンダースコア始まりのフィールド名を導出する</summary>
    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return "_field";
        }

        // 逐語的識別子の @ を除いた先頭文字を小文字化し _ を前置する
        var stripped = propertyName.TrimStart('@');
        return "_" + char.ToLowerInvariant(stripped[0]) + stripped[1..];
    }

    /// <summary>警告レベルの診断情報を生成する</summary>
    private static GenerationDiagnostic Warning(string message) =>
        new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };

    /// <summary>XML doc の summary へ説明文を安全に埋め込めるようエスケープする（&amp;/&lt;/&gt; エスケープ、改行は空白 1 つへ畳む）。空・空白のみは空文字列を返す</summary>
    private static string EscapeForXmlDocSummary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // & は最初にエスケープする（&lt; 等を二重エスケープしないため）
        var escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // CRLF/LF/CR いずれの改行も空白 1 つへ畳む（summary は 1 行前提）
        return Regex.Replace(escaped, "\r\n|\r|\n", " ");
    }

    /// <summary>
    /// 文字列を C# の通常文字列リテラル（<c>"..."</c>）へ安全に埋め込めるようエスケープする。
    /// バックスラッシュと二重引用符をエスケープし、改行（CRLF/LF/CR）は空白 1 つへ畳む（リテラルは 1 行前提）。
    /// 空・空白のみは空文字列を返す。<c>[DbColumnMeta]</c> / <c>[DbTableMeta]</c> の Description や DisplayName 既定値に共用する。
    /// </summary>
    private static string EscapeForCSharpString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // バックスラッシュを最初にエスケープする（後続の \" を二重エスケープしないため）
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // CRLF/LF/CR いずれの改行も空白 1 つへ畳む（1 行リテラル前提）
        return Regex.Replace(escaped, "\r\n|\r|\n", " ");
    }
}
