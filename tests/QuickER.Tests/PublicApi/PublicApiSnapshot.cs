using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace QuickER.Tests.PublicApi;

/// <summary>
/// アセンブリの公開 API 面を、決定的な正規化テキストへ書き出すハーネス。
/// </summary>
/// <remarks>
/// <para>
/// 配布 NuGet パッケージ（<c>QuickER.Runtime*</c>）の公開 API は、利用者のコードが直接触る唯一の面であり、
/// メンバーを 1 つ消す・引数を 1 つ増やすといった変更はコンパイル済み利用コードを黙って壊す。
/// テンプレートを介して生成されるため通常のコードレビューでは差分が見えにくく、
/// 既存のガード（依存集合＝<c>RuntimePackageProjectDependencyGuardTests</c>・型集合＝<c>SplitRuntimeSymmetryTests</c>）は
/// メンバーのシグネチャまで見ていない。ここで面そのものをスナップショットとして固定する。
/// </para>
/// <para>
/// <b>含めるもの</b>: 公開型（<see cref="Assembly.GetExportedTypes"/>＝入れ子の公開型を含む）、その種別・修飾子・
/// 基底型・実装インターフェイス（継承分を含む全件）、外部から到達できるメンバー
/// （<c>public</c> / <c>protected</c> / <c>protected internal</c>）のシグネチャ＝戻り値型・パラメータの型と名前と
/// <c>ref</c>/<c>out</c>/<c>in</c>/<c>params</c>・省略可能引数の既定値、定数フィールドの値、列挙体の各値。
/// </para>
/// <para>
/// <b>含めないもの</b>: XmlDoc・コメント（メタデータに残らない）、属性（<c>[Obsolete]</c> 等も対象外＝
/// 面の形を見る道具に絞る）、<c>internal</c> / <c>private</c> / <c>private protected</c> のメンバー、
/// コンパイラ生成物（プロパティ・イベントのアクセサメソッド、<c>&lt;&gt;</c> を含む名前、
/// <see cref="CompilerGeneratedAttribute"/> 付きの型・メンバー）、NULL 許容参照型の注釈
/// （リフレクションからの復元が煩雑なわりに得られる情報が少ないため意図的に落とす）。
/// </para>
/// <para>
/// 出力は全階層をオーディナル順に整列した CRLF テキストで、実行環境・実行順・ビルド構成に依存しない。
/// </para>
/// </remarks>
internal static class PublicApiSnapshot
{
    /// <summary>指定アセンブリの公開 API 面を正規化テキスト（CRLF・末尾改行あり）へ書き出す。</summary>
    /// <param name="assembly">対象アセンブリ</param>
    public static string Render(Assembly assembly)
    {
        var builder = new StringBuilder();

        // 承認ファイルを開いた人がその場で更新手順に辿り着けるよう、固定ヘッダを付ける
        // （アセンブリ名以外に可変要素を含めない＝バージョンや日時は入れない）。
        builder.Append("// QuickER 公開 API スナップショット: ").Append(assembly.GetName().Name);
        builder.Append("\r\n");
        builder.Append(
            "// 手編集しない。更新は QUICKER_REGEN_FIXTURES=1 での再生成（失敗メッセージにコマンドあり）。\r\n"
        );
        builder.Append("\r\n");

        var types = assembly
            .GetExportedTypes()
            .Where(type => !IsCompilerGenerated(type))
            .OrderBy(FormatType, StringComparer.Ordinal)
            .ToList();

        foreach (var type in types)
        {
            AppendType(builder, type);
        }

        return builder.ToString();
    }

    /// <summary>1 つの型宣言とそのメンバー行を書き出す。</summary>
    private static void AppendType(StringBuilder builder, Type type)
    {
        builder.Append(FormatTypeDeclaration(type)).Append("\r\n");

        foreach (var member in DescribeMembers(type))
        {
            builder.Append("    ").Append(member).Append("\r\n");
        }

        builder.Append("\r\n");
    }

