using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 生成コードが実行時に載る実装先（機能 × 実装先の行列の列）。テストのクラスの名前の実装先の綴りもこの値の名前で書く
/// </summary>
/// <remarks>
/// <para>
/// <c>Ado</c>＝QuickER 版 Repository（ADO.NET を直接使う生成の実装）。<c>EfCore</c>＝EF Core 版 Repository。
/// 方言の付いた値は、その方言の実 DB（SQL Server・PostgreSQL・MySQL・Oracle はコンテナ、SQLite は実ファイル）で動かす。
/// </para>
/// <para>
/// <see cref="Remote"/>＝生成の HTTP クライアント越し（in-process の Kestrel）。リモートの担当は、サーバーの裏の実装先
/// （ほかの値のどれか、またはリポジトリを持たない転送だけ）を必ず名乗る（<see cref="RuntimeCellAttribute"/>）。
/// </para>
/// <para>
/// 方言の付いた値の集合は製品の正本と一対一に対応させる（<see cref="RuntimeTargets.ProductProblems"/>）。
/// 製品が方言を足すと、値を足すまで赤になる。
/// </para>
/// </remarks>
public enum RuntimeTarget
{
    /// <summary>QuickER 版 Repository × SQL Server</summary>
    AdoSqlServer,

    /// <summary>QuickER 版 Repository × SQLite</summary>
    AdoSqlite,

    /// <summary>EF Core 版 Repository × SQL Server</summary>
    EfCoreSqlServer,

    /// <summary>EF Core 版 Repository × SQLite</summary>
    EfCoreSqlite,

    /// <summary>EF Core 版 Repository × PostgreSQL</summary>
    EfCorePostgreSql,

    /// <summary>EF Core 版 Repository × MySQL</summary>
    EfCoreMySql,

    /// <summary>EF Core 版 Repository × Oracle</summary>
    EfCoreOracle,

    /// <summary>インメモリ Repository</summary>
    InMemory,

    /// <summary>HTTP クライアント＋生成サーバー（サーバーの裏の実装先は担当の宣言が名乗る）</summary>
    Remote,
}

/// <summary>実装先の生成の実装の種類</summary>
public enum RuntimeEngineKind
{
    /// <summary>QuickER 版 Repository</summary>
    Ado,

    /// <summary>EF Core 版 Repository</summary>
    EfCore,

    /// <summary>インメモリ Repository</summary>
    InMemory,

    /// <summary>HTTP クライアント＋生成サーバー</summary>
    Remote,
}

/// <summary>実装先 1 つの性質</summary>
/// <param name="Target">実装先</param>
/// <param name="Engine">生成の実装の種類</param>
/// <param name="Dialect">方言の名前（<see cref="DialectNames"/>。方言を持たない実装先は null）</param>
/// <param name="Label">行列の見出しに使う短い名前</param>
public sealed record RuntimeTargetInfo(
    RuntimeTarget Target,
    RuntimeEngineKind Engine,
    string? Dialect,
    string Label
);

/// <summary>実装先の正本の性質と、製品の正本との照合</summary>
public static class RuntimeTargets
{
    /// <summary>実装先ごとの性質（列挙の並び）</summary>
    public static IReadOnlyList<RuntimeTargetInfo> All { get; } =
    [
        new(
            RuntimeTarget.AdoSqlServer,
            RuntimeEngineKind.Ado,
            DialectNames.SqlServer,
            "QuickER 版 SQL Server"
        ),
        new(
            RuntimeTarget.AdoSqlite,
            RuntimeEngineKind.Ado,
            DialectNames.Sqlite,
            "QuickER 版 SQLite"
        ),
        new(
            RuntimeTarget.EfCoreSqlServer,
            RuntimeEngineKind.EfCore,
            DialectNames.SqlServer,
            "EF Core SQL Server"
        ),
        new(
            RuntimeTarget.EfCoreSqlite,
            RuntimeEngineKind.EfCore,
            DialectNames.Sqlite,
            "EF Core SQLite"
        ),
        new(
            RuntimeTarget.EfCorePostgreSql,
            RuntimeEngineKind.EfCore,
            DialectNames.PostgreSql,
            "EF Core PostgreSQL"
        ),
        new(
            RuntimeTarget.EfCoreMySql,
            RuntimeEngineKind.EfCore,
            DialectNames.MySql,
            "EF Core MySQL"
        ),
        new(
            RuntimeTarget.EfCoreOracle,
            RuntimeEngineKind.EfCore,
            DialectNames.Oracle,
            "EF Core Oracle"
        ),
        new(RuntimeTarget.InMemory, RuntimeEngineKind.InMemory, null, "インメモリ"),
        new(RuntimeTarget.Remote, RuntimeEngineKind.Remote, null, "リモート"),
    ];

