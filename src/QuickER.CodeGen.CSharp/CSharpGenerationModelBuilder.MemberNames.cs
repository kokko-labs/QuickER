using QuickER.CodeGen.CSharp.Resources;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成クラスが発行する全メンバー名をシンボル表へ集め、同一クラス内の重複を検出する部分。
/// </summary>
/// <remarks>
/// <para>
/// 検証は生の <c>ErDiagram</c> ではなく構築済みモデルに対して行う。ナビゲーション名は
/// <c>ResolveAllNavigations</c> の解決結果でしか確定せず、EditModel の派生名（<c>Binding…</c>・
/// <c>_…</c>・<c>…Snapshot</c>）の規則もこのビルダーだけが持つためで、生成前検証
/// （<c>CSharpCodeGenerationService.Validate</c>）側へ置くと派生名規則の第 2 実装が生まれてドリフトの元になる。
/// </para>
/// <para>
/// 列由来プロパティ名の一意性検証（<c>CSharpCodeGenerationService.ValidateColumnPropertyNameUniqueness</c>）
/// とは層が違う。あちらは生成前検証で走り、エラーが出た時点でビルダーへ到達しない（サービスが診断のみを返す）ため、
/// 両方の診断が同時に出ることはない。同名列という最頻の誤りへ「どの列同士か」を並べた具体的なメッセージで先に答える
/// 早期検証として残し、こちらは派生名・ナビゲーションまで含む一般の網として働く。
/// </para>
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>生成される 1 メンバー名と、その由来（診断メッセージへ載せる説明文）</summary>
    private readonly record struct GeneratedMemberName(string Name, string Origin);

    /// <summary>
    /// Entity / EditModel の各クラスについて、テンプレートが発行するメンバー名の重複を検出し Error 診断を出す。
    /// </summary>
    /// <remarks>
    /// 重複は CS0102（同名メンバーの二重宣言）でコンパイル不能になり、表示名衝突のように「機構を省略して完走」させる
    /// 逃げ道がないため Warning ではなく Error とする（呼び出し側はエラー時にファイルを書き出さない）。
    /// Mapper・Repository・値オブジェクトは列ごとのメンバーを発行しない（Mapper のメンバーは固定名、
    /// 射影 DTO 名の衝突は名前付きクエリ検証の担当）ため対象外。
    /// </remarks>
    private static void ValidateGeneratedMemberNames(
        CSharpGenerationModel model,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (var entity in model.EntityClasses)
        {
            ReportDuplicateMemberNames(
                entity.ClassName,
                entity.TableName,
                CollectEntityMemberNames(entity),
                diagnostics
            );
        }

        foreach (var editModel in model.EditModelClasses)
        {
            ReportDuplicateMemberNames(
                editModel.ClassName,
                editModel.TableName,
                CollectEditModelMemberNames(editModel),
                diagnostics
            );
        }
    }

    /// <summary>
    /// Entity クラスがテンプレートで発行するメンバー名を列挙する。
    /// </summary>
    /// <remarks>
    /// 列挙の根拠（<c>Templates/CSharpRuntime/_02_Entities.scriban</c>。テンプレートへメンバーを追加したら
    /// ここと <see cref="GeneratedFixedMemberNames"/> も追随すること）:
    /// <list type="bullet">
    ///   <item><description>L223 <c>public (型) (property_name) { get; set; }</c> ＝列プロパティ</description></item>
    ///   <item><description>L228 <c>public (型) (navigation.property_name) { get; set; }</c> ＝ナビゲーションプロパティ</description></item>
    ///   <item><description>L229-231 <c>protected override string DefaultDisplayName</c> ＝表示名衝突が無く、かつテーブル説明があるときだけ発行される固定メンバー</description></item>
    /// </list>
    /// <c>DefaultDisplayName</c> は列由来・ナビゲーション由来のどちらと衝突しても CS0102 になるため、
    /// 発行条件（<see cref="CSharpClassModel.HasDisplayNameCollision"/> が false かつ
    /// <see cref="CSharpClassModel.DisplayNameDescription"/> が非 null）が成り立つときだけシンボル表へ入れる。
    /// 説明の無いテーブルでは override 自体が出ないため、同名の列があっても診断しない。
    /// 表示名機構の予約名（<c>DisplayName</c> / <c>CustomizeDisplayName</c>）は衝突しても機構の省略で救えるため、
    /// ここではなく <c>CodeGen_Warning_EntityDisplayNameCollision</c> の警告が担当する。
    /// </remarks>
    private static IEnumerable<GeneratedMemberName> CollectEntityMemberNames(
        CSharpClassModel entity
    )
    {
        foreach (var property in entity.Properties)
        {
            yield return new GeneratedMemberName(
                property.PropertyName,
                FormatColumnOrigin(property.ColumnName)
            );
        }

        foreach (var navigation in entity.Navigations)
        {
            yield return new GeneratedMemberName(
                navigation.PropertyName,
                FormatNavigationOrigin(navigation.PropertyName)
            );
        }

        // テーブル説明があり表示名衝突が無いときだけ DefaultDisplayName の override が発行される
        if (!entity.HasDisplayNameCollision && entity.DisplayNameDescription is not null)
        {
            foreach (var name in GeneratedFixedMemberNames.EntityWithTableDescription)
            {
                yield return new GeneratedMemberName(name, FormatFixedMemberOrigin(name));
            }
        }
    }

    /// <summary>
    /// EditModel クラスがテンプレートで発行するメンバー名を列挙する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 列挙の根拠（<c>Templates/CSharpRuntime/_03_EditModelsAndMappers.scriban</c>。テンプレートへメンバーを
    /// 追加したらここと <see cref="GeneratedFixedMemberNames"/> も追随すること）:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>L970/L972 <c>private (型) (p.field_name)</c> ＝確定値のバッキングフィールド（_prop）</description></item>
    ///   <item><description>L975 <c>private string (p.binding_field_name)</c> ＝バインディング文字列のフィールド（_bindingProp）</description></item>
    ///   <item><description>L978 <c>public (型) (p.property_name)</c> ＝確定値プロパティ（Prop）</description></item>
    ///   <item><description>L1010-L1019 <c>partial void On(p.property_name)Changing/Changed(...)</c> ＝値変更フック（4 本はシグネチャ違いのオーバーロード同士なので、名前は 2 つだけ登録する）</description></item>
    ///   <item><description>L1022 <c>public string (p.binding_property_name)</c> ＝バインディングプロパティ（BindingProp）</description></item>
    ///   <item><description>L1276 <c>private string (p.binding_field_name)Snapshot</c> ＝行編集のスナップショットフィールド</description></item>
    ///   <item><description>L1157/L1181 <c>private (型) (navigation.field_name)</c> ＝カスケード子のバッキングフィールド（親参照ナビは field_name が空で発行されない）</description></item>
    ///   <item><description>L1160/L1184/L1201 <c>public (型) (navigation.property_name)</c> ＝ナビゲーションプロパティ</description></item>
    ///   <item><description>L1205-L1330 の固定メンバー 18 名 ＝ <see cref="GeneratedFixedMemberNames.EditModelAlways"/>（無条件）</description></item>
    ///   <item><description>L1267 <c>RegisterChildren</c> ＝ <see cref="GeneratedFixedMemberNames.EditModelWithCascadeNavigations"/>（カスケード子を持つときのみ）</description></item>
    ///   <item><description>L1322 <c>ParentModel</c> ＝ <see cref="GeneratedFixedMemberNames.EditModelWithTypedParentModel"/>（型付き親モデルがあるときのみ）</description></item>
    ///   <item><description><c>ValidateUniqueAsync</c> ＝ <see cref="GeneratedFixedMemberNames.EditModelWithRepositoryFace"/>（Repository 契約面があるときのみ）</description></item>
    /// </list>
    /// <para>
    /// 値オブジェクト経路（<c>BuildValueObjectEditModelProperty</c>）も同じ派生名規則で組み立てるため、
    /// モデル側の名前を読む本メソッドは VO の有無に依らず正しく列挙できる。
    /// </para>
    /// <para>
    /// 固定メンバーのうち表示名解決ヘルパ（<see cref="GeneratedFixedMemberNames.EditModelDisplayNameHelpers"/>）だけは
    /// 列挙しない。衝突時は表示名機構ごと省略して生成を完走させる救済（Warning）が先に働くため、Error にすると
    /// 救済済みの図を誤って弾く。基底クラス（<c>EditModelBase</c>）にしかない名前も同様に対象外
    /// （同名の列プロパティは CS0108 の警告でコンパイル可能なので、Error にすると正当な図を弾く）。
    /// </para>
    /// </remarks>
    private static IEnumerable<GeneratedMemberName> CollectEditModelMemberNames(
        CSharpEditModelClassModel editModel
    )
    {
        foreach (var property in editModel.Properties)
        {
            var origin = FormatColumnOrigin(property.ColumnName);

            yield return new GeneratedMemberName(property.PropertyName, origin);
            yield return new GeneratedMemberName(property.FieldName, origin);
            yield return new GeneratedMemberName(property.BindingPropertyName, origin);
            yield return new GeneratedMemberName(property.BindingFieldName, origin);
            yield return new GeneratedMemberName(property.BindingFieldName + "Snapshot", origin);

            // 値変更フックは 1 プロパティにつき Changing / Changed の 2 名だけ登録する
            // （各 2 本はシグネチャ違いのオーバーロードで、同名を 2 回入れると自分自身を重複と誤検知する）
            yield return new GeneratedMemberName($"On{property.PropertyName}Changing", origin);
            yield return new GeneratedMemberName($"On{property.PropertyName}Changed", origin);
        }

        foreach (var navigation in editModel.Navigations)
        {
            var origin = FormatNavigationOrigin(navigation.PropertyName);

            yield return new GeneratedMemberName(navigation.PropertyName, origin);

            // カスケード子のみバッキングフィールドを持つ（親参照ナビの FieldName は空文字）
            if (!string.IsNullOrEmpty(navigation.FieldName))
            {
                yield return new GeneratedMemberName(navigation.FieldName, origin);
            }
        }

        foreach (var name in GeneratedFixedMemberNames.EditModelAlways)
        {
            yield return new GeneratedMemberName(name, FormatFixedMemberOrigin(name));
        }

        // カスケード子を持つときだけ RegisterChildren の override が発行される
        if (editModel.HasCascadeNavigations)
        {
            foreach (var name in GeneratedFixedMemberNames.EditModelWithCascadeNavigations)
            {
                yield return new GeneratedMemberName(name, FormatFixedMemberOrigin(name));
            }
        }

        // 親モデルの型が一意に定まるときだけ型付き ParentModel が発行される
        if (editModel.TypedParentModelTypeName is not null)
        {
            foreach (var name in GeneratedFixedMemberNames.EditModelWithTypedParentModel)
            {
                yield return new GeneratedMemberName(name, FormatFixedMemberOrigin(name));
            }
        }

        // Repository 契約面があるときだけ DB 照合糖衣 ValidateUniqueAsync が発行される
        if (editModel.HasRepositoryFace)
        {
            foreach (var name in GeneratedFixedMemberNames.EditModelWithRepositoryFace)
            {
                yield return new GeneratedMemberName(name, FormatFixedMemberOrigin(name));
            }
        }
    }

    /// <summary>集めたメンバー名を突き合わせ、2 回以上現れた名前を Error 診断として報告する</summary>
    /// <remarks>報告順を出現順に固定するため、挿入順リストと辞書で集約する（診断の決定性を保つ）</remarks>
    private static void ReportDuplicateMemberNames(
        string className,
        string tableName,
        IEnumerable<GeneratedMemberName> members,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var originsByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var member in members)
        {
            if (!originsByName.TryGetValue(member.Name, out var origins))
            {
                origins = [];
                originsByName.Add(member.Name, origins);
                order.Add(member.Name);
            }

            origins.Add(member.Origin);
        }

        foreach (var name in order.Where(memberName => originsByName[memberName].Count > 1))
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_GeneratedMemberNameCollision,
                        className,
                        tableName,
                        name,
                        string.Join(", ", originsByName[name])
                    )
                )
            );
        }
    }

    /// <summary>診断メッセージ用に「列 'xxx'」相当の由来表記を組み立てる</summary>
    private static string FormatColumnOrigin(string columnName) =>
        string.Format(Strings.CodeGen_Origin_Column, columnName);

    /// <summary>診断メッセージ用に「ナビゲーション 'Xxx'」相当の由来表記を組み立てる</summary>
    private static string FormatNavigationOrigin(string navigationName) =>
        string.Format(Strings.CodeGen_Origin_Navigation, navigationName);

    /// <summary>診断メッセージ用に「生成インフラの固定メンバー 'Xxx'」相当の由来表記を組み立てる</summary>
    private static string FormatFixedMemberOrigin(string memberName) =>
        string.Format(Strings.CodeGen_Origin_FixedMember, memberName);
}
