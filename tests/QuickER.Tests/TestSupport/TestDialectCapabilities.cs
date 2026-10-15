using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストが使う能力の宣言（<see cref="SyncDialectCapabilities"/>）の共有の値
/// </summary>
/// <remarks>
/// 製品の能力の宣言は既定値を持たない（規則 2）。方言を決めずに差分・計画の振る舞いを確かめるテストは
/// <see cref="Neutral"/> を使い、1 項目だけ変えるテストは <c>Neutral with { … }</c> で書く。
/// 単体と Integration が Compile のリンクで同じ 1 ファイルを使う。
/// </remarks>
public static class TestDialectCapabilities
{
    /// <summary>
    /// どの方言の制約も持たない宣言（ALTER・外部キーの変更・説明・名前の永続化・参照アクションをすべて持ち、
    /// 型表記は正規化せず、識別子の長さの上限なし）
    /// </summary>
    public static SyncDialectCapabilities Neutral { get; } =
        new()
        {
            SupportsAlterColumn = true,
            SupportsForeignKeyAlter = true,
            SupportsDescriptions = true,
            AlterColumnRequiresForeignKeyRebuild = false,
            AlterColumnRequiresKeyConstraintRebuild = false,
            ForeignKeyRequiresLeadingIndex = false,
            PersistsForeignKeyConstraintNames = true,
            ColumnReorder = ColumnReorderMode.None,
            NormalizeTypeNotation = static type => type,
            SupportsOnUpdateAction = true,
            SupportsSetDefaultAction = true,
            SetDefaultRequiresColumnDefault = false,
            SupportsSchemaQualifiedTableNames = true,
            SupportsComputedColumnTypeChange = false,
            SupportsUniqueConstraintOnPrimaryKey = true,
            ReferencedKeyRequiresSameColumnOrder = true,
            ForeignKeyAllowsStringLengthDifference = false,
            TransactionalDdl = true,
            IdentifierLengthLimit = IdentifierLengthLimit.Unlimited,
            TruncatesLongIdentifiers = false,
        };
}
