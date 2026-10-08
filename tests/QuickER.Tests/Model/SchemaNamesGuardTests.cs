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
/// 一覧は 3 種類: (1) 未移行＝区切り 22 で <c>SchemaNames</c> へ移して一覧から消す。(2) 未移行（系統 B）＝同期の計画と差分
/// （系統 B の作業範囲）。(3) 例外＝理由つきで素の比較を残す（DB 取込のカタログの照合・生 SQL のパラメータの束縛・
/// 生成した C# の識別子、およびモデルの名前ではないもの）。
/// </para>
/// </remarks>
public sealed class SchemaNamesGuardTests
{
    /// <summary>一覧の項目の種類</summary>
    public enum Group
    {
        /// <summary>未移行（区切り 22 で SchemaNames へ移す）</summary>
        Unmigrated,

        /// <summary>未移行（系統 B の作業範囲＝同期の計画と差分）</summary>
        UnmigratedSystemB,

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

    private static bool IsComparisonOrConversion(SyntaxNode node) =>
        node switch
        {
            MemberAccessExpressionSyntax access => Comparison.IsMatch(
                $"{access.Expression}.{access.Name.Identifier.Text}"
            ),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } =>
                CaseOrSpaceConversions.Contains(access.Name.Identifier.Text),
            _ => false,
        };

