using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Dialects;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：名前・方言の宣言（QuickER.Model・QuickER.Dialects の正本を外で写さない）</summary>
internal static partial class SourceRules
{
    /// <summary>生成物の除外の理由</summary>
    private const string GeneratedSourceReason =
        "生成物（*.g.cs のランタイムのパッケージ用ソース・*.Designer.cs のリソースのアクセサ）は手で書くソースではない";

    /// <summary>C# 生成の Repository 方言で分岐している（後の区切りで空にする）</summary>
    private const string DialectCodeGenBranch =
        "C# 生成の Repository 方言で分岐している（生成の方言の宣言へ置き換える区切りで消す）";

    /// <summary>DB 接続ダイアログが方言名で分岐している（後の区切りで空にする）</summary>
    private const string DialectConnectionDialogBranch =
        "DB 接続ダイアログが方言名で表示を出し分けている（接続の設定項目の宣言へ置き換える区切りで消す）";

    /// <summary>既定の方言</summary>
    private const string DialectDefault =
        "既定の方言（図・設定に方言が無いとき・解決できないときの SQL Server）";

    /// <summary>C# 生成の既定・対応一覧と分岐が同じファイルにある</summary>
    private const string DialectCodeGenDefaultAndBranch =
        "C# 生成の Repository 方言の既定・対応一覧と分岐（分岐は生成の方言の宣言へ置き換える区切りで消す）";

    /// <summary>利用者に見せる表示名</summary>
    private const string DialectShownName = "利用者に見せる方言の表示名";

    /// <summary>生成物の名前空間の接尾辞</summary>
    private const string DialectNamespaceSuffix =
        "生成物の方言別の名前空間の接尾辞（パッケージ名の接尾辞と同じ綴り）";

    /// <summary>永続化した値・設定の許可値</summary>
    private const string DialectPersistedValue =
        "図ファイル・設定ファイルに保存する方言の値（名前付きクエリの生 SQL のキー・quicker.json の許可値と例）";

    /// <summary>ランタイムパッケージ用ソースの描画</summary>
    private const string DialectRuntimePackageRendering =
        "ランタイムパッケージ用ソースは方言を固定して描画する（パッケージ 1 本＝方言 1 つ）";

    /// <summary>生成ランタイム</summary>
    private const string DialectGeneratedRuntime =
        "依存ゼロの生成ランタイムは QuickER.Dialects を参照できない（EF Core のプロバイダ名の部分を見て LIKE のエスケープを決める）";

    /// <summary>名前の照合の例外 3：生成する C# の識別子</summary>
    private const string SchemaNameCSharpIdentifier =
        "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）";

    /// <summary>制約名の置換規則の写し（<c>.Replace(".", "_").Replace(" ", "_")</c> の連鎖）</summary>
    private static readonly Regex ConstraintNameRuleCopy = new(
        @"\.Replace\(\s*""\.""\s*,\s*""_""\s*\)\s*\.Replace\(\s*"" ""\s*,\s*""_""\s*\)",
        RegexOptions.Compiled
    );

    /// <summary>既定名の組み立て（<c>$"FK_{</c>・<c>$"PK_{</c>・<c>"FK_" +</c>・<c>"PK_" +</c>）</summary>
    private static readonly Regex ConstraintNameComposition = new(
        @"\$""(FK|PK)_\{|""(FK|PK)_""\s*\+",
        RegexOptions.Compiled
    );

    /// <summary>名前の照合の比較の指定（修飾を除いた右端の 2 つの名前）</summary>
    private static readonly Regex SchemaNameComparisonOption = new(
        @"^(StringComparison|StringComparer)\.(Ordinal|OrdinalIgnoreCase|InvariantCulture|InvariantCultureIgnoreCase|CurrentCulture|CurrentCultureIgnoreCase)$",
        RegexOptions.Compiled
    );

    /// <summary>名前の照合の大小・空白の変換</summary>
    private static readonly HashSet<string> SchemaNameConversions = new(StringComparer.Ordinal)
    {
        "Trim",
        "TrimStart",
        "TrimEnd",
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant",
    };

    /// <summary>モデルの名前に触れる文の目印（トークンを空白なしで連結した文に掛ける）</summary>
    private static readonly Regex SchemaNameModelName = new(
        @"\.(TableName|ConstraintName|ResultTypeName|Name)\b|\b(tableName|columnName|queryName|constraintName|fieldName|parameterName)\b|<string,(Entity|Column|QueryDefinition|QueryParameter|ProjectionField|Relationship|UniqueConstraint|EntityViewModel|ColumnViewModel)>",
        RegexOptions.Compiled
    );

    /// <summary>名前の照合の文の先頭（既知の一覧のキーに使う長さ）</summary>
    private const int SchemaNameNeedleLength = 90;

