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

        // null が正当な結果である操作（単一戻り形・スカラー戻り形＝内側の型が nullable）は InvokeNullableAsync へ、
        // それ以外は「JSON リテラル null の本文」を転送失敗として分類する InvokeAsync へ振り分ける
        // （契約は C# シグネチャの nullability そのもの）
        var invokeMethod = innerType.EndsWith("?", StringComparison.Ordinal)
            ? "InvokeNullableAsync"
            : "InvokeAsync";

        // ペイロード: パラメータ名そのままの匿名型（VO はエンベロープの JSON 化時に VO コンバータで内包値になる）。
        // パラメータなしは null（本文 "null"。サーバー側もリクエストを読まない）
        var payload =
            shape.PayloadParameters.Count == 0
                ? "null"
                : $"new {{ {string.Join(", ", shape.PayloadParameters.Select(p => p.Name))} }}";

        var call = new StringBuilder()
            .Append(invokeMethod)
            .Append('<')
            .Append(innerType)
            .Append(">(\"")
            .Append(operation)
            .Append("\", ")
            .Append(payload)
            .Append(", cancellationToken)")
            .ToString();

        var builder = AppendDocSummary(new StringBuilder(), shape.Summary);
        AppendMethodHeader(
            builder,
            "public ",
            shape.ReturnTypeName,
            shape.MethodName,
            shape.ParameterList
        );

        // ページング引数を持つクエリは、直結実装と同じ例外で入口から弾く（HTTP を投げない）。
        // 素通しするとサーバーが 400 を返し、クライアントでは RemoteRepositoryException になる＝
        // 実装を差し替えると catch の型が変わってしまう（null 引数の ThrowIfNull と同じ規則）
        if (shape.PayloadParameters.Any(p => p.IsPaging))
        {
            builder
                .Append("\n    {\n")
                .Append(PagingGuardBlock)
                .Append("        return ")
                .Append(call)
                .Append(";\n    }");
        }
        else
        {
            builder.Append(" =>\n        ").Append(call).Append(';');
        }

        return builder.ToString();
    }

    /// <summary>
    /// ページング引数の入口検証ブロック（HTTP クライアント転送メソッドの先頭）。
    /// </summary>
    /// <remarks>
    /// 検証の順序・例外型・パラメータ名・文言は、直結実装が通る <c>SqlQuery.Skip</c> / <c>Take</c> と完全に一致させる
    /// （直結は <c>.Skip(skip).Take(take)</c> の順に呼ぶため skip が先。パラメータ名が <c>count</c> なのは
    /// <c>Skip(int count)</c> / <c>Take(int count)</c> の引数名そのもので、呼び出し側の catch フィルタが
    /// 実装の差し替えで挙動を変えないための写し）。文言の一致は実 HTTP のパリティテストが固定する。
    /// </remarks>
    private const string PagingGuardBlock = """
                // The paging arguments are rejected here with the exception a direct implementation raises, so that
                // swapping a direct repository for this client changes nothing a caller catches. Order, parameter name
                // and wording follow SqlQuery.Skip / Take, which the direct path calls as .Skip(skip).Take(take).
                if (skip < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        "count",
                        "The number of rows to skip must not be negative."
                    );
                }

                if (take <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        "count",
                        "The number of rows to fetch must be greater than zero."
                    );
                }


        """;

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
            .Append("                RemoteServerEngine.ExecuteAsync(\n")
            .Append("                    context,\n")
            .Append("                    async () =>\n")
            .Append("                    {\n");

        // 引数の復元（パラメータなしのクエリは本文を読まない）
        var arguments = new List<string>();

        if (shape.PayloadParameters.Count > 0)
        {
            builder
                .Append(
                    "                        var request = await RemoteServerEngine.ReadRequestAsync<"
                )
                .Append(RequestRecordName(shape, repositoryName))
                .Append(">(context).ConfigureAwait(false);\n");

            // ページング引数（take / skip）は ValidatedPaging を通してから渡す: クエリパイプラインが拒否する値
            // （take <= 0 / skip < 0）を素通しすると、クライアントが送った値の不備がサーバー側の未処理例外＝
            // 500（ログ＋OnServerError フック）に化ける。2 つは常に対で出るためまとめて 1 回検証する
            if (shape.PayloadParameters.Any(p => p.IsPaging))
            {
                builder.Append(
                    "                        var paging = RemoteServerEngine.ValidatedPaging(request.Take, request.Skip);\n"
                );
            }

            // 参照型のフィールドは Required で包む: エンベロープは positional record なので "{}" のような
            // 欠落ボディが既定の null のまま通り、リポジトリ奥の null 引数例外＝500 に化ける（クライアント起因の
            // 誤りは 400 が正）。値型は省略形を持たないためそのまま渡す（CRUD 側のエンベロープと同じ規則）
            arguments.AddRange(
                shape.PayloadParameters.Select(p =>
                {
                    var name = ToPascalCase(p.Name);

                    if (p.IsPaging)
                    {
                        return $"paging.{name}";
                    }

                    var member = $"request.{name}";
                    return p.IsReferenceType
                        ? $"RemoteServerEngine.Required({member}, \"{name}\")"
                        : member;
                })
            );
        }

        arguments.Add("context.RequestAborted");

        builder
            .Append("                        var repository = RemoteServerEngine.Repository<")
            .Append(remoteInterfaceName)
            .Append(">(context);\n")
            .Append("                        return (object?)await repository.")
            .Append(shape.MethodName)
            .Append('(')
            .Append(string.Join(", ", arguments))
            .Append(").ConfigureAwait(false);\n")
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

        return $"    /// <summary>Request body for {TrimAsyncSuffix(shape.MethodName)} ({repositoryName}).</summary>\n"
            + $"    private sealed record {RequestRecordName(shape, repositoryName)}({properties});";
    }

    /// <summary>サーバー側リクエストレコード名（例 <c>Order_GetByCustomerRequest</c>）を返す</summary>
    /// <remarks>
    /// リポジトリ名と操作名の間に <c>_</c> を挟む。素の連結だとエンティティをまたいで衝突し得る
    /// （<c>Order</c>×<c>LineSummary</c> と <c>OrderLine</c>×<c>Summary</c> が同名 CS0102）。
    /// private record なのでワイヤ・公開 API 面には現れず、区切りは無条件（衝突時だけ別名にする方式は
    /// 他エンティティの有無で名前が揺れ、出力の決定性を壊す）。
    /// </remarks>
    private static string RequestRecordName(QueryMethodShape shape, string repositoryName) =>
        $"{repositoryName}_{TrimAsyncSuffix(shape.MethodName)}Request";

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
