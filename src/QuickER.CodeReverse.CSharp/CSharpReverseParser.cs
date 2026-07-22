using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeReverse.CSharp.Resources;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// QuickER が <c>IncludeDataAnnotations</c> ON で生成した C# コードを Roslyn の構文解析のみ
/// （コンパイル・アセンブリ読込なし）で解析し、<see cref="ErDiagram"/> の意味モデルへ復元する。
/// </summary>
/// <remarks>
/// <para>
/// 解析対象は <c>[Table]</c> を持ち、かつ列プロパティに <c>[DbColumnMeta]</c> を 1 つ以上持つクラスのみ。
/// インフラクラス（基底・属性定義・Repository・EditModel・Mapper・DbContext）は <c>[Table]</c> を持たないため無視される。
/// </para>
/// <para>
/// 復元マッピング:
/// <list type="bullet">
///   <item>テーブル名＝<c>[Table("...")]</c>・列名＝<c>[Column("...")]</c></item>
///   <item>型＝<c>[DbColumnMeta]</c> の方言中立トークン → <see cref="CanonicalTypeToken.TryParse"/> →
///     対象方言の <see cref="ITypeCatalog.TryFormat"/> でネイティブ型へ（展開不能なトークンは警告してトークン文字列をそのまま採用）</item>
///   <item>PK＝<c>[Key]</c>・NULL 許容＝プロパティ型が <c>?</c> 付き（<see cref="NullableTypeSyntax"/>）。
///     生成コードは <c>#nullable enable</c> のもと NULL 許容列を型の <c>?</c> として必ず表すため、値型・参照型・VO の全ケースで型構文が正</item>
///   <item>説明＝<c>[DbColumnMeta].Description</c> / <c>[DbTableMeta].Description</c></item>
///   <item>リレーション＝<c>[NavigationReference]</c>（端点 4 つ組で双方向の重複を一意化）</item>
/// </list>
/// </para>
/// </remarks>
public sealed class CSharpReverseParser
{
    /// <summary>C# ソース文字列を構文解析し、意味モデルへ復元する</summary>
    /// <param name="sourceText">解析対象の C# ソース（QuickER 生成の本体 .g.cs）</param>
    /// <param name="typeCatalog">型トークンをネイティブ型へ展開する対象方言の型カタログ</param>
    /// <exception cref="CodeReverseException">解析対象クラスが 1 件も無い場合（案内メッセージ付き）</exception>
    public CodeReverseResult Parse(string sourceText, ITypeCatalog typeCatalog)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(typeCatalog);

        var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();
        var warnings = new List<string>();

        // 解析対象クラス（[Table] を持ち、かつ列プロパティに [DbColumnMeta] を 1 つ以上持つ）を抽出する
        var targetClasses = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(IsReverseTarget)
            .ToList();

        if (targetClasses.Count == 0)
        {
            throw new CodeReverseException(Strings.Reverse_NoTargetClasses);
        }

        // 先にエンティティ・列を復元し、テーブル名 → (列名 → 列) の索引を作る（リレーション解決に使う）
        var entities = new List<Entity>();
        var columnsByTable = new Dictionary<string, Dictionary<string, Column>>(
            StringComparer.Ordinal
        );
        var entityByTable = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var navigations = new List<NavigationInfo>();

        foreach (var classDecl in targetClasses)
        {
            var tableName = GetAttributeStringArgument(classDecl.AttributeLists, "Table", 0);

            if (tableName is null)
            {
                continue;
            }

            var entity = new Entity
            {
                TableName = tableName,
                Description =
                    GetAttributeNamedArgument(
                        classDecl.AttributeLists,
                        "DbTableMeta",
                        "Description"
                    ) ?? string.Empty,
            };

            var columnIndex = new Dictionary<string, Column>(StringComparer.Ordinal);

            foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                // ナビゲーション（[NavigationReference]）はリレーション復元で別途扱う
                if (HasAttribute(property.AttributeLists, "NavigationReference"))
                {
                    var nav = TryReadNavigation(property.AttributeLists);

                    if (nav is not null)
                    {
                        navigations.Add(nav);
                    }

                    continue;
                }

                // 列（[Column]）のみを対象にする
                if (!HasAttribute(property.AttributeLists, "Column"))
                {
                    continue;
                }

                var column = TryReadColumn(property, tableName, typeCatalog, warnings);

                if (column is null)
                {
                    continue;
                }

                entity.Columns.Add(column);
                columnIndex[column.Name] = column;
            }

