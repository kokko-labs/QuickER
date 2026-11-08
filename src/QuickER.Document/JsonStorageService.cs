using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using QuickER.Model;
using QuickER.Platform;

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
    /// 保存形式としては妥当だが、エンティティ Id・列 Id・リレーション Id のいずれかが重複しており図として扱えなかった
    /// </summary>
    /// <remarks>
    /// 重複箇所（テーブル名・列名・リレーションの名前・重複した Id）は <c>exception</c> のメッセージが名指しする。
    /// 手で直す以外に復旧手段が無いため、表示側は必ずその内容まで見せること。
    /// </remarks>
    DuplicateId,

    /// <summary>
    /// 保存形式としては妥当だが、名前・型のような<b>推測で埋められない必須の文字列</b>と、ほかから引かれる
    /// 表・列の Id のどれかで、キーが無いか、明示的な <c>null</c>（Id は空の Guid）が書かれており図として扱えなかった
    /// </summary>
    /// <remarks>
    /// 該当箇所（テーブル名・列名・プロパティ名）は <c>exception</c> のメッセージが名指しする。
    /// <see cref="DuplicateId"/> と同じく手で直す以外に復旧手段が無いため、表示側は必ずその内容まで見せること。
    /// 名前は歴史的な経緯で「Text」だが、表・列の Id の欠落も同じ種類で返す（どちらも「どの版も必ず書く必須の値」）。
    /// </remarks>
    MissingRequiredText,

    /// <summary>
    /// 保存形式としては妥当だが、リレーションの両端・列ペア・一意制約の構成列のどれかが、図に無い表・列を指している
    /// （参照の Guid のキーが無い＝空の Guid も含む）ため図として扱えなかった
    /// </summary>
    /// <remarks>
    /// 該当箇所（リレーション・列ペア・一意制約と、指していた Id）は <c>exception</c> のメッセージが名指しする。
    /// 普通の操作ではこれらの参照は切れない（表の削除はリレーションを、列の削除は列ペアと一意制約を一緒に消す）ので、
    /// 起こるのは JSON の手編集だけ。そのまま開くと GUI がリレーション・列ペアを黙って読み飛ばし、上書き保存で消える。
    /// 表示側は必ずその内容まで見せること。
    /// </remarks>
    BrokenReference,
}

