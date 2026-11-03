using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary><see cref="NullArgumentOp"/> が null を渡すリモート面の呼び出し</summary>
public enum ParityNullCall
{
    /// <summary><c>InsertAsync(null)</c></summary>
    Insert,

    /// <summary><c>UpdateAsync(null)</c></summary>
    Update,

    /// <summary>単数形の <c>SaveAsync(null)</c></summary>
    Save,

    /// <summary>複数形の <c>SaveAsync((IEnumerable)null)</c></summary>
    SaveMany,

    /// <summary><c>CheckUniquenessAsync(null)</c></summary>
    CheckUniqueness,
}

/// <summary>
/// 顧客のリモート面の書き込みと重複の事前チェックに、エンティティの代わりに null を渡す（全実装先にある）。直結の実装は入口で
/// <c>ArgumentNullException</c> を投げ、リモートのクライアントも HTTP を投げる前に同じ型・同じ引数名で断る契約
/// （<see cref="ParityFormat.Exception"/> が型と引数名を戻り値にする）
/// </summary>
public sealed record NullArgumentOp(ParityNullCall Call) : ParityOp
{
    public override string Name => $"NullArgument:{Call}";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var customers = backend.Customers;

        switch (Call)
        {
            case ParityNullCall.Insert:
                await customers.InsertAsync(null!, ct);
                return "inserted";
            case ParityNullCall.Update:
                return $"updated={await customers.UpdateAsync(null!, cancellationToken: ct)}";
            case ParityNullCall.Save:
                return $"rows={await customers.SaveAsync((CustomerEntity)null!, cancellationToken: ct)}";
            case ParityNullCall.SaveMany:
                return $"rows={await customers.SaveAsync((IEnumerable<CustomerEntity>)null!, cancellationToken: ct)}";
            case ParityNullCall.CheckUniqueness:
                return $"violations={(await customers.CheckUniquenessAsync(null!, ct)).Count}";
            default:
                throw new ArgumentOutOfRangeException(nameof(Call));
        }
    }
}

/// <summary>
/// 複数のスロットの根（すべて顧客）を、顧客のリポジトリの複数形のグラフ保存で 1 回に保存する。スロットの名前が null の位置は
/// null の要素のまま渡す（直結の実装は null の要素を飛ばし、リモートのクライアントも送る前の検査と保存の後の確定で飛ばす契約。
/// 応答の値を位置で行と突き合わせるときも、null の要素を飛ばした並びになる）
/// </summary>
public sealed record SaveManyCustomersWithNullsOp(IReadOnlyList<string?> SlotNames) : ParityOp
{
    public override string Name => "SaveManyCustomersWithNulls";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var roots = SlotNames
            .Select(s => s is null ? null! : (CustomerEntity)Root(backend, s))
            .ToList();
        var rows = await backend.Customers.SaveAsync(roots, cancellationToken: ct);

        return $"rows={rows}";
    }
}
