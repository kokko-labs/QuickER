using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 乱数の操作の列の顧客と連絡先の手（顧客をスロットの根にしたグラフ・紹介者・連絡先の外部キーの付け替え）と、列の末尾にだけ置く
/// 引き金の手（参照されている顧客のグラフ削除・代替キーの更新・Save フックの規則）
/// </summary>
/// <remarks>
/// <para>
/// 顧客のスロット（<see cref="CustomerSlotNames"/>）のグラフは、顧客の根と、根を指す外部キーで持つ連絡先・紹介先だけを持つ。
/// どの行も、同じ保存の中で持っている行を指す外部キーは、持っている関係の 1 本だけにする（<see cref="Consistent"/>）。EF Core は
/// 追跡した行の外部キーとナビゲーションを突き合わせて（fix-up）メモリのグラフを書き換えるので、食い違うグラフは決まった形の
/// シナリオ（P2-5-N2）だけが持ち、乱数の列では作らない。
/// </para>
/// <para>
/// 列の途中では、EF Core が割れると分かっている手（H1・H3・H5・P2-5-N1・P2-5-R1）を作らない。参照されている顧客の削除・代替キーの
/// 更新・Save フックの規則は列の末尾（<see cref="AppendTail"/>）にだけ置き、<see cref="DeviatingOp"/> の印で、受け入れた差
/// （インメモリ）と直す区切りのある差（EF Core・リモート）の実装先をその手から後の比較から外す。
/// </para>
/// </remarks>
public sealed partial class BackendParityGenerator
{
    /// <summary>列の途中の顧客のスロット</summary>
    private static readonly string[] CustomerSlotNames = ["C", "D"];

    /// <summary>末尾の手が使うスロット（列の途中では使わない）</summary>
    private const string TailHolder = "TC";

    private const string TailTarget = "TD";

    /// <summary>Save フックの規則の末尾の手が使うスロット（観測が規則の当たる保存を見分ける）</summary>
    public const string HookSlot = "H";

    /// <summary>Include の読み取りだけに使うスロット（保存しない）</summary>
    private const string ReadSlot = "R";

    private readonly Dictionary<int, string> _codes = new();
    private readonly Dictionary<int, int?> _referrers = new();
    private readonly Dictionary<int, ContactModel> _contacts = new();
    private readonly Dictionary<string, CustomerSlotModel> _customerSlots = new(
        StringComparer.Ordinal
    );
    private ParityHookRule? _hookRule;

    // ---- 種まき ----

    /// <summary>
    /// 顧客と連絡先の種まき（注文の無い顧客 3 と連絡先 500・501。顧客 3 は連絡先 500 の watcher に参照される＝
    /// 参照されている顧客のグラフ削除の末尾の手を最初から作れる）とモデルの初期値
    /// </summary>
    private IEnumerable<ParityOp> SeedCustomerData()
    {
        _customers[3] = "C3";
        _codes[1] = CustomerRow.DefaultCode(1);
        _codes[2] = CustomerRow.DefaultCode(2);
        _codes[3] = CustomerRow.DefaultCode(3);
        _referrers[1] = null;
        _referrers[2] = null;
        _referrers[3] = null;
        _contacts[500] = new ContactModel(1, Watcher: 3);
        _contacts[501] = new ContactModel(2, Backup: 1, Code: CustomerRow.DefaultCode(1));

        yield return new SeedOp(new CustomerRow(3, "C3"));
        yield return new SeedOp(new ContactRow(500, "w", 1, Watcher: 3));
        yield return new SeedOp(
            new ContactRow(501, "b", 2, Backup: 1, CustomerCode: CustomerRow.DefaultCode(1))
        );
    }

    // ---- 列の途中の手 ----

