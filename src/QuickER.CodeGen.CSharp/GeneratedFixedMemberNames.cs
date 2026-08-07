namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成テンプレートが列・ナビゲーションに依らず発行する「固定メンバー名」と、表示名機構が予約する名前の名簿。
/// </summary>
/// <remarks>
/// <para>
/// ここは名簿の唯一の正本で、次の 2 者が参照する:
/// (1) シンボル表検証（<c>CSharpGenerationModelBuilder.ValidateGeneratedMemberNames</c>）＝列・ナビゲーション由来の名前が
/// 固定メンバーと衝突する出力（CS0102 でコンパイル不能）を Error として止める、
/// (2) ドリフトガードテスト（<c>GeneratedFixedMemberDriftTests</c>）＝実生成した EditModel / Entity クラスの宣言メンバーから
/// 列由来の派生名を差し引いた残余が本名簿と完全一致することを表明し、テンプレートへ固定メンバーが増えた瞬間に落ちる。
/// </para>
/// <para>
/// 収録するのは「派生クラス（生成クラス）自身が宣言する」名前だけに限る。基底クラス（<c>EditModelBase</c> /
/// <c>EntityBase</c>）にしかないメンバー名（<c>Validate</c> / <c>AcceptChanges</c> / <c>SetProperty</c> 等）は、
/// 同名の列プロパティが出ても CS0108 の警告になるだけでコンパイルは通る。名簿へ入れると正当な図を誤って弾くため入れない。
/// </para>
/// <para>
/// 公開しているのはドリフトガードテストが参照するため（このアセンブリに <c>InternalsVisibleTo</c> は無く、
/// テストは公開 API 越しに検証する方針）。生成物側の名前ではなく、生成器が予約する名前の一覧である。
/// </para>
/// </remarks>
public static class GeneratedFixedMemberNames
{
    /// <summary>
    /// EditModel クラスが無条件に宣言する固定メンバー名（<c>Templates/CSharpRuntime/_03_EditModelsAndMappers.scriban</c>）。
    /// </summary>
    /// <remarks>
    /// 出典行（テンプレートを編集したらここも追随すること。並びは発行順）:
    /// L1205 <c>RevertCore</c>・L1212 <c>ValidateSelf</c>・L1222 <c>OnValidate</c>・
    /// L1225 <c>ResolveRequiredErrorMessage</c>・L1233 <c>CustomizeRequiredErrorMessage</c>・
    /// L1236 <c>ResolveParseErrorMessage</c>・L1249 <c>CustomizeParseErrorMessage</c>・
    /// L1279 <c>_rowStateSnapshot</c>・L1282 <c>BeginEditCore</c>・L1290 <c>OnBeginEdit</c>・
    /// L1293 <c>EndEditCore</c>・L1296 <c>OnEndEdit</c>・L1299 <c>CancelEditCore</c>・L1311 <c>OnCancelEdit</c>・
    /// <c>RegisterDuplicateError</c>・<c>ResolveDuplicateErrorMessage</c>・<c>CustomizeDuplicateErrorMessage</c>（重複値エラー）・
    /// L1314 <c>GetNext</c>・L1317 <c>GetPrevious</c>・L1320 <c>ParentCollection</c>・L1330 <c>MoveCore</c>。
    /// </remarks>
    public static IReadOnlySet<string> EditModelAlways { get; } =
        Create(
            "RevertCore",
            "ValidateSelf",
            "OnValidate",
            "ResolveRequiredErrorMessage",
            "CustomizeRequiredErrorMessage",
            "ResolveParseErrorMessage",
            "CustomizeParseErrorMessage",
            "RegisterDuplicateError",
            "ResolveDuplicateErrorMessage",
            "CustomizeDuplicateErrorMessage",
            "_rowStateSnapshot",
            "BeginEditCore",
            "OnBeginEdit",
            "EndEditCore",
            "OnEndEdit",
            "CancelEditCore",
            "OnCancelEdit",
            "GetNext",
            "GetPrevious",
            "ParentCollection",
            "MoveCore"
        );

    /// <summary>
    /// カスケード対象（子方向）のナビゲーションを持つ EditModel だけが宣言する固定メンバー名（L1267 <c>RegisterChildren</c>）。
    /// </summary>
    public static IReadOnlySet<string> EditModelWithCascadeNavigations { get; } =
        Create("RegisterChildren");

    /// <summary>
    /// 親モデルの型が一意に定まる EditModel だけが宣言する固定メンバー名（L1322 <c>ParentModel</c>）。
    /// </summary>
    public static IReadOnlySet<string> EditModelWithTypedParentModel { get; } =
        Create("ParentModel");

    /// <summary>
    /// Repository 契約面（<c>I{Entity}Repository</c>）が生成される EditModel だけが宣言する固定メンバー名
    /// （DB 照合糖衣 <c>ValidateUniqueAsync</c>）。
    /// </summary>
    /// <remarks>
    /// 発行条件は「Repository 契約の生成が有効」かつ「単一主キー」（＝そのエンティティの <c>I{Entity}Repository</c> が生成される）。
    /// 契約面が無い構成では呼び出し先が存在しないため、メソッドごと出さない。
    /// </remarks>
    public static IReadOnlySet<string> EditModelWithRepositoryFace { get; } =
        Create("ValidateUniqueAsync");

    /// <summary>
    /// EditModel の表示名解決ヘルパ名（L1256-L1265）。テンプレートは発行するが、シンボル表検証の対象には**しない**。
    /// </summary>
    /// <remarks>
    /// 列由来プロパティ名がこれらと一致した場合は、表示名機構そのものを省略して生成を完走させる救済
    /// （<c>CodeGen_Warning_EditModelDisplayNameCollision</c> の Warning）が先に働くため、Error にすると救済済みの図を誤って弾く。
    /// </remarks>
    public static IReadOnlySet<string> EditModelDisplayNameHelpers { get; } =
        Create("GetDisplayName", "CustomizePropertyDisplayName");

    /// <summary>
    /// テーブル説明があり、かつ表示名衝突が無い Entity だけが宣言する固定メンバー名
    /// （<c>Templates/CSharpRuntime/_02_Entities.scriban</c> L229-L231 <c>DefaultDisplayName</c> の override）。
    /// </summary>
    /// <remarks>
    /// 発行条件が「テーブル説明あり」なので、説明の無いテーブルでは同名の列があっても衝突しない（診断も出さない）。
    /// </remarks>
    public static IReadOnlySet<string> EntityWithTableDescription { get; } =
        Create("DefaultDisplayName");

    /// <summary>
    /// Entity の表示名機構が予約する名前。列由来プロパティ名がこれらと一致すると <c>DefaultDisplayName</c> の
    /// override を省略し（基底のクラス名フォールバックへ委ねる）、Warning で通知する。
    /// </summary>
    public static IReadOnlySet<string> EntityDisplayNameReserved { get; } =
        Create("DisplayName", "CustomizeDisplayName");

    /// <summary>序数比較の読み取り専用集合を組み立てる（メンバー名は識別子なので大文字小文字を区別する）</summary>
    private static IReadOnlySet<string> Create(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}
