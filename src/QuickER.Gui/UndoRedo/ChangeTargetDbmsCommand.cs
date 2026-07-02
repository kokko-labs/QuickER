using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Provider;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>
/// ターゲット DBMS の切替を単一の Undo 単位として扱う複合コマンド。
/// 「TargetDbms の変更」と「変換対象カラムの型変更」をまとめて適用・取消しする。
/// </summary>
/// <remarks>
/// Execute では対象プロバイダへ切り替えつつ各カラムの型を変換後の型へ更新し、
/// Undo では元プロバイダと元の型へ同時に巻き戻す（両者が常に一体で戻ることを保証する）。
/// </remarks>
public sealed class ChangeTargetDbmsCommand : IUndoableCommand
{
    /// <summary>方言切替を実際に反映するデリゲート（MainViewModel の内部フック）</summary>
    private readonly Action<IDatabaseProvider> _applyProvider;

    /// <summary>切替前のプロバイダ（Undo で復元する）</summary>
    private readonly IDatabaseProvider _from;

    /// <summary>切替後のプロバイダ（Execute / Redo で適用する）</summary>
    private readonly IDatabaseProvider _to;

    /// <summary>型変換対象のカラム ViewModel と新旧の型（適用・取消しに用いる）</summary>
    private readonly List<(ColumnViewModel Column, string OldType, string NewType)> _changes;

    /// <summary><see cref="ChangeTargetDbmsCommand"/> を生成する</summary>
    /// <param name="from">切替前のプロバイダ</param>
    /// <param name="to">切替後のプロバイダ</param>
    /// <param name="conversions">適用する型変換計画（変換成功分のみ）</param>
    /// <param name="columnsById">カラム ID から ViewModel を引くための索引</param>
    /// <param name="applyProvider">方言切替を反映するデリゲート</param>
    public ChangeTargetDbmsCommand(
        IDatabaseProvider from,
        IDatabaseProvider to,
        IReadOnlyList<ColumnTypeConversion> conversions,
        IReadOnlyDictionary<Guid, ColumnViewModel> columnsById,
        Action<IDatabaseProvider> applyProvider
    )
    {
        _from = from;
        _to = to;
        _applyProvider = applyProvider;
        _changes = new List<(ColumnViewModel, string, string)>();

        foreach (var conversion in conversions)
        {
            if (
                conversion.NewType is not null
                && columnsById.TryGetValue(conversion.ColumnId, out var column)
            )
            {
                _changes.Add((column, conversion.OldType, conversion.NewType));
            }
        }
    }

    /// <inheritdoc />
    public string Description => $"DB 種別を {_to.DisplayName} へ変更";

    /// <inheritdoc />
    public void Execute()
    {
        foreach (var change in _changes)
        {
            change.Column.DataType = change.NewType;
        }

        _applyProvider(_to);
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (var change in _changes)
        {
            change.Column.DataType = change.OldType;
        }

        _applyProvider(_from);
    }
}
