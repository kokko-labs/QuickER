using System.Text;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// クエリ糖衣の静的クラス（<c>SqlQueryExtensions</c>＝<c>SqlQuery&lt;T&gt;.IncludeGraph()</c> と
/// <c>GetByIdAsync</c>）のテンプレート用ブロックを構築する部分。
/// </summary>
/// <remarks>
/// <para>
/// グラフ保存（<c>SaveAsync</c>）が辿るのと同じカスケード閉包（子方向ナビゲーション）を Include ツリーへ写し、
/// エンティティごとに 1 本だけ静的に組み立てて共有する。名前付きクエリ・Stream アクセサと同じく
/// 「整形済みのメンバーテキスト」をここで組み立て、テンプレートは差し込むだけにする。
/// </para>
/// <para>
/// 同じ静的クラスへ主キー取得の終端糖衣（<c>GetByIdAsync</c>）も同居させる。主目的は
/// <c>Query().IncludeGraph().GetByIdAsync(id)</c>＝「キー指定でグラフごと 1 件」で、キーの型は契約
/// （<c>I{Entity}Repository.GetByIdAsync</c>）と同一のものを使う（＝Include なしなら契約版と同じ結果になる同名メソッド）。
/// </para>
/// <para>
/// fluent の <c>Include</c>/<c>ThenInclude</c> 連鎖を出さないのは、兄弟分岐のたびに同一ナビの
/// <c>IncludeNode</c> が重複して積まれ、SQLite／インメモリの置換バインドと SQL Server の同名 JSON 後勝ちで
/// 先行の孫が消えるため。<c>SqlQuery&lt;T&gt;.AddIncludeNodes</c> へツリーを直接渡すのがこの取りこぼしの回避策で、
/// 連鎖へ書き換えると型検査にもビルドにも出ないまま静かに回帰する。
/// </para>
/// <para>
/// 閉包はルートから現在ノードまでのパス上に既に現れたエンティティへ向かう辺を展開しない（edge-skip）。
/// 自己参照・相互参照は Include ツリーとして表せず（無限に深くなる）、実行器も有限のツリーしか受け取れないため、
/// 辿らずに Info 診断で名指しする。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>生成する拡張メソッド静的クラス名（IncludeGraph と GetByIdAsync が同居するため中立名）</summary>
    private const string SqlQueryExtensionsClassName = "SqlQueryExtensions";

    /// <summary>Include ツリーの 1 ノード（宣言順に採番した平坦表現）</summary>
    /// <param name="OwnerClassName">このナビゲーションを宣言する側の Entity クラス名</param>
    /// <param name="PropertyName">ナビゲーションプロパティ名</param>
    /// <param name="ParentIndex">親ノードの採番（ルート直下は -1）</param>
    private sealed record IncludeGraphNode(
        string OwnerClassName,
        string PropertyName,
        int ParentIndex
    );

    /// <summary>
    /// クエリ糖衣（<c>SqlQueryExtensions</c>＝IncludeGraph ＋ GetByIdAsync）の全文を組み立てる
    /// （Repository 契約が出ない構成では空文字）。
    /// </summary>
    /// <param name="diagram">対象の ER 図</param>
    /// <param name="navigationsByEntity">解決済みナビゲーション（エンティティ ID 単位）</param>
    /// <param name="repositoryClasses">Repository 契約の生成モデル一覧（＝<c>Query()</c> を持つエンティティ）</param>
    /// <param name="diagnostics">edge-skip を通知する Info 診断の出力先</param>
    private string BuildIncludeGraphExtensions(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, List<NavigationInfo>> navigationsByEntity,
        IReadOnlyList<CSharpRepositoryModel> repositoryClasses,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 契約が 1 つも出ない構成（Entity / EditModel のみ等）では SqlQuery<T> 自体が無いため拡張も出さない
        if (repositoryClasses.Count == 0)
        {
            return string.Empty;
        }

        // Entity クラス名 → Repository 契約（主キー取得糖衣のキー型は契約が持つものをそのまま使う＝
        // 契約の GetByIdAsync と引数型が食い違わない）
        var contractsByClassName = new Dictionary<string, CSharpRepositoryModel>(
            StringComparer.Ordinal
        );

        foreach (var repository in repositoryClasses)
        {
            contractsByClassName.TryAdd(repository.EntityClassName, repository);
        }

        // 子方向ナビの行き先はテーブル名で引く（同名テーブルは先勝ちで 1 つに畳む＝ナビ解決側と同じ扱い）
        var entitiesByTable = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var entity in diagram.Entities)
        {
            entitiesByTable.TryAdd(entity.TableName, entity);
        }

        // 子方向（カスケード）の辺。保存側の EnumerateCascadeChildren と同じ向き＝親参照は辿らない
        var edges = new Dictionary<Guid, List<(NavigationInfo Nav, Entity Child)>>();

        foreach (var entity in diagram.Entities)
        {
            var list = new List<(NavigationInfo, Entity)>();

            if (navigationsByEntity.TryGetValue(entity.Id, out var navigations))
            {
                foreach (var navigation in navigations)
                {
                    if (
                        !navigation.IsParentReference
                        && entitiesByTable.TryGetValue(navigation.TargetTableName, out var child)
                    )
                    {
                        list.Add((navigation, child));
                    }
                }
            }

            edges[entity.Id] = list;
        }

        // edge-skip した辺（"{Entity}.{Navigation}"）。ルートを変えて何度も現れるため重複を畳む
        var skippedEdges = new List<string>();
        var seenSkips = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<string>();

        foreach (var entity in diagram.Entities)
        {
            var className = _nameConverter.ToEntityClassName(entity.TableName);

            // Query() を持たないエンティティ（複合主キー等で契約が出ない）へは拡張を生やさない
            if (!contractsByClassName.TryGetValue(className, out var repository))
            {
                continue;
            }

            var nodes = new List<IncludeGraphNode>();
            CollectIncludeGraphNodes(
                entity,
                className,
                parentIndex: -1,
                path: [entity.Id],
                edges,
                nodes,
                skippedEdges,
                seenSkips
            );

            members.Add(BuildIncludeGraphMember(className, nodes));

            // 主キー取得の終端糖衣。契約は単一主キーのエンティティにしか出ないため、キー列は必ず 1 本ある
            members.Add(
                BuildGetByIdMembers(
                    className,
                    repository.KeyTypeName,
                    _nameConverter.ToPropertyName(
                        entity.Columns.First(column => column.IsPrimaryKey).Name
                    )
                )
            );
        }

        if (members.Count == 0)
        {
            return string.Empty;
        }

        // 展開しなかった辺があれば Info 診断で名指しする（生成物にはスキップの痕跡が残らないため）
        if (skippedEdges.Count > 0)
        {
            diagnostics.Add(
                GenerationDiagnostic.Info(
                    string.Format(
                        Strings.CodeGen_Info_IncludeGraphSkippedNavigations,
                        Environment.NewLine
                            + string.Join(
                                Environment.NewLine,
                                skippedEdges.Select(edge => "  " + edge)
                            )
                    )
                )
            );
        }

        return "/// <summary>Query extensions: including the whole cascade graph of an entity with a single method call (the read-side counterpart of the graph save - combine it with Where or GetByIdAsync to bound what is fetched), and fetching a single entity by its key.</summary>\n"
            + "/// <remarks>\n"
            + "/// The Include tree of each entity is built once and shared by every query, so it must stay unmodified after construction.\n"
            + "/// </remarks>\n"
            + "public static class "
            + SqlQueryExtensionsClassName
            + "\n{\n"
            + string.Join("\n\n", members)
            + "\n}";
    }

    /// <summary>
    /// カスケード閉包を宣言順に深さ優先で辿り、Include ツリーのノードを平坦な採番付きリストへ集める。
    /// </summary>
    /// <remarks>
    /// <paramref name="path"/> はルートから現在ノードまでに現れたエンティティ。既に現れたエンティティへ向かう辺は
    /// 展開せず <paramref name="skippedEdges"/> へ積む（自己参照・相互参照は有限のツリーに写せない）。
    /// 兄弟分岐は「1 つのノードに複数の子」として畳まれるため、同一ナビのノードが 2 本出ることは無い。
    /// </remarks>
    private void CollectIncludeGraphNodes(
        Entity owner,
        string ownerClassName,
        int parentIndex,
        HashSet<Guid> path,
        IReadOnlyDictionary<Guid, List<(NavigationInfo Nav, Entity Child)>> edges,
        List<IncludeGraphNode> nodes,
        List<string> skippedEdges,
        HashSet<string> seenSkips
    )
    {
        foreach (var (navigation, child) in edges[owner.Id])
        {
            if (path.Contains(child.Id))
            {
                var skipped = $"{ownerClassName}.{navigation.PropertyName}";

                if (seenSkips.Add(skipped))
                {
                    skippedEdges.Add(skipped);
                }

                continue;
            }

            var index = nodes.Count;
            nodes.Add(new IncludeGraphNode(ownerClassName, navigation.PropertyName, parentIndex));

            path.Add(child.Id);
            CollectIncludeGraphNodes(
                child,
                _nameConverter.ToEntityClassName(child.TableName),
                index,
                path,
                edges,
                nodes,
                skippedEdges,
                seenSkips
            );
            path.Remove(child.Id);
        }
    }

    /// <summary>1 エンティティ分のメンバー（静的ツリー＋拡張メソッド）を組み立てる</summary>
    /// <remarks>カスケード子が 1 つも無い（または全て edge-skip された）エンティティは、フィールドを持たない no-op になる</remarks>
    private static string BuildIncludeGraphMember(
        string entityClassName,
        IReadOnlyList<IncludeGraphNode> nodes
    )
    {
        var builder = new StringBuilder();

        if (nodes.Count == 0)
        {
            builder
                .Append("    /// <summary>Includes the cascade graph of ")
                .Append(entityClassName)
                .Append(
                    " (it has no child-direction navigation, so the query is returned unchanged).</summary>\n"
                )
                .Append("    public static SqlQuery<")
                .Append(entityClassName)
                .Append("> IncludeGraph(this SqlQuery<")
                .Append(entityClassName)
                .Append("> query) => query;");
            return builder.ToString();
        }

        var fieldName = IncludeGraphFieldName(entityClassName);

        builder
            .Append("    /// <summary>The Include tree of ")
            .Append(entityClassName)
            .Append(" (built once and shared by every query; never modify it).</summary>\n")
            .Append("    private static readonly Lazy<IReadOnlyList<IncludeNode>> ")
            .Append(fieldName)
            .Append(" = new(() =>\n    {\n");

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            builder
                .Append("        var n")
                .Append(index)
                .Append(" = new IncludeNode(typeof(")
                .Append(node.OwnerClassName)
                .Append(").GetProperty(nameof(")
                .Append(node.OwnerClassName)
                .Append('.')
                .Append(node.PropertyName)
                .Append("))!);\n");
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].ParentIndex >= 0)
            {
                builder
                    .Append("        n")
                    .Append(nodes[index].ParentIndex)
                    .Append(".Children.Add(n")
                    .Append(index)
                    .Append(");\n");
            }
        }

        var roots = Enumerable
            .Range(0, nodes.Count)
            .Where(index => nodes[index].ParentIndex < 0)
            .Select(index =>
                "n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );

        builder
            .Append("        return new IncludeNode[] { ")
            .Append(string.Join(", ", roots))
            .Append(" };\n    });\n\n");

        builder
            .Append("    /// <summary>Includes the cascade graph of ")
            .Append(entityClassName)
            .Append(
                " - the same child-direction navigations a graph save walks. A navigation pointing back to a table already on the path from the root is not followed.</summary>\n"
            )
            .Append("    public static SqlQuery<")
            .Append(entityClassName)
            .Append("> IncludeGraph(this SqlQuery<")
            .Append(entityClassName)
            .Append("> query) =>\n        query.AddIncludeNodes(")
            .Append(fieldName)
            .Append(".Value);");

        return builder.ToString();
    }

    /// <summary>1 エンティティ分の主キー取得糖衣（<c>GetByIdAsync</c> の 2 オーバーロード）を組み立てる</summary>
    /// <remarks>
    /// <para>
    /// 中身は <c>Where(x =&gt; x.{主キー} == id).FirstOrDefaultAsync()</c> の 1 行で、実行器・ランタイムには手を入れない。
    /// キーの型は Repository 契約（<c>I{Entity}Repository.GetByIdAsync</c>）と同じものを使うため、Include を挟まなければ
    /// 契約版と同じ結果になる＝同名で正直なメソッドになる。
    /// </para>
    /// <para>
    /// <c>IncludableSqlQuery</c> 版を併せて出すのは、fluent の <c>Include(...)</c> が
    /// <c>IncludableSqlQuery&lt;TEntity, TProperty&gt;</c> を返すため。これが無いと
    /// <c>Query().Include(x =&gt; x.Orders).GetByIdAsync(id)</c> が解決しない（<c>IncludeGraph()</c> は
    /// <c>SqlQuery</c> を返すので 1 つ目のオーバーロードで足りる）。
    /// </para>
    /// </remarks>
    /// <param name="entityClassName">対象の Entity クラス名</param>
    /// <param name="keyTypeName">契約が受け取る主キーの型名（値オブジェクト有効時は VO 型）</param>
    /// <param name="keyPropertyName">主キー列のプロパティ名</param>
    private static string BuildGetByIdMembers(
        string entityClassName,
        string keyTypeName,
        string keyPropertyName
    )
    {
        var body =
            "    ) => query.Where(entity => entity."
            + keyPropertyName
            + " == id).FirstOrDefaultAsync(cancellationToken);";

        return "    /// <summary>Fetches the single entity with the given key - the same key the repository contract's GetByIdAsync takes - and returns null when no row matches.</summary>\n"
            + "    /// <remarks>Combine it with Include or IncludeGraph to fetch that entity together with its graph in one call.</remarks>\n"
            + "    public static Task<"
            + entityClassName
            + "?> GetByIdAsync(\n"
            + "        this SqlQuery<"
            + entityClassName
            + "> query,\n"
            + "        "
            + keyTypeName
            + " id,\n"
            + "        CancellationToken cancellationToken = default\n"
            + body
            + "\n\n"
            + "    /// <summary>Fetches the single entity with the given key, keeping the Include chain written just before it (returns null when no row matches).</summary>\n"
            + "    public static Task<"
            + entityClassName
            + "?> GetByIdAsync<TProperty>(\n"
            + "        this IncludableSqlQuery<"
            + entityClassName
            + ", TProperty> query,\n"
            + "        "
            + keyTypeName
            + " id,\n"
            + "        CancellationToken cancellationToken = default\n"
            + body;
    }

    /// <summary>Entity クラス名から静的ツリーのフィールド名（例 <c>OrderEntity</c> → <c>_orderEntityGraph</c>）を作る</summary>
    private static string IncludeGraphFieldName(string entityClassName) =>
        "_" + char.ToLowerInvariant(entityClassName[0]) + entityClassName[1..] + "Graph";
}
