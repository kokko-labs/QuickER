using System.Globalization;
using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// パッケージ参照モード（<see cref="CodeGenerationOptions.UseRuntimePackages"/>）で、生成コードが必要とする
/// NuGet パッケージ（<see cref="RuntimePackages"/>）の一覧と、そのまま csproj へ貼れる <c>&lt;PackageReference&gt;</c> 行を案内する。
/// </summary>
/// <remarks>
/// <para>
/// 参照の届け方は「案内のみ」（csproj には触らない）。生成ヘッダコメントと、GUI/CLI の生成後メッセージが
/// この案内を表示し、利用者が自分の csproj へ追記する。パッケージ集合の決定規則は
/// <see cref="Compute"/> に集約し、GUI/CLI・ヘッダで同一の結果を使う。
/// </para>
/// <para>
/// 決定規則:
/// <list type="bullet">
///   <item>常に <see cref="RuntimePackages.Core"/>（Entity のみの構成でも EntityBase・属性・VO 基底で必要）</item>
///   <item><see cref="CodeGenerationOptions.GenerateRepositories"/> 時: 実効方言に応じ
///     <see cref="RuntimePackages.SqlServer"/> / <see cref="RuntimePackages.Sqlite"/>（マルチターゲットなら両方）</item>
///   <item><see cref="CodeGenerationOptions.GenerateEfCore"/> 時: <see cref="RuntimePackages.EntityFrameworkCore"/></item>
/// </list>
/// バージョンは呼び出し側から受け取る（版の実配線は後続タスク）。
/// </para>
/// </remarks>
public static class RuntimePackageReferenceGuidance
{
    /// <summary>
    /// 生成オプションから、参照すべきパッケージ ID の一覧を決定する（Core を先頭に、方言・EF Core を続ける安定順）。
    /// </summary>
    /// <param name="options">生成オプション（実効方言・QuickER 版 Repository / EF Core の有無を参照する）</param>
    /// <returns>参照が必要なパッケージ ID（重複なし・安定順）。<see cref="RuntimePackages.Core"/> を必ず含む</returns>
    public static IReadOnlyList<string> Compute(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var packages = new List<string> { RuntimePackages.Core };

        if (options.GenerateRepositories)
        {
            // 実効方言の解決は未対応方言で例外を投げるが、案内はプレビュー等でも呼ばれ得るため非例外にする
            // （未対応方言は sqlserver 相当へフォールバック。実効方言の検証・診断は生成本体が担う）。
            IReadOnlyList<string> dialects;

            try
            {
                dialects = options.EffectiveRepositoryDialects;
            }
            catch (ArgumentException)
            {
                dialects = ["sqlserver"];
            }

            foreach (var dialect in dialects)
            {
                var package = IsSqlite(dialect)
                    ? RuntimePackages.Sqlite
                    : RuntimePackages.SqlServer;

                if (!packages.Contains(package, StringComparer.Ordinal))
                {
                    packages.Add(package);
                }
            }
        }

        if (options.GenerateEfCore && !packages.Contains(RuntimePackages.EntityFrameworkCore))
        {
            packages.Add(RuntimePackages.EntityFrameworkCore);
        }

        return packages;
    }

    /// <summary>
    /// 参照すべきパッケージの <c>&lt;PackageReference Include="..." Version="..." /&gt;</c> 行を、指定バージョンで組み立てて返す。
    /// </summary>
    /// <param name="options">生成オプション</param>
    /// <param name="version">全パッケージへ付与するバージョン文字列（ロックステップ運用。実配線は後続タスク）</param>
    /// <returns>パッケージ ID 昇順ではなく <see cref="Compute"/> の安定順で並んだ PackageReference 行の一覧</returns>
    public static IReadOnlyList<string> BuildPackageReferenceLines(
        CodeGenerationOptions options,
        string version
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return Compute(options)
            .Select(id => $"<PackageReference Include=\"{id}\" Version=\"{version}\" />")
            .ToList();
    }

    /// <summary>
    /// 生成ヘッダコメント／GUI/CLI 表示用に、必要パッケージの一覧と PackageReference 行をまとめた案内テキストを組み立てる。
    /// </summary>
    /// <remarks>
    /// 各行は行頭にコメント接頭辞を付けずに返す（ヘッダへ埋め込む側が <c>// </c> を付ける）。空行は含めない。
    /// 生成物（ヘッダ・.g.md）へ埋め込む経路はカルチャ省略＝英語固定（同一入力→バイト一致の決定性を保つ）。
    /// GUI / CLI の画面表示は <see cref="CultureInfo.CurrentUICulture"/> を渡してローカライズする。
    /// </remarks>
    /// <param name="options">生成オプション</param>
    /// <param name="version">案内へ載せるパッケージバージョン</param>
    /// <param name="culture">見出し行の言語。null（既定）は英語固定＝生成物へ埋め込む経路用</param>
    /// <returns>案内テキストの行一覧（見出し＋PackageReference 行）</returns>
    public static IReadOnlyList<string> BuildGuidanceLines(
        CodeGenerationOptions options,
        string version,
        CultureInfo? culture = null
    )
    {
        var heading = Strings.ResourceManager.GetString(
            nameof(Strings.CodeGen_RuntimePackageGuidanceHeading),
            culture ?? CultureInfo.InvariantCulture
        )!;

        var lines = new List<string> { heading };

        lines.AddRange(BuildPackageReferenceLines(options, version));
        return lines;
    }

    /// <summary>指定方言が SQLite かどうか（プロバイダ名と同一の識別子で判定）</summary>
    private static bool IsSqlite(string dialect) =>
        string.Equals(dialect, "sqlite", StringComparison.OrdinalIgnoreCase);
}