            entities.Add(entity);
            columnsByTable[tableName] = columnIndex;
            entityByTable[tableName] = entity;
        }

        var relationships = BuildRelationships(navigations, entityByTable, columnsByTable);

        return new CodeReverseResult
        {
            Entities = entities,
            Relationships = relationships,
            Warnings = warnings,
        };
    }

    /// <summary>クラスが解析対象か（<c>[Table]</c> を持ち、かつ <c>[DbColumnMeta]</c> 付きの列プロパティを持つ）</summary>
    private static bool IsReverseTarget(ClassDeclarationSyntax classDecl)
    {
        if (!HasAttribute(classDecl.AttributeLists, "Table"))
        {
            return false;
        }

        return classDecl
            .Members.OfType<PropertyDeclarationSyntax>()
            .Any(property => HasAttribute(property.AttributeLists, "DbColumnMeta"));
    }

    /// <summary>列プロパティ 1 件を復元する（<c>[DbColumnMeta]</c> が無いプロパティは警告して <c>null</c>）</summary>
    private static Column? TryReadColumn(
        PropertyDeclarationSyntax property,
        string tableName,
        ITypeCatalog typeCatalog,
        List<string> warnings
    )
    {
        var columnName = GetAttributeStringArgument(property.AttributeLists, "Column", 0);

        if (columnName is null)
        {
            return null;
        }

        var typeToken = GetAttributeStringArgument(property.AttributeLists, "DbColumnMeta", 0);

        if (typeToken is null)
        {
            // [Column] はあるが [DbColumnMeta] が無い＝生成時に型トークンを解決できなかった自由記述型。
            // 型を復元できないため列をスキップし、取りこぼしを警告で通知する。
            warnings.Add(
                string.Format(Strings.Reverse_ColumnMissingTypeMeta, tableName, columnName)
            );

            return null;
        }

        var isPrimaryKey = HasAttribute(property.AttributeLists, "Key");
        // NULL 許容はプロパティ型構文で判定する。生成コードは #nullable enable のもと、NULL 許容列を
        // 型の ?（NullableTypeSyntax。例 BalanceValue? / int? / string?）として必ず表すため、値型・参照型・
        // VO の全ケースで型構文が正。[Required]/[Key] は NULL 判定に使わない（[Required] は参照型にしか
        // 出ず、VO 無し・値型 NOT NULL 非 PK 列（例 int Amount）を NULL 許容と誤判定するため）。
        var isNullable = property.Type is NullableTypeSyntax;

        return new Column
        {
            Name = columnName,
            DataType = ResolveDataType(typeToken, tableName, columnName, typeCatalog, warnings),
            IsPrimaryKey = isPrimaryKey,
            IsNullable = isNullable,
            Description =
                GetAttributeNamedArgument(property.AttributeLists, "DbColumnMeta", "Description")
                ?? string.Empty,
        };
    }

    /// <summary>型トークンを対象方言のネイティブ型へ展開する（展開不能はトークン文字列をそのまま採用し警告）</summary>
    private static string ResolveDataType(
        string typeToken,
        string tableName,
        string columnName,
        ITypeCatalog typeCatalog,
        List<string> warnings
    )
    {
        if (
            CanonicalTypeToken.TryParse(typeToken, out var canonical)
            && typeCatalog.TryFormat(canonical, out var nativeType)
        )
        {
            return nativeType;
        }

        // トークンの解析、または対象方言での整形に失敗＝トークン文字列をそのまま型として採用する
        warnings.Add(
            string.Format(Strings.Reverse_TypeTokenUnresolved, typeToken, tableName, columnName)
        );

        return typeToken;
    }

    /// <summary>
    /// <c>[NavigationReference]</c> の端点 4 つ組で双方向の重複を一意化し、リレーション一覧を組み立てる。
    /// </summary>
    /// <remarks>
    /// 意味論は DB 取込の <see cref="ForeignKeyRelationshipBuilder"/> と揃える:
    /// 参照先（principal・PK 側）を起点（Source）、FK 保有側（dependent）を終点（Target）とし、
    /// <c>SourceColumn</c>＝principal 列・<c>TargetColumn</c>＝dependent 列。いずれかの端で
    /// <c>IsCollection=true</c> なら 1 対多、そうでなければ 1 対 1 とする。
    /// </remarks>
    private static List<Relationship> BuildRelationships(
        List<NavigationInfo> navigations,
        IReadOnlyDictionary<string, Entity> entityByTable,
        IReadOnlyDictionary<string, Dictionary<string, Column>> columnsByTable
    )
    {
        // 端点 4 つ組（principal テーブル・列 / dependent テーブル・列）でグループ化する（双方向属性の重複排除）
        var groups =
            new Dictionary<
                (
                    string PrincipalTable,
                    string PrincipalColumn,
                    string DependentTable,
                    string DependentColumn
                ),
                bool
            >();

        foreach (var nav in navigations)
        {
            var key = (
                nav.PrincipalTable,
                nav.PrincipalColumn,
                nav.DependentTable,
                nav.DependentColumn
            );

            // いずれかの端点で IsCollection=true なら 1 対多（親側コレクションが真を持つ・親参照側は常に偽）
            groups[key] = groups.TryGetValue(key, out var isCollection)
                ? isCollection || nav.IsCollection
                : nav.IsCollection;
        }

        var relationships = new List<Relationship>();

        foreach (var (key, isCollection) in groups)
        {
            // 両端テーブルが解析対象に存在しなければスキップ（解決できない参照）
            if (
                !entityByTable.TryGetValue(key.PrincipalTable, out var principalEntity)
                || !entityByTable.TryGetValue(key.DependentTable, out var dependentEntity)
            )
            {
                continue;
            }

            var sourceColumnId = ResolveColumnId(
                columnsByTable,
                key.PrincipalTable,
                key.PrincipalColumn
            );
            var targetColumnId = ResolveColumnId(
                columnsByTable,
                key.DependentTable,
                key.DependentColumn,
                markForeignKey: true
            );

            relationships.Add(
                new Relationship
                {
                    SourceEntityId = principalEntity.Id, // 参照先（PK 側）を起点として表現
                    TargetEntityId = dependentEntity.Id, // FK 保有側
                    Type = isCollection ? RelationshipType.OneToMany : RelationshipType.OneToOne,
                    SourceColumnId = sourceColumnId,
                    TargetColumnId = targetColumnId,
                }
            );
        }

        return relationships;
    }

    /// <summary>テーブル名・列名から列 Id を解決する（<paramref name="markForeignKey"/> 時は FK フラグを立てる）</summary>
    private static Guid? ResolveColumnId(
        IReadOnlyDictionary<string, Dictionary<string, Column>> columnsByTable,
        string tableName,
        string columnName,
        bool markForeignKey = false
    )
    {
        if (
            columnsByTable.TryGetValue(tableName, out var columns)
            && columns.TryGetValue(columnName, out var column)
        )
        {
            if (markForeignKey)
            {
                column.IsForeignKey = true;
            }

            return column.Id;
        }

        return null;
    }

    /// <summary><c>[NavigationReference]</c> 属性から端点情報を読む（引数不足時は <c>null</c>）</summary>
    private static NavigationInfo? TryReadNavigation(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var attribute = FindAttribute(attributeLists, "NavigationReference");
        var arguments = attribute?.ArgumentList?.Arguments;

        // 位置引数: principalTable, principalColumn, dependentTable, dependentColumn, isCollection, cascade, isParentReference
        var positional = arguments?.Where(argument => argument.NameEquals is null).ToList();

        if (positional is null || positional.Count < 5)
        {
            return null;
        }

        var principalTable = ReadStringLiteral(positional[0]);
        var principalColumn = ReadStringLiteral(positional[1]);
        var dependentTable = ReadStringLiteral(positional[2]);
        var dependentColumn = ReadStringLiteral(positional[3]);

        if (
            principalTable is null
            || principalColumn is null
            || dependentTable is null
            || dependentColumn is null
        )
        {
            return null;
        }

        return new NavigationInfo(
            principalTable,
            principalColumn,
            dependentTable,
            dependentColumn,
            IsCollection: ReadBooleanLiteral(positional[4])
        );
    }

    // ---- Roslyn 属性ヘルパー ----

    /// <summary>属性リストに指定名の属性があるか（<c>Attribute</c> 接尾辞は無視して照合）</summary>
    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string name) =>
        FindAttribute(attributeLists, name) is not null;

    /// <summary>指定名の属性を探す（見つからなければ <c>null</c>）</summary>
    private static AttributeSyntax? FindAttribute(
        SyntaxList<AttributeListSyntax> attributeLists,
        string name
    ) =>
        attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute =>
                string.Equals(GetSimpleAttributeName(attribute), name, StringComparison.Ordinal)
            );

    /// <summary>属性の指定位置の位置引数（<c>NameEquals</c> 無し）を文字列として取得する</summary>
    private static string? GetAttributeStringArgument(
        SyntaxList<AttributeListSyntax> attributeLists,
        string attributeName,
        int index
    )
    {
        var attribute = FindAttribute(attributeLists, attributeName);
        var positional = attribute
            ?.ArgumentList?.Arguments.Where(argument => argument.NameEquals is null)
            .ToList();

        if (positional is null || positional.Count <= index)
        {
            return null;
        }

        return ReadStringLiteral(positional[index]);
    }

    /// <summary>属性の名前付き引数（<c>Name = "..."</c>）を文字列として取得する</summary>
    private static string? GetAttributeNamedArgument(
        SyntaxList<AttributeListSyntax> attributeLists,
        string attributeName,
        string argumentName
    )
    {
        var attribute = FindAttribute(attributeLists, attributeName);
        var named = attribute?.ArgumentList?.Arguments.FirstOrDefault(argument =>
            string.Equals(
                argument.NameEquals?.Name.Identifier.Text,
                argumentName,
                StringComparison.Ordinal
            )
        );

        return named is null ? null : ReadStringLiteral(named);
    }

    /// <summary>属性名を単純名（名前空間・<c>Attribute</c> 接尾辞を除いた識別子）へ正規化する</summary>
    private static string GetSimpleAttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name;

        while (name is QualifiedNameSyntax qualified)
        {
            name = qualified.Right;
        }

        var identifier = (name as IdentifierNameSyntax)?.Identifier.Text ?? name.ToString();

        return identifier.EndsWith("Attribute", StringComparison.Ordinal)
            ? identifier[..^"Attribute".Length]
            : identifier;
    }

    /// <summary>属性引数の式が文字列リテラルであればその値（エスケープ解決済み）を返す</summary>
    private static string? ReadStringLiteral(AttributeArgumentSyntax argument) =>
        argument.Expression is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    /// <summary>属性引数の式が <c>true</c> リテラルであるか（それ以外・不明は false）</summary>
    private static bool ReadBooleanLiteral(AttributeArgumentSyntax argument) =>
        argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression);

    /// <summary><c>[NavigationReference]</c> の端点情報（重複一意化・リレーション組み立てに使う）</summary>
    private sealed record NavigationInfo(
        string PrincipalTable,
        string PrincipalColumn,
        string DependentTable,
        string DependentColumn,
        bool IsCollection
    );
}