    /// <summary>一覧（未移行・系統 B・例外）</summary>
    public static readonly IReadOnlyList<Entry> Entries =
    [
        // ---- 未移行（区切り 22 で SchemaNames へ移して一覧から消す） ----
        new(
            "MockDesignDocExporter.cs",
            "entityisnotnull&&string.Equals(entity.Name,entityName,StringComparison.OrdinalIgnoreCase)",
            Group.Unmigrated,
            "モック生成の CRUD 申告のエンティティ名と図のテーブル名の照合"
        ),
        new(
            "MockFolderDesignSession.cs",
            "names.Add(entity.TableName.Trim());",
            Group.Unmigrated,
            "図のテーブル名の集合（モック生成の CRUD 申告の照合に使う）"
        ),
        new(
            "CSharpGenerationModelBuilder.IncludeGraph.cs",
            "varentitiesByTable=newDictionary<string,Entity>(StringComparer.Ordinal);",
            Group.Unmigrated,
            "テーブル名でエンティティを引く辞書（序数一致）"
        ),
        new(
            "CSharpGenerationModelBuilder.Sync.cs",
            "columnOwners.TryGetValue(pair.SourceColumnId,outvarparent)&&parent.Id==entity.Id&&columnOw",
            Group.Unmigrated,
            "同期の対象のテーブル名の集合（序数一致）"
        ),
        new(
            "QueryDefinitionDialogViewModel.cs",
            "varduplicate=Queries.GroupBy(q=>(q.EntityId,Name:q.Name.Trim().ToLowerInvariant())).FirstO",
            Group.Unmigrated,
            "クエリ名の重複の判定（ToLowerInvariant＝OrdinalIgnoreCase と答えが割れる）"
        ),
        new(
            "QueryDefinitionDialogViewModel.cs",
            "returnstring.Format(Strings.QueryDialog_Status_DuplicateName,duplicate.First().Name.Trim()",
            Group.Unmigrated,
            "クエリ名の保存形（Trim を入口に写している）"
        ),
        new(
            "QueryItemViewModel.cs",
            "returnnewQueryDefinition{Id=Id,EntityId=EntityId,Name=Name.Trim(),Description=Description,",
            Group.Unmigrated,
            "クエリ名の保存形（Trim を入口に写している＝モデルの setter へ）"
        ),
        new(
            "QueryItemViewModel.cs",
            "vardeclared=Parameters.Select(p=>p.Name.Trim()).Where(name=>name.Length>0).ToList();",
            Group.Unmigrated,
            "パラメータ名の保存形（Trim を入口に写している）"
        ),
        new(
            "QueryItemViewModel.cs",
            "varname=string.IsNullOrWhiteSpace(parameter.Name)?\"arg\":parameter.Name.Trim();",
            Group.Unmigrated,
            "パラメータ名の保存形（Trim を入口に写している）"
        ),
        new(
            "CSharpReverseParser.cs",
            "varcolumnsByTable=newDictionary<string,Dictionary<string,Column>>(StringComparer.Ordinal);",
            Group.Unmigrated,
            "C# リバース: テーブル名で列を引く辞書（序数一致）"
        ),
        new(
            "CSharpReverseParser.cs",
            "varentityByTable=newDictionary<string,Entity>(StringComparer.Ordinal);",
            Group.Unmigrated,
            "C# リバース: テーブル名でエンティティを引く辞書（序数一致）"
        ),
        new(
            "CSharpReverseParser.cs",
            "varcolumnIndex=newDictionary<string,Column>(StringComparer.Ordinal);",
            Group.Unmigrated,
            "C# リバース: 列名で列を引く辞書（序数一致）"
        ),
        new(
            "DocumentErDiagramToolHost.cs",
            "varother=document.Schema.Entities.FirstOrDefault(candidate=>!ReferenceEquals(candidate,ent",
            Group.Unmigrated,
            "44 / M1: 改名の重複の判定（空白を除かない）"
        ),
        new(
            "DocumentErDiagramToolHost.cs",
            "returnsourceisnotnull&&targetisnotnull&&string.Equals(source.TableName,sourceTable,StringC",
            Group.Unmigrated,
            "リレーションの両端のテーブル名の照合"
        ),
        new(
            "DocumentErDiagramToolHost.cs",
            "varbyName=matches.Where(r=>string.Equals(r.ConstraintName,constraintName,StringComparison.",
            Group.Unmigrated,
            "制約名の照合"
        ),
        new(
            "QueryToolCore.cs",
            "varexisting=diagram.Queries.FirstOrDefault(query=>query.EntityId==entity.Id&&string.Equals",
            Group.Unmigrated,
            "N7: クエリ名の上書きの照合（空白を除かない）"
        ),
        new(
            "QueryToolCore.cs",
            "varquery=diagram.Queries.FirstOrDefault(candidate=>candidate.EntityId==entity.Id&&string.E",
            Group.Unmigrated,
            "N7: クエリ名の削除の照合（空白を除かない）"
        ),
        new(
            "SchemaLookup.cs",
            "publicstaticEntity?FindEntity(ErDiagramschema,stringtableName)=>schema.Entities.FirstOrDef",
            Group.Unmigrated,
            "44 / M1: テーブル名の引き当て（空白を除かない）"
        ),
        new(
            "SchemaLookup.cs",
            "publicstaticColumn?FindColumn(Entityentity,stringcolumnName)=>entity.Columns.FirstOrDefaul",
            Group.Unmigrated,
            "列名の引き当て（空白を除かない）"
        ),
        new(
            "Entity.cs",
            "keys.DistinctBy(column=>column.Name,StringComparer.Ordinal).Count()!=keys.Count",
            Group.Unmigrated,
            "主キーの順序の引き継ぎ（列名の序数一致）"
        ),
        new(
            "Entity.cs",
            "varkeyByName=keys.ToDictionary(column=>column.Name,StringComparer.Ordinal);",
            Group.Unmigrated,
            "主キーの順序の引き継ぎ（列名の序数一致）"
        ),
        new(
            "ForeignKeyColumnResolver.cs",
            "varranks=new(Func<CandidateColumn,bool>Matches,boolAllowPrimaryKey)[]{(column=>expectedNam",
            Group.Unmigrated,
            "外部キーの列の推論（列名の照合）"
        ),
        new(
            "ForeignKeyColumnResolver.cs",
            "privatestaticboolIsBareId(stringcolumnName)=>string.Equals(columnName,\"id\",StringCompariso",
            Group.Unmigrated,
            "外部キーの列の推論（列名の照合）"
        ),
        new(
            "ForeignKeyColumnResolver.cs",
            "privatestaticboolIsSameName(CandidateColumncolumn,string?sourceKeyColumnName)=>sourceKeyCo",
            Group.Unmigrated,
            "外部キーの列の推論（列名の照合）"
        ),
        new(
            "QueryConditionParser.cs",
            "varresolved=entity.Columns.FirstOrDefault(c=>string.Equals(c.Name,column.Text,StringCompar",
            Group.Unmigrated,
            "簡易 DSL の列名の解決（空白を除かない）"
        ),
        new(
            "QueryConditionParser.cs",
            "varresolved=parameters.FirstOrDefault(p=>string.Equals(p.Name,parameter.Text,StringCompari",
            Group.Unmigrated,
            "簡易 DSL のパラメータ名の解決（空白を除かない）"
        ),
        new(
            "QueryConditionParser.cs",
            "vardefinition=parameters.FirstOrDefault(p=>string.Equals(p.Name,operand.Text,StringCompari",
            Group.Unmigrated,
            "簡易 DSL のパラメータ名の解決（空白を除かない）"
        ),
        new(
            "DbmlImporter.cs",
            "varentities=newDictionary<string,Entity>(StringComparer.OrdinalIgnoreCase);",
            Group.Unmigrated,
            "DBML 取込: テーブル名の辞書（空白を除かない）"
        ),
        new(
            "DbmlImporter.cs",
            "varcolumn=entity.Columns.FirstOrDefault(c=>string.Equals(c.Name,columnName,StringCompariso",
            Group.Unmigrated,
            "DBML 取込: 列名の解決"
        ),
        new(
            "DbmlImporter.cs",
            "varcolumn=entity.Columns.FirstOrDefault(c=>string.Equals(c.Name,columnName,StringCompariso",
            Group.Unmigrated,
            "DBML 取込: 列名の解決"
        ),
        new(
            "DbmlImporter.cs",
            "privatestaticColumn?FindColumn(Entityentity,stringcolumnName)=>entity.Columns.FirstOrDefau",
            Group.Unmigrated,
            "DBML 取込: 列名の解決"
        ),
        new(
            "ErDiagramDynamicTools.cs",
            "varother=vm.Entities.FirstOrDefault(candidate=>!ReferenceEquals(candidate,entity)&&string.",
            Group.Unmigrated,
            "44 / M1: 改名の重複の判定（空白を除かない）"
        ),
        new(
            "ErDiagramDynamicTools.cs",
            "privatestaticEntityViewModel?FindEntity(MainViewModelvm,stringtableName)=>vm.Entities.Firs",
            Group.Unmigrated,
            "44 / M1: テーブル名の引き当て（空白を除かない）"
        ),
        new(
            "ErDiagramDynamicTools.cs",
            "privatestaticColumnViewModel?FindColumn(EntityViewModelentity,stringcolumnName)=>entity.Co",
            Group.Unmigrated,
            "列名の引き当て（空白を除かない）"
        ),
        new(
            "ErDiagramDynamicTools.cs",
            "varmatches=vm.Relationships.Where(r=>string.Equals(r.Source.TableName,sourceTable,StringCo",
            Group.Unmigrated,
            "リレーションの両端のテーブル名の照合"
        ),
        new(
            "ErDiagramDynamicTools.cs",
            "varbyName=matches.Where(r=>string.Equals(r.ConstraintName,constraintName,StringComparison.",
            Group.Unmigrated,
            "制約名の照合"
        ),
        new(
            "MermaidImporter.cs",
            "varentities=newDictionary<string,Entity>(StringComparer.OrdinalIgnoreCase);",
            Group.Unmigrated,
            "Mermaid 取込: テーブル名の辞書（空白を除かない）"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "varentities=newDictionary<string,Entity>(StringComparer.OrdinalIgnoreCase);",
            Group.Unmigrated,
            "Excel 定義書の取込: テーブル名の辞書（空白を除かない）"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "varchildColumn=child.Columns.FirstOrDefault(column=>string.Equals(column.Name,childColumnN",
            Group.Unmigrated,
            "Excel 定義書の取込: 列名の解決"
        ),
        new(
            "TableDefinitionDocumentImporter.cs",
            "varparentColumn=parent.Columns.FirstOrDefault(column=>string.Equals(column.Name,parentColu",
            Group.Unmigrated,
            "Excel 定義書の取込: 列名の解決"
        ),
        new(
            "MainViewModel.cs",
            "Entities.Any(entity=>string.Equals(entity.TableName,candidate,StringComparison.OrdinalIgno",
            Group.Unmigrated,
            "複製するテーブルの重複しない名前の判定"
        ),
        // ---- 未移行（系統 B の作業範囲＝同期の計画と差分） ----
        new(
            "SchemaDiffService.cs",
            "varrenamedColumns=liveColumnOf.Where(pair=>!string.Equals(pair.Key.Name,pair.Value.Name,St",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SchemaDiffService.cs",
            "returne.TableName.Trim();",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SchemaDiffService.cs",
            "returnstring.Join(\"|\",NormalizeTable(fk.Parent).ToLowerInvariant(),NormalizeTable(fk.Child",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vardroppedTables=selected.Where(i=>i.Kind==SchemaDiffKind.DropTable).Select(i=>i.TableName",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartouchesPrimaryKey=selected.Any(i=>i.Kind==SchemaDiffKind.AlterColumn&&i.ColumnNameisnot",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartable=item.TableName.Trim();",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vardroppedTables=selected.Where(i=>i.Kind==SchemaDiffKind.DropTable).Select(i=>i.TableName",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "byTable.TryGetValue(item.TableName.Trim(),outvarsignatures)",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "Signatures(byTable,item.TableName.Trim()).Add(UniqueConstraintNaming.ColumnSetSignature(it",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "varexplicitlyDropped=selected.Where(i=>i.Kind==SchemaDiffKind.DropUniqueConstraint).Select",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "!TableComparer.Equals(drop.TableName.Trim(),parentTable)",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "varaddedTables=selected.Where(i=>i.Kind==SchemaDiffKind.AddTable).Select(i=>i.TableName.Tr",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "varexistingRebuildTables=selected.Where(i=>IsRebuildTriggerKind(i.Kind)&&!addedTables.Cont",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "!context.TableCreateSql.TryGetValue(rebuild.TableName.Trim(),outvarcreateSql)",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartable=addTable.TableName.Trim();",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "varfkItems=selected.Where(i=>i.Kind==SchemaDiffKind.AddForeignKey&&TableComparer.Equals(i.",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartableItems=selected.Where(i=>TableComparer.Equals(i.TableName.Trim(),table)&&(IsRebuild",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "varauxiliaryObjects=context.AuxiliaryObjects.Where(a=>TableComparer.Equals(a.TableName.Tri",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartable=item.TableName.Trim();",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        new(
            "SyncPlanner.cs",
            "vartableSelected=selected.Where(i=>TableComparer.Equals(i.TableName.Trim(),table)).ToList(",
            Group.UnmigratedSystemB,
            "系統 B の範囲（同期の計画と差分＝図と DB の照合）"
        ),
        // ---- 例外（理由つきで素の比較を残す） ----
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
            "CSharpGenerationModelBuilder.Queries.cs",
            "string.Equals(field.Name,resultTypeName,StringComparison.Ordinal)",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝射影のフィールド名（プロパティ）と結果の型名（クラス）の衝突"
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
            "QueryToolCore.cs",
            "varcanonical=SupportedDbms.FirstOrDefault(dbms=>string.Equals(dbms,property.Name,StringCom",
            Group.Exception,
            "モデルの名前ではない（方言名＝sql のキー）"
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
            "SchemaTableEntry.cs",
            "publicDictionary<string,Column>ColumnsByName{get;}=new(StringComparer.OrdinalIgnoreCase);",
            Group.Exception,
            "例外 1: DB 取込のカタログの照合（大小だけ違う列名の扱いは区切り 22＝CLAUDE.md の既知の残件）"
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
            "string.Equals(field.Name,resultTypeName,StringComparison.Ordinal)",
            Group.Exception,
            "例外 3: 生成する C# の識別子（言語が区別する綴りで衝突を判定する）＝射影のフィールド名（プロパティ）と結果の型名（クラス）の衝突"
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