    /// <summary>実装先の性質を引く</summary>
    /// <param name="target">実装先</param>
    public static RuntimeTargetInfo Of(RuntimeTarget target) =>
        All.SingleOrDefault(info => info.Target == target)
        ?? throw new ArgumentOutOfRangeException(nameof(target), target, "性質の宣言が無い");

    /// <summary>生成の実装の種類と方言から実装先を引く（無ければ null）</summary>
    /// <param name="engine">生成の実装の種類</param>
    /// <param name="dialect">方言の名前</param>
    public static RuntimeTarget? Find(RuntimeEngineKind engine, string? dialect) =>
        All.SingleOrDefault(info =>
            info.Engine == engine && string.Equals(info.Dialect, dialect, StringComparison.Ordinal)
        )?.Target;

    /// <summary>
    /// 実装先の正本と製品の正本の食い違いをすべて並べる（無ければ空）：列挙の値ごとに性質がちょうど 1 つあること、
    /// QuickER 版の方言の集合が製品の QuickER 版の方言と、EF Core 版の方言の集合が方言の全体と一致すること、
    /// インメモリとリモートがちょうど 1 つずつで方言を持たないこと
    /// </summary>
    /// <param name="adoDialects">製品の QuickER 版 Repository が対応する方言（<c>CodeGenerationOptions.SupportedRepositoryDialects</c>）</param>
    /// <param name="allDialects">方言の全体（<see cref="DialectNames.All"/>）</param>
    public static IReadOnlyList<string> ProductProblems(
        IReadOnlyCollection<string> adoDialects,
        IReadOnlyCollection<string> allDialects
    )
    {
        ArgumentNullException.ThrowIfNull(adoDialects);
        ArgumentNullException.ThrowIfNull(allDialects);

        var problems = new List<string>();

        foreach (var target in Enum.GetValues<RuntimeTarget>())
        {
            var count = All.Count(info => info.Target == target);

            if (count != 1)
            {
                problems.Add($"実装先 {target} の性質の宣言が {count} 個ある");
            }
        }

        foreach (
            var (engine, expected) in new[]
            {
                (RuntimeEngineKind.Ado, adoDialects),
                (RuntimeEngineKind.EfCore, allDialects),
            }
        )
        {
            var declared = All.Where(info => info.Engine == engine)
                .Select(info => info.Dialect ?? "(方言なし)")
                .ToList();

            foreach (
                var missing in expected
                    .Where(dialect => !declared.Contains(dialect, StringComparer.Ordinal))
                    .OrderBy(dialect => dialect, StringComparer.Ordinal)
            )
            {
                problems.Add($"製品の {engine} の方言 {missing} に実装先が無い");
            }

            foreach (
                var extra in declared
                    .Where(dialect => !expected.Contains(dialect, StringComparer.Ordinal))
                    .OrderBy(dialect => dialect, StringComparer.Ordinal)
            )
            {
                problems.Add($"実装先の {engine} の方言 {extra} が製品に無い");
            }

            foreach (
                var duplicate in declared
                    .GroupBy(dialect => dialect, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
            )
            {
                problems.Add($"実装先の {engine} の方言 {duplicate} が重複している");
            }
        }

        foreach (var engine in new[] { RuntimeEngineKind.InMemory, RuntimeEngineKind.Remote })
        {
            var infos = All.Where(info => info.Engine == engine).ToList();

            if (infos.Count != 1 || infos[0].Dialect is not null)
            {
                problems.Add($"{engine} の実装先は方言を持たずにちょうど 1 つ置く");
            }
        }

        return problems;
    }
}

/// <summary>
/// テストのクラスが担当する機能 × 実装先のセル（Integration の <c>RuntimeTestMatrixTests</c> が全数を照合し、行列の生成物に載せる）
/// </summary>
/// <param name="feature">機能の名前（行列の行の表の定数）</param>
/// <param name="target">実装先</param>
/// <remarks>
/// リモート（<see cref="RuntimeTarget.Remote"/>）の担当は、サーバーの裏の実装先を <see cref="Server"/> か
/// <see cref="ServerNone"/>（リポジトリを持たない転送だけのスタブ）のちょうど一方で名乗る。リモート以外の担当はどちらも書かない。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RuntimeCellAttribute(string feature, RuntimeTarget target) : Attribute
{
    private RuntimeTarget? _server;

    /// <summary>機能の名前</summary>
    public string Feature { get; } = feature;

    /// <summary>実装先</summary>
    public RuntimeTarget Target { get; } = target;

    /// <summary>リモートのサーバーの裏の実装先（リモートの担当だけが書く）</summary>
    public RuntimeTarget Server
    {
        get => _server ?? throw new InvalidOperationException("サーバーを名乗っていない");
        set => _server = value;
    }

    /// <summary>名乗ったサーバー（名乗っていなければ null）</summary>
    public RuntimeTarget? DeclaredServer => _server;

    /// <summary>リモートのサーバーがリポジトリを持たない（転送だけのスタブ・HTTP を使わない印の検査）</summary>
    public bool ServerNone { get; set; }
}
