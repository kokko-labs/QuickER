using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成テンプレートが発行する「固定メンバー名」の名簿（<see cref="GeneratedFixedMemberNames"/>）が、
/// 実際の生成出力と一致し続けることを表明するドリフトガード。
/// </summary>
/// <remarks>
/// <para>
/// 名簿はシンボル表検証（列・ナビゲーション由来の名前と固定メンバーの衝突を Error にする）の入力なので、
/// テンプレートへ固定メンバーが増えたのに名簿へ追随し忘れると、その名前と同名の列を持つ図が診断ゼロで
/// CS0102（コンパイル不能）を出す出力になる。ビルドも型検査も検出できない静かな回帰のため、
/// 「実生成した EditModel / Entity クラスの宣言メンバーから列・ナビゲーション由来の派生名を差し引いた残余」が
/// 名簿と<strong>完全一致</strong>することを表明し、テンプレートへメンバーが増えた瞬間に落として列挙の更新を強制する。
/// </para>
/// <para>
/// 検証は Roslyn の構文木で行う（正規表現ではメンバー宣言と本文中の呼び出しを区別できないため）。
/// </para>
/// </remarks>
public sealed class GeneratedFixedMemberDriftTests
{
    /// <summary>
    /// 条件付きの固定メンバーをすべて発火させる最小構成の図。
    /// </summary>
    /// <remarks>
    /// 構成の意図:
    /// <list type="bullet">
    ///   <item><description><c>invoices</c>（説明あり・子コレクションを持つ）＝ Entity の <c>DefaultDisplayName</c> と EditModel の <c>RegisterChildren</c> を発火させる</description></item>
    ///   <item><description><c>invoice_lines</c>（説明あり・親参照を持つ）＝ EditModel の型付き <c>ParentModel</c> を発火させる</description></item>
    ///   <item><description><c>notes</c>（説明なし・関連なし）＝ 条件付きメンバーが 1 つも出ない対照。Entity の残余が空になることを確かめる</description></item>
    /// </list>
    /// 値オブジェクトは使わない（VO 化すると表示名ヘルパの発行条件が変わるため）。
    /// </remarks>
    /// <param name="withUniqueConstraint"><c>notes</c> へ UNIQUE 制約を足すか（EditModel の制約テーブルを発火させる）</param>
    private static ErDiagram BuildDiagram(bool withUniqueConstraint = false)
    {
        var invoiceId = Guid.NewGuid();
        var invoiceLineId = Guid.NewGuid();
        var invoicePk = Guid.NewGuid();
        var invoiceLineFk = Guid.NewGuid();
        var noteCode = Guid.NewGuid();

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = invoiceId,
                    TableName = "invoices",
                    Description = "Invoice header",
                    Columns =
                    [
                        new Column
                        {
                            Id = invoicePk,
                            Name = "invoice_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = invoiceLineId,
                    TableName = "invoice_lines",
                    Description = "Invoice line",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "invoice_line_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = invoiceLineFk,
                            Name = "invoice_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "notes",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "note_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = noteCode,
                            Name = "note_code",
                            DataType = "nvarchar(20)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = invoiceId,
                    TargetEntityId = invoiceLineId,
                    ColumnPairs = [new(invoicePk, invoiceLineFk)],
                },
            ],
        };

