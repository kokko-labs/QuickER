using System.IO;
using System.Text.Json;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>名前の総当たりで、1 つの名前を図のどこに置くか</summary>
public enum NameRole
{
    /// <summary>テーブル名</summary>
    Table,

    /// <summary>列名（外部キーの列）</summary>
    Column,

    /// <summary>一意制約の名前</summary>
    UniqueConstraint,

    /// <summary>外部キーの制約名</summary>
    ForeignKey,
}

/// <summary>
/// 名前の総当たり（<see cref="NameFuzz"/>）の名前で図を作る部品。図は MCP のツール（<see cref="DocumentErDiagramToolHost"/>）で
/// 作る＝ツールが受け付ける名前だけが図に入る（実際に作れる図だけを後段へ流す）。
/// </summary>
/// <remarks>
/// QuickER.Tests（C# 生成の総当たり）と QuickER.Tests.Integration（DDL の往復）が同じ部品を使う（後者は Compile リンク）。
/// </remarks>
public static class NameFuzzDiagrams
{
    /// <summary>
    /// 既定のシードで必ず流す代表の名前（性質ごとに 1 つ）。シードを増やすと <see cref="NamesForRun"/> がランダムな名前を足す
    /// </summary>
    public static IReadOnlyList<string> Representative { get; } =
    [
        NameFuzz.Variants("Orders")[1],
        NameFuzz.Variants("Task")[4],
        NameFuzz.Variants("class")[2],
        "q\"x",
        "q]x",
        "q`x",
        "q'x",
        "a.b",
        NameFuzz.BaseNames[13],
        NameFuzz.BaseNames[14],
        NameFuzz.BaseNames[15],
        NameFuzz.BaseNames[17],
        "select",
        "Group",
        "1st",
        // 識別子の長さの上限（PostgreSQL 63 バイト・MySQL 64 文字・SQL Server 128 文字・Oracle 128 バイト）のちょうどと 1 つ超え
        new string('a', 63),
        new string('a', 64),
        new string('a', 65),
        new string('a', 128),
        new string('a', 129),
        // マルチバイト（UTF-8 で 3 バイト）: 文字数は上限内でもバイト数が超える（PostgreSQL は 21 文字＝63 バイトまで・
        // Oracle は 42 文字＝126 バイトまで）
        new string(NameFuzz.BaseNames[15][0], 21),
        new string(NameFuzz.BaseNames[15][0], 22),
        new string(NameFuzz.BaseNames[15][0], 43),
    ];

