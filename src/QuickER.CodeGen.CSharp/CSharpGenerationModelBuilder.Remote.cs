using System.Text;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// リモートサービス生成（<see cref="CodeGenerationOptions.GenerateRemoteServices"/>）向けの
/// 名前付きクエリ転送コード（HTTP クライアントメソッド・サーバーハンドラ・リクエストレコード）を組み立てる partial。
/// </summary>
/// <remarks>
/// クエリの実装方式（Dsl / Sql / Manual）に依らず、クライアントは「同一シグネチャの転送メソッド」・サーバーは
/// 「リクエスト復元→リモート面（I{Entity}RemoteRepository）呼び出し」で一様に扱える（実装の実体はサーバー側の
/// 実リポジトリが担うため、クライアント側に方式の分岐は存在しない）。ペイロードのプロパティ名はクライアントの
/// 匿名型（パラメータ名そのまま）とサーバーのレコード（PascalCase）で綴りが違うが、転送 JSON は大文字小文字を
/// 無視して読む（RemoteJson.Options の PropertyNameCaseInsensitive）ため一致する。
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>クエリ 1 件分の HTTP クライアント転送メソッド（Http{Entity}RemoteRepository の本体メンバー）を構築する</summary>
    private static string BuildRemoteClientMember(QueryMethodShape shape)
    {
        // 戻り値の内側の型（Task<X> → X）。InvokeAsync<TResult> の型引数に使う
        var innerType = StripTaskType(shape.ReturnTypeName);
        var operation = TrimAsyncSuffix(shape.MethodName);

        // ペイロード: パラメータ名そのままの匿名型（VO はエンベロープの JSON 化時に VO コンバータで内包値になる）。
        // パラメータなしは null（本文 "null"。サーバー側もリクエストを読まない）
        var payload =
            shape.PayloadParameters.Count == 0
                ? "null"
                : $"new {{ {string.Join(", ", shape.PayloadParameters.Select(p => p.Name))} }}";

        var builder = new StringBuilder();
        builder.Append("    /// <summary>").Append(shape.Summary).Append("</summary>\n");
        builder
            .Append("    public ")
            .Append(shape.ReturnTypeName)
            .Append(' ')
            .Append(shape.MethodName)
            .Append('(')
            .Append(shape.ParameterList)
            .Append(") =>\n        InvokeAsync<")
            .Append(innerType)
            .Append(">(\"")
            .Append(operation)
            .Append("\", ")
            .Append(payload)
            .Append(", cancellationToken);");
        return builder.ToString();
    }

    /// <summary>クエリ 1 件分のサーバー側エンドポイントマッピング（Map{Entity}Endpoints 内の MapPost 呼び出し）を構築する</summary>
    /// <remarks>インデントはサーバーテンプレートのメソッド本体（8 スペース起点）に合わせる</remarks>
    private static string BuildRemoteServerMap(QueryMethodShape shape, string repositoryName)
    {
        var operation = TrimAsyncSuffix(shape.MethodName);
        var remoteInterfaceName = $"I{repositoryName}RemoteRepository";

        var builder = new StringBuilder();
        builder
            .Append("        group.MapPost(\n            \"")
            .Append(repositoryName)
            .Append('/')
            .Append(operation)
            .Append("\",\n            (HttpContext context) =>\n")
            .Append("                ExecuteAsync(\n")
            .Append("                    context,\n")
            .Append("                    async () =>\n")
            .Append("                    {\n");

        // 引数の復元（パラメータなしのクエリは本文を読まない）
        var arguments = new List<string>();

        if (shape.PayloadParameters.Count > 0)
        {
            builder
                .Append("                        var request = await ReadRequestAsync<")
                .Append(RequestRecordName(shape, repositoryName))
                .Append(">(context);\n");
            arguments.AddRange(
                shape.PayloadParameters.Select(p => $"request.{ToPascalCase(p.Name)}")
            );
        }

        arguments.Add("context.RequestAborted");

        builder
            .Append(
                "                        var repository = context.RequestServices.GetRequiredService<"
            )
            .Append(remoteInterfaceName)
            .Append(">();\n")
            .Append("                        return (object?)await repository.")
            .Append(shape.MethodName)
            .Append('(')
            .Append(string.Join(", ", arguments))
            .Append(");\n")
            .Append("                    }\n")
            .Append("                )\n")
            .Append("        );");
        return builder.ToString();
    }

    /// <summary>クエリ 1 件分のサーバー側リクエストレコード（クラスレベル）を構築する（パラメータなしのクエリは null）</summary>
    private static string? BuildRemoteServerRecord(QueryMethodShape shape, string repositoryName)
    {
        if (shape.PayloadParameters.Count == 0)
        {
            return null;
        }

        var properties = string.Join(
            ", ",
            shape.PayloadParameters.Select(p => $"{p.TypeName} {ToPascalCase(p.Name)}")
        );

        return $"    /// <summary>{TrimAsyncSuffix(shape.MethodName)}（{repositoryName}）のリクエスト本文</summary>\n"
            + $"    private sealed record {RequestRecordName(shape, repositoryName)}({properties});";
    }

    /// <summary>サーバー側リクエストレコード名（例 <c>OrderGetByCustomerRequest</c>）を返す</summary>
    private static string RequestRecordName(QueryMethodShape shape, string repositoryName) =>
        $"{repositoryName}{TrimAsyncSuffix(shape.MethodName)}Request";

    /// <summary>戻り値型 <c>Task&lt;X&gt;</c> から内側の型 X を取り出す</summary>
    private static string StripTaskType(string returnTypeName) =>
        returnTypeName.StartsWith("Task<", StringComparison.Ordinal)
        && returnTypeName.EndsWith(">", StringComparison.Ordinal)
            ? returnTypeName[5..^1]
            : returnTypeName;

    /// <summary>メソッド名の末尾 <c>Async</c> を除いた操作名（ルートセグメント）を返す</summary>
    private static string TrimAsyncSuffix(string methodName) =>
        methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName[..^5] : methodName;

    /// <summary>パラメータ名（camelCase）をレコードプロパティ名（PascalCase）へ変換する</summary>
    private static string ToPascalCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
