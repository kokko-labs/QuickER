using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>テーブル再構築で rowid の別名になる写す列の NULL の守り</summary>
internal static class TableRebuildRowidAlias
{
    /// <summary>
    /// 移送の前に NULL を断る写す列（<see cref="TableRebuildPlan.RowidAliasNullGuardColumn"/>）。合成後の主キーが単一列で、その宣言の型で
    /// DB が NULL を採番の値へ置き換え、live ではその列が同じ置き換えをする主キーでなかった写す列。無ければ null
    /// </summary>
    /// <remarks>
    /// live でも置き換える主キー（SQLite の rowid の別名）だった列は NULL を持てないので印さない。型の変更・主キーの移動・複合の主キーの
    /// 縮小・主キーの付与のどれで別名になっても同じ判定になる。
    /// </remarks>
    internal static string? NullGuardColumn(
        Entity live,
        Entity newDefinition,
        IReadOnlyList<string> copyColumns,
        SyncDialectCapabilities capabilities
    )
    {
        var newKey = newDefinition.GetPrimaryKeyColumnsInOrder();

        if (
            newKey.Count != 1
            || !capabilities.SingleColumnPrimaryKeyReplacesNull(newKey[0].DataType)
            || !copyColumns.Any(c => SchemaNames.AreSame(c, newKey[0].Name))
        )
        {
            return null;
        }

        var liveKey = live.GetPrimaryKeyColumnsInOrder();
        var aliasInLive =
            liveKey.Count == 1
            && SchemaNames.AreSame(liveKey[0].Name, newKey[0].Name)
            && capabilities.SingleColumnPrimaryKeyReplacesNull(liveKey[0].DataType);

        return aliasInLive ? null : newKey[0].Name;
    }
}
