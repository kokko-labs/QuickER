using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using QuickER.Model;
using QuickER.Settings;

namespace QuickER.Documents;

/// <summary><see cref="JsonStorageService.TryLoad"/> が読込を断念した理由の種別</summary>
/// <remarks>
/// 表示文言は持たない。読込経路ごとに要求が異なる（MCP／CLI のツールは英語固定・GUI と CLI 本体は
/// ローカライズ）ため、種別から文言への変換は呼び出し側の責務とする。
/// </remarks>
public enum DocumentLoadError
{
    /// <summary>失敗していない（読込に成功した）</summary>
    None,

    /// <summary>ファイルを読み取れなかった（不在・アクセス不可などの IO エラー）</summary>
    ReadFailed,

    /// <summary>内容を JSON として解析できなかった</summary>
    InvalidJson,

    /// <summary>JSON としては妥当だが、ER 図の保存形式（<c>Version</c>・<c>Schema</c>）ではなかった</summary>
    NotDiagramDocument,
}

/// <summary>ER 図を JSON ファイルへ保存・読み込みするトップレベルサービス</summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を用い、WPF 型（Brush など）を含まない保存文書
/// （<see cref="DiagramDocument"/>: 意味モデル schema ＋ 視覚情報 layout）をシリアライズする
/// </remarks>
public static class JsonStorageService
{
    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力・null プロパティは省略）</summary>
    /// <remarks>
    /// null の省略（<see cref="JsonIgnoreCondition.WhenWritingNull"/>）は「値なし」をキーごと出さない
    /// 図ファイルの正準形。読み込み側はキー欠落をプロパティ既定値で吸収するため相互に可換で、
    /// 古い形式（null を明記した図ファイル）もそのまま読める（<see cref="Normalize"/> が
    /// 非 null 契約のプロパティに書かれた null を既定値へ修復する）。
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>保存文書をファイルへ単純に書き出す（JSON は <c>{ version, schema, layout }</c> 形式）</summary>
    /// <remarks>
    /// 既存ファイルを切り詰めてから書くため、書き込み途中の中断で保存先が破損し得る。
    /// プロダクションのファイル書き出しはすべて <see cref="SaveAtomic"/> を使うこと
    /// （本メソッドはテストのフィクスチャ書き出し等、破損しても影響のない用途に残している）。
    /// </remarks>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void Save(string path, DiagramDocument document)
    {
        File.WriteAllText(path, Serialize(document));
    }

    /// <summary>保存文書をアトミックに（書き込み途中の中断で既存ファイルを壊さずに）保存する</summary>
    /// <remarks>
    /// 素の <see cref="Save"/>（<see cref="File.WriteAllText(string, string?)"/>）は既存ファイルを
    /// 切り詰めてから書くため、途中でプロセスが落ちる・ディスクが満杯になるとユーザーの図ファイルが
    /// 破損した JSON として残る。これを防ぐため、書き込みは <see cref="AtomicFile.WriteAllText"/>
    /// （一時ファイルへ全量を書き切ってから本体へ差し替える。アルゴリズムの詳細はそちらを参照）へ委譲する。
    /// <b>プロダクションのファイル書き出し（GUI の上書き／別名保存・スキーマのみ JSON のエクスポート・
    /// MCP のツール実行・CLI のリバース出力・クラッシュ時の緊急保存）はすべてこちらを使う。</b>
    /// </remarks>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void SaveAtomic(string path, DiagramDocument document)
    {
        AtomicFile.WriteAllText(path, Serialize(document));
    }

    /// <summary>保存文書を図ファイルの正準形（<see cref="Options"/>）で JSON 文字列へ直列化する</summary>
    /// <remarks><see cref="Save"/> と <see cref="SaveAtomic"/> で出力を完全に一致させるための共有ヘルパ</remarks>
    private static string Serialize(DiagramDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>ファイルから保存文書を読み込み、非 null 契約を満たすよう正規化して返す</summary>
    /// <remarks>
    /// <b>図の読込経路（GUI の開く／外部変更の再読込／自動保存の復元・MCP のツール実行・CLI）は
    /// すべてこのメソッドを通るため、null 正規化はここ 1 箇所に集約する。</b>
    /// <para>
    /// 本メソッドは欠落キーを既定値で補うため、ER 図と無関係な JSON も「空図」として読める。
    /// ユーザーが指すファイルを読む経路（＝無関係な JSON を渡され得る経路）は、形式検証込みの
    /// <see cref="TryLoad"/> を使うこと。素の <see cref="Load"/> は「自分が書いたファイルを読み戻す」
    /// 用途（自動保存の復元・テストのフィクスチャ）に用いる。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <returns>読み込んだ <see cref="DiagramDocument"/>（内容が空の場合は新規インスタンス）</returns>
    public static DiagramDocument Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>ER 図の保存形式として妥当か検証したうえでファイルから保存文書を読み込む</summary>
    /// <remarks>
    /// 検証は「読み取り → JSON 解析 → ルートが <c>Version</c>・<c>Schema</c> を持つ JSON オブジェクトか」の
    /// 3 段で、<see cref="JsonStorageService"/> の読込仕様に合わせキー名の大文字小文字は区別する。
    /// 無関係な JSON（例 <c>package.json</c>）を「空図」として読み込み、誤解釈・上書きするのを防ぐ。
    /// <para>
    /// フォーマット版の判定（<see cref="DiagramDocument.IsNewerFormat"/>）は含まない。新フォーマットを
    /// 拒否するか警告して続行するかは経路ごとに異なるため、読み込んだ文書を見て呼び出し側が決める。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <param name="document">読み込んだ文書（失敗時は <c>null</c>）</param>
    /// <param name="error">失敗の種別（成功時は <see cref="DocumentLoadError.None"/>）</param>
    /// <param name="exception">
    /// 失敗の原因となった例外。<see cref="DocumentLoadError.ReadFailed"/>・
    /// <see cref="DocumentLoadError.InvalidJson"/> のときだけ非 null で、形式検証で弾いた場合と成功時は null。
    /// </param>
    /// <returns>読み込めた場合は <c>true</c></returns>
    public static bool TryLoad(
        string path,
        out DiagramDocument? document,
        out DocumentLoadError error,
        out Exception? exception
    )
    {
        document = null;
        error = DocumentLoadError.None;
        exception = null;

        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = DocumentLoadError.ReadFailed;
            exception = ex;
            return false;
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            error = DocumentLoadError.InvalidJson;
            exception = ex;
            return false;
        }

        if (root is not JsonObject obj || obj["Version"] is null || obj["Schema"] is not JsonObject)
        {
            error = DocumentLoadError.NotDiagramDocument;
            return false;
        }

        document = Deserialize(json);
        return true;
    }

