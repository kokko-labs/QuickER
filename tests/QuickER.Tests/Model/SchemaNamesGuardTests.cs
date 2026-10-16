using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Model;

/// <summary>
/// 図の名前の同一性の判定が <c>SchemaNames</c> の外で素の比較（<c>StringComparison.Ordinal*</c>・<c>StringComparer.Ordinal*</c>・
/// <c>Trim</c>・<c>ToLowerInvariant</c> など）で書かれていないことを、ソースを走査して確かめるガード
/// </summary>
/// <remarks>
/// <para>
/// 名前の照合を入口ごとに写すと、入口ごとに答えが割れる（三巡目レビューの欠陥の型 ①＝空白を除く前の名前で重複を判定し、
/// setter が空白を除いて完全に同名ができる・大小だけ違う名前を片方の入口だけが別物として受け付ける）。
/// </para>
/// <para>
/// <b>検出する形</b>（完全な網ではない）: <c>src/</c> の <c>*.cs</c>（<c>bin</c> / <c>obj</c>・<c>*.g.cs</c>・<c>*.Designer.cs</c>・
/// 正本の <c>SchemaNames.cs</c> を除く）を構文木で読み、比較の指定（<c>StringComparison</c> / <c>StringComparer</c> の
/// Ordinal・IgnoreCase 系）か大小・空白の変換（<c>Trim</c>・<c>ToLower*</c>・<c>ToUpper*</c>）を含む文のうち、モデルの名前
/// （<c>.TableName</c>・<c>.Name</c>・<c>.ConstraintName</c>・<c>.ResultTypeName</c>・<c>tableName</c> などの変数・モデルの型を値に持つ
/// 文字列キーの辞書）に触れるものを拾う。拾った文は、すべて下の一覧（<see cref="Entries"/>）のどれかに当たらなければ赤、
/// 一覧の項目がどの文にも当たらなければ（直した・消した）赤。
/// </para>
/// <para>
/// 一覧は 2 種類: (1) 未移行＝<c>SchemaNames</c> へ移すまでの置き場（<see cref="Unmigrated_IsEmpty"/> が空を保つ）。
/// (2) 例外＝理由つきで素の比較を残す（DB 取込のカタログの照合・生 SQL のパラメータの束縛・生成した C# の識別子、
/// 綴りの判定〔<c>SchemaNames</c> が同じとみなす相手と綴りまで一致するかを見る〕、およびモデルの名前ではないもの）。
/// </para>
/// </remarks>
public sealed class SchemaNamesGuardTests
{
    /// <summary>一覧の項目の種類</summary>
    /// <remarks>
    /// 同期の計画と差分（<c>SyncPlanner</c> / <c>SchemaDiffService</c>）の照合も <c>SchemaNames</c> へ移し終えたので、
    /// 未移行の置き場は 1 つだけにした。素の比較が新しく現れたら、<see cref="Unmigrated"/>（空でなければ赤）か
    /// <see cref="Exception"/>（理由つき）のどちらかに載る。
    /// </remarks>
    public enum Group
    {
        /// <summary>未移行（SchemaNames へ移す。<see cref="Unmigrated_IsEmpty"/> が空を保つ）</summary>
        Unmigrated,

        /// <summary>例外（理由つきで素の比較を残す）</summary>
        Exception,
    }

    /// <summary>一覧の項目</summary>
    /// <param name="File">ファイル名（src からの相対パスの末尾）</param>
    /// <param name="Needle">文の先頭（空白・コメントを除いたトークンの連結）</param>
    /// <param name="Kind">種類</param>
    /// <param name="Reason">理由・指摘</param>
    public sealed record Entry(string File, string Needle, Group Kind, string Reason);

    private static readonly Regex Comparison = new(
        @"^(StringComparison|StringComparer)\.(Ordinal|OrdinalIgnoreCase|InvariantCulture|InvariantCultureIgnoreCase|CurrentCulture|CurrentCultureIgnoreCase)$",
        RegexOptions.Compiled
    );

    private static readonly HashSet<string> CaseOrSpaceConversions = new(StringComparer.Ordinal)
    {
        "Trim",
        "TrimStart",
        "TrimEnd",
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant",
    };

