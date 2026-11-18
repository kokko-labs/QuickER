using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>計画の文の並びを歩いた 1 つの位置</summary>
/// <param name="Index">文の位置（並びの先頭が 0）</param>
/// <param name="Step">その位置の文</param>
/// <param name="Before">その文を当てる前の位置の状態</param>
/// <param name="Drops">その文が外す制約の身元（外さない文は <c>null</c>）</param>
/// <param name="Adds">その文が付ける制約の身元（付けない文は <c>null</c>）</param>
internal sealed record WalkPosition(
    int Index,
    SyncStep Step,
    ConstraintState Before,
    ConstraintIdentity? Drops,
    ConstraintIdentity? Adds
);
