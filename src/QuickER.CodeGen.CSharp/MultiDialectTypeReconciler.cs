using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// マルチ方言（実効方言 2 つ以上）の列型辞書を突き合わせ、共有 Entity の型整合性を検証・補完する純ロジック。
/// </summary>
/// <remarks>
/// 生成器は DB 非依存を保つため、型解決（DB 型 → C# 型）は呼び出し側（プロバイダ）が方言ごとに行って渡す。
/// 共有 Entity は 1 型のため、方言間で C# 型（型名・参照/値区分）が食い違うと生成物が壊れる。ここで不一致を
/// 診断エラーにして黙って劣化させないほか、sqlserver がターゲットに含まれる場合は <c>[SqlColumnType]</c> の
/// メタ情報（SqlDbType・Size 等）を sqlserver 辞書から主辞書へ補完する（図の方言が非 sqlserver でも属性を出す）。
/// </remarks>
internal static class MultiDialectTypeReconciler
{
    /// <summary>
    /// 方言間で共有 Entity の C# 型（型名・参照/値区分）が食い違うカラムを診断エラーにする。
    /// </summary>
    /// <remarks>共有 Entity は単一型のため、方言間で型が食い違うと生成物が壊れる。黙って劣化させず明示エラーにする。</remarks>
    public static void DiagnoseTypeMismatches(
        ErDiagram diagram,
        IReadOnlyList<string> effectiveDialects,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 実効方言のうち辞書が渡されているものだけを対象にする
        var dicts = effectiveDialects
            .Where(columnTypesByDialect.ContainsKey)
            .Select(d => (Dialect: d, Types: columnTypesByDialect[d]))
            .ToList();

        if (dicts.Count < 2)
        {
            return;
        }

        var baseline = dicts[0];

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                if (!baseline.Types.TryGetValue(column.Id, out var baseInfo))
                {
                    continue;
                }

                foreach (var (dialect, types) in dicts.Skip(1))
                {
                    if (!types.TryGetValue(column.Id, out var info))
                    {
                        continue;
                    }

                    if (
                        !string.Equals(info.TypeName, baseInfo.TypeName, StringComparison.Ordinal)
                        || info.IsReferenceType != baseInfo.IsReferenceType
                    )
                    {
                        diagnostics.Add(
                            GenerationDiagnostic.Error(
                                string.Format(
                                    Strings.CodeGen_Error_TypeMismatch,
                                    entity.TableName,
                                    column.Name,
                                    baseline.Dialect,
                                    baseInfo.TypeName,
                                    dialect,
                                    info.TypeName
                                )
                            )
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// 主辞書に <c>[SqlColumnType]</c> のメタ情報（SqlDbType・宣言長・精度）を sqlserver 辞書から補完する。
    /// </summary>
    /// <remarks>
    /// 図の方言が非 sqlserver でも、sqlserver 実装が明示 SqlParameter を組み立てるため属性が必要になる。
    /// 主辞書の SqlDbTypeName が空で sqlserver 辞書に値がある列だけ、その 4 項目（SqlDbTypeName / SqlDeclaredLength /
    /// Precision / Scale）を差し替えた新しい型情報を作る。型名・参照区分など他の項目は主辞書のまま保つ。
    /// sqlserver がターゲットに含まれない場合は主辞書をそのまま返す。
    /// </remarks>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> SupplementSqlColumnTypes(
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primaryColumnTypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>> columnTypesByDialect
    )
    {
        var sqlServerKey = columnTypesByDialect.Keys.FirstOrDefault(k =>
            string.Equals(k, "sqlserver", StringComparison.OrdinalIgnoreCase)
        );

        if (sqlServerKey is null)
        {
            return primaryColumnTypes;
        }

        var sqlServerTypes = columnTypesByDialect[sqlServerKey];
        var result = new Dictionary<Guid, CSharpTypeInfo>(primaryColumnTypes.Count);

        foreach (var (columnId, primary) in primaryColumnTypes)
        {
            // 主辞書が既に SqlDbTypeName を持つ（図の方言が sqlserver）場合はそのまま使う
            if (
                primary.SqlDbTypeName is not null
                || !sqlServerTypes.TryGetValue(columnId, out var sqlServer)
                || sqlServer.SqlDbTypeName is null
            )
            {
                result[columnId] = primary;

                continue;
            }

            // 主辞書の型（型名・参照区分・MaxLength）は保ちつつ、SqlServer 由来の SqlColumnType メタ情報だけ載せる
            result[columnId] = new CSharpTypeInfo
            {
                TypeName = primary.TypeName,
                IsReferenceType = primary.IsReferenceType,
                MaxLength = primary.MaxLength,
                Precision = sqlServer.Precision,
                Scale = sqlServer.Scale,
                SqlDbTypeName = sqlServer.SqlDbTypeName,
                SqlDeclaredLength = sqlServer.SqlDeclaredLength,
                IsRowVersion = primary.IsRowVersion,
                IsUnboundedBinary = primary.IsUnboundedBinary,
            };
        }

        return result;
    }
}