    /// <summary>JSON 文字列を保存文書へ逆直列化し、非 null 契約を満たすよう正規化する</summary>
    /// <remarks><see cref="Load"/> と <see cref="TryLoad"/> で読込結果を完全に一致させるための共有ヘルパ</remarks>
    private static DiagramDocument Deserialize(string json) =>
        Normalize(
            JsonSerializer.Deserialize<DiagramDocument>(json, Options) ?? new DiagramDocument()
        );

    /// <summary>読み込んだ保存文書のコレクション・必須値を、モデルの非 null 契約に合わせて修復する</summary>
    /// <remarks>
    /// モデル側のコレクション・必須文字列は「通常 setter ＋初期化子」で宣言されているため、JSON に
    /// 明示的な <c>null</c>（例 <c>"Entities": null</c>）が書かれているとデシリアライザが初期値を
    /// <c>null</c> で上書きし、以降の <c>Count</c> 参照などが <see cref="NullReferenceException"/> になる。
    /// 手書き・外部ツール生成の図ファイルでも起こり得るため、読込時に既定値へ寄せて修復する。
    /// <para>
    /// 方針は「修復であって拒否ではない」。<c>System.Text.Json</c> の
    /// <c>RespectNullableAnnotations</c> による例外化は、キー欠落・古い形式もそのまま読める
    /// という <see cref="Options"/> の互換契約を壊すため採らない。図として妥当かどうか
    /// （無関係な JSON でないか）の判定は、修復前段の形式検証（<see cref="TryLoad"/>）が担う。
    /// </para>
    /// </remarks>
    private static DiagramDocument Normalize(DiagramDocument document)
    {
        document.Schema ??= new ErDiagram();

        var schema = document.Schema;

        // 対象 DBMS は方言プロバイダ解決の起点で null を許さない（既定値は ErDiagram の初期値を正とする）
        if (schema.TargetDbms is null)
        {
            schema.TargetDbms = new ErDiagram().TargetDbms;
        }

        schema.Entities = Compact(schema.Entities);
        schema.Relationships = Compact(schema.Relationships);
        schema.Queries = Compact(schema.Queries);

        // 列ペアはリスト自体・要素の双方が null になりうる（旧形式・手書き JSON）ため掃除する
        foreach (var relationship in schema.Relationships)
        {
            relationship.ColumnPairs = Compact(relationship.ColumnPairs);
        }

        foreach (var entity in schema.Entities)
        {
            entity.Columns = Compact(entity.Columns);
            entity.UniqueConstraints = Compact(entity.UniqueConstraints);

            // ColumnIds は値型リストのため要素の null を持てない。リスト自体の null だけ既定値へ寄せる
            foreach (var constraint in entity.UniqueConstraints)
            {
                constraint.ColumnIds ??= new List<Guid>();
            }
        }

        foreach (var query in schema.Queries)
        {
            query.Parameters = Compact(query.Parameters);
            query.OrderBy = Compact(query.OrderBy);
            query.Fields = Compact(query.Fields);
            query.Sql = Compact(query.Sql);
        }

        // layout の null は「スキーマのみ文書」の正当な表現なのでそのまま残し、
        // 辞書の値だけを掃除する（値が null だとエンティティ生成時に落ちる）
        if (document.Layout is not null)
        {
            document.Layout = Compact(document.Layout);
        }

        return document;
    }

    /// <summary>リストの null（リスト自体・要素の双方）を取り除いた新しいリストを返す</summary>
    private static List<T> Compact<T>(List<T>? items)
        where T : class =>
        items is null ? new List<T>() : items.Where(item => item is not null).ToList();

    /// <summary>辞書の null（辞書自体・値の双方）を取り除いた新しい辞書を返す</summary>
    private static Dictionary<TKey, TValue> Compact<TKey, TValue>(Dictionary<TKey, TValue>? entries)
        where TKey : notnull
        where TValue : class =>
        entries is null
            ? new Dictionary<TKey, TValue>()
            : entries
                .Where(entry => entry.Value is not null)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
}