    /// <summary>顧客の手の候補を足す</summary>
    private void AddCustomerCandidates(List<(int Weight, Action Emit)> candidates)
    {
        candidates.Add((3, InsertContact));
        candidates.Add((4, LoadCustomer));

        var usable = _customerSlots.Where(s => !s.Value.Stale).ToList();

        if (usable.Count > 0)
        {
            candidates.Add((3, AttachContact));
            candidates.Add((2, AttachReferral));
            candidates.Add((3, EditCustomerNote));
            candidates.Add((2, EditReferrer));
            candidates.Add((3, EditContact));
            candidates.Add((2, RemoveContact));
            candidates.Add((1, RemoveCustomerRoot));
            candidates.Add((4, SaveCustomer));
            candidates.Add((1, UpdateCustomer));
        }

        if (usable.Count == 2)
        {
            candidates.Add((2, SaveManyCustomers));
        }

        if (_mode == ParityGeneratorMode.Full)
        {
            candidates.Add((2, LoadCustomerWithContacts));
        }
    }

    private void InsertContact()
    {
        var id = Fresh(500, 540, _contacts.ContainsKey);
        var live = LiveCustomers();

        if (id is null || live.Count == 0)
        {
            return;
        }

        var owner = PickFrom(live);
        var contact = new ContactModel(
            owner,
            Watcher: MaybeCustomer(),
            Backup: MaybeCustomer(),
            Fallback: MaybeCustomer(),
            Code: _random.Next(3) == 0 ? _codes[PickFrom(live)] : null
        );
        _contacts[id.Value] = contact;
        _ops.Add(new InsertOp(contact.ToRow(id.Value, $"l{_step}")));
    }

    private void LoadCustomer()
    {
        if (_customers.Count == 0)
        {
            return;
        }

        var slot = Pick(CustomerSlotNames);
        var id = PickFrom([.. _customers.Keys]);
        _customerSlots[slot] = new CustomerSlotModel(id, _referrers[id], _codes[id]);
        _ops.Add(new LoadOp(slot, "customers", id));
    }

    /// <summary>顧客を連絡先と紹介先ごと Include して読む（読み取りだけ＝保存しない。同じ連絡先が関係ごとに別のインスタンスになり得る＝P2-5-N3）</summary>
    private void LoadCustomerWithContacts()
    {
        if (_customers.Count == 0)
        {
            return;
        }

        _ops.Add(new LoadCustomerWithContactsOp(ReadSlot, PickFrom([.. _customers.Keys])));
    }