    private static readonly Regex ModelName = new(
        @"\.(TableName|ConstraintName|ResultTypeName|Name)\b|\b(tableName|columnName|queryName|constraintName|fieldName|parameterName)\b|<string,(Entity|Column|QueryDefinition|QueryParameter|ProjectionField|Relationship|UniqueConstraint|EntityViewModel|ColumnViewModel)>",
        RegexOptions.Compiled
    );

    [Fact(
        DisplayName = "図の名前の照合は SchemaNames の外で素の比較を書いていない（一覧と厳密に一致する）"
    )]
    public void NameComparisons_MatchTheList()
    {
        var hits = FindHits();
        // 一覧の項目は文を 1 つずつ消費する（書き出しが同じ文が 2 つあるとき、片方を直したら項目が 1 つ余って赤になる）
        var unmatched = hits.ToList();
        var stale = new List<Entry>();

        foreach (var entry in Entries)
        {
            var index = unmatched.FindIndex(hit => Matches(entry, hit));

            if (index < 0)
            {
                stale.Add(entry);
            }
            else
            {
                unmatched.RemoveAt(index);
            }
        }
        var problems = new StringBuilder();

        if (unmatched.Count > 0)
        {
            problems.AppendLine(
                "SchemaNames を通さない名前の照合が見つかった（SchemaNames.AreSame / Comparer / ToStoredForm を使うか、"
                    + "モデルの名前でない・例外なら理由つきで一覧に足す）:"
            );

            foreach (var hit in unmatched)
            {
                problems.AppendLine(
                    $"  new(\"{FileName(hit.Path)}\", \"{Escape(Needle(hit.Text))}\", Group.Unmigrated, \"\"),  // {hit.Path}:{hit.Line}"
                );
            }
        }

        if (stale.Count > 0)
        {
            problems.AppendLine(
                "一覧の項目がどの文にも当たらない（直した・消したなら一覧から外す）:"
            );

            foreach (var entry in stale)
            {
                problems.AppendLine(
                    $"  {entry.File}: {entry.Needle}（{entry.Kind}・{entry.Reason}）"
                );
            }
        }

        if (problems.Length > 0)
        {
            throw new Xunit.Sdk.XunitException(problems.ToString());
        }
    }

