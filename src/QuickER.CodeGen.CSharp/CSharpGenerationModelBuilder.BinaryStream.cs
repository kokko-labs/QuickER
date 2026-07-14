using System.Text;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 無制限バイナリ列（<see cref="CSharpTypeInfo.IsUnboundedBinary"/>）の Stream アクセサ（<c>Read/Write{Column}Async</c>）の
/// テンプレート用ブロックを構築する部分。
/// </summary>
/// <remarks>
/// <para>
/// 名前付きクエリと同じく「整形済みのメンバーテキスト」を組み立て、テンプレートは差し込むだけにする
/// （Scriban 側のロジックを増やさない）。生成条件は <c>GenerateRepositories &amp;&amp; ExcludeUnboundedBinaryColumns</c>
/// かつ除外列（無制限バイナリ列）が存在すること。この 3 条件を満たさない場合は全ブロックを空文字にし、
/// テンプレートは（名前付きクエリブロックと同様に）非空のときだけ出力する。
/// </para>
/// <para>
/// 契約は全機能面（<c>I{Entity}Repository</c>）にのみ載せ、リモート面（<c>I{Entity}RemoteRepository</c>）には載せない
/// （ネットワーク境界を越えるストリーミングは将来対応）。実装は Repository (QuickER) 2 方言・インメモリを固定 infra の
/// エンジンへ委譲する薄いメソッドで賄い、EF Core は方言固有ストリーミングを持てないため <c>NotSupportedException</c> を投げる。
/// ファイル糖衣（<c>Read/Write{Column}ToFile/FromFile</c>）は各実装クラスへ重複させず、契約面への拡張メソッド静的クラス 1 本にする。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>EF Core で Stream アクセサが使えないことを示す例外メッセージ（生成コードへ埋め込む）</summary>
    private const string EfCoreStreamNotSupportedMessage =
        "EF Core モードでは Stream アクセサ（無制限バイナリ列の読み書き）は使用できません。Repository (QuickER) を使うか、partial クラスで実装してください。";

    /// <summary>1 エンティティ分の Stream アクセサブロック（テンプレートへ渡す整形済みテキスト群）</summary>
    private sealed record BinaryStreamBlocks(
        string ContractBlock,
        string ThinImplBlock,
        string EfImplBlock,
        string FileExtensionsBlock
    );

    /// <summary>ブロックが空のときの既定値（除外列なし・オプション OFF・Repository 非生成）</summary>
    private static readonly BinaryStreamBlocks EmptyBinaryStreamBlocks = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty
    );

    /// <summary>エンティティの無制限バイナリ列から Stream アクセサのテンプレート用ブロックを構築する</summary>
    private BinaryStreamBlocks BuildBinaryStreamBlocks(
        Entity entity,
        string entityClassName,
        string interfaceName,
        string repositoryName,
        string keyTypeName,
        CodeGenerationOptions options
    )
    {
        // 生成条件: Repository (QuickER) を生成し、無制限バイナリ除外オプションが ON で、除外列が実在すること。
        // どれか一つでも欠ければ Stream アクセサは生成しない（契約にも現れない）。
        if (!options.GenerateRepositories || !options.ExcludeUnboundedBinaryColumns)
        {
            return EmptyBinaryStreamBlocks;
        }

        // 除外列（無制限バイナリ列）とその C# プロパティ名・元カラム名を定義順に集める。
        var columns = entity
            .Columns.Where(column => _columnTypes[column.Id].IsUnboundedBinary)
            .Select(column =>
                (ColumnName: column.Name, PropertyName: _nameConverter.ToPropertyName(column.Name))
            )
            .ToList();

        if (columns.Count == 0)
        {
            return EmptyBinaryStreamBlocks;
        }

        var contractMembers = new List<string>();
        var thinMembers = new List<string>();
        var efMembers = new List<string>();
        var fileMethods = new List<string>();

        foreach (var (columnName, propertyName) in columns)
        {
            contractMembers.Add(
                BuildBinaryStreamContractMember(columnName, propertyName, keyTypeName)
            );
            thinMembers.Add(
                BuildBinaryStreamThinImplMember(entityClassName, propertyName, keyTypeName)
            );
            efMembers.Add(BuildBinaryStreamEfImplMember(propertyName, keyTypeName));
            fileMethods.Add(
                BuildBinaryStreamFileMethods(columnName, propertyName, interfaceName, keyTypeName)
            );
        }

        return new BinaryStreamBlocks(
            string.Join("\n\n", contractMembers),
            string.Join("\n\n", thinMembers),
            string.Join("\n\n", efMembers),
            BuildBinaryStreamFileExtensionsClass(entityClassName, repositoryName, fileMethods)
        );
    }

    /// <summary>Stream アクセサの契約メンバー（全機能インターフェイス本体・Read/Write の 2 宣言）を構築する</summary>
    private static string BuildBinaryStreamContractMember(
        string columnName,
        string propertyName,
        string keyTypeName
    )
    {
        var builder = new StringBuilder();
        builder
            .Append("    /// <summary>")
            .Append(columnName)
            .Append(
                " を宛先ストリームへ読み出す（無制限バイナリ列・O(チャンク) のストリーミング。true=書き込んだ・false=行なし または NULL）</summary>\n"
            )
            .Append("    Task<bool> Read")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(" id, Stream destination, CancellationToken cancellationToken = default);\n\n");
        builder
            .Append("    /// <summary>")
            .Append(columnName)
            .Append(
                " をストリームから書き込む（無制限バイナリ列・O(チャンク) のストリーミング。source=null で NULL を設定・CanSeek でない Stream は length 指定が必須。true=更新した・false=行なし）</summary>\n"
            )
            .Append("    Task<bool> Write")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(
                " id, Stream? source, long? length = null, CancellationToken cancellationToken = default);"
            );
        return builder.ToString();
    }

    /// <summary>Stream アクセサの薄い実装メンバー（固定 infra のエンジンへ委譲。QuickER 2 方言・インメモリ共通）を構築する</summary>
    private static string BuildBinaryStreamThinImplMember(
        string entityClassName,
        string propertyName,
        string keyTypeName
    )
    {
        var nameOf = $"nameof({entityClassName}.{propertyName})";
        var builder = new StringBuilder();
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Read")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(" id, Stream destination, CancellationToken cancellationToken = default) =>\n")
            .Append("        ReadUnboundedBinaryColumnAsync(")
            .Append(nameOf)
            .Append(", id, destination, cancellationToken);\n\n");
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Write")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(
                " id, Stream? source, long? length = null, CancellationToken cancellationToken = default) =>\n"
            )
            .Append("        WriteUnboundedBinaryColumnAsync(")
            .Append(nameOf)
            .Append(", id, source, length, cancellationToken);");
        return builder.ToString();
    }

    /// <summary>Stream アクセサの EF Core 実装メンバー（NotSupportedException を投げる）を構築する</summary>
    private static string BuildBinaryStreamEfImplMember(string propertyName, string keyTypeName)
    {
        var throwBody =
            $"        throw new NotSupportedException(\n            \"{EfCoreStreamNotSupportedMessage}\"\n        );";
        var builder = new StringBuilder();
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Read")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(" id, Stream destination, CancellationToken cancellationToken = default) =>\n")
            .Append(throwBody)
            .Append("\n\n");
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Write")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(
                " id, Stream? source, long? length = null, CancellationToken cancellationToken = default) =>\n"
            )
            .Append(throwBody);
        return builder.ToString();
    }

    /// <summary>ファイル糖衣の 2 メソッド（Read...ToFile / Write...FromFile。拡張メソッド）を構築する</summary>
    private static string BuildBinaryStreamFileMethods(
        string columnName,
        string propertyName,
        string interfaceName,
        string keyTypeName
    )
    {
        var builder = new StringBuilder();
        builder
            .Append("    /// <summary>")
            .Append(columnName)
            .Append(
                " をファイルへ読み出す（Stream 版へ委譲。true=書き込んだ・false=行なし または NULL）</summary>\n"
            )
            .Append("    public static async Task<bool> Read")
            .Append(propertyName)
            .Append("ToFileAsync(\n")
            .Append("        this ")
            .Append(interfaceName)
            .Append(" repository,\n")
            .Append("        ")
            .Append(keyTypeName)
            .Append(" id,\n")
            .Append("        string path,\n")
            .Append("        CancellationToken cancellationToken = default\n")
            .Append("    )\n    {\n")
            .Append("        ArgumentNullException.ThrowIfNull(repository);\n")
            .Append("        await using var destination = File.Create(path);\n")
            .Append("        return await repository.Read")
            .Append(propertyName)
            .Append("Async(id, destination, cancellationToken);\n    }\n\n");
        builder
            .Append("    /// <summary>")
            .Append(columnName)
            .Append(
                " をファイルから書き込む（Stream 版へ委譲。true=更新した・false=行なし）</summary>\n"
            )
            .Append("    public static async Task<bool> Write")
            .Append(propertyName)
            .Append("FromFileAsync(\n")
            .Append("        this ")
            .Append(interfaceName)
            .Append(" repository,\n")
            .Append("        ")
            .Append(keyTypeName)
            .Append(" id,\n")
            .Append("        string path,\n")
            .Append("        CancellationToken cancellationToken = default\n")
            .Append("    )\n    {\n")
            .Append("        ArgumentNullException.ThrowIfNull(repository);\n")
            .Append("        await using var source = File.OpenRead(path);\n")
            .Append("        return await repository.Write")
            .Append(propertyName)
            .Append("Async(id, source, source.Length, cancellationToken);\n    }");
        return builder.ToString();
    }

    /// <summary>ファイル糖衣メソッド群を拡張メソッド静的クラスへ包む</summary>
    private static string BuildBinaryStreamFileExtensionsClass(
        string entityClassName,
        string repositoryName,
        IReadOnlyList<string> fileMethods
    )
    {
        var builder = new StringBuilder();
        builder
            .Append("/// <summary>")
            .Append(entityClassName)
            .Append(
                " の無制限バイナリ列アクセサのファイル糖衣（DB⇔ファイルの blob 転送を 1 呼び出しで行う・Stream 版へ委譲）</summary>\n"
            )
            .Append("public static class ")
            .Append(repositoryName)
            .Append("RepositoryBinaryStreamExtensions\n{\n")
            .Append(string.Join("\n\n", fileMethods))
            .Append("\n}");
        return builder.ToString();
    }
}
