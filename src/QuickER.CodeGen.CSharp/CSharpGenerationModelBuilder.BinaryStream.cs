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
/// 契約はリモート契約生成（<c>GenerateRemoteContracts</c> または <c>GenerateRemoteServices</c>）の有無で挿入先が変わる
/// （リモート面 ON なら <c>I{Entity}RemoteRepository</c>＝ネットワーク境界を越えられる操作・OFF なら全機能面 <c>I{Entity}Repository</c>。
/// 全機能面はリモート面を継承するため、移設しても既存の実装クラス・利用コードはコンパイル無変更で通る）。実装は
/// QuickER 版 Repository 2 方言・インメモリを固定 infra のエンジンへ委譲する薄いメソッドで賄い、EF Core は方言固有ストリーミングを
/// 持てないため <c>NotSupportedException</c> を投げる。ファイル糖衣（<c>Read/Write{Column}ToFile/FromFile</c>）は各実装クラスへ
/// 重複させず、契約面（リモート面 ON ならリモート面）への拡張メソッド静的クラス 1 本にする。
/// </para>
/// <para>
/// リモートサービス生成時は HTTP + JSON の専用エンドポイント（<c>GET/PUT/DELETE {prefix}/{エンティティ}/{列名}?id=</c>）で
/// ストリーミング転送する。クライアント（<c>Http{Entity}RemoteRepository</c>）は固定 infra の共通ヘルパーへ委譲し、
/// サーバー（<c>Map{Entity}Endpoints</c>）は除外列ごとに 3 動詞を出力する。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>EF Core で Stream アクセサが使えないことを示す例外メッセージ（生成コードへ埋め込む）</summary>
    private const string EfCoreStreamNotSupportedMessage =
        "Stream accessors (reading/writing unbounded binary columns) are not supported in EF Core mode. Use the QuickER Repository, or implement them in a partial class.";

    /// <summary>1 エンティティ分の Stream アクセサブロック（テンプレートへ渡す整形済みテキスト群）</summary>
    private sealed record BinaryStreamBlocks(
        string ContractBlock,
        string ThinImplBlock,
        string EfImplBlock,
        string FileExtensionsBlock,
        string RemoteClientBlock,
        string RemoteServerBlock
    );

    /// <summary>ブロックが空のときの既定値（除外列なし・オプション OFF・Repository 非生成）</summary>
    private static readonly BinaryStreamBlocks EmptyBinaryStreamBlocks = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty
    );

    /// <summary>エンティティの無制限バイナリ列から Stream アクセサのテンプレート用ブロックを構築する</summary>
    private BinaryStreamBlocks BuildBinaryStreamBlocks(
        Entity entity,
        string entityClassName,
        string repositoryName,
        string keyTypeName,
        CodeGenerationOptions options
    )
    {
        // 生成条件: QuickER 版 Repository を生成し、無制限バイナリ除外オプションが ON で、除外列が実在すること。
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

        // リモート契約が生成される構成では契約とファイル糖衣の対象をリモート面へ移す
        // （リモート面 ON はネットワーク境界を越えられる操作の定義に合致。全機能面はリモート面を継承）。
        var remoteContracts = options.GenerateRemoteContracts || options.GenerateRemoteServices;
        var interfaceName = $"I{repositoryName}Repository";
        var remoteInterfaceName = $"I{repositoryName}RemoteRepository";
        var fileExtensionsTarget = remoteContracts ? remoteInterfaceName : interfaceName;

        var contractMembers = new List<string>();
        var thinMembers = new List<string>();
        var efMembers = new List<string>();
        var fileMethods = new List<string>();
        var remoteClientMembers = new List<string>();
        var remoteServerMembers = new List<string>();

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
                BuildBinaryStreamFileMethods(
                    columnName,
                    propertyName,
                    fileExtensionsTarget,
                    keyTypeName
                )
            );
            remoteClientMembers.Add(BuildBinaryStreamRemoteClientMember(propertyName, keyTypeName));
            remoteServerMembers.Add(
                BuildBinaryStreamRemoteServerMember(
                    propertyName,
                    remoteInterfaceName,
                    repositoryName,
                    keyTypeName
                )
            );
        }

        return new BinaryStreamBlocks(
            string.Join("\n\n", contractMembers),
            string.Join("\n\n", thinMembers),
            string.Join("\n\n", efMembers),
            BuildBinaryStreamFileExtensionsClass(entityClassName, repositoryName, fileMethods),
            string.Join("\n\n", remoteClientMembers),
            string.Join("\n\n", remoteServerMembers)
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
            .Append("    /// <summary>Reads the ")
            .Append(columnName)
            .Append(
                " column into the destination stream (unbounded binary column, O(chunk) streaming; true = written, false = no row or NULL).</summary>\n"
            )
            .Append("    Task<bool> Read")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(" id, Stream destination, CancellationToken cancellationToken = default);\n\n");
        builder
            .Append("    /// <summary>Writes the ")
            .Append(columnName)
            .Append(
                " column from a stream (unbounded binary column, O(chunk) streaming; source = null sets NULL, non-seekable streams require an explicit length; true = updated, false = no row).</summary>\n"
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
            .Append("    /// <summary>Reads the ")
            .Append(columnName)
            .Append(
                " column into a file (delegates to the Stream overload; true = written, false = no row or NULL).</summary>\n"
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
            .Append("Async(id, destination, cancellationToken).ConfigureAwait(false);\n    }\n\n");
        builder
            .Append("    /// <summary>Writes the ")
            .Append(columnName)
            .Append(
                " column from a file (delegates to the Stream overload; true = updated, false = no row).</summary>\n"
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
            .Append(
                "Async(id, source, source.Length, cancellationToken).ConfigureAwait(false);\n    }"
            );
        return builder.ToString();
    }

    /// <summary>Stream アクセサの HTTP クライアント転送メンバー（固定 infra の GET/PUT/DELETE ヘルパーへ委譲）を構築する</summary>
    private static string BuildBinaryStreamRemoteClientMember(
        string propertyName,
        string keyTypeName
    )
    {
        // 列名（プロパティ名）はサーバーの列ルートセグメントと同一文字列を使う（クライアント・サーバーで一致）
        var builder = new StringBuilder();
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Read")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(" id, Stream destination, CancellationToken cancellationToken = default) =>\n")
            .Append("        DownloadUnboundedBinaryColumnAsync(\"")
            .Append(propertyName)
            .Append("\", id, destination, cancellationToken);\n\n");
        builder
            .Append("    /// <inheritdoc />\n")
            .Append("    public Task<bool> Write")
            .Append(propertyName)
            .Append("Async(")
            .Append(keyTypeName)
            .Append(
                " id, Stream? source, long? length = null, CancellationToken cancellationToken = default) =>\n"
            )
            .Append("        UploadUnboundedBinaryColumnAsync(\"")
            .Append(propertyName)
            .Append("\", id, source, length, cancellationToken);");
        return builder.ToString();
    }

    /// <summary>Stream アクセサのサーバー側バイナリエンドポイント 3 動詞（GET/PUT/DELETE）を構築する</summary>
    /// <remarks>インデントはサーバーテンプレートのメソッド本体（8 スペース起点）に合わせる。ルートは <c>{エンティティ}/{列名}</c></remarks>
    private static string BuildBinaryStreamRemoteServerMember(
        string propertyName,
        string remoteInterfaceName,
        string repositoryName,
        string keyTypeName
    )
    {
        var route = $"{repositoryName}/{propertyName}";
        var resolve =
            $"var repository = context.RequestServices.GetRequiredService<{remoteInterfaceName}>();";
        var parseKey = $"RemoteServerEngine.ParseKeyFromQuery<{keyTypeName}>(context)";

        var builder = new StringBuilder();

        // GET: ダウンロード。読み取り関数が false（行なし/NULL）のときは本文未送信のまま 404 になる
        builder
            .Append("        group.MapGet(\n            \"")
            .Append(route)
            .Append("\",\n            (HttpContext context) =>\n")
            .Append(
                "                RemoteServerEngine.ExecuteDownloadAsync(\n                    context,\n"
            )
            .Append("                    destination =>\n                    {\n")
            .Append("                        ")
            .Append(resolve)
            .Append("\n                        return repository.Read")
            .Append(propertyName)
            .Append("Async(\n                            ")
            .Append(parseKey)
            .Append(",\n                            destination,\n")
            .Append(
                "                            context.RequestAborted\n                        );\n"
            )
            .Append("                    }\n                )\n        );\n\n");

        // PUT: アップロード。このエンドポイントのみリクエストサイズ制限を解除する（メタデータ付与）
        builder
            .Append("        group\n            .MapPut(\n                \"")
            .Append(route)
            .Append("\",\n                (HttpContext context) =>\n")
            .Append(
                "                    RemoteServerEngine.ExecuteUploadAsync(\n                        context,\n"
            )
            .Append("                        (body, length) =>\n                        {\n")
            .Append("                            ")
            .Append(resolve)
            .Append("\n                            return repository.Write")
            .Append(propertyName)
            .Append("Async(\n                                ")
            .Append(parseKey)
            .Append(
                ",\n                                body,\n                                length,\n"
            )
            .Append(
                "                                context.RequestAborted\n                            );\n"
            )
            .Append("                        }\n                    )\n            )\n")
            .Append("            .WithMetadata(DisableRequestBodySizeLimit.Instance);\n\n");

        // DELETE: 列を NULL 化（source=null 相当）
        builder
            .Append("        group.MapDelete(\n            \"")
            .Append(route)
            .Append("\",\n            (HttpContext context) =>\n")
            .Append(
                "                RemoteServerEngine.ExecuteDeleteAsync(\n                    context,\n"
            )
            .Append("                    () =>\n                    {\n")
            .Append("                        ")
            .Append(resolve)
            .Append("\n                        return repository.Write")
            .Append(propertyName)
            .Append("Async(\n                            ")
            .Append(parseKey)
            .Append(",\n                            null,\n                            null,\n")
            .Append(
                "                            context.RequestAborted\n                        );\n"
            )
            .Append("                    }\n                )\n        );");

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
            .Append(
                "/// <summary>File convenience methods for the unbounded binary column accessors of "
            )
            .Append(entityClassName)
            .Append(
                " (transfer blobs between the database and files in a single call; delegates to the Stream overloads).</summary>\n"
            )
            .Append("public static class ")
            .Append(repositoryName)
            .Append("RepositoryBinaryStreamExtensions\n{\n")
            .Append(string.Join("\n\n", fileMethods))
            .Append("\n}");
        return builder.ToString();
    }
}