    [Fact(DisplayName = "未移行の項目は一覧に残っていない（SchemaNames へ移し終えている）")]
    public void Unmigrated_IsEmpty()
    {
        var unmigrated = Entries.Where(entry => entry.Kind == Group.Unmigrated).ToList();

        if (unmigrated.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                "未移行の項目が残っている（SchemaNames へ移すか、理由つきの例外にする）: "
                    + string.Join(
                        " / ",
                        unmigrated.Select(entry => $"{entry.File}: {entry.Needle}")
                    )
            );
        }
    }

    /// <summary>文の先頭（一覧の項目の照合に使う長さ）</summary>
    private const int NeedleLength = 90;

    private static string Needle(string text) =>
        text.Length <= NeedleLength ? text : text[..NeedleLength];

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool Matches(Entry entry, (string Path, int Line, string Text) hit) =>
        hit.Path.EndsWith("/" + entry.File, StringComparison.Ordinal)
        && hit.Text.StartsWith(entry.Needle, StringComparison.Ordinal);

    /// <summary>素の比較・変換を含み、モデルの名前に触れる文を集める</summary>
    private static List<(string Path, int Line, string Text)> FindHits()
    {
        var src = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src");
        var hits = new List<(string Path, int Line, string Text)>();

        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path).Replace('\\', '/');

            if (
                relative.Split('/').Any(segment => segment is "bin" or "obj")
                || relative.EndsWith(".g.cs", StringComparison.Ordinal)
                || relative.EndsWith(".Designer.cs", StringComparison.Ordinal)
                || relative.EndsWith("/SchemaNames.cs", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            var containers = new HashSet<SyntaxNode>();

            foreach (var node in root.DescendantNodes())
            {
                if (!IsComparisonOrConversion(node))
                {
                    continue;
                }

                var container = ContainerOf(node);

                if (container is not null && containers.Add(container))
                {
                    var text = string.Concat(
                        container.DescendantTokens().Select(token => token.Text)
                    );

                    if (ModelName.IsMatch(text))
                    {
                        var line = container.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        hits.Add((relative, line, text));
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// 比較・変換を含む単位（文。ただし if / while などの見出しの式の中なら、本体を含めずその式だけ。
    /// 文の外＝式本体のメンバー・フィールドの初期化子ならそのメンバー）
    /// </summary>
    private static SyntaxNode? ContainerOf(SyntaxNode node)
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

    /// <remarks>
    /// 比較の指定は、修飾を除いた右端の 2 つの名前で見る（<c>StringComparison.Ordinal</c>・<c>System.StringComparison.Ordinal</c>・
    /// <c>global::System.StringComparison.Ordinal</c> のどの綴りでも当たる）。
    /// </remarks>
    private static bool IsComparisonOrConversion(SyntaxNode node) =>
        node switch
        {
            MemberAccessExpressionSyntax access => Comparison.IsMatch(
                $"{RightmostName(access.Expression)}.{access.Name.Identifier.Text}"
            ),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } =>
                CaseOrSpaceConversions.Contains(access.Name.Identifier.Text),
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

    /// <summary>一覧（未移行・例外）</summary>
    public static readonly IReadOnlyList<Entry> Entries =
    [
        // ---- 未移行（区切り 22 で SchemaNames へ移して一覧から消す。移し終えた P1 の時点で空） ----
        // ---- 例外（理由つきで素の比較を残す） ----
        new(
            "SchemaDiffService.cs",
            "varrenameColumns=columnPairs.Any(pair=>!string.Equals(pair.Key.Name,pair.Value.Name,String",
            Group.Exception,
            "同一性の判定ではなく、SchemaNames が同じとみなして組んだ相手と綴りまで一致するかを見る判定（違えば DB の綴りへ揃える）。同一性は SchemaNames が決める"
        ),
        new(
            "ForeignKeyColumnResolver.cs",
            "varranks=new(Func<CandidateColumn,bool>Matches,boolAllowPrimaryKey)[]{(column=>expectedNam",
            Group.Exception,
            "外部キーの列の推論の優先ランク。同名の判定は SchemaNames.AreSame へ移した。残る素の比較は接尾辞の一致（EndsWith）で同一性の判定ではない"
        ),
        new(
            "QueryItemViewModel.cs",
            "returnnewQueryDefinition{Id=Id,EntityId=EntityId,Name=Name,Description=Description,Returns",
            Group.Exception,
            "クエリ定義の組み立て。名前の前後の空白はモデルの setter が除く。残る Trim は型トークン・戻り型の値（名前ではない）"
        ),
        new(
            "MockDesignDocExporter.cs",
            "varlabels=members.Select(m=>ResolveName(m.Attrs,m.Tag,forLabels,wrappingLabels).Name).Wher",
            Group.Exception,
            "モデルの名前ではない（モック画面の項目のラベル）"
        ),
        new(
            "MockDesignDocExporter.cs",
            "privatestaticstringScreenDisplayName(MockScreenscreen)=>string.IsNullOrWhiteSpace(screen.N",
            Group.Exception,
            "モデルの名前ではない（モック画面の名前）"
        ),
        new(
            "MockResumePrompt.cs",
            "varname=string.IsNullOrWhiteSpace(screen.Name)?screen.File:screen.Name.Trim();",
            Group.Exception,
            "モデルの名前ではない（モック画面の名前）"
        ),
        new(
            "ChatConnectionSettingsViewModel.cs",
            "ApiModelCandidates.Any(c=>string.Equals(c.Name,m,StringComparison.OrdinalIgnoreCase))",
            Group.Exception,
            "モデルの名前ではない（AI のモデル名）"
        ),
        new(
            "ChatConnectionSettingsViewModel.cs",
            "CopilotModelCandidates.Any(c=>string.Equals(c.Name,m,StringComparison.OrdinalIgnoreCase))",
            Group.Exception,
            "モデルの名前ではない（AI のモデル名）"
        ),
        new(
            "GenerationConfigLoader.cs",
            "privatestaticreadonlyHashSet<string>KnownConfigKeys=new(GenerationConfigSchema.Keys.Select",
            Group.Exception,
            "モデルの名前ではない（設定ファイルのキー）"
        ),
        new(
            "CSharpGenerationModelBuilder.SampleData.cs",
            "var(label,separator)=column.IsPrimaryKey?(column.Name.ToUpperInvariant(),\"-00\"):(column.Na",
            Group.Exception,
            "照合ではない（インメモリのサンプル値の組み立て）"
        ),
        new(
            "CSharpGenerationModelBuilder.ValueObjects.cs",
            "!string.Equals(key,_nameConverter.ToColumnKey(column.Name),StringComparison.Ordinal)",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝値オブジェクトの列キー"
        ),
        new(
            "CSharpGenerationModelBuilder.ValueObjects.cs",
            "vardistinctTypes=resolved.Select(id=>_nameConverter.ToValueObjectClassName(owners[id].Colu",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝値オブジェクトのクラス名"
        ),
        new(
            "CSharpGenerationModelBuilder.ValueObjects.cs",
            "varnatives=members.Where(member=>string.Equals(_nameConverter.ToColumnKey(member.Column.Na",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝値オブジェクトの列キー"
        ),
        new(
            "OutputFolderNamespaceSuggester.cs",
            "csproj=current.EnumerateFiles(\"*.csproj\",SearchOption.TopDirectoryOnly).OrderBy(file=>file",
            Group.Exception,
            "モデルの名前ではない（ファイル名の並び）"
        ),
        new(
            "CSharpReverseParser.cs",
            "varcolumnsByPropertyName=newDictionary<string,Column>(StringComparer.Ordinal);",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝プロパティ名から列を引く"
        ),
        new(
            "CSharpReverseParser.cs",
            "varargument=FindAttribute(classDecl.AttributeLists,\"DbTableMeta\")?.ArgumentList?.Arguments",
            Group.Exception,
            "モデルの名前ではない（C# の属性の引数名）"
        ),
        new(
            "CSharpReverseParser.cs",
            "varnamed=attribute.ArgumentList?.Arguments.FirstOrDefault(argument=>string.Equals(argument",
            Group.Exception,
            "モデルの名前ではない（C# の属性の引数名）"
        ),
        new(
            "DbConnectionDialogViewModel.cs",
            "Mode==DbConnectionDialogMode.Sync&&!string.Equals(lastUsed.Value.Profile.Dbms,SelectedProv",
            Group.Exception,
            "モデルの名前ではない（方言名）"
        ),
        new(
            "DbConnectionDialogViewModel.cs",
            "privateboolIsSameDbms(stringdbms)=>string.Equals(dbms,SelectedProvider.Name,StringComparis",
            Group.Exception,
            "モデルの名前ではない（方言名）"
        ),
        new(
            "DbConnectionDialogViewModel.cs",
            "varexisting=Profiles.FirstOrDefault(p=>string.Equals(p.Profile.Name,name,StringComparison.",
            Group.Exception,
            "モデルの名前ではない（接続プロファイル名）"
        ),
        new(
            "DbConnectionDialogViewModel.cs",
            "privateboolCanRenameProfile()=>SelectedProfileis{}profile&&!string.IsNullOrWhiteSpace(Prof",
            Group.Exception,
            "モデルの名前ではない（接続プロファイル名）"
        ),
        new(
            "DbConnectionDialogViewModel.cs",
            "varconflict=Profiles.Select(item=>item.Profile).FirstOrDefault(profile=>profile.Id!=target",
            Group.Exception,
            "モデルの名前ではない（接続プロファイル名）"
        ),
        new(
            "SqlConnectionProfileStore.cs",
            "publicList<SqlConnectionProfile>LoadAll()=>LoadData().Profiles.OrderBy(p=>p.Name,StringCom",
            Group.Exception,
            "モデルの名前ではない（接続プロファイル名の並び）"
        ),
        new(
            "ErDiagramHostChatAdapter.cs",
            "privatestaticreadonlyDictionary<string,object>CatalogSchemas=ErDiagramToolCatalog.GetDefin",
            Group.Exception,
            "モデルの名前ではない（ツール名 → 入力スキーマ）"
        ),
        new(
            "McpToolSet.cs",
            "_schemas=tools.ToDictionary(tool=>tool.Name,tool=>tool.InputSchema,StringComparer.Ordinal)",
            Group.Exception,
            "モデルの名前ではない（ツール名 → 入力スキーマ）"
        ),
        new(
            "DatabaseProviderRegistry.cs",
            "_byName=providers.ToDictionary(p=>p.Name,StringComparer.OrdinalIgnoreCase);",
            Group.Exception,
            "モデルの名前ではない（方言名）"
        ),
        new(
            "SchemaSignature.cs",
            "returnstring.Join(\",\",entity.UniqueConstraints.Select(u=>(u.Name??\"\")+\"(\"+string.Join(\"+\",",
            Group.Exception,
            "変更の検知（綴りの違いも変更として数える署名）"
        ),
        new(
            "SchemaSignature.cs",
            "vare=string.Join(\"|\",entities.OrderBy(x=>x.TableName,StringComparer.Ordinal).Select(x=>x.T",
            Group.Exception,
            "変更の検知（綴りの違いも変更として数える署名）＝テーブルの並びを序数の全順序で決める"
        ),
        new(
            "SchemaSignature.cs",
            "varr=string.Join(\"|\",relationships.Select(x=>x.SourceEntityId+\">\"+x.TargetEntityId+\":\"+x.T",
            Group.Exception,
            "変更の検知（綴りの違いも変更として数える署名）＝リレーションの並びを序数の全順序で決める"
        ),
        new(
            "SqlTypeText.cs",
            "varoffenders=columns.Where(pair=>!IsSafe(pair.Column.DataType)).Select(pair=>$\"{SqlComment",
            Group.Exception,
            "照合ではない（エラーメッセージの組み立て）"
        ),
        new(
            "QueryDefinitionRules.cs",
            "varsiblingMethodNames=others.Where(other=>other.EntityId==query.EntityId).Select(other=>To",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝生成するメソッド名の衝突"
        ),
        new(
            "QueryDefinitionRules.cs",
            "varotherResultTypeNames=others.Where(other=>other.Returns==QueryReturnShape.Projection&&To",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝射影の DTO の型名の衝突"
        ),
        new(
            "QueryDefinitionRules.cs",
            "returnstring.Equals(fieldName,resultTypeName,StringComparison.Ordinal)?QueryRuleViolation.",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝射影のフィールド名（プロパティ）と結果の型名（クラス）の衝突"
        ),
        new(
            "QueryDefinitionRules.cs",
            "IsReservedParameterName(parameterName)||string.Equals(parameterName,selectorFieldName,Stri",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝パラメータ名と射影の選択式の静的フィールド名の衝突"
        ),
        new(
            "DocumentFileWatcher.cs",
            "string.Equals(e.Name,targetName,StringComparison.OrdinalIgnoreCase)",
            Group.Exception,
            "モデルの名前ではない（ファイル名）"
        ),
        new(
            "MermaidExporter.cs",
            "returnwritten.Select((relationship,index)=>(relationship,index)).Any(item=>!string.IsNullO",
            Group.Exception,
            "往復の検査（書いた名前が綴りまでそのまま取り込まれるかを見る）"
        ),
        new(
            "TableDefinitionContentBuilder.cs",
            "publicstaticList<Entity>OrderEntities(IEnumerable<Entity>entities)=>entities.OrderBy(entit",
            Group.Exception,
            "照合ではない（定義書の表示の並び）"
        ),
        new(
            "TableDefinitionDocumentExporter.cs",
            "varsanitized=newstring(tableName.Where(ch=>!InvalidWorksheetNameChars.Contains(ch)).ToArra",
            Group.Exception,
            "モデルの名前ではない（シート名の組み立て）"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "vartargetDbms=workbook.CustomProperties.FirstOrDefault(property=>string.Equals(property.Na",
            Group.Exception,
            "モデルの名前ではない（ブックのカスタムプロパティ名）"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "varraw=workbook.CustomProperties.FirstOrDefault(property=>string.Equals(property.Name,Tabl",
            Group.Exception,
            "モデルの名前ではない（ブックのカスタムプロパティ名）"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "varcolumn=newColumn{Name=columnName,Description=GetCellText(worksheet,row,3),DataType=data",
            Group.Exception,
            "照合ではない（キー欄の PK / FK の記号）"
        ),
        new(
            "MainViewModel.Search.cs",
            "(entity.TableName??string.Empty).Contains(query,StringComparison.OrdinalIgnoreCase)",
            Group.Exception,
            "部分一致の検索（同一性の判定ではない）"
        ),
        new(
            "MainViewModel.Search.cs",
            "(column.Name??string.Empty).Contains(query,StringComparison.OrdinalIgnoreCase)",
            Group.Exception,
            "部分一致の検索（同一性の判定ではない）"
        ),
    ];
}
