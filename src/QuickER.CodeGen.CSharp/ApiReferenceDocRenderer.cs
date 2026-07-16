using System.Text;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成モデルとオプションから、その図のスキーマに即した API リファレンス Markdown（日本語）を描画するレンダラー。
/// </summary>
/// <remarks>
/// <para>
/// テンプレート本文は埋め込みリソース（Templates/ApiReferenceDoc.scriban）として保持し、
/// <see cref="ScribanCSharpRenderer"/> と同じ手順（読込 → <see cref="Template.Parse"/> を static キャッシュ →
/// <see cref="ScriptObject"/> で変数供給 → Render）で描画する。
/// </para>
/// <para>
/// 出力に生成日時・環境依存値などの非決定的要素は一切含めない（後 Stage でバイト一致のドリフト検証を追加するため）。
/// 改行は <see cref="Environment.NewLine"/> へ正規化する。
/// </para>
/// </remarks>
internal sealed class ApiReferenceDocRenderer
{
    /// <summary>API リファレンス Markdown を出力する Scriban テンプレート本文（埋め込みリソース）</summary>
    private static readonly string TemplateText = LoadTemplate();

    /// <summary>埋め込みリソースから Scriban テンプレート本文を読み込む</summary>
    private static string LoadTemplate()
    {
        const string resourceName = "QuickER.CodeGen.CSharp.Templates.ApiReferenceDoc.scriban";
        var assembly = typeof(ApiReferenceDocRenderer).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋め込みリソース '{resourceName}' が見つかりません。{Environment.NewLine}"
                    + $"アセンブリ '{assembly.GetName().Name}' に Templates/ApiReferenceDoc.scriban が "
                    + "EmbeddedResource として含まれているか確認してください。"
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>テンプレートは固定なので一度だけ解析してキャッシュする</summary>
    private static readonly Template ParsedTemplate = ParseTemplate();

    /// <summary>テンプレートを解析し、解析エラーがあれば例外を投げる</summary>
    private static Template ParseTemplate()
    {
        var template = Template.Parse(TemplateText);

        if (template.HasErrors)
        {
            var message = string.Join(
                Environment.NewLine,
                template.Messages.Select(m => m.ToString())
            );

            throw new InvalidOperationException(
                $"API リファレンステンプレートの解析に失敗しました。{Environment.NewLine}{message}"
            );
        }

        return template;
    }

    /// <summary>
    /// 生成モデルとオプションから API リファレンス Markdown 文字列を描画する。
    /// </summary>
    public string Render(CSharpGenerationModel model, CodeGenerationOptions options)
    {
        // 共通契約（Repository 契約・データアクセス API）が生成されるか。QuickER 版 Repository・EF Core・
        // インメモリのいずれかが有効なら契約が出るため、データアクセス節・使い方節を出力する。
        var hasContract =
            options.GenerateRepositories
            || options.GenerateEfCore
            || options.GenerateInMemoryRepositories;

        // 契約が出るときだけ、Repository モデルを Entity クラスへ突き合わせる索引を作る（インターフェイス名・主キー型）。
        var repositoryByEntity = model.RepositoryClasses.ToDictionary(
            repository => repository.EntityClassName,
            repository => repository,
            StringComparer.Ordinal
        );

        var entities = model
            .EntityClasses.Select(entity =>
                BuildEntityView(entity, repositoryByEntity, hasContract)
            )
            .ToList();

        var scriptObject = new ScriptObject
        {
            ["namespace_name"] = model.NamespaceName,
            ["entities"] = entities,
            ["has_contract"] = hasContract,
            // 実際の生成モードに追従した DI 登録例（単一方言 / マルチ方言 keyed / EF Core / インメモリ）。
            ["di_registrations"] = hasContract ? BuildDiRegistrations(options) : new List<object>(),
            // 図の実際の先頭エンティティで具体化した CRUD/クエリ例（契約が出るときのみ）。
            ["example"] = hasContract ? BuildExample(model, repositoryByEntity) : null,
            // パッケージ参照モードのときだけ必要な PackageReference 一覧を載せる（ガイダンスと同一文言）。
            ["package_guidance_lines"] =
                hasContract && options.UseRuntimePackages
                    ? RuntimePackageReferenceGuidance
                        .BuildGuidanceLines(options, RuntimePackages.ResolveGuidanceVersion())
                        .Select(EscapeCell)
                        .ToList()
                    : new List<string>(),
            // 生成ファイル構成（分割・マルチ方言時に特に有用）。プランと同じ結果を表にする。
            ["generated_files"] = BuildGeneratedFileRows(options),
        };

        var context = new TemplateContext { LoopLimit = 0, LimitToString = 0 };
        context.PushGlobal(scriptObject);

        // ScribanCSharpRenderer に倣い、改行を環境改行へ正規化し末尾を単一改行に揃える。
        // さらに 3 行以上連続する空行（条件ブロックのスキップ由来）を 1 空行へ畳む。
        var rendered =
            ParsedTemplate.Render(context).ReplaceLineEndings(Environment.NewLine).TrimEnd()
            + Environment.NewLine;

        return Regex.Replace(
            rendered,
            $"(?:{Regex.Escape(Environment.NewLine)}){{3,}}",
            Environment.NewLine + Environment.NewLine
        );
    }

    /// <summary>1 エンティティ分の表示モデル（プロパティ表・ナビゲーション表・Repository 契約）を組み立てる</summary>
    private static ScriptObject BuildEntityView(
        CSharpClassModel entity,
        IReadOnlyDictionary<string, CSharpRepositoryModel> repositoryByEntity,
        bool hasContract
    )
    {
        var properties = entity
            .Properties.Select(property => new ScriptObject
            {
                ["name"] = property.PropertyName,
                ["type_name"] = property.TypeName,
                ["canonical_type_token"] = EscapeCell(property.CanonicalTypeToken ?? string.Empty),
                ["is_primary_key"] = property.IsPrimaryKey,
                // 非 NULL（参照型なら「必須」の意味）を必須とみなす
                ["is_required"] = !property.IsNullable,
                ["description"] = EscapeCell(property.Description),
            })
            .ToList();

        var navigations = entity
            .Navigations.Select(navigation => new ScriptObject
            {
                ["name"] = navigation.PropertyName,
                // 親参照（FK 先の 1 側）か子コレクション（1 対多の多側）かを日本語で示す
                ["kind"] = navigation.IsParentReference ? "親参照" : "子コレクション",
                ["target"] = navigation.TypeName,
            })
            .ToList();

        var view = new ScriptObject
        {
            ["class_name"] = entity.ClassName,
            ["table_name"] = entity.TableName,
            ["description"] = EscapeCell(entity.Description),
            ["properties"] = properties,
            ["navigations"] = navigations,
        };

        // 契約が出るときは、対応する Repository インターフェイス名・主キー型を載せる（対応がなければ空）
        if (hasContract && repositoryByEntity.TryGetValue(entity.ClassName, out var repository))
        {
            view["repository_interface"] = repository.InterfaceName;
            view["key_type_name"] = repository.KeyTypeName;
        }
        else
        {
            view["repository_interface"] = string.Empty;
            view["key_type_name"] = string.Empty;
        }

        return view;
    }

    /// <summary>
    /// 実際の生成モードに追従した DI 登録例（説明＋コード）の一覧を組み立てる。
    /// </summary>
    /// <remarks>
    /// 出し分けはテンプレート <c>Templates/CSharpRuntime.scriban</c> の DI 登録リージョンと拡張メソッド名を一致させる:
    /// QuickER 版 Repository → エンジン別 <c>AddGeneratedSqlServerRepositories</c> / <c>AddGeneratedSqliteRepositories</c>
    /// （単一方言・マルチ方言とも同名。マルチ方言は keyed 版あり）、EF Core → <c>AddGeneratedEfCoreRepositories</c>、
    /// インメモリ → <c>AddGeneratedInMemoryRepositories</c>。複数モードが同時に有効なら該当ぶんをすべて載せる。
    /// </remarks>
    private static List<ScriptObject> BuildDiRegistrations(CodeGenerationOptions options)
    {
        var registrations = new List<ScriptObject>();

        if (options.GenerateRepositories)
        {
            IReadOnlyList<string> dialects;

            try
            {
                dialects = options.EffectiveRepositoryDialects;
            }
            catch (ArgumentException)
            {
                dialects = ["sqlserver"];
            }

            // QuickER 版 Repository の DI 登録はエンジン別（AddGenerated{方言}Repositories）で統一。
            // 単一方言でも方言別名を使い、マルチ方言（実効方言 2 つ以上）では keyed 解決の例も添える。
            foreach (var dialect in dialects)
            {
                var suffix = GeneratedFilePlanner.DialectNamespaceSuffix(dialect);
                registrations.Add(
                    Registration(
                        $"QuickER 版 Repository（{suffix}）を DI コンテナへ登録します。",
                        $"services.AddGenerated{suffix}Repositories(connectionString);"
                    )
                );
            }

            if (dialects.Count >= 2)
            {
                registrations.Add(
                    Registration(
                        "複数方言を同時に利用する場合は keyed DI を使い、`[FromKeyedServices(...)]` で方言別の実装を解決します。",
                        string.Join(
                            Environment.NewLine,
                            dialects.Select(dialect =>
                            {
                                var suffix = GeneratedFilePlanner.DialectNamespaceSuffix(dialect);
                                return $"services.AddGenerated{suffix}Repositories(\"{suffix.ToLowerInvariant()}\", connectionString);";
                            })
                        )
                    )
                );
            }
        }

        if (options.GenerateEfCore)
        {
            registrations.Add(
                Registration(
                    "EF Core 版 Repository を DI コンテナへ登録します（方言・接続文字列はアプリ側で構成します）。",
                    "services.AddGeneratedEfCoreRepositories(options => options.UseSqlServer(connectionString));"
                )
            );
        }

        if (options.GenerateInMemoryRepositories)
        {
            registrations.Add(
                Registration(
                    "インメモリ Repository を DI コンテナへ登録します（プロトタイピング・テスト向け）。",
                    "services.AddGeneratedInMemoryRepositories();"
                )
            );
        }

        return registrations;
    }

    /// <summary>DI 登録例（説明＋コード）の 1 項目を作る</summary>
    private static ScriptObject Registration(string description, string code) =>
        new() { ["description"] = description, ["code"] = code };

    /// <summary>
    /// 図の先頭エンティティで CRUD/クエリ例を具体化する。Repository モデルがない（対応 Repository なし）場合は null を返す。
    /// </summary>
    private static ScriptObject? BuildExample(
        CSharpGenerationModel model,
        IReadOnlyDictionary<string, CSharpRepositoryModel> repositoryByEntity
    )
    {
        var firstEntity = model.EntityClasses.FirstOrDefault();

        if (firstEntity is null)
        {
            return null;
        }

        // 例に使う主キー型・並べ替えプロパティを図から取る。Repository が対応していれば主キー型を優先する。
        var keyProperty = firstEntity.Properties.FirstOrDefault(property => property.IsPrimaryKey);
        var keyTypeName = repositoryByEntity.TryGetValue(firstEntity.ClassName, out var repository)
            ? repository.KeyTypeName
            : keyProperty?.TypeName ?? "int";

        // 並べ替えに使う代表プロパティ（主キーがあれば主キー、なければ先頭プロパティ）
        var orderProperty =
            keyProperty?.PropertyName
            ?? firstEntity.Properties.FirstOrDefault()?.PropertyName
            ?? "Id";

        return new ScriptObject
        {
            ["entity_class"] = firstEntity.ClassName,
            ["key_type_name"] = keyTypeName,
            // 実装差し替えを避けるためローカル変数名は Repository インターフェイス由来ではなく汎用の "repository"
            ["repository_field"] = "repository",
            ["order_property"] = orderProperty,
            ["sample_key_literal"] = SampleKeyLiteral(keyTypeName),
        };
    }

    /// <summary>主キー型に応じた例示リテラル（`int` → 1、`string` → "..."、`Guid` → Guid.Empty 等）を返す</summary>
    private static string SampleKeyLiteral(string keyTypeName)
    {
        var normalized = keyTypeName.TrimEnd('?');

        return normalized switch
        {
            "string" => "\"...\"",
            "Guid" => "Guid.Empty",
            "long" => "1L",
            _ => "1",
        };
    }

    /// <summary>生成ファイル構成の表行（ファイル名・名前空間・含むバケット）を組み立てる</summary>
    private static List<ScriptObject> BuildGeneratedFileRows(CodeGenerationOptions options)
    {
        // Plan はプレビューでも呼ばれ例外を投げない（未対応方言は sqlserver 相当へフォールバック）。
        // 同一 OutputFileName のスペックは 1 ファイルへ連結されるため、ファイル名でまとめて重複を除く。
        var rows = new List<ScriptObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var spec in GeneratedFilePlanner.Plan(options))
        {
            if (!seen.Add(spec.FileName))
            {
                continue;
            }

            rows.Add(
                new ScriptObject
                {
                    ["file_name"] = spec.FileName,
                    ["namespace_name"] = spec.NamespaceName,
                    ["buckets"] = EscapeCell(
                        string.Join(" / ", spec.Buckets.Select(bucket => bucket.ToString()))
                    ),
                }
            );
        }

        return rows;
    }

    /// <summary>
    /// Markdown 表のセルへ入れる値を安全化する（<c>|</c> をエスケープし、改行を空白へ潰す）。
    /// </summary>
    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // 改行（CRLF / LF）を半角空白へ、パイプをエスケープする（表の列崩れを防ぐ）
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim();
    }
}
