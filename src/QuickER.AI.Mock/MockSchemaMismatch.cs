using System.Text;
using QuickER.AI.Mock.Resources;
using QuickER.Model;

namespace QuickER.AI.Mock;

/// <summary>
/// 現在の ER 図と、モックの設計（<c>mock.json</c>）の元になったスキーマ・画面が宣言したテーブルとの食い違い。
/// </summary>
/// <param name="SourceSchemaRecorded">設計の元のスキーマ（<see cref="MockManifest.SourceSchema"/>）が記録されていたか</param>
/// <param name="TablesOnlyInDesign">設計の元のスキーマにあって、今の図に無いテーブル</param>
/// <param name="TablesOnlyInDiagram">今の図にあって、設計の元のスキーマに無いテーブル</param>
/// <param name="ChangedTables">両方にあって、内容（列・型・NULL・キー・説明）が違うテーブル</param>
/// <param name="RelationshipsChanged">リレーションが違うか</param>
/// <param name="TargetChanged">対象 DBMS など、テーブルより前の部分が違うか</param>
/// <param name="ScreenTablesNotInDiagram">画面が宣言しているのに、今の図に無いテーブル（画面のファイル名とテーブル名）</param>
internal sealed record MockSchemaMismatch(
    bool SourceSchemaRecorded,
    IReadOnlyList<string> TablesOnlyInDesign,
    IReadOnlyList<string> TablesOnlyInDiagram,
    IReadOnlyList<string> ChangedTables,
    bool RelationshipsChanged,
    bool TargetChanged,
    IReadOnlyList<(string Screen, string Table)> ScreenTablesNotInDiagram
)
{
    /// <summary>
    /// 生成の前に利用者へ確認するか（テーブルの有無と、画面が宣言しているのに図に無いテーブル）。
    /// </summary>
    /// <remarks>
    /// これらは画面が作れない原因になる（設計の画面が図に無いエンティティを使う）。内容・リレーション・対象 DBMS だけの違いは
    /// 画面を作る妨げになりにくいので確認を出さず、ログに残すだけにする。
    /// </remarks>
    public bool RequiresConfirmation =>
        TablesOnlyInDesign.Count > 0
        || TablesOnlyInDiagram.Count > 0
        || ScreenTablesNotInDiagram.Count > 0;

    /// <summary>確認を出さない種類の違い（内容・リレーション・対象 DBMS）があるか</summary>
    public bool HasContentDifferences =>
        ChangedTables.Count > 0 || RelationshipsChanged || TargetChanged;

    /// <summary>今の図と設計を比べる</summary>
    /// <param name="diagram">今の ER 図</param>
    /// <param name="manifest">モックの設計（<c>mock.json</c>）</param>
    /// <remarks>
    /// スキーマは <see cref="MockSchemaSerializer.Serialize"/> の出力どうしで比べる。違いの有無は再開と同じ
    /// <see cref="MockResumePrompt.IsSchemaChanged"/> で決め、違うときだけ <see cref="MockSchemaSerializer.Split"/> で
    /// テーブルごとに分けて内訳を作る。テーブル名は <see cref="SchemaNames"/> の規則で照らす。
    /// 元のスキーマが記録されていない古い設計は、スキーマを比べず、画面の宣言だけを照らす。
    /// </remarks>
    public static MockSchemaMismatch Compare(ErDiagram diagram, MockManifest manifest)
    {
        var tableNames = SchemaTableNames(diagram);
        var screenTables = (manifest.Screens ?? new List<MockScreen>())
            .SelectMany(screen =>
                (screen.Entities ?? new List<MockScreenEntity>())
                    .Where(entity =>
                        !string.IsNullOrWhiteSpace(entity.Name) && !tableNames.Contains(entity.Name)
                    )
                    .Select(entity => (screen.File, entity.Name))
            )
            .ToList();

        var recorded = !string.IsNullOrWhiteSpace(manifest.SourceSchema);
        var current = MockSchemaSerializer.Serialize(diagram);

        if (!recorded || !MockResumePrompt.IsSchemaChanged(current, manifest))
        {
            return new MockSchemaMismatch(recorded, [], [], [], false, false, screenTables);
        }

        var design = MockSchemaSerializer.Split(manifest.SourceSchema);
        var now = MockSchemaSerializer.Split(current);
        var designTables = ToLookup(design.Tables);
        var nowTables = ToLookup(now.Tables);

        return new MockSchemaMismatch(
            recorded,
            design.Tables.Select(t => t.Key).Where(name => !nowTables.ContainsKey(name)).ToList(),
            now.Tables.Select(t => t.Key).Where(name => !designTables.ContainsKey(name)).ToList(),
            now.Tables.Where(t =>
                    designTables.TryGetValue(t.Key, out var body)
                    && !string.Equals(body, t.Value, StringComparison.Ordinal)
                )
                .Select(t => t.Key)
                .ToList(),
            !string.Equals(design.Relationships, now.Relationships, StringComparison.Ordinal),
            !string.Equals(design.Header, now.Header, StringComparison.Ordinal),
            screenTables
        );
    }

    /// <summary>
    /// 図のテーブル名の集合（<see cref="SchemaNames"/> の規則で照らす）。画面の宣言をテーブルと照らすときの唯一の作り方。
    /// </summary>
    internal static HashSet<string> SchemaTableNames(ErDiagram diagram)
    {
        var names = new HashSet<string>(SchemaNames.Comparer);

        foreach (var entity in diagram.Entities)
        {
            if (!string.IsNullOrWhiteSpace(entity.TableName))
            {
                names.Add(entity.TableName);
            }
        }

        return names;
    }

    /// <summary>確認の詳細とログに出す内訳（1 項目 1 行・UI 言語）</summary>
    public string Describe()
    {
        var builder = new StringBuilder();

        void AppendNames(string format, IReadOnlyList<string> names)
        {
            if (names.Count > 0)
            {
                builder.AppendLine(string.Format(format, string.Join(", ", names)));
            }
        }

        AppendNames(Strings.Mock_SchemaMismatch_TablesOnlyInDesignFormat, TablesOnlyInDesign);
        AppendNames(Strings.Mock_SchemaMismatch_TablesOnlyInDiagramFormat, TablesOnlyInDiagram);

        foreach (var (screen, table) in ScreenTablesNotInDiagram)
        {
            builder.AppendLine(
                string.Format(Strings.Mock_SchemaMismatch_ScreenTableFormat, screen, table)
            );
        }

        AppendNames(Strings.Mock_SchemaMismatch_ChangedTablesFormat, ChangedTables);

        if (RelationshipsChanged)
        {
            builder.AppendLine(Strings.Mock_SchemaMismatch_RelationshipsChanged);
        }

        if (TargetChanged)
        {
            builder.AppendLine(Strings.Mock_SchemaMismatch_TargetChanged);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>テーブル名から節の本文を引く辞書（<see cref="SchemaNames"/> の規則・同じ名前は先の節）</summary>
    private static Dictionary<string, string> ToLookup(
        IReadOnlyList<KeyValuePair<string, string>> tables
    )
    {
        var lookup = new Dictionary<string, string>(SchemaNames.Comparer);

        foreach (var (name, body) in tables)
        {
            lookup.TryAdd(name, body);
        }

        return lookup;
    }
}