        // UNIQUE 制約は条件付き固定メンバー（制約テーブル）の発火条件そのものなので、要求されたときだけ足す
        if (withUniqueConstraint)
        {
            diagram
                .Entities.Single(entity => entity.TableName == "notes")
                .UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { noteCode } });
        }

        return diagram;
    }

    /// <summary>
    /// EditModel クラスの残余メンバー（列・ナビゲーション由来を差し引いた分）が固定メンバー名簿と完全一致することを検証する
    /// </summary>
    [Fact(DisplayName = "EditModel の固定メンバーが名簿と完全一致する")]
    public void EditModelDeclaredMembers_MinusColumnDerived_ShouldEqualFixedMemberRoster()
    {
        var source = GenerateSource();

        // 子コレクションを持つ側 ＝ RegisterChildren が出て、型付き ParentModel は出ない
        ResidualMembers(source, "InvoiceEditModel", "InvoiceEntity")
            .Should()
            .BeEquivalentTo(
                GeneratedFixedMemberNames
                    .EditModelAlways.Concat(
                        GeneratedFixedMemberNames.EditModelWithCascadeNavigations
                    )
                    .Concat(GeneratedFixedMemberNames.EditModelDisplayNameHelpers),
                "テンプレートの固定メンバーと GeneratedFixedMemberNames の列挙は一致していなければならない"
            );

        // 親参照を持つ側 ＝ 型付き ParentModel が出て、RegisterChildren は出ない
        ResidualMembers(source, "InvoiceLineEditModel", "InvoiceLineEntity")
            .Should()
            .BeEquivalentTo(
                GeneratedFixedMemberNames
                    .EditModelAlways.Concat(GeneratedFixedMemberNames.EditModelWithTypedParentModel)
                    .Concat(GeneratedFixedMemberNames.EditModelDisplayNameHelpers)
            );

        // 関連なし ＝ 条件付きメンバーは 1 つも出ない
        ResidualMembers(source, "NoteEditModel", "NoteEntity")
            .Should()
            .BeEquivalentTo(
                GeneratedFixedMemberNames.EditModelAlways.Concat(
                    GeneratedFixedMemberNames.EditModelDisplayNameHelpers
                )
            );
    }

    /// <summary>
    /// Repository 契約面が生成される構成では、EditModel の残余メンバーへ DB 照合糖衣が加わることを検証する
    /// （<see cref="GeneratedFixedMemberNames.EditModelWithRepositoryFace"/> の条件付き集合の発火）
    /// </summary>
    [Fact(DisplayName = "Repository 契約ありの EditModel は DB 照合糖衣を宣言する")]
    public void EditModelDeclaredMembers_WithRepositoryContract_IncludeValidateUniqueAsync()
    {
        var source = GenerateSource(withRepositories: true);

        ResidualMembers(source, "NoteEditModel", "NoteEntity")
            .Should()
            .BeEquivalentTo(
                GeneratedFixedMemberNames
                    .EditModelAlways.Concat(GeneratedFixedMemberNames.EditModelDisplayNameHelpers)
                    .Concat(GeneratedFixedMemberNames.EditModelWithRepositoryFace)
            );
    }

    /// <summary>
    /// UNIQUE 制約を持つテーブルでは、EditModel の残余メンバーへ制約テーブルが加わることを検証する
    /// （<see cref="GeneratedFixedMemberNames.EditModelWithUniqueConstraints"/> の条件付き集合の発火）
    /// </summary>
    [Fact(DisplayName = "UNIQUE 制約ありの EditModel は制約テーブルを宣言する")]
    public void EditModelDeclaredMembers_WithUniqueConstraint_IncludeUniquenessConstraints()
    {
        var source = GenerateSource(withUniqueConstraint: true);

        ResidualMembers(source, "NoteEditModel", "NoteEntity")
            .Should()
            .BeEquivalentTo(
                GeneratedFixedMemberNames
                    .EditModelAlways.Concat(GeneratedFixedMemberNames.EditModelDisplayNameHelpers)
                    .Concat(GeneratedFixedMemberNames.EditModelWithUniqueConstraints)
            );
    }

    /// <summary>
    /// Entity クラスの残余メンバーが固定メンバー名簿と完全一致することを検証する
    /// （テーブル説明があるときだけ <c>DefaultDisplayName</c> の override が出る）
    /// </summary>
    [Fact(DisplayName = "Entity の固定メンバーが名簿と完全一致する")]
    public void EntityDeclaredMembers_MinusColumnDerived_ShouldEqualFixedMemberRoster()
    {
        var source = GenerateSource();

        // 説明ありのテーブルは DefaultDisplayName の override を宣言する
        EntityResidualMembers(source, "InvoiceEntity")
            .Should()
            .BeEquivalentTo(GeneratedFixedMemberNames.EntityWithTableDescription);
        EntityResidualMembers(source, "InvoiceLineEntity")
            .Should()
            .BeEquivalentTo(GeneratedFixedMemberNames.EntityWithTableDescription);

        // 説明なしのテーブルは固定メンバーを 1 つも宣言しない（同名の列があっても衝突しない根拠）
        EntityResidualMembers(source, "NoteEntity").Should().BeEmpty();
    }

    /// <summary>
    /// 差し引きに使う派生名が固定メンバー名と重ならない（テストの図が固定メンバーを覆い隠さない）ことを検証する
    /// </summary>
    /// <remarks>
    /// 残余の算出は「宣言メンバー − 派生名」なので、派生名の中に固定メンバー名が混ざると
    /// その固定メンバーが差し引かれて残余から消え、名簿から欠けても気づけない。図の側の安全性を明示的に守る。
    /// </remarks>
    [Fact(DisplayName = "検証用の図の派生名は固定メンバー名と重ならない")]
    public void DerivedMemberNames_ShouldNotOverlapFixedMemberRoster()
    {
        var source = GenerateSource();

        var allFixed = GeneratedFixedMemberNames
            .EditModelAlways.Concat(GeneratedFixedMemberNames.EditModelWithCascadeNavigations)
            .Concat(GeneratedFixedMemberNames.EditModelWithTypedParentModel)
            .Concat(GeneratedFixedMemberNames.EditModelWithRepositoryFace)
            .Concat(GeneratedFixedMemberNames.EditModelWithUniqueConstraints)
            .Concat(GeneratedFixedMemberNames.EditModelDisplayNameHelpers)
            .Concat(GeneratedFixedMemberNames.EntityWithTableDescription)
            .ToHashSet(StringComparer.Ordinal);

        foreach (
            var entityClassName in new[] { "InvoiceEntity", "InvoiceLineEntity", "NoteEntity" }
        )
        {
            var derived = DerivedMemberNames(RootNames(source, entityClassName));

            derived
                .Should()
                .NotIntersectWith(
                    allFixed,
                    $"{entityClassName} の列・ナビゲーション由来の派生名が固定メンバー名を覆い隠している"
                );
        }
    }

    /// <summary>検証用の図を実際に生成し、全出力ファイルの本文を返す</summary>
    /// <param name="withRepositories">Repository 契約（＝EditModel の DB 照合糖衣）も生成するか</param>
    /// <param name="withUniqueConstraint">UNIQUE 制約（＝EditModel の制約テーブル）も生成するか</param>
    private static IReadOnlyList<string> GenerateSource(
        bool withRepositories = false,
        bool withUniqueConstraint = false
    )
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildDiagram(withUniqueConstraint),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = withRepositories,
            }
        );

        result.HasErrors.Should().BeFalse("ドリフト検証の前提として図は正常に生成できること");

        return result.Files.Select(file => file.Content).ToList();
    }

    /// <summary>EditModel クラスの宣言メンバーから、列・ナビゲーション由来の派生名を差し引いた残余を返す</summary>
    private static IReadOnlySet<string> ResidualMembers(
        IReadOnlyList<string> source,
        string editModelClassName,
        string entityClassName
    )
    {
        var declared = DeclaredMemberNames(source, editModelClassName);
        declared.ExceptWith(DerivedMemberNames(RootNames(source, entityClassName)));

        return declared;
    }

    /// <summary>Entity クラスの宣言メンバーから、列・ナビゲーション由来のプロパティ名を差し引いた残余を返す</summary>
    private static IReadOnlySet<string> EntityResidualMembers(
        IReadOnlyList<string> source,
        string entityClassName
    )
    {
        var declared = DeclaredMemberNames(source, entityClassName);
        declared.ExceptWith(RootNames(source, entityClassName));

        return declared;
    }

    /// <summary>
    /// 列・ナビゲーション由来の「元の名前」（プロパティ名）を Entity クラスの宣言から取り出す。
    /// </summary>
    /// <remarks>
    /// 識別は名簿との差ではなく<strong>属性の有無</strong>で行う（名簿で引くと「名簿に無い固定メンバーは
    /// 由来名とみなされて差し引かれる」ため、名簿の欠落をこのテストが検出できなくなる）。
    /// 列プロパティは <c>[Column("…")]</c>、ナビゲーションプロパティは <c>[NavigationReference(…)]</c> を必ず伴い、
    /// 固定メンバーはどちらも持たない。テスト側でカラム名→パスカルケース変換やナビゲーション名の複数形化を
    /// 再実装しないための取り方でもある（派生名規則の第 2 実装を作らない）。
    /// </remarks>
    private static IReadOnlySet<string> RootNames(
        IReadOnlyList<string> source,
        string entityClassName
    )
    {
        var names = FindClass(source, entityClassName)
            .Members.OfType<PropertyDeclarationSyntax>()
            .Where(property =>
                HasAttribute(property, "Column") || HasAttribute(property, "NavigationReference")
            )
            .Select(property => property.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        names
            .Should()
            .NotBeEmpty(
                $"{entityClassName} の列・ナビゲーション由来プロパティを属性で識別できること（属性の付与規則が変わるとこのテストは無意味になる）"
            );

        return names;
    }

    /// <summary>プロパティ宣言が指定名の属性を持つかどうか</summary>
    private static bool HasAttribute(PropertyDeclarationSyntax property, string attributeName) =>
        property
            .AttributeLists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() == attributeName);

    /// <summary>
    /// 1 つの元の名前 N から EditModel / Entity が派生させ得るメンバー名を列挙する
    /// （<c>N</c>・<c>_n</c>・<c>BindingN</c>・<c>_bindingN</c>・<c>_bindingNSnapshot</c>・<c>OnNChanging</c>・<c>OnNChanged</c>）。
    /// </summary>
    /// <remarks>
    /// ナビゲーション由来の名前では一部（<c>BindingN</c> 等）が実際には発行されないが、差し引く側なので
    /// 出ない名前が混じっても害はない。過不足なく差し引くことより「固定メンバーを取りこぼさない」ことを優先する。
    /// </remarks>
    private static IReadOnlySet<string> DerivedMemberNames(IEnumerable<string> rootNames)
    {
        var derived = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rootName in rootNames)
        {
            var bindingName = "Binding" + rootName;

            derived.Add(rootName);
            derived.Add(ToFieldName(rootName));
            derived.Add(bindingName);
            derived.Add(ToFieldName(bindingName));
            derived.Add(ToFieldName(bindingName) + "Snapshot");
            derived.Add($"On{rootName}Changing");
            derived.Add($"On{rootName}Changed");
        }

        return derived;
    }

    /// <summary>プロパティ名からバッキングフィールド名を組み立てる（生成側 <c>ToFieldName</c> と同じ規則）</summary>
    private static string ToFieldName(string propertyName) =>
        "_" + char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

    /// <summary>生成出力から指定名のクラス宣言を 1 つだけ取り出す</summary>
    private static ClassDeclarationSyntax FindClass(IReadOnlyList<string> source, string className)
    {
        var declaration = source
            .Select(content => CSharpSyntaxTree.ParseText(content).GetRoot())
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .SingleOrDefault(node => node.Identifier.ValueText == className);

        declaration.Should().NotBeNull($"生成出力に class {className} が 1 つだけ存在すること");

        return declaration!;
    }

    /// <summary>指定クラスが宣言するメンバー名（フィールド・プロパティ・メソッド）を Roslyn の構文木から列挙する</summary>
    private static HashSet<string> DeclaredMemberNames(
        IReadOnlyList<string> source,
        string className
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in FindClass(source, className).Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        names.Add(variable.Identifier.ValueText);
                    }

                    break;

                case PropertyDeclarationSyntax property:
                    names.Add(property.Identifier.ValueText);
                    break;

                case MethodDeclarationSyntax method:
                    // 同名オーバーロード（On{Prop}Changing の 4 本など）は集合が畳む
                    names.Add(method.Identifier.ValueText);
                    break;

                case EventDeclarationSyntax @event:
                    names.Add(@event.Identifier.ValueText);
                    break;

                case EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        names.Add(variable.Identifier.ValueText);
                    }

                    break;
            }
        }

        return names;
    }
}
