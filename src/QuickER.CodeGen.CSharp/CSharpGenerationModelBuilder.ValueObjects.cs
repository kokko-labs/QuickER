using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 値オブジェクト（Value Object）の定義レジストリを構築する部分。
/// </summary>
/// <remarks>
/// 全テーブルの列を「正規化したカラム名」でグローバルにグルーピングし、列名ごとに 1 つの VO 型へ集約する。
/// PK と同名の FK が同一 VO 型を共有することで型安全を得る。同名なのに型・長さ・精度などが食い違う場合は
/// 競合として Warning 診断を出し、PK 定義優先（無ければ最大定義）で生成する。
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>1 つの値オブジェクトへ集約される列のメンバー（所属テーブル・列・解決済み C# 型）</summary>
    private readonly record struct ValueObjectMember(
        Entity Entity,
        Column Column,
        CSharpTypeInfo TypeInfo
    );

    /// <summary>ER 図全体から値オブジェクト定義を構築する（GenerateValueObjects が OFF のときは空）</summary>
    private IReadOnlyDictionary<string, CSharpValueObjectModel> BuildValueObjects(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GenerateValueObjects)
        {
            return new Dictionary<string, CSharpValueObjectModel>();
        }

        // 列名（正規化 Pascal）でグローバルにグルーピングする
        var groups = new Dictionary<string, List<ValueObjectMember>>(StringComparer.Ordinal);
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                var key = _nameConverter.ToColumnKey(column.Name);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<ValueObjectMember>();
                    groups[key] = list;
                }

                list.Add(new ValueObjectMember(entity, column, _columnTypes[column.Id]));
            }
        }

        var result = new Dictionary<string, CSharpValueObjectModel>(StringComparer.Ordinal);
        foreach (var (key, members) in groups)
        {
            result[key] = BuildValueObjectModel(key, members, options, diagnostics);
        }

        return result;
    }

    /// <summary>1 グループ（同名列の集合）から 1 つの VO 生成モデルを構築する。競合は警告し PK 優先/最大定義で解決する</summary>
    private CSharpValueObjectModel BuildValueObjectModel(
        string key,
        List<ValueObjectMember> members,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // PK があれば PK の定義を正、無ければ「最も広い定義」を正とする
        var primaryKeyMember = members.FirstOrDefault(member => member.Column.IsPrimaryKey);
        var authoritative = primaryKeyMember.Column is not null
            ? primaryKeyMember
            : members.OrderByDescending(WidthScore).First();

        var className = _nameConverter.ToValueObjectClassName(authoritative.Column.Name);
        var isGuidKey =
            options.UseGuidKeyForStringPrimaryKey
            && primaryKeyMember.Column is not null
            && primaryKeyMember.TypeInfo.TypeName == "string";

        var valueType = isGuidKey ? "string" : authoritative.TypeInfo.TypeName;
        var maxLength = isGuidKey ? null : authoritative.TypeInfo.MaxLength;
        var precision = isGuidKey ? null : authoritative.TypeInfo.Precision;
        var scale = isGuidKey ? null : authoritative.TypeInfo.Scale;

        // 同名グループの定義が食い違う場合は競合として警告（NULL 可否は競合に含めない）
        var signatures = members.Select(Signature).Distinct().ToList();
        if (signatures.Count > 1)
        {
            var locations = string.Join(
                "、",
                members.Select(member =>
                    $"{member.Entity.TableName}.{member.Column.Name} ({member.Column.DataType})"
                )
            );
            diagnostics.Add(
                Warning(
                    $"値オブジェクト '{className}' に集約される同名列の定義が一致しません。PK 定義優先（無ければ最大定義）で生成しますが、ER 図の定義統一を推奨します: {locations}"
                )
            );
        }

        return new CSharpValueObjectModel
        {
            ClassName = className,
            ValueTypeName = valueType,
            BaseDeclaration = BuildValueObjectBaseDeclaration(className, valueType, isGuidKey),
            InterfaceDeclaration = $"IValueObject<{className}, {valueType}>",
            IsGuidKey = isGuidKey,
            ColumnName = authoritative.Column.Name,
            DescriptionXmlDoc = EscapeForXmlDocSummary(authoritative.Column.Description),
            // 既定表示名: 代表列の Description があればそれ、無ければプロパティ名（例 "Name"。メッセージの後方互換）
            DisplayName = string.IsNullOrWhiteSpace(authoritative.Column.Description)
                ? _nameConverter.ToPropertyName(authoritative.Column.Name)
                : EscapeForCSharpString(authoritative.Column.Description),
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
        };
    }

    /// <summary>列名（正規化キー）から対応する値オブジェクトを引く。VO 化対象外なら null</summary>
    private CSharpValueObjectModel? ResolveValueObject(Column column) =>
        _valueObjects.TryGetValue(_nameConverter.ToColumnKey(column.Name), out var model)
            ? model
            : null;

    /// <summary>VO 化された列の EditModel プロパティ生成モデルを構築する（確定値は常に VO?、バインド setter は TryCreate で検証）</summary>
    private CSharpEditModelPropertyModel BuildValueObjectEditModelProperty(
        Column column,
        CSharpValueObjectModel valueObject
    )
    {
        var underlying = valueObject.ValueTypeName; // TValue（素の型）
        var isBinary = underlying == "byte[]";
        var isString = underlying == "string";
        var needsParse = !isBinary && !isString; // 数値・日時・bool・Guid 等は文字列から TryParse

        var propertyName = _nameConverter.ToPropertyName(column.Name);
        var bindingPropertyName = "Binding" + propertyName;

        return new CSharpEditModelPropertyModel
        {
            PropertyName = propertyName,
            // VO 有効プロパティの表示名は VO の静的 DisplayName を参照するため既定値は使われないが、必須フィールドを満たす
            DisplayName = string.IsNullOrWhiteSpace(column.Description)
                ? propertyName
                : EscapeForCSharpString(column.Description),
            DescriptionXmlDoc = EscapeForXmlDocSummary(column.Description),
            TypeName = valueObject.ClassName + "?", // 確定値は常に NULL 許容
            FieldName = ToFieldName(propertyName),
            BindingPropertyName = bindingPropertyName,
            BindingFieldName = ToFieldName(bindingPropertyName),
            NeedsParse = needsParse,
            ParseTypeName = needsParse ? underlying : string.Empty,
            FieldInitializer = string.Empty, // 参照型・nullable → null 既定
            BindingFieldInitializer = "string.Empty",
            IsNullable = true,
            IsReferenceType = true,
            IsBinary = isBinary,
            IsRequired = !column.IsNullable,
            RevertBindingExpression = $"{propertyName}?.ToString() ?? string.Empty",
            IsValueObject = true,
            ValueObjectClassName = valueObject.ClassName,
        };
    }

    /// <summary>競合判定用の定義シグネチャ（型・長さ・精度・スケール。NULL 可否は含めない）</summary>
    private static (string Type, int? MaxLength, int? Precision, int? Scale) Signature(
        ValueObjectMember member
    ) =>
        (
            member.TypeInfo.TypeName,
            member.TypeInfo.MaxLength,
            member.TypeInfo.Precision,
            member.TypeInfo.Scale
        );

    /// <summary>「最も広い定義」を選ぶためのスコア（string は最大長、decimal は精度。無指定 = 無制限を最大とみなす）</summary>
    private static long WidthScore(ValueObjectMember member) =>
        member.TypeInfo.TypeName switch
        {
            "string" => member.TypeInfo.MaxLength ?? long.MaxValue,
            "decimal" => member.TypeInfo.Precision ?? long.MaxValue,
            _ => 0,
        };

    /// <summary>C# 値型から継承すべき基底クラス宣言（型引数込み）を決める</summary>
    private static string BuildValueObjectBaseDeclaration(
        string className,
        string valueType,
        bool isGuidKey
    )
    {
        if (isGuidKey)
        {
            return $"ValueObjectGuidKeyBase<{className}>";
        }

        return valueType switch
        {
            "byte"
            or "short"
            or "int"
            or "long"
            or "float"
            or "double"
            or "decimal"
            or "TimeSpan"
            or "DateTimeOffset" => $"ValueObjectOrderedBase<{className}, {valueType}>",
            "string" => $"ValueObjectStringBase<{className}>",
            "bool" => $"ValueObjectBooleanBase<{className}>",
            "DateTime" => $"ValueObjectDateTimeBase<{className}>",
            "byte[]" => $"ValueObjectBinaryBase<{className}>",
            // Guid など順序付けしない型は等価のみの基底
            _ => $"ValueObjectBase<{className}, {valueType}>",
        };
    }
}