    /// <summary>根を指す外部キーがちょうど 1 本の連絡先を、その関係のコレクションへ足す</summary>
    private void AttachContact()
    {
        var (name, slot) = UsableCustomerSlot();
        var candidates = _contacts
            .Where(c =>
                !slot.Contacts.ContainsKey(c.Key) && c.Value.RelationsTo(slot.Id, _codes).Count == 1
            )
            .Select(c => c.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var id = PickFrom(candidates);
        var relation = _contacts[id].RelationsTo(slot.Id, _codes)[0];
        slot.Contacts[id] = new HeldContact(relation, _contacts[id].Copy());
        _ops.Add(new AttachContactOp(name, $"customers/{slot.Id}", id, relation));
    }

    /// <summary>根を紹介者に持つ顧客を、紹介先のコレクションへ足す</summary>
    private void AttachReferral()
    {
        var (name, slot) = UsableCustomerSlot();
        var candidates = _referrers
            .Where(r =>
                r.Value == slot.Id && r.Key != slot.Id && !slot.Referrals.ContainsKey(r.Key)
            )
            .Select(r => r.Key)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var id = PickFrom(candidates);
        slot.Referrals[id] = RowState.Unchanged;
        _ops.Add(new AttachChildCustomerOp(name, id));
    }

    /// <summary>根か紹介先のメモを書き換える</summary>
    private void EditCustomerNote()
    {
        var (name, slot) = UsableCustomerSlot();

        if (slot.Root == RowState.Removed)
        {
            return;
        }

        var targets = new List<int> { slot.Id };
        targets.AddRange(slot.Referrals.Where(r => r.Value != RowState.Removed).Select(r => r.Key));
        var target = PickFrom(targets);

        if (target == slot.Id)
        {
            slot.Root = RowState.Updated;
        }
        else
        {
            slot.Referrals[target] = RowState.Updated;
        }

        _ops.Add(new EditCustomerOp(name, target, Note: $"n{_step}"));
    }

    /// <summary>根の紹介者を、ほかの顧客へ付け替えるか外す（グラフに持っている顧客と自分は指さない）</summary>
    private void EditReferrer()
    {
        var (name, slot) = UsableCustomerSlot();

        if (slot.Root == RowState.Removed)
        {
            return;
        }

        var targets = LiveCustomers()
            .Where(c => c != slot.Id && !slot.Referrals.ContainsKey(c))
            .ToList();

        if (_random.Next(3) == 0 || targets.Count == 0)
        {
            slot.Referrer = null;
            _ops.Add(new EditCustomerOp(name, slot.Id, ClearReferrer: true));
        }
        else
        {
            var target = PickFrom(targets);
            slot.Referrer = target;
            _ops.Add(new EditCustomerOp(name, slot.Id, Referrer: target));
        }

        slot.Root = RowState.Updated;
    }

    /// <summary>連絡先のラベルか、持っている関係以外の外部キーを書き換える（根を指すようにはしない）</summary>
    private void EditContact()
    {
        var (name, slot) = UsableCustomerSlot();
        var held = slot.Contacts.Where(c => c.Value.State != RowState.Removed).ToList();

        if (held.Count == 0)
        {
            return;
        }

        var (id, contact) = PickFrom(held);
        var others = Enum.GetValues<ParityContactRelation>()
            .Where(r => r != contact.Relation)
            .ToList();

        if (_random.Next(3) == 0)
        {
            _ops.Add(new EditContactOp(name, id, $"e{_step}"));
        }
        else
        {
            var relation = PickFrom(others);
            var targets = LiveCustomers().Where(c => c != slot.Id).ToList();

            if (relation == ParityContactRelation.CustomerCode)
            {
                var code =
                    _random.Next(3) == 0 || targets.Count == 0 ? null : _codes[PickFrom(targets)];
                contact.Values.Code = code;
                _ops.Add(
                    new EditContactOp(
                        name,
                        id,
                        Target: new ParityContactTarget(relation, null, code)
                    )
                );
            }
            else
            {
                // 必須の owner は外さない
                int? target =
                    relation != ParityContactRelation.Owner
                    && (_random.Next(3) == 0 || targets.Count == 0)
                        ? null
                    : targets.Count == 0 ? null
                    : PickFrom(targets);

                if (relation == ParityContactRelation.Owner && target is null)
                {
                    return;
                }

                contact.Values.Set(relation, target);
                _ops.Add(
                    new EditContactOp(name, id, Target: new ParityContactTarget(relation, target))
                );
            }
        }

        if (contact.State == RowState.Unchanged)
        {
            slot.Contacts[id] = contact with { State = RowState.Updated };
        }
    }

    private void RemoveContact()
    {
        var (name, slot) = UsableCustomerSlot();
        var held = slot
            .Contacts.Where(c => c.Value.State is RowState.Unchanged or RowState.Updated)
            .Select(c => c.Key)
            .ToList();

        if (held.Count == 0)
        {
            return;
        }

        var id = PickFrom(held);
        slot.Contacts[id] = slot.Contacts[id] with { State = RowState.Removed };
        _ops.Add(new MarkRemovedOp(name, $"contacts/{id}"));
    }

    /// <summary>
    /// 根に削除の印を付ける（列の途中では、子をグラフに持たず、注文も無く、どの行からも参照されない顧客だけ＝参照されている顧客の削除は
    /// 末尾の手）
    /// </summary>
    private void RemoveCustomerRoot()
    {
        var (name, slot) = UsableCustomerSlot();

        if (
            slot.Root == RowState.Removed
            || slot.Contacts.Count > 0
            || slot.Referrals.Count > 0
            || IsReferenced(slot.Id)
            || PendingTargets().Contains(slot.Id)
        )
        {
            return;
        }

        slot.Root = RowState.Removed;
        _ops.Add(new MarkRemovedOp(name, $"customers/{slot.Id}"));
    }

    private void SaveCustomer()
    {
        var (name, slot) = UsableCustomerSlot();

        if (!Consistent([slot]))
        {
            return;
        }

        _ops.Add(
            new SaveOp(
                name,
                new(Mode: Pick(ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite))
            )
        );
        ApplyCustomerSave(name, slot);
    }

    private void SaveManyCustomers()
    {
        var slots = CustomerSlotNames.Select(n => _customerSlots[n]).ToList();

        if (!Consistent(slots))
        {
            return;
        }

        _ops.Add(new SaveManyCustomersOp(CustomerSlotNames));

        foreach (var name in CustomerSlotNames)
        {
            ApplyCustomerSave(name, _customerSlots[name]);
        }
    }

    /// <summary>根だけを UpdateAsync で書く（紹介者の付け替えも書く）</summary>
    private void UpdateCustomer()
    {
        var (name, slot) = UsableCustomerSlot();

        if (slot.Root == RowState.Removed || (slot.Referrer is { } r && !_customers.ContainsKey(r)))
        {
            return;
        }

        _ops.Add(
            new UpdateCustomerOp(
                name,
                Pick(ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite)
            )
        );
        _referrers[slot.Id] = slot.Referrer;
        MarkStaleHoldingCustomer(slot.Id, except: name);
    }

    // ---- 保存の後のモデル ----

    /// <summary>
    /// 1 回の保存に載るグラフが、EF Core の fix-up でメモリを書き換えない形か：持っている行の外部キーが同じ保存の顧客を指すのは、
    /// 持っている関係の 1 本だけ。スロットどうしは行を共有しない
    /// </summary>
    private bool Consistent(IReadOnlyList<CustomerSlotModel> slots)
    {
        if (slots.Any(s => s.Stale))
        {
            return false;
        }

        var customers = slots.SelectMany(s => s.Referrals.Keys.Prepend(s.Id)).ToList();
        var contacts = slots.SelectMany(s => s.Contacts.Keys).ToList();

        if (
            customers.Distinct().Count() != customers.Count
            || contacts.Distinct().Count() != contacts.Count
        )
        {
            return false;
        }

        var held = customers.ToHashSet();
        var heldCodes = held.Select(c => _codes[c]).ToHashSet(StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            // 根の紹介者・紹介先の紹介者（紹介先は根を指す 1 本だけ）
            if (slot.Referrer is { } r && held.Contains(r))
            {
                return false;
            }

            foreach (var contact in slot.Contacts.Values)
            {
                var pointsToHeld =
                    contact.Values.CustomerReferences().Count(held.Contains)
                    + (contact.Values.Code is { } code && heldCodes.Contains(code) ? 1 : 0);

                if (pointsToHeld != 1)
                {
                    return false;
                }
            }

            // 外部キーが消えた顧客を指していない
            if (slot.Referrer is { } referrer && !_customers.ContainsKey(referrer))
            {
                return false;
            }

            if (
                slot.Contacts.Values.Any(c =>
                    c.Values.CustomerReferences().Any(id => !_customers.ContainsKey(id))
                    || (c.Values.Code is { } code && !_codes.ContainsValue(code))
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>顧客のグラフ保存が成功した後のモデル（列の途中の保存は、参照されていない根しか消さないので必ず成功する）</summary>
    private void ApplyCustomerSave(string name, CustomerSlotModel slot)
    {
        foreach (var (id, contact) in slot.Contacts.ToList())
        {
            switch (contact.State)
            {
                case RowState.Updated:
                    _contacts[id] = contact.Values.Copy();
                    slot.Contacts[id] = contact with { State = RowState.Unchanged };
                    MarkStaleHoldingContact(id, except: name);
                    break;
                case RowState.Removed:
                    _contacts.Remove(id);
                    slot.Contacts.Remove(id);
                    MarkStaleHoldingContact(id, except: name);
                    break;
            }
        }

        foreach (var (id, state) in slot.Referrals.ToList())
        {
            if (state == RowState.Updated)
            {
                slot.Referrals[id] = RowState.Unchanged;
                MarkStaleHoldingCustomer(id, except: name);
            }
        }

        switch (slot.Root)
        {
            case RowState.Removed:
                RemoveCustomer(slot.Id);
                _customerSlots.Remove(name);
                break;
            case RowState.Updated:
                _referrers[slot.Id] = slot.Referrer;
                slot.Root = RowState.Unchanged;
                MarkStaleHoldingCustomer(slot.Id, except: name);
                break;
        }
    }

    // ---- 列の末尾の手 ----

    /// <summary>
    /// 種の半分で、列の末尾に受け入れた差か直す区切りのある差を持つ手を 1 つ置き、続けて普通の手を 0〜2 回足す
    /// </summary>
    private void AppendTail()
    {
        if (_random.Next(3) == 0)
        {
            return;
        }

        var emitted = _random.Next(8) switch
        {
            0 => ParentDeleteWithChildren(),
            1 => OrphanInsertTail(),
            2 or 3 => ReferencedCustomerDeleteTail(),
            4 => AlternateKeyUpdateTail(),
            _ => HookTail(),
        };

        if (!emitted)
        {
            OrphanInsertTail();
        }

        var follow = _random.Next(3);

        for (var i = 0; i < follow; i++)
        {
            Next();
        }
    }

    private bool OrphanInsertTail()
    {
        OrphanInsert();

        return true;
    }

    /// <summary>
    /// 注文の無い顧客 X を、X を指す行（紹介先か連絡先）を別の顧客のグラフに持ったまま 1 回の保存で消す。依存行を編集したときは
    /// <see cref="ParityDeviation.DependentHeldParentDelete"/>（EF Core＝H5・P2-5-N1）、編集しないときは
    /// <see cref="ParityDeviation.ParentDeleteWithChildren"/>（インメモリ＝P1 だけ）の印を付ける
    /// </summary>
    private bool ReferencedCustomerDeleteTail()
    {
        var candidates = new List<(int Parent, Action Hold, int HolderId)>();

        foreach (var x in _customers.Keys.Where(c => _orders.Values.All(o => o.Customer != c)))
        {
            // 紹介先（紹介者の外部キーだけが X を指す顧客）
            foreach (var d in _referrers.Where(r => r.Value == x && r.Key != x).Select(r => r.Key))
            {
                var dependent = d;
                candidates.Add((x, () => HoldReferral(dependent), dependent));
            }

            // 連絡先（X を指す外部キーがちょうど 1 本で、別の顧客を owner か watcher で指す）
            foreach (
                var (c, contact) in _contacts.Where(c => c.Value.RelationsTo(x, _codes).Count == 1)
            )
            {
                var holder = contact.HolderOtherThan(x, _codes);

                if (holder is { } h)
                {
                    var id = c;
                    candidates.Add((x, () => HoldContact(id, h.Customer, h.Relation), h.Customer));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var (parent, hold, _) = PickFrom(candidates);
        var modify = _random.Next(2) == 0;
        _tailModify = modify;
        hold();
        _ops.Add(new LoadOp(TailTarget, "customers", parent));
        _ops.Add(new MarkRemovedOp(TailTarget, $"customers/{parent}"));
        _ops.Add(
            new DeviatingOp(
                new SaveManyCustomersOp([TailHolder, TailTarget]),
                modify
                    ? ParityDeviation.DependentHeldParentDelete
                    : ParityDeviation.ParentDeleteWithChildren
            )
        );
        ApplyReferencedDelete(parent);

        return true;
    }

    private bool _tailModify;

    private void HoldReferral(int dependent)
    {
        _ops.Add(new LoadOp(TailHolder, "customers", dependent));

        if (_tailModify)
        {
            _ops.Add(new EditCustomerOp(TailHolder, dependent, Note: $"t{_step}"));
        }
    }

    private void HoldContact(int contact, int holder, ParityContactRelation relation)
    {
        _ops.Add(new LoadOp(TailHolder, "customers", holder));
        _ops.Add(new AttachContactOp(TailHolder, $"customers/{holder}", contact, relation));

        if (_tailModify)
        {
            _ops.Add(new EditContactOp(TailHolder, contact, $"t{_step}"));
        }
    }

    /// <summary>
    /// QuickER 版の結果のモデル：X を指す行が SET NULL・SET DEFAULT の外部キーだけなら X を消してそれを空け、ほかの外部キーが残れば
    /// 保存全体が断られて何も変わらない
    /// </summary>
    private void ApplyReferencedDelete(int x)
    {
        var blocked =
            _referrers.Any(r => r.Value == x && r.Key != x)
            || _contacts.Values.Any(c =>
                c.Owner == x || c.Watcher == x || (c.Code is { } code && code == _codes[x])
            );

        if (blocked)
        {
            return;
        }

        foreach (var contact in _contacts.Values)
        {
            if (contact.Backup == x)
            {
                contact.Backup = null;
            }

            if (contact.Fallback == x)
            {
                contact.Fallback = null;
            }
        }

        RemoveCustomer(x);
    }

    /// <summary>
    /// 顧客の代替キー（code）を新しい値へ変え、グラフ保存か UpdateAsync で書く。参照されていれば
    /// <see cref="ParityDeviation.ReferencedKeyUpdate"/>（インメモリ＝P2・EF Core＝H1）、されていなければ
    /// <see cref="ParityDeviation.AlternateKeyUpdate"/>（EF Core＝H1）の印を付ける
    /// </summary>
    private bool AlternateKeyUpdateTail()
    {
        if (_customers.Count == 0)
        {
            return false;
        }

        // 半分は参照されている代替キーを選ぶ（連絡先の customer_code が指す顧客は少ない）
        var referencedKeys = _customers
            .Keys.Where(c => _contacts.Values.Any(contact => contact.Code == _codes[c]))
            .ToList();
        var x =
            referencedKeys.Count > 0 && _random.Next(2) == 0
                ? PickFrom(referencedKeys)
                : PickFrom([.. _customers.Keys]);
        var referenced = _contacts.Values.Any(c => c.Code == _codes[x]);
        var code = $"N{x}x{_step}";
        _ops.Add(new LoadOp(TailHolder, "customers", x));
        _ops.Add(new EditCustomerOp(TailHolder, x, Code: code));
        _ops.Add(
            new DeviatingOp(
                _random.Next(2) == 0
                    ? new SaveOp(TailHolder, new(Mode: TailMode()))
                    : new UpdateCustomerOp(TailHolder, TailMode()),
                referenced
                    ? ParityDeviation.ReferencedKeyUpdate
                    : ParityDeviation.AlternateKeyUpdate
            )
        );

        if (!referenced)
        {
            _codes[x] = code;
        }

        return true;
    }

    /// <summary>
    /// 新しい顧客 T を挿入し、T の更新か削除に Save フックの規則を当てて保存する（ほかの顧客の更新を同じ保存に載せることもある）。
    /// 書き換えてから飛ばす規則は <see cref="ParityDeviation.HookEditsSkippedRow"/>（EF Core＝H3・リモート＝P2-5-R1）、書き換えて
    /// 続ける削除は <see cref="ParityDeviation.HookEditsDeletedRow"/>（リモート＝P2-5-R1）の印を付ける
    /// </summary>
    private bool HookTail()
    {
        var t = Fresh(1, 9, _customers.ContainsKey);

        if (t is null)
        {
            return false;
        }

        var id = t.Value;
        var behavior = Pick(
            ParityHookBehavior.Skip,
            ParityHookBehavior.EditThenSkip,
            ParityHookBehavior.EditThenContinue,
            ParityHookBehavior.ThrowBefore,
            ParityHookBehavior.ThrowAfter
        );
        // 操作は更新・削除・挿入（挿入はグラフ保存の新しい根として足す）
        var operation = Pick("Update", "Delete", "Insert");
        var delete = operation == "Delete";
        var insert = operation == "Insert";
        _hookRule = new ParityHookRule($"{operation}:customers/{id}", behavior);

        if (insert)
        {
            _ops.Add(new NewRootOp(HookSlot, new CustomerRow(id, $"C{id}"), RowState.Added));
        }
        else
        {
            AddCustomerModel(id);
            _ops.Add(new InsertOp(new CustomerRow(id, $"C{id}")));
            _ops.Add(new LoadOp(HookSlot, "customers", id));
            _ops.Add(
                delete
                    ? new MarkRemovedOp(HookSlot, $"customers/{id}")
                    : new EditCustomerOp(HookSlot, id, Note: $"h{_step}")
            );
        }

        // 同じ保存にほかの顧客の更新も載せる（After で投げたときの Before の列の比べ方を通す）
        var others = _customers.Keys.Where(c => c != id).ToList();
        var withOther = others.Count > 0 && _random.Next(2) == 0;
        ParityOp save;

        if (withOther)
        {
            var other = PickFrom(others);
            _ops.Add(new LoadOp(TailTarget, "customers", other));
            _ops.Add(new EditCustomerOp(TailTarget, other, Note: $"o{_step}"));
            save = new SaveManyCustomersOp([HookSlot, TailTarget]);
        }
        else
        {
            save = new SaveOp(HookSlot, new(Mode: TailMode()));
        }

        var deviation = behavior switch
        {
            ParityHookBehavior.EditThenSkip => ParityDeviation.HookEditsSkippedRow,
            ParityHookBehavior.EditThenContinue when delete => ParityDeviation.HookEditsDeletedRow,
            _ => (ParityDeviation?)null,
        };
        _ops.Add(deviation is { } d ? new DeviatingOp(save, d) : save);

        // 書けたときのモデル（投げる規則は保存全体が戻る・飛ばす規則は T の操作だけが行われない）
        if (behavior == ParityHookBehavior.EditThenContinue)
        {
            if (delete)
            {
                RemoveCustomer(id);
            }

            if (insert)
            {
                AddCustomerModel(id);
            }
        }

        return true;
    }

    /// <summary>新しい顧客（紹介者なし・既定のコード）をモデルへ足す</summary>
    private void AddCustomerModel(int id)
    {
        _customers[id] = $"C{id}";
        _codes[id] = CustomerRow.DefaultCode(id);
        _referrers[id] = null;
    }

    // ---- 補助 ----

    /// <summary>末尾の単数の保存・UpdateAsync の並行性の扱い（版の無い表なので、どちらでも結果は同じはず）</summary>
    private ConcurrencyMode TailMode() =>
        Pick(ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite);

    /// <summary>顧客を消したときのモデル（顧客を持つスロットを使わなくする）</summary>
    private void RemoveCustomer(int id)
    {
        _customers.Remove(id);
        _codes.Remove(id);
        _referrers.Remove(id);
        MarkStaleHoldingCustomer(id, except: null);
    }

    /// <summary>顧客がどの行（注文・紹介先・連絡先）からも参照されていないか</summary>
    private bool IsReferenced(int id) =>
        _orders.Values.Any(o => o.Customer == id)
        || _referrers.Any(r => r.Value == id && r.Key != id)
        || _contacts.Values.Any(c => c.RelationsTo(id, _codes).Count > 0);

    /// <summary>どれかのスロットが、その顧客の根に削除の印を付けている（その顧客を新しく指さない）</summary>
    private bool RemovalPending(int id) =>
        _customerSlots.Values.Any(s => !s.Stale && s.Id == id && s.Root == RowState.Removed);

    /// <summary>新しく指してよい顧客（削除の印の付いた顧客を除く）</summary>
    private List<int> LiveCustomers() => _customers.Keys.Where(c => !RemovalPending(c)).ToList();

    /// <summary>スロットのグラフの編集が指している顧客（削除の印を付けてはいけない）</summary>
    private HashSet<int> PendingTargets()
    {
        var targets = new HashSet<int>();

        foreach (var slot in _customerSlots.Values.Where(s => !s.Stale))
        {
            if (slot.Referrer is { } r)
            {
                targets.Add(r);
            }

            foreach (var contact in slot.Contacts.Values)
            {
                targets.UnionWith(contact.Values.CustomerReferences());

                if (contact.Values.Code is { } code)
                {
                    targets.UnionWith(_codes.Where(c => c.Value == code).Select(c => c.Key));
                }
            }
        }

        return targets;
    }

    private int? MaybeCustomer()
    {
        var live = LiveCustomers();

        return _random.Next(2) == 0 || live.Count == 0 ? null : PickFrom(live);
    }

    private (string Name, CustomerSlotModel Slot) UsableCustomerSlot()
    {
        var usable = _customerSlots.Where(s => !s.Value.Stale).ToList();
        var pick = usable[_random.Next(usable.Count)];

        return (pick.Key, pick.Value);
    }

    /// <summary>その顧客を根か紹介先に持つスロットを、読み直すまで使わない</summary>
    private void MarkStaleHoldingCustomer(int id, string? except)
    {
        foreach (var (name, slot) in _customerSlots)
        {
            if (name != except && (slot.Id == id || slot.Referrals.ContainsKey(id)))
            {
                slot.Stale = true;
            }
        }
    }

    /// <summary>その連絡先を持つスロットを、読み直すまで使わない</summary>
    private void MarkStaleHoldingContact(int id, string? except)
    {
        foreach (var (name, slot) in _customerSlots)
        {
            if (name != except && slot.Contacts.ContainsKey(id))
            {
                slot.Stale = true;
            }
        }
    }

    /// <summary>連絡先の外部キーの値（モデル）</summary>
    private sealed class ContactModel(
        int owner,
        int? Watcher = null,
        int? Backup = null,
        int? Fallback = null,
        string? Code = null
    )
    {
        public int Owner { get; set; } = owner;

        public int? Watcher { get; set; } = Watcher;

        public int? Backup { get; set; } = Backup;

        public int? Fallback { get; set; } = Fallback;

        public string? Code { get; set; } = Code;

        public ContactModel Copy() => new(Owner, Watcher, Backup, Fallback, Code);

        public ContactRow ToRow(int id, string label) =>
            new(id, label, Owner, Watcher, Backup, Fallback, Code);

        /// <summary>顧客の主キーを指す外部キーの値（null を除く）</summary>
        public IEnumerable<int> CustomerReferences()
        {
            yield return Owner;

            foreach (var value in new[] { Watcher, Backup, Fallback })
            {
                if (value is { } v)
                {
                    yield return v;
                }
            }
        }

        /// <summary>顧客を指している関係</summary>
        public IReadOnlyList<ParityContactRelation> RelationsTo(
            int customer,
            IReadOnlyDictionary<int, string> codes
        )
        {
            var relations = new List<ParityContactRelation>();

            if (Owner == customer)
            {
                relations.Add(ParityContactRelation.Owner);
            }

            if (Watcher == customer)
            {
                relations.Add(ParityContactRelation.Watcher);
            }

            if (Backup == customer)
            {
                relations.Add(ParityContactRelation.Backup);
            }

            if (Fallback == customer)
            {
                relations.Add(ParityContactRelation.Fallback);
            }

            if (Code is { } code && codes.TryGetValue(customer, out var own) && own == code)
            {
                relations.Add(ParityContactRelation.CustomerCode);
            }

            return relations;
        }

        /// <summary>
        /// 顧客 <paramref name="x"/> 以外で、この連絡先を指す外部キーがちょうど 1 本の顧客と、その関係（連絡先を持たせる顧客。
        /// 無ければ null）
        /// </summary>
        public (int Customer, ParityContactRelation Relation)? HolderOtherThan(
            int x,
            IReadOnlyDictionary<int, string> codes
        )
        {
            foreach (var customer in CustomerReferences().Distinct().Where(c => c != x))
            {
                var relations = RelationsTo(customer, codes);

                if (relations.Count == 1)
                {
                    return (customer, relations[0]);
                }
            }

            return null;
        }

        public void Set(ParityContactRelation relation, int? customer)
        {
            switch (relation)
            {
                case ParityContactRelation.Owner:
                    Owner = customer!.Value;
                    break;
                case ParityContactRelation.Watcher:
                    Watcher = customer;
                    break;
                case ParityContactRelation.Backup:
                    Backup = customer;
                    break;
                case ParityContactRelation.Fallback:
                    Fallback = customer;
                    break;
            }
        }
    }

    /// <summary>スロットが持っている連絡先（持っている関係・状態・スロットの中の値）</summary>
    private sealed record HeldContact(ParityContactRelation Relation, ContactModel Values)
    {
        public RowState State { get; init; } = RowState.Unchanged;
    }

    /// <summary>顧客のスロットが持っているグラフのモデル</summary>
    private sealed class CustomerSlotModel(int id, int? referrer, string code)
    {
        public int Id { get; } = id;

        public RowState Root { get; set; } = RowState.Unchanged;

        /// <summary>スロットの中の根の紹介者</summary>
        public int? Referrer { get; set; } = referrer;

        /// <summary>スロットの中の根のコード</summary>
        public string Code { get; } = code;

        public Dictionary<int, HeldContact> Contacts { get; } = new();

        public Dictionary<int, RowState> Referrals { get; } = new();

        /// <summary>持っている行が消された・ほかのスロットに書かれた（読み直すまで使わない）</summary>
        public bool Stale { get; set; }
    }
}