/// <summary>ER 図を JSON ファイルへ保存・読み込みするトップレベルサービス</summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を用い、WPF 型（Brush など）を含まない保存文書
/// （<see cref="DiagramDocument"/>: 意味モデル schema ＋ 視覚情報 layout）をシリアライズする
/// </remarks>
public static class JsonStorageService
{
    /// <summary>
    /// 逆直列化で、推測で埋められない必須の値を空（文字列は <c>null</c>・Id は空の Guid）にしたインスタンスを作る型ごとの作り方
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は <see cref="FindMissingRequiredValue"/> が見る 8 つ＝テーブル名・列名・列の型・クエリ名・
    /// パラメータ名・射影フィールド名の 6 つの文字列と、表・列の Id で、型は 5 つ。モデルの初期値（<c>NewTable</c>・<c>int</c>・
    /// 新しい Guid など）は GUI・ツールの新規作成のための値なので、キーの無い図ファイルにそれを当てると、壊れた図が
    /// それらしい名前・型のまま読めてしまい、上書き保存で元の内容が戻らなくなる。表・列の Id はレイアウト・リレーション・
    /// 列ペア・一意制約・主キーの順序・クエリから引かれるので、新しい Guid で読むとそれらが黙って切れる。
    /// ここで空から始めれば、キーが無いときも明示の <c>null</c>（空の Guid）と同じ値が残り、
    /// <see cref="TryLoad"/> の <see cref="FindMissingRequiredValue"/> が同じ分岐で場所を名指しして断る
    /// （検査の置き場は 1 箇所のまま・修復しない）。
    /// </para>
    /// <para>
    /// 差し替えるのは作り方だけで、ほかのプロパティの初期値（コレクション・説明など）は
    /// コンストラクタのとおり。直列化はこの作り方を使わないので、書き出す内容は変わらない。
    /// 0.1.0 以降の版はこれらのキーを常に書く（初期値が非 null・Id は値型で、<see cref="JsonIgnoreCondition.WhenWritingNull"/>
    /// で消えるのはメモリ上が <c>null</c> のときだけ）ので、キーが無いのは手編集の図だけ。
    /// </para>
    /// <para>
    /// リレーション・一意制約・クエリの Id は対象にしない（図の中で誰も引かないので、キーが無ければ新しい Guid で読んでも
    /// 失うものが無い。手書きの図を不要に断らない）。参照の Guid（リレーションの両端・列ペア・クエリの参照先）は
    /// 初期化子が無いので、キーが無ければ元から空の Guid になる（扱いは <see cref="FindBrokenReference"/>）。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<Type, Func<object>> CreateWithRequiredValuesUnset = new()
    {
        [typeof(Entity)] = () => new Entity { Id = Guid.Empty, TableName = null! },
        [typeof(Column)] = () =>
            new Column
            {
                Id = Guid.Empty,
                Name = null!,
                DataType = null!,
            },
        [typeof(QueryDefinition)] = () => new QueryDefinition { Name = null! },
        [typeof(QueryParameter)] = () => new QueryParameter { Name = null! },
        [typeof(ProjectionField)] = () => new ProjectionField { Name = null! },
    };

    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力・null プロパティは省略）</summary>
    /// <remarks>
    /// null の省略（<see cref="JsonIgnoreCondition.WhenWritingNull"/>）は「値なし」をキーごと出さない
    /// 図ファイルの正準形。読み込み側は、<b>推測で埋められない必須の値を除き</b>、キー欠落を
    /// プロパティ既定値で吸収するため相互に可換で、古い形式（null を明記した図ファイル）もそのまま読める
    /// （<see cref="Normalize"/> が非 null 契約のプロパティに書かれた null を既定値へ修復する）。
    /// 必須の値はキーが無ければ空のまま残し（<see cref="CreateWithRequiredValuesUnset"/>）、
    /// 明示の <c>null</c> と同じく <see cref="TryLoad"/> が断る。
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { LeaveRequiredValuesUnset },
        },
    };

    /// <summary>
    /// 必須の値を持つ型の作り方を <see cref="CreateWithRequiredValuesUnset"/> へ差し替える
    /// （<see cref="Options"/> の型情報の修飾子）
    /// </summary>
    private static void LeaveRequiredValuesUnset(JsonTypeInfo typeInfo)
    {
        if (CreateWithRequiredValuesUnset.TryGetValue(typeInfo.Type, out var create))
        {
            typeInfo.CreateObject = create;
        }
    }

    /// <summary>保存文書をアトミックに（書き込み途中の中断で既存ファイルを壊さずに）保存する</summary>
    /// <remarks>
    /// 素の <see cref="File.WriteAllText(string, string?)"/> は既存ファイルを
    /// 切り詰めてから書くため、途中でプロセスが落ちる・ディスクが満杯になるとユーザーの図ファイルが
    /// 破損した JSON として残る。これを防ぐため、書き込みは <see cref="AtomicFile.WriteAllText"/>
    /// （一時ファイルへ全量を書き切ってから本体へ差し替える。アルゴリズムの詳細はそちらを参照）へ委譲する。
    /// 図ファイルの書き出し（GUI の上書き／別名保存・スキーマのみ JSON のエクスポート・MCP のツール実行・
    /// CLI のリバース出力・クラッシュ時の緊急保存）はすべてこちらを使う（新規作成で置き換えないときは <see cref="SaveNew"/>）。
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

    /// <summary>保存文書を新しいファイルとして書き出す（既にファイルがあれば何も変えずに <see cref="IOException"/>）</summary>
    /// <remarks>
    /// 新しい図を作る経路（MCP の <c>create_diagram</c>）用。存在を確かめてから <see cref="SaveAtomic"/> で書く形では、
    /// その隙間に別のプロセスが作った図を上書きする。書き方は <see cref="AtomicFile.CreateText"/>
    /// （書き切ってから置き換えずに置く）で、内容は <see cref="SaveAtomic"/> と同じ。
    /// </remarks>
    /// <param name="path">作るファイルのパス</param>
    /// <param name="document">保存対象の文書</param>
    public static void SaveNew(string path, DiagramDocument document) =>
        AtomicFile.CreateText(path, Serialize(document));

    /// <summary>
    /// 保存文書を図ファイルの正準形で直列化した文字列を返す（<see cref="SaveAtomic"/> が書く内容と同じ）
    /// </summary>
    /// <remarks>
    /// 変更の前後で図が変わったかを、保存する形のまま比べるのに使う（MCP の変更系ツールが、何も変えない呼び出しで
    /// ファイルを書き直さないため）。
    /// </remarks>
    /// <param name="document">直列化する文書</param>
    public static string ToJson(DiagramDocument document) => Serialize(document);

    /// <summary>保存文書を図ファイルの正準形（<see cref="Options"/>）で JSON 文字列へ直列化する</summary>
    /// <remarks><see cref="SaveAtomic"/>・<see cref="SaveNew"/>・<see cref="ToJson"/> で出力を完全に一致させるための共有ヘルパ</remarks>
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
    /// 用途（テストのフィクスチャ）に用いる。
    /// </para>
    /// <para>
    /// 検査はしないので、必須の値（<see cref="FindMissingRequiredValue"/> の 6 つの文字列と表・列の Id）は、キーが無くても
    /// 明示の <c>null</c>（空の Guid）でも空のまま返す。図に無い表・列を指す参照もそのまま返す（<see cref="TryLoad"/> と同じ <see cref="Options"/> で読む＝
    /// 読込結果は一致し、断るかどうかだけが違う）。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <returns>読み込んだ <see cref="DiagramDocument"/>（内容が空の場合は新規インスタンス）</returns>
    public static DiagramDocument Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>ER 図の保存形式として妥当か検証したうえでファイルから保存文書を読み込む</summary>
    /// <remarks>
    /// 検証は「読み取り → JSON 解析 → ルートが JSON オブジェクトか → ルートのキー重複 →
    /// <c>Version</c>・<c>Schema</c> を持つか → 逆直列化 → 版番号 → 必須の値の欠落（キーが無いか明示の <c>null</c>・空の Guid） →
    /// Id の重複 → 図に無い表・列を指す参照」の順で、
    /// <see cref="JsonStorageService"/> の読込仕様に合わせ
    /// キー名の大文字小文字は区別する。無関係な JSON（例 <c>package.json</c>）を「空図」として読み込み、
    /// 誤解釈・上書きするのを防ぐ。
    /// <para>
    /// フォーマット版の判定（<see cref="DiagramDocument.IsNewerFormat"/>）は含まない。新フォーマットを
    /// 拒否するか警告して続行するかは経路ごとに異なるため、読み込んだ文書を見て呼び出し側が決める。
    /// </para>
    /// <para>
    /// <b>逆直列化とルートキーの評価も try の内側で行う。</b>ルートのキー重複（どちらの値を採るか
    /// 決まらない）とプロパティの型不一致（<see cref="JsonException"/>）を外へ漏らすと、呼び出し側は
    /// 種別を持てず「読めなかった」以上のことを言えなくなる。どちらも <see cref="DocumentLoadError.InvalidJson"/>
    /// として原因の例外を持ち帰る。入れ子のキー重複は逆直列化の既定どおり後勝ちで読む。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <param name="document">読み込んだ文書（失敗時は <c>null</c>）</param>
    /// <param name="error">失敗の種別（成功時は <see cref="DocumentLoadError.None"/>）</param>
    /// <param name="exception">
    /// 失敗の原因となった例外。<see cref="DocumentLoadError.ReadFailed"/>・
    /// <see cref="DocumentLoadError.InvalidJson"/>・<see cref="DocumentLoadError.DuplicateId"/>・
    /// <see cref="DocumentLoadError.MissingRequiredText"/>・<see cref="DocumentLoadError.BrokenReference"/> のときだけ非 null で、
    /// 形式検証で弾いた場合と成功時は null。
    /// </param>
    /// <returns>読み込めた場合は <c>true</c></returns>
    public static bool TryLoad(
        string path,
        [NotNullWhen(true)] out DiagramDocument? document,
        out DocumentLoadError error,
        out Exception? exception
    ) => TryLoad(path, out document, out error, out exception, out _);

    /// <summary>
    /// <see cref="TryLoad(string, out DiagramDocument?, out DocumentLoadError, out Exception?)"/> と同じ検証で
    /// 読み込み、あわせて<b>実際に読み取ったファイルのバイト列</b>を返す
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用途は GUI の外部変更検知で、読込直後の「最終既知ハッシュ」をディスクの読み直しではなく
    /// 読み込んだ内容そのものから求めるため。読み直す方式では、読み取りからハッシュ採取までの隙間に
    /// 外部プロセスが書いた内容を「読み込んだ内容」として記録してしまう。
    /// </para>
    /// <para>
    /// 文字列でなくバイト列を返すのは、文字列から符号化し直すとファイルのバイト列と一致しないことが
    /// あるため（BOM は復号で剥がれ、UTF-16 のファイルは別の符号化になる）。復号の規則は
    /// <see cref="File.ReadAllText(string)"/> と同じ（UTF-8 既定・BOM を検出）で、読込結果は
    /// 4 引数版と変わらない。
    /// </para>
    /// </remarks>
    /// <param name="path">読み込むファイルパス</param>
    /// <param name="document">読み込んだ文書（失敗時は <c>null</c>）</param>
    /// <param name="error">失敗の種別（成功時は <see cref="DocumentLoadError.None"/>）</param>
    /// <param name="exception">失敗の原因となった例外（4 引数版と同じ）</param>
    /// <param name="rawContents">
    /// 読み取ったファイルのバイト列。読み取り自体に失敗した場合だけ <c>null</c>
    /// （読み取れた後の検証で失敗した場合も、読み取った内容を返す）
    /// </param>
    /// <returns>読み込めた場合は <c>true</c></returns>
    public static bool TryLoad(
        string path,
        [NotNullWhen(true)] out DiagramDocument? document,
        out DocumentLoadError error,
        out Exception? exception,
        [NotNullWhen(true)] out byte[]? rawContents
    )
    {
        document = null;
        error = DocumentLoadError.None;
        exception = null;
        rawContents = null;

        try
        {
            // 別のプロセス（GUI・別の MCP サーバ）の保存と重なった一瞬の共有違反では諦めない
            rawContents = AtomicFile.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            error = DocumentLoadError.ReadFailed;
            exception = ex;
            return false;
        }

        var json = DecodeText(rawContents);
        DiagramDocument loaded;

        try
        {
            // 解析は 1 回だけ。逆直列化も解析済みの要素から行う（元の JSON テキストを読むので、
            // 文字列から逆直列化した場合と結果は同じ＝入れ子のキー重複も従来どおり後勝ち）
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = DocumentLoadError.NotDiagramDocument;
                return false;
            }

            // ルートのキー重複は、どちらの値を図として採るかが決まらないため InvalidJson として断る
            if (FindDuplicateRootProperty(root) is { } duplicateName)
            {
                throw new JsonException(
                    $"The property '{duplicateName}' appears more than once in the root object."
                );
            }

            if (
                !root.TryGetProperty("Version", out var version)
                || version.ValueKind == JsonValueKind.Null
                || !root.TryGetProperty("Schema", out var schema)
                || schema.ValueKind != JsonValueKind.Object
            )
            {
                error = DocumentLoadError.NotDiagramDocument;
                return false;
            }

            loaded = Normalize(root.Deserialize<DiagramDocument>(Options) ?? new DiagramDocument());
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

        // 名前・型・表と列の Id の欠落（キーが無いか明示の null・空の Guid。どちらも逆直列化の後は空）は
        // 修復せず拒否する（理由は FindMissingRequiredValue）。Id の重複より先に見るのは、名前が null のままでは
        // 重複の説明が場所を名指しできないため（空の Id が 2 つあれば重複にも見えるが、欠落として先に名指しする）
        if (FindMissingRequiredValue(loaded.Schema) is { } missing)
        {
            error = DocumentLoadError.MissingRequiredText;
            exception = new InvalidDataException(missing);
            return false;
        }

        // Id の重複は修復せず拒否する（理由は FindDuplicateId）
        if (FindDuplicateId(loaded.Schema) is { } duplicate)
        {
            error = DocumentLoadError.DuplicateId;
            exception = new InvalidDataException(duplicate);
            return false;
        }

        // 図に無い表・列を指す参照は修復せず拒否する（理由は FindBrokenReference）。
        // Id の重複より後に見るのは、表・列を Id で一意に引ける前提で参照を解くため
        if (FindBrokenReference(loaded.Schema) is { } broken)
        {
            error = DocumentLoadError.BrokenReference;
            exception = new InvalidDataException(broken);
            return false;
        }

        document = loaded;
        return true;
    }

    /// <summary>
    /// 名前・型のような「推測で埋められない必須の文字列」と、ほかから引かれる表・列の Id が欠けていないか
    /// （キーが無いか明示的な <c>null</c>・空の Guid）調べ、最初に見つかった箇所の説明を返す（無ければ null）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>修復はしない。</b>これらのプロパティはモデル側が非 NULL 許容で宣言しており、アプリ側に
    /// <c>null</c> を書く経路も、キーを書かない経路も無い（GUI・MCP ツール・各種取込・C# リバースはいずれも
    /// 文字列を組み立てて渡し、NULL 許容の綻びはビルドが警告＝エラーとして止める。Id は値型でいつも書かれ、
    /// <see cref="Guid.NewGuid"/> は空の Guid を作らない）。起こるのは JSON の手編集だけなので、
    /// 既定値で埋めて意味を推測するより、どこが欠けているかを名指しして読込を断るほうが直せる。
    /// </para>
    /// <para>
    /// キーの欠落と明示の <c>null</c>（Id は空の Guid）は、逆直列化の後はどちらも空になる
    /// （<see cref="CreateWithRequiredValuesUnset"/> が空から始めるため）。ここは 1 つの分岐で両方を見て、
    /// 文言も区別しない（<c>missing or null</c>・<c>missing or empty</c>）。
    /// </para>
    /// <para>
    /// 対象は<b>推測で埋めると図の意味が変わるもの</b>だけ＝テーブル名・列名・<b>列の型</b>・
    /// クエリ名・パラメータ名・射影フィールド名の 6 つと、<b>表・列の Id</b>。列の型を既定値（<c>int</c>）で埋めるのは、
    /// 名前を勝手に付け直すのと同じ性質の書き換えになる。表・列の Id を新しい Guid で埋めると、レイアウト・リレーション・
    /// 列ペア・一意制約・主キーの順序・クエリからの参照が黙って切れる。
    /// 説明・メモのような任意の文字列は空文字へ修復する（<see cref="Normalize"/>）＝埋めても意味が変わらない。
    /// 対象 DBMS も従来どおり既定値へ修復する（方言は図の意味でなく出力先の指定で、既定が定まっている）。
    /// リレーション・一意制約・クエリの Id は誰も引かないので見ない（<see cref="CreateWithRequiredValuesUnset"/>）。
    /// </para>
    /// <para>
    /// 名前が <c>null</c> のものは名指しできないため、現れた順の番号（1 始まり）で位置を示す。
    /// 説明文は例外メッセージと同じ扱いで英語固定とし、利用者向けの見出しは表示側が付ける
    /// （<see cref="FindDuplicateId"/> と同じ規約）。
    /// </para>
    /// </remarks>
    private static string? FindMissingRequiredValue(ErDiagram schema)
    {
        for (var entityIndex = 0; entityIndex < schema.Entities.Count; entityIndex++)
        {
            var entity = schema.Entities[entityIndex];

            if (entity.TableName is null)
            {
                return $"\"TableName\" is missing or null for entity #{entityIndex + 1}.";
            }

            if (entity.Id == Guid.Empty)
            {
                return $"\"Id\" is missing or empty for table '{entity.TableName}'.";
            }

            for (var columnIndex = 0; columnIndex < entity.Columns.Count; columnIndex++)
            {
                var column = entity.Columns[columnIndex];

                if (column.Name is null)
                {
                    return $"\"Name\" is missing or null for column #{columnIndex + 1} of table '{entity.TableName}'.";
                }

                if (column.DataType is null)
                {
                    return $"\"DataType\" is missing or null for column '{column.Name}' of table '{entity.TableName}'.";
                }

                if (column.Id == Guid.Empty)
                {
                    return $"\"Id\" is missing or empty for column '{column.Name}' of table '{entity.TableName}'.";
                }
            }
        }

        for (var queryIndex = 0; queryIndex < schema.Queries.Count; queryIndex++)
        {
            var query = schema.Queries[queryIndex];

            if (query.Name is null)
            {
                return $"\"Name\" is missing or null for query #{queryIndex + 1}.";
            }

            for (var index = 0; index < query.Parameters.Count; index++)
            {
                if (query.Parameters[index].Name is null)
                {
                    return $"\"Name\" is missing or null for parameter #{index + 1} of query '{query.Name}'.";
                }
            }

            for (var index = 0; index < query.Fields.Count; index++)
            {
                if (query.Fields[index].Name is null)
                {
                    return $"\"Name\" is missing or null for projection field #{index + 1} of query '{query.Name}'.";
                }
            }
        }

        return null;
    }

    /// <summary>エンティティ Id・列 Id・リレーション Id の重複を探し、最初に見つかった重複の説明を返す（無ければ null）</summary>
    /// <remarks>
    /// <para>
    /// <b>修復はしない。</b>重複 Id を持つ図ファイルを作る経路はアプリ側に無く（GUI・MCP ツール・
    /// 各種取込はいずれも新しい Guid を採る）、起こるのは JSON の手編集だけなので、Id を勝手に
    /// 張り替えて意味を推測するより、どこが重複しているかを名指しして読込を断るほうが直せる。
    /// </para>
    /// <para>
    /// 検査対象はエンティティ Id・列 Id・リレーション Id の 3 つ（列 Id は同一テーブル内とテーブル間の双方を見る）。
    /// エンティティ Id と列 Id は辞書のキーにされ、リレーション・一意制約・主キー順・クエリからの参照先にもなるため、
    /// 重複したまま読み込むと上書き保存（レイアウト辞書の構築）が毎回失敗し、例外を握り潰す自動保存も
    /// 一度も成功しない＝作業内容を保存する手段が両方とも消える。リレーションの Id も辞書のキーに使われる
    /// （<c>ConstraintNames.ResolveForeignKeyNamesById</c> の <c>ToDictionary</c>＝重複すると MCP・内蔵チャットの
    /// ツールが例外・コード生成のナビゲーション名の辞書＝黙って上書き・C# リバースの関係のメタデータ）。
    /// 一意制約・クエリの Id は参照も辞書化もされないので検査しない。
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

        var relationshipById = new Dictionary<Guid, string>();

        foreach (var relationship in schema.Relationships)
        {
            var described = DescribeRelationship(relationship, tableByEntityId);

            if (relationshipById.TryGetValue(relationship.Id, out var existingRelationship))
            {
                return $"Duplicate relationship Id '{relationship.Id}' is used by {existingRelationship} and {described}.";
            }

            relationshipById.Add(relationship.Id, described);
        }

        return null;
    }

    /// <summary>
    /// 図に無い表・列を指す参照（リレーションの両端・列ペアの列・一意制約の構成列）を探し、
    /// 最初に見つかった箇所の説明を返す（無ければ null）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>修復はしない。</b>普通の操作ではこれらの参照は切れない（表の削除は接するリレーションを、列の削除は
    /// その列を使う列ペアを全部空にし、その列を含む一意制約を消す。GUI・MCP・内蔵チャットとも v0.1.0 から。
    /// 取込は列を引けないペアを空にして警告する）。起こるのは JSON の手編集だけ。そのまま開くと GUI は両端を
    /// 解けないリレーションと列を解けない列ペアを黙って読み飛ばし、上書き保存で消える。一意制約は保持されるが、
    /// DDL・生成が警告なしでその制約を出さない。どれも推測で直せないので、場所を名指しして断る。
    /// 参照の Guid のキーが無いときは空の Guid になる（初期化子が無い）ので、図に無い Id と同じに扱い、
    /// 文言だけ「missing」と言い分ける。
    /// </para>
    /// <para>
    /// 列ペアの列は端の表の列でなければならない（親の列は <see cref="Relationship.SourceEntityId"/> の表、
    /// 子の列は <see cref="Relationship.TargetEntityId"/> の表）。GUI の整合処理（<c>RelationshipViewModel</c>）と同じ判定で、
    /// 別の表の列を指すペアもここで断る。
    /// </para>
    /// <para>
    /// <b>次の参照は断らない</b>（見ないことが仕様）。
    /// 主キーの順序 <see cref="Entity.PrimaryKeyColumnIds"/> の解けない Id は、列の削除・主キーの切り替えが掃除しない
    /// 設計で、普通に保存した図に残る（読み手 <see cref="Entity.GetPrimaryKeyColumnsInOrder"/> が無視する）。
    /// クエリの <see cref="QueryDefinition.EntityId"/> は、表の削除がクエリを消すようになる前の版（v0.2.0 まで）で
    /// 保存した図に残りうる。クエリの列参照（パラメータ・並び順・射影）は、列の削除がクエリを掃除しないので
    /// 普通の操作で切れる。どちらも見えて直せる（定義ダイアログが OK を止め、生成が警告してそのクエリを飛ばし、
    /// MCP の <c>list_queries</c> が孤児として出して <c>remove_query</c> で消せる）。レイアウトの、表を指さないキーは
    /// 図の意味を持たず、GUI の保存で消えるだけ。断ると、どれも普通に保存した図・公開版の図を開けなくする。
    /// </para>
    /// <para>
    /// 表・列を Id で一意に引けることが前提なので、<see cref="FindDuplicateId"/> の後に呼ぶ。
    /// 説明文は英語固定で、利用者向けの見出しは表示側が付ける（<see cref="FindDuplicateId"/> と同じ規約）。
    /// </para>
    /// </remarks>
    private static string? FindBrokenReference(ErDiagram schema)
    {
        var entityById = schema.Entities.ToDictionary(entity => entity.Id);

        for (var index = 0; index < schema.Relationships.Count; index++)
        {
            var relationship = schema.Relationships[index];
            var named = string.IsNullOrEmpty(relationship.ConstraintName)
                ? $"relationship #{index + 1}"
                : $"relationship '{relationship.ConstraintName}'";

            if (
                DescribeBrokenEntityReference(
                    nameof(Relationship.SourceEntityId),
                    relationship.SourceEntityId,
                    named,
                    entityById
                ) is
                { } brokenSource
            )
            {
                return brokenSource;
            }

            if (
                DescribeBrokenEntityReference(
                    nameof(Relationship.TargetEntityId),
                    relationship.TargetEntityId,
                    named,
                    entityById
                ) is
                { } brokenTarget
            )
            {
                return brokenTarget;
            }

            var source = entityById[relationship.SourceEntityId];
            var target = entityById[relationship.TargetEntityId];

            for (var pairIndex = 0; pairIndex < relationship.ColumnPairs.Count; pairIndex++)
            {
                var pair = relationship.ColumnPairs[pairIndex];
                var place = $"column pair #{pairIndex + 1} of {named}";

                if (
                    DescribeBrokenColumnReference(
                        nameof(RelationshipColumnPair.SourceColumnId),
                        pair.SourceColumnId,
                        place,
                        source
                    ) is
                    { } brokenParentColumn
                )
                {
                    return brokenParentColumn;
                }

                if (
                    DescribeBrokenColumnReference(
                        nameof(RelationshipColumnPair.TargetColumnId),
                        pair.TargetColumnId,
                        place,
                        target
                    ) is
                    { } brokenChildColumn
                )
                {
                    return brokenChildColumn;
                }
            }
        }

        foreach (var entity in schema.Entities)
        {
            for (var index = 0; index < entity.UniqueConstraints.Count; index++)
            {
                var constraint = entity.UniqueConstraints[index];
                var place = string.IsNullOrEmpty(constraint.Name)
                    ? $"unique constraint #{index + 1} of table '{entity.TableName}'"
                    : $"unique constraint '{constraint.Name}' of table '{entity.TableName}'";

                foreach (var columnId in constraint.ColumnIds)
                {
                    if (
                        DescribeBrokenColumnReference(
                            nameof(UniqueConstraint.ColumnIds),
                            columnId,
                            place,
                            entity
                        ) is
                        { } brokenMember
                    )
                    {
                        return brokenMember;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>表を指す参照が図に無い表を指していれば説明を返す（解ければ null）</summary>
    /// <param name="property">参照のプロパティ名（文言に出す）</param>
    /// <param name="entityId">参照している表の Id（空の Guid はキーの欠落）</param>
    /// <param name="place">参照の持ち主の呼び名（文言に出す）</param>
    /// <param name="entityById">図の表の Id からの引き当て</param>
    private static string? DescribeBrokenEntityReference(
        string property,
        Guid entityId,
        string place,
        Dictionary<Guid, Entity> entityById
    )
    {
        if (entityById.ContainsKey(entityId))
        {
            return null;
        }

        return entityId == Guid.Empty
            ? $"\"{property}\" is missing for {place}."
            : $"\"{property}\" of {place} refers to table Id '{entityId}', which is not in the diagram.";
    }

    /// <summary>列を指す参照が、指すべき表の列でなければ説明を返す（その表の列なら null）</summary>
    /// <param name="property">参照のプロパティ名（文言に出す）</param>
    /// <param name="columnId">参照している列の Id（空の Guid はキーの欠落）</param>
    /// <param name="place">参照の持ち主の呼び名（文言に出す）</param>
    /// <param name="owner">列を持っているべき表</param>
    private static string? DescribeBrokenColumnReference(
        string property,
        Guid columnId,
        string place,
        Entity owner
    )
    {
        if (owner.Columns.Any(column => column.Id == columnId))
        {
            return null;
        }

        return columnId == Guid.Empty
            ? $"\"{property}\" is missing for {place}."
            : $"\"{property}\" of {place} refers to column Id '{columnId}', which is not a column of table '{owner.TableName}'.";
    }

    /// <summary>重複の説明に使うリレーションの呼び名（制約名があれば制約名、無ければ親と子のテーブル名）</summary>
    /// <param name="relationship">名指しするリレーション</param>
    /// <param name="tableByEntityId">エンティティ Id からテーブル名への対応（引けない Id はそのまま書く）</param>
    private static string DescribeRelationship(
        Relationship relationship,
        Dictionary<Guid, string> tableByEntityId
    )
    {
        if (!string.IsNullOrEmpty(relationship.ConstraintName))
        {
            return $"relationship '{relationship.ConstraintName}'";
        }

        string TableOf(Guid id) =>
            tableByEntityId.TryGetValue(id, out var table) ? table : id.ToString();

        return $"relationship from table '{TableOf(relationship.SourceEntityId)}' to table '{TableOf(relationship.TargetEntityId)}'";
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
    /// （<c>"2"</c>・<c>2.0</c>・<see cref="int"/> に収まらない値・<c>null</c>・オブジェクト・配列。この版が書く形ではないので、
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
                || !root.ContainsKey("Version")
            )
            {
                return false;
            }

            // キーがある以上は版を名乗っているので true。値が null・オブジェクト・配列なら解釈できない版
            // （version は null のまま）として扱い、int として読めたときだけ値を持ち帰る
            if (root["Version"] is JsonValue value && value.TryGetValue(out int parsed))
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

    /// <summary>ファイルのバイト列を <see cref="File.ReadAllText(string)"/> と同じ規則で文字列へ復号する</summary>
    /// <remarks>UTF-8 を既定とし、BOM があればそれに従う（BOM 自体は結果に含めない）</remarks>
    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(
            new MemoryStream(bytes, writable: false),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }

    /// <summary>ルートオブジェクトのキー重複を探し、最初に重複したキー名を返す（無ければ null）</summary>
    /// <remarks>
    /// 比較は大文字小文字を区別する序数比較（<see cref="JsonStorageService"/> の読込仕様と同じ）。
    /// 見るのはルートだけで、入れ子のキー重複は逆直列化の既定どおり後勝ちで読む。
    /// </remarks>
    private static string? FindDuplicateRootProperty(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return property.Name;
            }
        }

        return null;
    }

    /// <summary>JSON 文字列を保存文書へ逆直列化し、非 null 契約を満たすよう正規化する</summary>
    /// <remarks>
    /// <see cref="Load"/> 用。<see cref="TryLoad(string, out DiagramDocument?, out DocumentLoadError, out Exception?, out byte[]?)"/>
    /// は解析済みの要素から逆直列化し、同じ <see cref="Options"/> と <see cref="Normalize"/> を通す（読込結果は一致する）。
    /// </remarks>
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
    /// <c>System.Text.Json</c> の <c>RespectNullableAnnotations</c> による例外化は、キー欠落・
    /// 古い形式もそのまま読めるという <see cref="Options"/> の互換契約を壊すため採らない。
    /// </para>
    /// <para>
    /// <b>ここで修復するのは「埋めても図の意味が変わらないもの」だけ</b>＝コレクション（空へ）・
    /// 説明とメモ（空文字へ）・対象 DBMS（既定の方言へ）。名前・型・表と列の Id のような<b>推測で埋めると
    /// 図の意味が変わるもの</b>（キーが無いときも空のまま届く）と参照の Guid には触れず、<see cref="TryLoad"/> の
    /// <see cref="FindMissingRequiredValue"/>・<see cref="FindBrokenReference"/> が場所を名指しして拒否する。
    /// 図として妥当かどうか（無関係な JSON でないか）の判定も <see cref="TryLoad"/> が担う。
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
            // 説明・メモは空文字が正当な値なので、明示 null は空文字へ寄せる（意味は変わらない）
            entity.Description ??= string.Empty;
            entity.Memo ??= string.Empty;

            entity.Columns = Compact(entity.Columns);
            entity.UniqueConstraints = Compact(entity.UniqueConstraints);

            foreach (var column in entity.Columns)
            {
                column.Description ??= string.Empty;
            }

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
            query.Description ??= string.Empty;
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