    /// <summary>型宣言行（アクセシビリティ・修飾子・種別・名前・基底型・インターフェイス）を組み立てる。</summary>
    private static string FormatTypeDeclaration(Type type)
    {
        var parts = new List<string> { TypeAccessibility(type) };

        if (type.IsEnum)
        {
            parts.Add("enum");
        }
        else if (type.IsInterface)
        {
            parts.Add("interface");
        }
        else if (typeof(Delegate).IsAssignableFrom(type))
        {
            parts.Add("delegate");
        }
        else if (type.IsValueType)
        {
            parts.Add("struct");
        }
        else
        {
            if (type is { IsAbstract: true, IsSealed: true })
            {
                parts.Add("static");
            }
            else if (type.IsAbstract)
            {
                parts.Add("abstract");
            }
            else if (type.IsSealed)
            {
                parts.Add("sealed");
            }

            parts.Add("class");
        }

        parts.Add(FormatType(type));

        var declaration = string.Join(" ", parts);
        var bases = new List<string>();

        if (type.IsEnum)
        {
            // 列挙体は基になる整数型が値の互換性そのものなので明示する。
            // System.Enum が持つ IComparable / IConvertible 等はどの列挙体にも一律に付くノイズなので出さない
            return $"{declaration} : {FormatType(type.GetEnumUnderlyingType())}";
        }

        if (
            type.BaseType is not null
            && type.BaseType != typeof(object)
            && type.BaseType != typeof(ValueType)
            && type.BaseType != typeof(MulticastDelegate)
        )
        {
            bases.Add(FormatType(type.BaseType));
        }

        // 継承分も含めた全インターフェイスを並べる（宣言の直系だけを見ると、
        // 基底の差し替えでインターフェイスが落ちた変化を取り逃す）
        bases.AddRange(
            type.GetInterfaces()
                .Where(item => item.IsPublic || item.IsNestedPublic)
                .Select(FormatType)
                .OrderBy(name => name, StringComparer.Ordinal)
        );

        return bases.Count == 0 ? declaration : $"{declaration} : {string.Join(", ", bases)}";
    }

    /// <summary>型の外部到達可能なメンバーを、整列済みの行として列挙する。</summary>
    private static IEnumerable<string> DescribeMembers(Type type)
    {
        if (type.IsEnum)
        {
            // 列挙体は値そのものが公開 API（利用側のコンパイル済み定数に焼き込まれる）
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => $"{field.Name} = {FormatConstant(field.GetRawConstantValue())}")
                .OrderBy(line => line, StringComparer.Ordinal);
        }

        const BindingFlags Flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        // プロパティ・イベントのアクセサは、プロパティ／イベント行の側で表現するのでメソッドとしては出さない
        var accessors = new HashSet<MethodInfo>();

        foreach (var property in type.GetProperties(Flags))
        {
            foreach (var accessor in property.GetAccessors(nonPublic: true))
            {
                accessors.Add(accessor);
            }
        }

        foreach (var @event in type.GetEvents(Flags))
        {
            foreach (
                var accessor in new[]
                {
                    @event.GetAddMethod(nonPublic: true),
                    @event.GetRemoveMethod(nonPublic: true),
                    @event.GetRaiseMethod(nonPublic: true),
                }
            )
            {
                if (accessor is not null)
                {
                    accessors.Add(accessor);
                }
            }
        }

        var lines = new List<string>();

        foreach (var field in type.GetFields(Flags))
        {
            if (IsCompilerGenerated(field) || FieldAccessibility(field) is not { } accessibility)
            {
                continue;
            }

            lines.Add(FormatField(field, accessibility));
        }

        foreach (var constructor in type.GetConstructors(Flags))
        {
            if (
                IsCompilerGenerated(constructor)
                || MethodAccessibility(constructor) is not { } accessibility
            )
            {
                continue;
            }

            lines.Add($"{accessibility} .ctor({FormatParameters(constructor.GetParameters())})");
        }

        foreach (var method in type.GetMethods(Flags))
        {
            if (
                accessors.Contains(method)
                || IsCompilerGenerated(method)
                || MethodAccessibility(method) is not { } accessibility
            )
            {
                continue;
            }

            lines.Add(FormatMethod(method, accessibility));
        }

        foreach (var property in type.GetProperties(Flags))
        {
            if (IsCompilerGenerated(property))
            {
                continue;
            }

            var line = FormatProperty(property);

            if (line is not null)
            {
                lines.Add(line);
            }
        }

        foreach (var @event in type.GetEvents(Flags))
        {
            var add = @event.GetAddMethod(nonPublic: true);

            if (add is null || MethodAccessibility(add) is not { } accessibility)
            {
                continue;
            }

            var modifier = add.IsStatic ? "static " : string.Empty;

            lines.Add(
                $"{accessibility} {modifier}event {FormatType(@event.EventHandlerType!)} {@event.Name}"
            );
        }

