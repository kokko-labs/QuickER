using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 参照集合から ADO・EF Core のアセンブリを外したコンパイル（「生成物がその依存を出さない」を、参照を絞ってもコンパイルできることで示す）
/// </summary>
/// <remarks>
/// <para>
/// 生成テキストに名前空間の文字列が無いことを探す代わりに使う。外したアセンブリの型を 1 つでも使えば、参照が解決できずにエラーになる
/// （コメント・文字列の中の言及は数えない）。DI（<c>Microsoft.Extensions.DependencyInjection</c>）は生成物の登録の拡張メソッドが使うので常に残す。
/// 配布ランタイムのアセンブリ（<c>QuickER.Runtime*</c>）は外す（<see cref="RuntimeReferenceSet.Create"/> の <c>excludeRuntimePackages</c>）。
/// </para>
/// <para>インライン出力（パッケージ参照モードでない）・層別でない行で使う（<c>.g.cs</c> と手動実装のソースを 1 アセンブリへまとめる）。</para>
/// </remarks>
/// <param name="SqlClient">Microsoft.Data.SqlClient を残す</param>
/// <param name="Sqlite">Microsoft.Data.Sqlite を残す</param>
/// <param name="EfCore">EF Core を残す</param>
internal sealed record ReducedReferences(bool SqlClient, bool Sqlite, bool EfCore)
{
    /// <summary>EF Core を外す</summary>
    public static ReducedReferences WithoutEfCore { get; } = new(true, true, false);

    /// <summary>Microsoft.Data.SqlClient を外す</summary>
    public static ReducedReferences WithoutSqlClient { get; } = new(false, true, true);

    /// <summary>ADO（SqlClient・Sqlite）と EF Core を全部外す</summary>
    public static ReducedReferences WithoutDataAccess { get; } = new(false, false, false);

    /// <summary>外したものの説明</summary>
    public string Description =>
        string.Join(
            "・",
            new[] { (SqlClient, "SqlClient"), (Sqlite, "Sqlite"), (EfCore, "EF Core") }
                .Where(item => !item.Item1)
                .Select(item => item.Item2)
        ) + " を外した参照集合";

    /// <summary>コンパイルする（問題を返す）</summary>
    /// <param name="result">生成結果</param>
    /// <param name="manualSources">手動実装のソース</param>
    public IReadOnlyList<string> Check(
        CodeGenerationResult result,
        IReadOnlyList<GeneratedFile> manualSources
    )
    {
        var emitted = GeneratedCodeCompiler.Emit(
            result
                .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
                .Concat(manualSources)
                .Select(file => (FileLayoutOracle.PathOf(file), file.Content)),
            $"QuickER.CodegenConfig.Reduced.{Guid.NewGuid():N}",
            RuntimeReferenceSet
                .Create(SqlClient, Sqlite, EfCore, di: true, excludeRuntimePackages: true)
                .Build()
        );

        return emitted.Success
            ? []
            :
            [
                $"参照を絞ったコンパイル（{Description}）: エラー{Environment.NewLine}{emitted.DescribeErrors()}",
            ];
    }
}
