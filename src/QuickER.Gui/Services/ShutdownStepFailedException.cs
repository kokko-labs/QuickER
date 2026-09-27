namespace QuickER.Services;

/// <summary>終了の連鎖の 1 段が失敗したことを、段の名前とともに記録するための包み</summary>
/// <remarks>
/// メッセージは不具合報告に添える機械向け診断のため、UI 言語に追従させず英語で固定する。
/// </remarks>
public sealed class ShutdownStepFailedException : Exception
{
    /// <summary>失敗した段の名前と元の例外を指定して生成する</summary>
    /// <param name="step">失敗した段の識別子（英語）</param>
    /// <param name="innerException">その段が投げた例外</param>
    public ShutdownStepFailedException(string step, Exception innerException)
        : base($"Shutdown step failed: {step}", innerException)
    {
        Step = step;
    }

    /// <summary>失敗した段の識別子</summary>
    public string Step { get; }
}