        return lines.OrderBy(line => line, StringComparer.Ordinal);
    }

    /// <summary>フィールド行（定数は値まで含める）を組み立てる。</summary>
    private static string FormatField(FieldInfo field, string accessibility)
    {
        var modifiers = new StringBuilder();

        if (field.IsLiteral)
        {
            modifiers.Append("const ");
        }
        else
        {
            if (field.IsStatic)
            {
                modifiers.Append("static ");
            }

            if (field.IsInitOnly)
            {
                modifiers.Append("readonly ");
            }
        }

        var line =
            $"{accessibility} {modifiers}{FormatType(field.FieldType)} {field.Name}".TrimEnd();

        // 定数は利用側のアセンブリへ焼き込まれるため、値が変わるだけで意味が変わる＝値も面の一部
        return field.IsLiteral ? $"{line} = {FormatConstant(field.GetRawConstantValue())}" : line;
    }

    /// <summary>メソッド行（修飾子・戻り値型・ジェネリック引数・パラメータ）を組み立てる。</summary>
    private static string FormatMethod(MethodInfo method, string accessibility)
    {
        var modifiers = new StringBuilder();

        if (method.IsStatic)
        {
            modifiers.Append("static ");
        }
        else if (method.IsAbstract && !method.DeclaringType!.IsInterface)
        {
            modifiers.Append("abstract ");
        }
        else if (method is { IsVirtual: true, IsFinal: false })
        {
            // 仮想メソッドは利用者側の override 可否が変わるため、override か新規かを区別して残す
            modifiers.Append(
                method.GetBaseDefinition() != method && !method.DeclaringType!.IsInterface
                    ? "override "
                    : "virtual "
            );
        }

        var generics = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(FormatType))}>"
            : string.Empty;

        return $"{accessibility} {modifiers}{FormatType(method.ReturnType)} {method.Name}{generics}({FormatParameters(method.GetParameters())})";
    }

    /// <summary>プロパティ行を組み立てる。外部から到達できるアクセサが 1 つも無ければ null を返す。</summary>
    private static string? FormatProperty(PropertyInfo property)
    {
        var getter = property.GetGetMethod(nonPublic: true);
        var setter = property.GetSetMethod(nonPublic: true);
        var getterAccessibility = getter is null ? null : MethodAccessibility(getter);
        var setterAccessibility = setter is null ? null : MethodAccessibility(setter);

        if (getterAccessibility is null && setterAccessibility is null)
        {
            return null;
        }

        // プロパティ自身のアクセシビリティは、外部から見えるアクセサのうち最も緩いもの
        var accessibility =
            getterAccessibility is "public" || setterAccessibility is "public"
                ? "public"
                : (getterAccessibility ?? setterAccessibility)!;
        var accessor = (MethodInfo?)getter ?? setter!;
        var modifiers = new StringBuilder();

        if (accessor.IsStatic)
        {
            modifiers.Append("static ");
        }
        else if (accessor.IsAbstract && !property.DeclaringType!.IsInterface)
        {
            modifiers.Append("abstract ");
        }
        else if (accessor is { IsVirtual: true, IsFinal: false })
        {
            modifiers.Append(
                accessor.GetBaseDefinition() != accessor && !property.DeclaringType!.IsInterface
                    ? "override "
                    : "virtual "
            );
        }

        var indexParameters = property.GetIndexParameters();
        var name =
            indexParameters.Length == 0
                ? property.Name
                : $"this[{FormatParameters(indexParameters)}]";
        var body = new StringBuilder("{");

        if (getterAccessibility is not null)
        {
            body.Append(
                getterAccessibility == accessibility ? " get;" : $" {getterAccessibility} get;"
            );
        }

        if (setterAccessibility is not null)
        {
            // init アクセサはオブジェクト初期化子からのみ書ける＝set とは別物として残す
            var keyword = IsInitOnly(setter!) ? "init;" : "set;";

            body.Append(
                setterAccessibility == accessibility
                    ? $" {keyword}"
                    : $" {setterAccessibility} {keyword}"
            );
        }

        body.Append(" }");

        return $"{accessibility} {modifiers}{FormatType(property.PropertyType)} {name} {body}";
    }

    /// <summary>セッターが <c>init</c> アクセサか（modreq に IsExternalInit が付く）。</summary>
    private static bool IsInitOnly(MethodInfo setter) =>
        setter
            .ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    /// <summary>パラメータ列（修飾子・型・名前・既定値）を組み立てる。</summary>
    private static string FormatParameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(FormatParameter));

    /// <summary>1 つのパラメータを組み立てる。</summary>
    private static string FormatParameter(ParameterInfo parameter)
    {
        var builder = new StringBuilder();

        if (parameter.ParameterType.IsByRef)
        {
            builder.Append(
                parameter.IsOut ? "out "
                : parameter.IsIn ? "in "
                : "ref "
            );
        }

        if (parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false))
        {
            builder.Append("params ");
        }

        var type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;

        builder.Append(FormatType(type)).Append(' ').Append(parameter.Name);

        if (parameter.IsOptional && parameter.HasDefaultValue)
        {
            // 省略可能引数の既定値は呼び出し側へ焼き込まれるため、値が変われば面が変わる。
            // 値型の既定値はメタデータ上 null で表現されるので、参照型の null と混同しないよう default と書く
            var isDefaultOfValueType = parameter.RawDefaultValue is null && type.IsValueType;

            builder
                .Append(" = ")
                .Append(
                    isDefaultOfValueType ? "default" : FormatConstant(parameter.RawDefaultValue)
                );
        }

        return builder.ToString();
    }

    /// <summary>定数値を、環境（カルチャ）に依らない表記へ整形する。</summary>
    private static string FormatConstant(object? value) =>
        value switch
        {
            null => "null",
            string text =>
                $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            bool flag => flag ? "true" : "false",
            char character => $"'{character}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>型名を決定的に整形する（プリミティブは C# キーワード・入れ子は <c>.</c> 区切り）。</summary>
    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            return FormatType(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var commas = rank > 1 ? new string(',', rank - 1) : string.Empty;

            return $"{FormatType(type.GetElementType()!)}[{commas}]";
        }

        if (type.IsPointer)
        {
            return $"{FormatType(type.GetElementType()!)}*";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{FormatType(underlying)}?";
        }

        if (Keywords.TryGetValue(type, out var keyword))
        {
            return keyword;
        }

        // 型引数にジェネリック引数を含む型（Task<TRequest> 等）は FullName が null になるため、
        // 名前空間を自前で前置する（さもないと同じ型が Task<T> と System.Threading.Tasks.Task で揺れる）
        var name =
            type.FullName
            ?? (string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}");
        var backtick = name.IndexOf('`', StringComparison.Ordinal);

        if (backtick < 0)
        {
            return name.Replace('+', '.');
        }

        // ジェネリック型は `n を落として実引数（または型引数）を並べる
        var arguments = type.IsGenericType ? type.GetGenericArguments() : [];
        var withoutArity = name[..backtick].Replace('+', '.');

        return arguments.Length == 0
            ? withoutArity
            : $"{withoutArity}<{string.Join(", ", arguments.Select(FormatType))}>";
    }

    /// <summary>プリミティブ型を C# キーワードへ寄せる対応表（読みやすさと表記ゆれ防止）</summary>
    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
    };

    /// <summary>型のアクセシビリティ（公開型のみを対象とするため public / protected の 2 択）</summary>
    private static string TypeAccessibility(Type type) =>
        type.IsNestedFamily ? "protected"
        : type.IsNestedFamORAssem ? "protected internal"
        : "public";

    /// <summary>外部から到達できるメソッドのアクセシビリティ。到達できないなら null。</summary>
    private static string? MethodAccessibility(MethodBase method) =>
        method.IsPublic ? "public"
        : method.IsFamily ? "protected"
        : method.IsFamilyOrAssembly ? "protected internal"
        : null;

    /// <summary>外部から到達できるフィールドのアクセシビリティ。到達できないなら null。</summary>
    private static string? FieldAccessibility(FieldInfo field) =>
        field.IsPublic ? "public"
        : field.IsFamily ? "protected"
        : field.IsFamilyOrAssembly ? "protected internal"
        : null;

    /// <summary>コンパイラ生成物か（属性付き・または名前に <c>&lt;&gt;</c> を含む）</summary>
    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || member.Name.Contains('<', StringComparison.Ordinal);
}