    private static IEnumerable<SourceRule> Names() =>
        [
            // 制約名の置換規則（ConstraintNames.SafeName）の写しを、正本の外に書かない。写しは変更のたびにずれ、同期の計画だけが
            // 前後の空白を除くようになって DDL との名前が食い違った。本文の文字列を全文の正規表現で見る（コメント・文字列の中も数える）
            SourceRule.Forbid(
                "constraint-name-safe-rule-copies",
                "制約名の置換規則は ConstraintNames.SafeName を呼び、写さない",
                ExceptGenerated(Src)
                    .Except(
                        "QuickER.Model/ConstraintNames.cs",
                        "制約名の規則の正本（ConstraintNames）そのもの"
                    ),
                GuardFinder.File(
                    GuardReader.Text,
                    file =>
                        ConstraintNameRuleCopy.Match(file.Text) is { Success: true } match
                            ?
                            [
                                new GuardFinding(
                                    "Replace(\".\", \"_\").Replace(\" \", \"_\")",
                                    file.LineAt(match.Index)
                                ),
                            ]
                            : []
                )
            ),
            // 外部キー・主キーの既定名の組み立てを ConstraintNames の外に書かない（1 行ずつ正規表現で見る。1 行に 1 件）。
            // string.Concat や書式指定などの別の形はすり抜けるので、既定名は必ず ConstraintNames のメソッドで組み立てる
            SourceRule.Forbid(
                "constraint-name-default-compositions",
                "外部キー・主キーの既定名は ConstraintNames.ForeignKey / PrimaryKey で組み立てる",
                ExceptGenerated(Src)
                    .Except(
                        "QuickER.Model/ConstraintNames.cs",
                        "制約名の規則の正本（ConstraintNames）そのもの"
                    ),
                GuardFinder.File(GuardReader.Text, FindConstraintNameCompositions),
                new GuardKnown(
                    "src/QuickER.Provider.Sqlite/SqliteSchemaImporter.cs",
                    "$\"FK_{",
                    1,
                    "名前を持たない SQLite の外部キーへ取込時に付ける識別用の名前（$\"FK_{テーブル}_{参照先}_{id}\"。id を含み、既定名の規則とは別物）"
                )
            ),
            // 一意制約の合成名（UniqueConstraint.SynthesizeName）を直接呼ぶのは、図全体の名前の解決（ConstraintNames.SynthesizeFreeName）の
            // 1 か所だけ。呼び出し側が直接合成すると、どれかの出力だけが番号を足さない名前を出し、ぶつかる図で DDL が断られる。
            // 呼び出しの形（修飾・using static）に依らず、呼ばれる名前が SynthesizeName の呼び出しを、含むメンバーの名前で数える
            SourceRule.Forbid(
                "unique-constraint-synthesize-name-calls",
                "一意制約の名前は ConstraintNames.ResolveUniqueConstraintNames / ResolveUniqueConstraintName を通す",
                ExceptGenerated(Src),
                GuardFinder.Nodes(DescribeSynthesizeNameCall),
                new GuardKnown(
                    "src/QuickER.Model/ConstraintNames.cs",
                    "SynthesizeFreeName",
                    1,
                    "図全体の名前の解決そのもの（ぶつかる名前に番号を足す）"
                )
            ),
            // 意味モデル（QuickER.Model）は方言名から識別子の上限を引かない（上限は呼び出し側が引数で渡す）。
            // 構文木で IdentifierLengthLimits という名前の参照を数える（コメント・XmlDoc の中の言及は数えない）
            SourceRule.Forbid(
                "model-dialect-lookups",
                "Model は方言名から識別子の上限を引かず、上限は呼び出し側が引数で渡す",
                SourceScope.Under("src/QuickER.Model"),
                GuardFinder.Nodes(node =>
                    node is IdentifierNameSyntax { Identifier.ValueText: "IdentifierLengthLimits" }
                        ? "IdentifierLengthLimits"
                        : null
                )
            ),
            // 図の名前の同一性の判定を SchemaNames の外で素の比較で書かない。比較の指定（StringComparison / StringComparer の Ordinal・
            // IgnoreCase 系）か大小・空白の変換（Trim・ToLower*・ToUpper*）を含む文のうち、モデルの名前に触れるものを拾う。
            // 拾う単位は文（if / while などの見出しの式の中ならその式だけ・文の外ならメンバー）で、何かは文のトークンを空白なしで
            // 連結した先頭 90 字。例外は理由つき（DB 取込のカタログの照合・生成した C# の識別子・綴りの判定・モデルの名前でないもの）
            SourceRule.Forbid(
                "schema-name-comparisons",
                "図の名前の照合は SchemaNames（AreSame / Comparer / ToStoredForm / Key）を使い、モデルの名前でない・例外なら理由つきで一覧に載せる",
                ExceptGenerated(Src)
                    .Except(
                        "QuickER.Model/SchemaNames.cs",
                        "名前の同一性の正本（SchemaNames）そのもの"
                    ),
                GuardFinder.File(GuardReader.SyntaxNodes, FindSchemaNameComparisons),
                new(
                    "src/QuickER.Provider/SchemaDiffService.cs",
                    "varrenameColumns=columnPairs.Any(pair=>!string.Equals(pair.Key.Name,pair.Value.Name,String",
                    1,
                    "同一性の判定ではなく、SchemaNames が同じとみなして組んだ相手と綴りまで一致するかを見る判定（違えば DB の綴りへ揃える）。同一性は SchemaNames が決める"
                ),
                new(
                    "src/QuickER.Model/ForeignKeyColumnResolver.cs",
                    "varranks=new(Func<CandidateColumn,bool>Matches,boolAllowPrimaryKey)[]{(column=>expectedNam",
                    1,
                    "外部キーの列の推論の優先ランク。同名の判定は SchemaNames.AreSame へ移した。残る素の比較は接尾辞の一致（EndsWith）で同一性の判定ではない"
                ),
                new(
                    "src/QuickER.CodeGen.UI/QueryItemViewModel.cs",
                    "returnnewQueryDefinition{Id=Id,EntityId=EntityId,Name=Name,Description=Description,Returns",
                    1,
                    "クエリ定義の組み立て。名前の前後の空白はモデルの setter が除く。残る Trim は型トークン・戻り型の値（名前ではない）"
                ),
                new(
                    "src/QuickER.AI.Mock/MockDesignDocExporter.cs",
                    "varlabels=members.Select(m=>ResolveName(m.Attrs,m.Tag,forLabels,wrappingLabels).Name).Wher",
                    1,
                    "モデルの名前ではない（モック画面の項目のラベル）"
                ),
                new(
                    "src/QuickER.AI.Mock/MockDesignDocExporter.cs",
                    "privatestaticstringScreenDisplayName(MockScreenscreen)=>string.IsNullOrWhiteSpace(screen.N",
                    1,
                    "モデルの名前ではない（モック画面の名前）"
                ),
                new(
                    "src/QuickER.AI.Mock/MockResumePrompt.cs",
                    "varname=string.IsNullOrWhiteSpace(screen.Name)?screen.File:screen.Name.Trim();",
                    1,
                    "モデルの名前ではない（モック画面の名前）"
                ),
                new(
                    "src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs",
                    "ApiModelCandidates.Any(c=>string.Equals(c.Name,m,StringComparison.OrdinalIgnoreCase))",
                    1,
                    "モデルの名前ではない（AI のモデル名）"
                ),
                new(
                    "src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs",
                    "CopilotModelCandidates.Any(c=>string.Equals(c.Name,m,StringComparison.OrdinalIgnoreCase))",
                    1,
                    "モデルの名前ではない（AI のモデル名）"
                ),
                new(
                    "src/QuickER.Cli/GenerationConfigLoader.cs",
                    "privatestaticreadonlyHashSet<string>KnownConfigKeys=new(GenerationConfigSchema.Keys.Select",
                    1,
                    "モデルの名前ではない（設定ファイルのキー）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.SampleData.cs",
                    "var(label,separator)=column.IsPrimaryKey?(column.Name.ToUpperInvariant(),\"-00\"):(column.Na",
                    1,
                    "照合ではない（インメモリのサンプル値の組み立て）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedTypeNames.cs",
                    "varrefused=newHashSet<string>(collisions.SelectMany(collision=>collision.Names).Concat(res",
                    1,
                    "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝エラーにした型名"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedTypeNames.cs",
                    "varcaseVariants=remaining.GroupBy(use=>use.Name,StringComparer.OrdinalIgnoreCase).Where(gr",
                    1,
                    "例外 3: 生成する C# の識別子＝大文字小文字だけ違う型名の組（警告。綴りは言語が区別するので止めない）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.MemberNames.cs",
                    "varspellings=group.Select(member=>member.Name).Distinct(StringComparer.Ordinal).ToList();",
                    1,
                    "例外 3: 生成する C# の識別子＝JSON の規則（大小無視）でまとめたメンバー名の綴りを数える（綴りが 2 つ以上ならエラー）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.ValueObjects.cs",
                    "!string.Equals(key,_nameConverter.ToColumnKey(column.Name),StringComparison.Ordinal)",
                    1,
                    SchemaNameCSharpIdentifier + "＝値オブジェクトの列キー"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.ValueObjects.cs",
                    "vardistinctTypes=resolved.Select(id=>_nameConverter.ToValueObjectClassName(owners[id].Colu",
                    1,
                    SchemaNameCSharpIdentifier + "＝値オブジェクトのクラス名"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpGenerationModelBuilder.ValueObjects.cs",
                    "varnatives=members.Where(member=>string.Equals(_nameConverter.ToColumnKey(member.Column.Na",
                    1,
                    SchemaNameCSharpIdentifier + "＝値オブジェクトの列キー"
                ),
                new(
                    "src/QuickER.CodeGen.UI/OutputFolderNamespaceSuggester.cs",
                    "csproj=current.EnumerateFiles(\"*.csproj\",SearchOption.TopDirectoryOnly).OrderBy(file=>file",
                    1,
                    "モデルの名前ではない（ファイル名の並び）"
                ),
                new(
                    "src/QuickER.CodeReverse.CSharp/CSharpReverseParser.cs",
                    "varcolumnsByPropertyName=newDictionary<string,Column>(StringComparer.Ordinal);",
                    1,
                    SchemaNameCSharpIdentifier + "＝プロパティ名から列を引く"
                ),
                new(
                    "src/QuickER.CodeReverse.CSharp/CSharpReverseParser.cs",
                    "varargument=FindAttribute(classDecl.AttributeLists,\"DbTableMeta\")?.ArgumentList?.Arguments",
                    1,
                    "モデルの名前ではない（C# の属性の引数名）"
                ),
                new(
                    "src/QuickER.CodeReverse.CSharp/CSharpReverseParser.cs",
                    "varnamed=attribute.ArgumentList?.Arguments.FirstOrDefault(argument=>string.Equals(argument",
                    1,
                    "モデルの名前ではない（C# の属性の引数名）"
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "Mode==DbConnectionDialogMode.Sync&&!string.Equals(lastUsed.Value.Profile.Dbms,SelectedProv",
                    1,
                    "モデルの名前ではない（方言名）"
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "privateboolIsSameDbms(stringdbms)=>string.Equals(dbms,SelectedProvider.Name,StringComparis",
                    1,
                    "モデルの名前ではない（方言名）"
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "varexisting=Profiles.FirstOrDefault(p=>string.Equals(p.Profile.Name,name,StringComparison.",
                    1,
                    "モデルの名前ではない（接続プロファイル名）"
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "privateboolCanRenameProfile()=>SelectedProfileis{}profile&&!string.IsNullOrWhiteSpace(Prof",
                    1,
                    "モデルの名前ではない（接続プロファイル名）"
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "varconflict=Profiles.Select(item=>item.Profile).FirstOrDefault(profile=>profile.Id!=target",
                    1,
                    "モデルの名前ではない（接続プロファイル名）"
                ),
                new(
                    "src/QuickER.Db.UI/SqlConnectionProfileStore.cs",
                    "publicList<SqlConnectionProfile>LoadAll()=>LoadData().Profiles.OrderBy(p=>p.Name,StringCom",
                    1,
                    "モデルの名前ではない（接続プロファイル名の並び）"
                ),
                new(
                    "src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs",
                    "privatestaticreadonlyDictionary<string,object>CatalogSchemas=ErDiagramToolCatalog.GetDefin",
                    1,
                    "モデルの名前ではない（ツール名 → 入力スキーマ）"
                ),
                new(
                    "src/QuickER.Mcp/McpToolSet.cs",
                    "_schemas=tools.ToDictionary(tool=>tool.Name,tool=>tool.InputSchema,StringComparer.Ordinal)",
                    1,
                    "モデルの名前ではない（ツール名 → 入力スキーマ）"
                ),
                new(
                    "src/QuickER.Provider/DatabaseProviderRegistry.cs",
                    "_byName=providers.ToDictionary(p=>p.Name,StringComparer.OrdinalIgnoreCase);",
                    1,
                    "モデルの名前ではない（方言名）"
                ),
                new(
                    "src/QuickER.Provider/SchemaSignature.cs",
                    "returnstring.Join(\",\",entity.UniqueConstraints.Select(u=>(u.Name??\"\")+\"(\"+string.Join(\"+\",",
                    1,
                    "変更の検知（綴りの違いも変更として数える署名）"
                ),
                new(
                    "src/QuickER.Provider/SchemaSignature.cs",
                    "vare=string.Join(\"|\",entities.OrderBy(x=>x.TableName,StringComparer.Ordinal).Select(x=>x.T",
                    1,
                    "変更の検知（綴りの違いも変更として数える署名）＝テーブルの並びを序数の全順序で決める"
                ),
                new(
                    "src/QuickER.Provider/SchemaSignature.cs",
                    "varr=string.Join(\"|\",relationships.Select(x=>x.SourceEntityId+\">\"+x.TargetEntityId+\":\"+x.T",
                    1,
                    "変更の検知（綴りの違いも変更として数える署名）＝リレーションの並びを序数の全順序で決める"
                ),
                new(
                    "src/QuickER.Provider/SqlTypeText.cs",
                    "varoffenders=columns.Where(pair=>!IsSafe(pair.Column.DataType)).Select(pair=>$\"{SqlComment",
                    1,
                    "照合ではない（エラーメッセージの組み立て）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/Queries/QueryDefinitionRules.cs",
                    "varsiblingMethodNames=others.Where(other=>other.EntityId==query.EntityId).Select(other=>To",
                    1,
                    SchemaNameCSharpIdentifier + "＝生成するメソッド名の衝突"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/Queries/QueryDefinitionRules.cs",
                    "varotherResultTypeNames=others.Where(other=>other.Returns==QueryReturnShape.Projection&&To",
                    1,
                    SchemaNameCSharpIdentifier + "＝射影の DTO の型名の衝突"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/Queries/QueryDefinitionRules.cs",
                    "returnstring.Equals(fieldName,resultTypeName,StringComparison.Ordinal)?QueryRuleViolation.",
                    1,
                    SchemaNameCSharpIdentifier
                        + "＝射影のフィールド名（プロパティ）と結果の型名（クラス）の衝突"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/Queries/QueryDefinitionRules.cs",
                    "IsReservedParameterName(parameterName)||string.Equals(parameterName,selectorFieldName,Stri",
                    1,
                    SchemaNameCSharpIdentifier
                        + "＝パラメータ名と射影の選択式の静的フィールド名の衝突"
                ),
                new(
                    "src/QuickER.Gui/Services/DocumentFileWatcher.cs",
                    "string.Equals(e.Name,targetName,StringComparison.OrdinalIgnoreCase)",
                    1,
                    "モデルの名前ではない（ファイル名）"
                ),
                new(
                    "src/QuickER.Gui/Services/MermaidExporter.cs",
                    "returnwritten.Select((relationship,index)=>(relationship,index)).Any(item=>!string.IsNullO",
                    1,
                    "往復の検査（書いた名前が綴りまでそのまま取り込まれるかを見る）"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionContentBuilder.cs",
                    "publicstaticList<Entity>OrderEntities(IEnumerable<Entity>entities)=>entities.OrderBy(entit",
                    1,
                    "照合ではない（定義書の表示の並び）"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs",
                    "varsanitized=newstring(tableName.Where(ch=>!InvalidWorksheetNameChars.Contains(ch)).ToArra",
                    1,
                    "モデルの名前ではない（シート名の組み立て）"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
                    "vartargetDbms=workbook.CustomProperties.FirstOrDefault(property=>string.Equals(property.Na",
                    1,
                    "モデルの名前ではない（ブックのカスタムプロパティ名）"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
                    "varraw=workbook.CustomProperties.FirstOrDefault(property=>string.Equals(property.Name,Tabl",
                    1,
                    "モデルの名前ではない（ブックのカスタムプロパティ名）"
                ),
                new(
                    "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
                    "varcolumn=newColumn{Name=columnName,Description=GetCellText(worksheet,row,3),DataType=data",
                    1,
                    "照合ではない（キー欄の PK / FK の記号）"
                ),
                new(
                    "src/QuickER.Gui/ViewModels/MainViewModel.Search.cs",
                    "(entity.TableName??string.Empty).Contains(query,StringComparison.OrdinalIgnoreCase)",
                    1,
                    "部分一致の検索（同一性の判定ではない）"
                ),
                new(
                    "src/QuickER.Gui/ViewModels/MainViewModel.Search.cs",
                    "(column.Name??string.Empty).Contains(query,StringComparison.OrdinalIgnoreCase)",
                    1,
                    "部分一致の検索（同一性の判定ではない）"
                )
            ),
            // 方言名の文字列は方言の宣言（QuickER.Dialects）に置き、機能は方言の宣言に問い合わせる（規則 1・3）。
            // 値が 5 つの方言名のどれかと大文字小文字を無視して完全に一致する文字列リテラルを、綴りどおりの値で数える
            // （部分一致・前後の空白・補間文字列の中は拾わない。コメント・XmlDoc の中は数えない）
            SourceRule.Forbid(
                "dialect-name-literals",
                "方言名の文字列は QuickER.Dialects に置き、機能は方言の宣言に問い合わせる（規則 1・3）",
                Src.Except("QuickER.Dialects/", "方言名の正本（方言の宣言）そのもの"),
                GuardFinder.Nodes(node =>
                    node is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && DialectNames.All.Any(name =>
                        string.Equals(
                            name,
                            literal.Token.ValueText,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        ? literal.Token.ValueText
                        : null
                ),
                new(
                    "src/QuickER.AI.Mock/MockProjectScaffoldService.cs",
                    "sqlserver",
                    1,
                    DialectDefault
                ),
                new(
                    "src/QuickER.AI.Mock/MockProjectTargetProfile.cs",
                    "sqlite",
                    2,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.AI.Mock/MockProjectTargetProfile.cs",
                    "sqlserver",
                    2,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.Cli/GenerationConfigSchema.cs",
                    "sqlite",
                    2,
                    DialectPersistedValue
                ),
                new(
                    "src/QuickER.Cli/GenerationConfigSchema.cs",
                    "sqlserver",
                    1,
                    DialectPersistedValue
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs",
                    "sqlite",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs",
                    "sqlserver",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CodeGenerationOptions.cs",
                    "sqlite",
                    1,
                    DialectCodeGenDefaultAndBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CodeGenerationOptions.cs",
                    "sqlserver",
                    4,
                    DialectCodeGenDefaultAndBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs",
                    "SqlServer",
                    1,
                    DialectNamespaceSuffix
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs",
                    "Sqlite",
                    1,
                    DialectNamespaceSuffix
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedFilePlanner.cs",
                    "sqlite",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/GeneratedFileUsings.cs",
                    "sqlite",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/MultiDialectTypeReconciler.cs",
                    "sqlserver",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/RuntimePackageSourceRenderer.cs",
                    "sqlite",
                    1,
                    DialectRuntimePackageRendering
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/RuntimePackageSourceRenderer.cs",
                    "sqlserver",
                    11,
                    DialectRuntimePackageRendering
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/RuntimePackages.cs",
                    "sqlite",
                    1,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs",
                    "SQLite",
                    1,
                    DialectShownName
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs",
                    "sqlite",
                    2,
                    DialectCodeGenBranch
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/ScribanCSharpRenderer.cs",
                    "sqlserver",
                    2,
                    DialectCodeGenDefaultAndBranch
                ),
                new(
                    "src/QuickER.CodeGen.UI/QueryItemViewModel.cs",
                    "sqlite",
                    3,
                    DialectPersistedValue
                ),
                new(
                    "src/QuickER.CodeGen.UI/QueryItemViewModel.cs",
                    "sqlserver",
                    3,
                    DialectPersistedValue
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "mysql",
                    1,
                    DialectConnectionDialogBranch
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "oracle",
                    1,
                    DialectConnectionDialogBranch
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "postgresql",
                    1,
                    DialectConnectionDialogBranch
                ),
                new(
                    "src/QuickER.Db.UI/DbConnectionDialogViewModel.cs",
                    "sqlserver",
                    1,
                    DialectDefault
                ),
                new(
                    "src/QuickER.Db.UI/IDbConnectionDialogPresenter.cs",
                    "sqlserver",
                    1,
                    DialectDefault
                ),
                new("src/QuickER.Gui/ViewModels/MainViewModel.cs", "sqlserver", 1, DialectDefault),
                new("src/QuickER.Provider.MySql/MySqlProvider.cs", "MySQL", 1, DialectShownName),
                new("src/QuickER.Provider.Oracle/OracleProvider.cs", "Oracle", 1, DialectShownName),
                new(
                    "src/QuickER.Provider.PostgreSql/PostgreSqlProvider.cs",
                    "PostgreSQL",
                    1,
                    DialectShownName
                ),
                new("src/QuickER.Provider.Sqlite/SqliteProvider.cs", "SQLite", 1, DialectShownName),
                new(
                    "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
                    "MySql",
                    1,
                    DialectGeneratedRuntime
                ),
                new(
                    "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
                    "Oracle",
                    1,
                    DialectGeneratedRuntime
                ),
                new(
                    "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
                    "SqlServer",
                    1,
                    DialectGeneratedRuntime
                ),
                new(
                    "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
                    "Sqlite",
                    1,
                    DialectGeneratedRuntime
                )
            ),
            // テーブル名を . で分ける・探す処理は SchemaQualifiedName（QuickER.Dialects）の外に写さない。第 1 引数が '.' か "." の
            // Split・IndexOf・LastIndexOf・Contains の呼び出しを、メソッドの名前で数える（テーブル名でない用途も理由つきで一覧へ）。
            // 生成ランタイムの EntitySaveMetadata.QuoteTableName の写しはこの範囲の外（DialectIdentifierTableTests の行 runtime-quote-* が固定）
            SourceRule.Forbid(
                "schema-qualified-name-dot-splits",
                "テーブル名を . で分ける・探すときは SchemaQualifiedName（QuickER.Dialects）を使う",
                ExceptGenerated(
                    SrcProjectsWhere(
                        "テーブル名を扱う QuickER.Dialects・QuickER.Model・QuickER.Provider*・QuickER.CodeGen.CSharp だけを見る",
                        name =>
                            name.StartsWith("QuickER.Dialects", StringComparison.Ordinal)
                            || name.StartsWith("QuickER.Model", StringComparison.Ordinal)
                            || name.StartsWith("QuickER.Provider", StringComparison.Ordinal)
                            || name.StartsWith("QuickER.CodeGen.CSharp", StringComparison.Ordinal)
                    ),
                    resourceAccessors: false
                ),
                GuardFinder.Nodes(node =>
                    node
                        is InvocationExpressionSyntax
                        {
                            Expression: MemberAccessExpressionSyntax
                            {
                                Name.Identifier.ValueText: "Split"
                                    or "IndexOf"
                                    or "LastIndexOf"
                                    or "Contains"
                            } member,
                        } invocation
                    && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                        is LiteralExpressionSyntax { Token.ValueText: "." }
                        ? member.Name.Identifier.ValueText
                        : null
                ),
                new(
                    "src/QuickER.Dialects/SchemaQualifiedName.cs",
                    "Contains",
                    1,
                    "規則の正本そのもの"
                ),
                new(
                    "src/QuickER.Dialects/SchemaQualifiedName.cs",
                    "IndexOf",
                    1,
                    "規則の正本そのもの"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/CSharpNamespaceValidator.cs",
                    "Split",
                    1,
                    "名前空間を区間に分ける（テーブル名ではない）"
                ),
                new(
                    "src/QuickER.CodeGen.CSharp/Queries/QueryConditionTypeChecker.cs",
                    "Contains",
                    1,
                    "簡易 DSL の数値のリテラルに小数点があるか（テーブル名ではない）"
                )
            ),
            // 方言の能力の宣言（SyncDialectCapabilities）のプロパティは初期化子（既定値）を持たない。required と初期化子は両立し、
            // 既定値が残ってもビルドは通るため、構文木で確かめる（required であることは SyncDialectCapabilitiesNoDefaultsTests が反射で見る）
            SourceRule.Forbid(
                "sync-capabilities-initializers",
                "方言の宣言に既定値を持たせない（規則 2）",
                SourceScope.Files("src", "src/QuickER.Provider/SyncDialectCapabilities.cs"),
                GuardFinder.Nodes(node =>
                    node
                        is PropertyDeclarationSyntax
                        {
                            Initializer: not null,
                            Parent: RecordDeclarationSyntax
                            {
                                Identifier.ValueText: "SyncDialectCapabilities"
                            },
                        } property
                        ? property.Identifier.ValueText
                        : null
                )
            ),
            // 方言の宣言をまとめる QuickER.Dialects は何も参照しない（依存ゼロ）。参照すると、それが参照する側（Model・Mcp）の
            // 依存へそのまま広がる。依存の 3 種類（PackageReference / ProjectReference / FrameworkReference）をすべて見る
            SourceRule.Forbid(
                "dialects-project-references",
                "方言の宣言は最下層に置き、何も参照しない（参照する側の依存の層を崩さない）",
                SourceScope.Files("src", "src/QuickER.Dialects/QuickER.Dialects.csproj"),
                GuardFinder.File(GuardReader.Xml, FindProjectReferences)
            ),
        ];

    /// <summary>範囲から、根の下の生成物（<c>*.g.cs</c>・<c>*.Designer.cs</c>）を 1 つずつ除く（いまあるファイルを列挙して除外にする）</summary>
    private static SourceScope ExceptGenerated(SourceScope scope, bool resourceAccessors = true) =>
        SourceFiles
            .Enumerate(scope.Root, "*" + scope.Extension)
            .Select(file => file.AreaRelativePath)
            .Where(path =>
                path.EndsWith(".g.cs", StringComparison.Ordinal)
                || (resourceAccessors && path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            )
            .Order(StringComparer.Ordinal)
            .Aggregate(scope, (current, path) => current.Except(path, GeneratedSourceReason));

    /// <summary>src のうち、フォルダ（プロジェクト）の名前が条件に合うものだけ（ほかのプロジェクトのフォルダを 1 つずつ除く）</summary>
    /// <param name="reason">除く理由（範囲の形）</param>
    /// <param name="keep">残すプロジェクトのフォルダの名前の条件</param>
    private static SourceScope SrcProjectsWhere(string reason, Func<string, bool> keep) =>
        Directory
            .EnumerateDirectories(Path.Combine(SourceFiles.RepositoryRoot, "src"))
            .Select(directory => Path.GetFileName(directory))
            .Where(name => !keep(name))
            .Order(StringComparer.Ordinal)
            .Aggregate(Src, (scope, name) => scope.Except(name + "/", reason));

    /// <summary>既定名の組み立ての行（1 行に 1 件。何かは最初に一致した表記）</summary>
    private static IEnumerable<GuardFinding> FindConstraintNameCompositions(GuardFile file)
    {
        var lines = file.Text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (ConstraintNameComposition.Match(lines[i]) is { Success: true } match)
            {
                yield return new GuardFinding(Regex.Replace(match.Value, @"\s+", " "), i + 1);
            }
        }
    }

    /// <summary>呼ばれる名前が <c>SynthesizeName</c> の呼び出しなら、含むメンバー（メソッド・プロパティ）の名前</summary>
    private static string? DescribeSynthesizeNameCall(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        } == "SynthesizeName"
            ? invocation
                .Ancestors()
                .OfType<MemberDeclarationSyntax>()
                .Select(member =>
                    member switch
                    {
                        MethodDeclarationSyntax method => method.Identifier.ValueText,
                        PropertyDeclarationSyntax property => property.Identifier.ValueText,
                        _ => null,
                    }
                )
                .FirstOrDefault(name => name is not null)
                ?? ""
            : null;

    /// <summary>素の比較・変換を含み、モデルの名前に触れる文（同じ文は 1 回だけ。何かは文の先頭 90 字）</summary>
    private static IEnumerable<GuardFinding> FindSchemaNameComparisons(GuardFile file)
    {
        var containers = new HashSet<(int Kind, Microsoft.CodeAnalysis.Text.TextSpan Span)>();

        foreach (var node in file.Nodes)
        {
            if (!IsSchemaNameComparisonOrConversion(node))
            {
                continue;
            }

            var container = SchemaNameContainerOf(node);

            if (container is null || !containers.Add((container.RawKind, container.Span)))
            {
                continue;
            }

            var text = string.Concat(container.DescendantTokens().Select(token => token.Text));

            if (SchemaNameModelName.IsMatch(text))
            {
                yield return new GuardFinding(
                    text.Length <= SchemaNameNeedleLength ? text : text[..SchemaNameNeedleLength],
                    GuardFile.LineOf(container)
                );
            }
        }
    }

    /// <summary>
    /// 比較・変換を含む単位（文。ただし if / while などの見出しの式の中なら、本体を含めずその式だけ。
    /// 文の外＝式本体のメンバー・フィールドの初期化子ならそのメンバー）
    /// </summary>
    private static SyntaxNode? SchemaNameContainerOf(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            var header = ancestor switch
            {
                IfStatementSyntax statement => statement.Condition,
                WhileStatementSyntax statement => statement.Condition,
                DoStatementSyntax statement => statement.Condition,
                ForEachStatementSyntax statement => statement.Expression,
                SwitchStatementSyntax statement => statement.Expression,
                _ => null,
            };

            if (header is not null && header.Span.Contains(node.Span))
            {
                return header;
            }

            if (ancestor is StatementSyntax and not BlockSyntax)
            {
                return ancestor;
            }

            if (ancestor is MemberDeclarationSyntax)
            {
                return ancestor;
            }
        }

        return null;
    }

    /// <summary>比較の指定か大小・空白の変換か（比較の指定は修飾を除いた右端の 2 つの名前で見る）</summary>
    private static bool IsSchemaNameComparisonOrConversion(SyntaxNode node) =>
        node switch
        {
            MemberAccessExpressionSyntax access => SchemaNameComparisonOption.IsMatch(
                $"{RightmostName(access.Expression)}.{access.Name.Identifier.Text}"
            ),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } =>
                SchemaNameConversions.Contains(access.Name.Identifier.Text),
            _ => false,
        };

    /// <summary>式の右端の単純名（<c>System.StringComparison</c> なら <c>StringComparison</c>。名前でなければ式そのもの）</summary>
    private static string RightmostName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => expression.ToString(),
        };

    /// <summary>csproj の依存の参照（PackageReference / ProjectReference / FrameworkReference。何かは「要素名 Include」）</summary>
    private static IEnumerable<GuardFinding> FindProjectReferences(GuardFile file) =>
        XDocument
            .Parse(file.Text, LoadOptions.SetLineInfo)
            .Descendants()
            .Where(element =>
                element.Name.LocalName
                    is "PackageReference"
                        or "ProjectReference"
                        or "FrameworkReference"
            )
            .Select(element => new GuardFinding(
                $"{element.Name.LocalName} {(string?)element.Attribute("Include")}",
                ((IXmlLineInfo)element).LineNumber
            ));
}
