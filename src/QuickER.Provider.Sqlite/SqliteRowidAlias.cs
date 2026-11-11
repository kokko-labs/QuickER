using System;

namespace QuickER.Provider.Sqlite;

/// <summary>
/// SQLite の主キーが rowid の別名（暗黙の自動採番）になるかの規則（取込の NULL 許容の補正と、同期の宣言
/// <see cref="SyncDialectCapabilities.SingleColumnPrimaryKeyReplacesNull"/> が共有する）
/// </summary>
/// <remarks>
/// <para>
/// rowid の別名になるのは、rowid 表の<b>単一列</b>の主キーで、宣言の型がちょうど <c>INTEGER</c>（大文字小文字は問わない・前後の空白は
/// 許す）のときだけ（SQLite の仕様）。<c>INT</c> / <c>BIGINT</c> / <c>INTEGER(10)</c> はどれも別名にならず、互換のため NULL を許す
/// 普通の主キーのまま。別名の列は NULL を持てず、入れた NULL は断られずに採番の値へ置き換わる。
/// </para>
/// <para>
/// <c>INTEGER PRIMARY KEY DESC</c> は別名にならないが、宣言の型だけでは区別できないので別名の側へ倒す（取込の
/// <c>PRAGMA table_xinfo</c> は <c>DESC</c> を返さず、QuickER の DDL は表の制約の形 <c>PRIMARY KEY (x)</c> で書くので別名になる）。
/// </para>
/// </remarks>
internal static class SqliteRowidAlias
{
    /// <summary>単一列の主キーの宣言の型が、その列を rowid の別名にするか</summary>
    /// <param name="declaredType">宣言の型</param>
    public static bool IsAliasType(string declaredType) =>
        string.Equals(declaredType.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);
}
