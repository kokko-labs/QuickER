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

    /// <summary>
    /// 保存形式としては妥当だが、エンティティ Id または列 Id が重複しており図として扱えなかった
    /// </summary>
    /// <remarks>
    /// 重複箇所（テーブル名・列名・重複した Id）は <c>exception</c> のメッセージが名指しする。
    /// 手で直す以外に復旧手段が無いため、表示側は必ずその内容まで見せること。
    /// </remarks>
    DuplicateId,
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
    /// <returns>
    /// 実際に書き出した JSON 文字列。保存直後の内容ハッシュを<b>ディスクの読み直しではなく
    /// 書いた内容そのもの</b>から求めるために返す（GUI の外部変更検知が使う。読み直す方式では、
    /// 書き込み完了からハッシュ採取までの隙間に外部プロセスが書いた内容を「自分が保存した内容」として
    /// 記録してしまう）。戻り値は無視してよい。
    /// </returns>
    public static string SaveAtomic(string path, DiagramDocument document)
    {
        var contents = Serialize(document);
        AtomicFile.WriteAllText(path, contents);
        return contents;
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
    /// 検証は「読み取り → JSON 解析 → ルートが <c>Version</c>・<c>Schema</c> を持つ JSON オブジェクトか
    /// → 逆直列化 → 版番号 → Id の重複」の順で、<see cref="JsonStorageService"/> の読込仕様に合わせ
    /// キー名の大文字小文字は区別する。無関係な JSON（例 <c>package.json</c>）を「空図」として読み込み、
    /// 誤解釈・上書きするのを防ぐ。
    /// <para>
    /// フォーマット版の判定（<see cref="DiagramDocument.IsNewerFormat"/>）は含まない。新フォーマットを
    /// 拒否するか警告して続行するかは経路ごとに異なるため、読み込んだ文書を見て呼び出し側が決める。
    /// </para>
    /// <para>
    /// <b>逆直列化とルートキーの評価も try の内側で行う。</b>JSON のキー重複は
    /// <see cref="JsonNode"/> の遅延評価のため <see cref="JsonNode.Parse(string, JsonNodeOptions?, JsonDocumentOptions)"/>
    /// では出ず、ルートオブジェクトのキーへ最初に触れた時点で <see cref="ArgumentException"/> として現れる。
    /// プロパティの型不一致（<see cref="JsonException"/>）も含め、これらを外へ漏らすと呼び出し側は
    /// 種別を持てず「読めなかった」以上のことを言えなくなる。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <param name="document">読み込んだ文書（失敗時は <c>null</c>）</param>
    /// <param name="error">失敗の種別（成功時は <see cref="DocumentLoadError.None"/>）</param>
    /// <param name="exception">
    /// 失敗の原因となった例外。<see cref="DocumentLoadError.ReadFailed"/>・
    /// <see cref="DocumentLoadError.InvalidJson"/>・<see cref="DocumentLoadError.DuplicateId"/>
    /// のときだけ非 null で、形式検証で弾いた場合と成功時は null。
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

        DiagramDocument loaded;

        try
        {
            var root = JsonNode.Parse(json);

            // ルートキーへ最初に触れるのはここ（キー重複の ArgumentException が出るのもここ）
            if (
                root is not JsonObject obj
                || obj["Version"] is null
                || obj["Schema"] is not JsonObject
            )
            {
                error = DocumentLoadError.NotDiagramDocument;
                return false;
            }

            loaded = Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            error = DocumentLoadError.InvalidJson;
            exception = ex;
            return false;
        }

        // 版番号は 1 始まり。0・負数を黙って受理すると、Version キーを持つだけの無関係な JSON が
        // 「この版より古い文書」として通り、保存で図ファイルへ化ける
        if (loaded.Version < 1)
        {
            error = DocumentLoadError.NotDiagramDocument;
            return false;
        }

        // Id の重複は修復せず拒否する（理由は FindDuplicateId）
        if (FindDuplicateId(loaded.Schema) is { } duplicate)
        {
            error = DocumentLoadError.DuplicateId;
            exception = new InvalidDataException(duplicate);
            return false;
        }

        document = loaded;
        return true;
    }

    /// <summary>エンティティ Id・列 Id の重複を探し、最初に見つかった重複の説明を返す（無ければ null）</summary>
    /// <remarks>
    /// <para>
    /// <b>修復はしない。</b>重複 Id を持つ図ファイルを作る経路はアプリ側に無く（GUI・MCP ツール・
    /// 各種取込はいずれも新しい Guid を採る）、起こるのは JSON の手編集だけなので、Id を勝手に
    /// 張り替えて意味を推測するより、どこが重複しているかを名指しして読込を断るほうが直せる。
    /// </para>
    /// <para>
    /// 検査対象はエンティティ Id と列 Id の 2 つだけ（列 Id は同一テーブル内とテーブル間の双方を見る）。
    /// この 2 つは辞書のキーにされ、リレーション・一意制約・主キー順・クエリからの参照先にもなるため、
    /// 重複したまま読み込むと上書き保存（レイアウト辞書の構築）が毎回失敗し、例外を握り潰す自動保存も
    /// 一度も成功しない＝作業内容を保存する手段が両方とも消える。リレーション・一意制約・クエリ自身の
    /// Id は参照も辞書化もされないため検査しない。
    /// </para>
    /// <para>
    /// 説明文は例外メッセージと同じ扱いで英語固定とし、利用者向けの見出しは表示側（GUI の resx・
    /// CLI / MCP の応答文言）が付ける。
    /// </para>
    /// </remarks>
    private static string? FindDuplicateId(ErDiagram schema)
    {
        var tableByEntityId = new Dictionary<Guid, string>();
        var ownerByColumnId = new Dictionary<Guid, (string Table, string Column)>();

        foreach (var entity in schema.Entities)
        {
            if (tableByEntityId.TryGetValue(entity.Id, out var existingTable))
            {
                return $"Duplicate entity Id '{entity.Id}' is used by table '{existingTable}' and table '{entity.TableName}'.";
            }

            tableByEntityId.Add(entity.Id, entity.TableName);

            var columnsInEntity = new Dictionary<Guid, string>();

            foreach (var column in entity.Columns)
            {
                // 同一テーブル内の重複（テーブル間の検査とは別に見る＝報告する情報が違う）
                if (columnsInEntity.TryGetValue(column.Id, out var siblingColumn))
                {
                    return $"Duplicate column Id '{column.Id}' in table '{entity.TableName}' is used by column '{siblingColumn}' and column '{column.Name}'.";
                }

                columnsInEntity.Add(column.Id, column.Name);

                // テーブルをまたぐ重複（列は図の全体で一意でなければ参照の引き当てが割れる）
                if (ownerByColumnId.TryGetValue(column.Id, out var owner))
                {
                    return $"Duplicate column Id '{column.Id}' is used by column '{owner.Column}' of table '{owner.Table}' and column '{column.Name}' of table '{entity.TableName}'.";
                }

                ownerByColumnId.Add(column.Id, (entity.TableName, column.Name));
            }
        }

        return null;
    }

    /// <summary>ファイルの保存フォーマット版（ルートの <c>Version</c>）だけを読み取る</summary>
    /// <remarks>
    /// <b>意図的に軽い。</b>ルートを <see cref="JsonNode"/> として読んで <c>Version</c> を見るだけで、
    /// 逆直列化も Id の重複検査も通さない。用途は「これから上書きするファイルが、この版で書き戻すと
    /// 情報を落とす相手か」を保存のたびに確かめること（<see cref="DiagramDocument.IsNewerFormat"/> と
    /// 同じ判定を、文書を読み込まずに行う）で、保存経路へ重い検証を持ち込むためのものではない。
    /// <para>
    /// 戻り値は 3 状態を区別する。<c>false</c>＝版を名乗っていない（ファイル不在・読み取り失敗・
    /// JSON でない・<c>Version</c> キーが無い）。<c>true</c> かつ <paramref name="version"/> が非 null＝
    /// その版。<c>true</c> かつ <c>null</c>＝<b>版を名乗っているのに解釈できない</b>
    /// （<c>"2"</c>・<c>2.0</c>・<see cref="int"/> に収まらない値。この版が書く形ではないので、
    /// 別の版か手編集のファイル）。
    /// </para>
    /// <para>
    /// 解釈できない版を「将来版ではない」と読むと、保存先が現在の文書でない場合（「名前を付けて保存」で
    /// 既存ファイルを選んだ場合）に内容ハッシュ照合の網も掛からず、確認なしで上書きしてしまう。
    /// 呼び出し側は安全側＝確認する側へ倒すこと。
    /// </para>
    /// </remarks>
    /// <param name="path">読み取るファイルパス</param>
    /// <param name="version">読み取った保存フォーマット版（版を名乗っているが解釈できない場合は <c>null</c>）</param>
    /// <returns>ルートが <c>Version</c> キーを持っていた場合は <c>true</c></returns>
    public static bool TryReadFormatVersion(string path, out int? version)
    {
        version = null;

        try
        {
            if (
                JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root
                || root["Version"] is not JsonValue value
            )
            {
                return false;
            }

            // 版を名乗ってはいるので true。int として読めたときだけ値を持ち帰る
            if (value.TryGetValue(out int parsed))
            {
                version = parsed;
            }

            return true;
        }
        catch
        {
            // 版が読めないことは呼び出し側の主処理（保存）を妨げない
            return false;
        }
    }

    /// <summary>ディスク上のファイルを、この版の形式で書き戻すと情報を落とす相手として扱うか</summary>
    /// <remarks>
    /// 図 JSON を既存ファイルへ書き出す全経路（GUI の上書き保存・Schema JSON エクスポート・CLI の
    /// <c>reverse</c>）が共有する判定。版を名乗っていないファイル（不在・破損・JSON でない・
    /// <c>Version</c> キーなし）は対象外。版を名乗っているのに解釈できない値（<c>"2"</c>・<c>2.0</c> など、
    /// この版が書かない形）は<b>対象に含める</b>＝別の版か手編集のファイルであり、黙って現行形式で書き潰さない。
    /// </remarks>
    /// <param name="path">これから上書きするファイルパス</param>
    /// <returns>確認なしに上書きしてはいけない場合は <c>true</c></returns>
    public static bool IsNewerFormatFile(string path) =>
        TryReadFormatVersion(path, out var version)
        && (version is null || version > DiagramDocument.CurrentVersion);

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

            // 主キーの順序も値型リストのため、リスト自体の null だけ既定値（＝列宣言順）へ寄せる
            entity.PrimaryKeyColumnIds ??= new List<Guid>();

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