    /// <summary>流す名前（代表の名前＋普段の既定の数を超えたシードごとにランダムな名前 6 つ）</summary>
    /// <param name="plan">流すシード（普段の既定の数までのシードは代表の名前で足りるので、名前を足さない）</param>
    public static IReadOnlyList<string> NamesForRun(SeedPlan plan)
    {
        var names = new List<string>(Representative);
        var firstExtra = plan.Settings.FirstSeed + plan.Settings.RegularCount;

        foreach (var seed in plan.Seeds.Where(seed => seed >= firstExtra))
        {
            var random = new Random(seed);

            for (var i = 0; i < 6; i++)
            {
                names.Add(NameFuzz.Name(random));
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 名前の性質（既知の失敗の一覧のキーに使う）。複数あれば優先順（長さ〔UTF-8 で 60 バイト超〕・空白・ドット・引用符・サロゲート・非 ASCII・数字始まり・予約語）の
    /// 最初の 1 つ＝シードを増やしてランダムな名前の組み合わせが変わっても、同じ原因の失敗が同じキーになる
    /// </summary>
    public static string Classify(string name)
    {
        var traits = new List<string>();

        // 識別子の長さの上限で一番小さいのは PostgreSQL の 63 バイトで、既定の制約名は名前に PK_ などの 3 文字を足す
        if (System.Text.Encoding.UTF8.GetByteCount(name) > 60)
        {
            traits.Add("Long");
        }

        if (name.Trim() != name)
        {
            traits.Add("Whitespace");
        }

        if (name.Contains('.'))
        {
            traits.Add("Dot");
        }

        if (name.IndexOfAny(['"', '\'', ']', '`']) >= 0)
        {
            traits.Add("Quote");
        }

        if (name.Any(char.IsSurrogate))
        {
            traits.Add("Surrogate");
        }
        else if (name.Any(c => c > '~'))
        {
            traits.Add("NonAscii");
        }

        if (name.Length > 0 && char.IsDigit(name.Trim()[0]))
        {
            traits.Add("LeadingDigit");
        }

        if (Reserved.Contains(name.Trim()))
        {
            traits.Add("Reserved");
        }

        return traits.Count == 0 ? "Plain" : traits[0];
    }

    private static readonly HashSet<string> Reserved = new(
        ["order", "group", "select", "class", "query", "task"],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>方言ごとの整数型と文字列型の表記</summary>
    public static (string Int, string Text) TypesFor(string dialect) =>
        dialect switch
        {
            "sqlserver" => ("int", "nvarchar(50)"),
            "postgresql" => ("integer", "varchar(50)"),
            "mysql" => ("int", "varchar(50)"),
            "oracle" => ("NUMBER(10)", "NVARCHAR2(50)"),
            "sqlite" => ("INTEGER", "TEXT"),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>
    /// 親 P(Id) と子（Id・P を参照する列・一意制約を持つ列 U）の図を作り、<paramref name="name"/> を <paramref name="role"/> に置く
    /// </summary>
    /// <returns>ツールが名前を受け付けなければ null</returns>
    public static ErDiagram? BuildForRole(string dialect, string name, NameRole role)
    {
        var (intType, textType) = TypesFor(dialect);
        // 足したテーブルを後続のツールで引くときは保存形（前後の空白を除いた名前）で引く
        // （空白つきの名前で引けるかは重複の判定の総当たり＝性質 A が見る）
        var child = role == NameRole.Table ? name : "C";
        var childRef = NameIdentityRule.StoredForm(child);
        var column = role == NameRole.Column ? name : "Ref";

        return WithTools(
            dialect,
            exec =>
                exec("add_entity", new { table_name = "P" })
                && exec("add_column", Column("P", "Id", intType, primaryKey: true))
                && exec("add_entity", new { table_name = child })
                && exec("add_column", Column(childRef, "Id", intType, primaryKey: true))
                && exec("add_column", Column(childRef, column, intType))
                && exec("add_column", Column(childRef, "U", textType))
                && exec(
                    "set_unique_constraint",
                    role == NameRole.UniqueConstraint
                        ? new
                        {
                            table_name = childRef,
                            columns = new[] { "U" },
                            name,
                        }
                        : new
                        {
                            table_name = childRef,
                            columns = new[] { "U" },
                            name = "UQ_C_U",
                        }
                )
                && exec(
                    "add_relationship",
                    new
                    {
                        source_table = "P",
                        target_table = childRef,
                        source_columns = new[] { "Id" },
                        target_columns = new[] { column },
                        relationship_type = "OneToMany",
                        constraint_name = role == NameRole.ForeignKey ? name : "FK_C_P",
                    }
                )
        );
    }

    /// <summary>
    /// 長さの上限を確かめる組: 上限の後ろでだけ違う 2 つの名前と、何が上限を超えるか。DB が上限で切り詰めると 2 つが同じ名前になる
    /// </summary>
    public static IReadOnlyList<(string First, string Second, string Shape)> LongPairs { get; } =
    [
        // テーブル名は 63 文字（PostgreSQL の上限内）で、主キーの既定名 PK_{テーブル} が 66 文字＝切り詰めで同じ名前になる
        (new string('b', 62) + "1", new string('b', 62) + "2", "DerivedConstraintName"),
        // テーブル名そのものが 64 文字（PostgreSQL の上限を 1 つ超え）＝切り詰めで同じ名前になる
        (new string('b', 63) + "1", new string('b', 63) + "2", "TableName"),
        // マルチバイトで 22 文字＝66 バイト（文字数は上限内）＝切り詰めで同じ名前になる
        (
            new string(NameFuzz.BaseNames[15][0], 21) + "1",
            new string(NameFuzz.BaseNames[15][0], 21) + "2",
            "MultibyteTableName"
        ),
    ];

    /// <summary>名前ごとに主キー Id だけを持つテーブルの図を作る（ツールが断れば null）</summary>
    public static ErDiagram? BuildTables(string dialect, params string[] names)
    {
        var (intType, _) = TypesFor(dialect);

        return WithTools(
            dialect,
            exec =>
                names.All(name =>
                    exec("add_entity", new { table_name = name })
                    && exec("add_column", Column(name, "Id", intType, primaryKey: true))
                )
        );
    }

    /// <summary>
    /// 名前ごとに、その名前のテーブル（Id・その名前の列・U）と、その名前の一意制約を持つ図を作る（ツールが受け付けた名前だけを入れる）
    /// </summary>
    /// <returns>図と、図に入った名前</returns>
    public static (ErDiagram Diagram, IReadOnlyList<string> Names) BuildAll(
        string dialect,
        IEnumerable<string> names
    )
    {
        var (intType, textType) = TypesFor(dialect);
        var accepted = new List<string>();
        var diagram = WithTools(
            dialect,
            exec =>
            {
                // 規則上同じ名前は 1 つの図に 2 つ置かない（重複の判定は性質 A が見る）
                var distinct = names
                    .DistinctBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var name in distinct)
                {
                    // 1 つの名前の追加が途中で断られても、その名前の分だけを飛ばす（図は前の名前までのまま）。
                    // 足したテーブルを後続のツールで引くときは保存形で引く（BuildForRole と同じ理由）
                    var table = NameIdentityRule.StoredForm(name);

                    if (
                        exec("add_entity", new { table_name = name })
                        && exec("add_column", Column(table, "Id", intType, primaryKey: true))
                        && exec("add_column", Column(table, name, intType))
                        && exec("add_column", Column(table, "U", textType))
                        && exec(
                            "set_unique_constraint",
                            new
                            {
                                table_name = table,
                                columns = new[] { "U" },
                                name,
                            }
                        )
                    )
                    {
                        accepted.Add(name);
                    }
                    else
                    {
                        exec("remove_entity", new { table_name = table });
                    }
                }

                return true;
            }
        )!;

        return (diagram, accepted);
    }

    private static object Column(
        string table,
        string name,
        string dataType,
        bool primaryKey = false
    ) =>
        new
        {
            table_name = table,
            column_name = name,
            data_type = dataType,
            is_primary_key = primaryKey,
            is_nullable = !primaryKey,
        };

    /// <summary>一時ファイルの図に対してツールを流し、全部成功したら図を返す</summary>
    private static ErDiagram? WithTools(string dialect, Func<Func<string, object, bool>, bool> body)
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "quicker-name-fuzz-diagram-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "diagram.json");

        try
        {
            bool Exec(string tool, object args) =>
                DocumentErDiagramToolHost
                    .Execute(tool, file, JsonSerializer.SerializeToElement(args))
                    .Success;

            Exec(DocumentErDiagramToolHost.CreateDiagramToolName, new { target_dbms = dialect });

            return body(Exec) ? JsonStorageService.Load(file).Schema : null;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // 後始末の失敗はテストの結果に影響させない
            }
        }
    }
}
