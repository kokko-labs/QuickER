<#
.SYNOPSIS
    変異テスト（Stryker.NET）を 1 つの範囲で流す。mutation.yml と手元の実行の両方がこのスクリプトを呼ぶ。

.DESCRIPTION
    Stryker 5 は、渡したソリューションの中のプロジェクトのパスが ../ で始まると、テストを 0 件と数えて止まる。
    そこで、正本 stryker/QuickER.TestsOnly.slnx から ../ を外した写しをリポジトリのルートに一時の名前で作り、
    ルートを作業フォルダにして Stryker を呼び、終わったら（失敗しても）写しを消す。

    写しをルートに残すと、ルートで引数なしの dotnet build / dotnet test が MSB1011（ソリューションが 2 つ）で止まる。
    だから必ず消し、念のため .gitignore にも入れている。

    範囲・除外・点数で赤にしない設定・初回のテストの失敗で止める設定は stryker/<範囲>.json が持つ
    （Stryker は設定の solution を読まないので、ソリューションはここで -s に渡す）。
    結果のフォルダには、HTML・JSON の報告と logs/ のログ（-L）が書かれる。要約はそのログから Safe Mode と Dubious を読む。

    ネイティブコマンドの失敗は pwsh を止めないので、Stryker の終了コードを見て、0 でなければ失敗にする。

.PARAMETER Target
    範囲の名前（stryker/<Target>.json の拡張子を除いた名前。例：model）。

.PARAMETER OutputDirectory
    結果を書くフォルダ。相対パスは呼び出し元の作業フォルダから解く。

.PARAMETER Concurrency
    Stryker の並列度（テストを流す実行器の数）。既定値を持たせない（ホストのランナーと手元で適した値が違う）。

.EXAMPLE
    ./scripts/run-mutation.ps1 -Target model -OutputDirectory ./artifacts/stryker/model -Concurrency 8
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][int]$Concurrency
)

$ErrorActionPreference = 'Stop'

# リポジトリのルート（このスクリプトの親フォルダ）を基準に動かす
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configRelative = "stryker/$Target.json"
$solutionSource = Join-Path $repositoryRoot 'stryker/QuickER.TestsOnly.slnx'
$solutionCopyName = 'QuickER.TestsOnly.stryker.slnx'
$solutionCopy = Join-Path $repositoryRoot $solutionCopyName

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $configRelative) -PathType Leaf)) {
    throw "範囲の設定が無い: $configRelative"
}

# 相対パスは呼び出し元の作業フォルダから解く（下でルートへ移るので、その前に絶対パスにする）
$outputFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)

# 正本のパスはどれも ../ で始まる（ParityTests の行 stryker-tests-only-solution-projects が固定する）。
# 先頭の ../ を外すと、ルートから見た同じプロジェクトを指す
$solutionText = [System.IO.File]::ReadAllText($solutionSource)
$rewritten = $solutionText.Replace('Path="../', 'Path="')

if ($rewritten.Contains('../')) {
    throw "テストだけのソリューションに、先頭以外の ../ が残っている: $solutionSource"
}

Push-Location $repositoryRoot

try {
    [System.IO.File]::WriteAllText($solutionCopy, $rewritten)

    dotnet stryker -f $configRelative -s $solutionCopyName -O $outputFullPath -c $Concurrency --skip-version-check -L

    if ($LASTEXITCODE -ne 0) {
        throw "Stryker が失敗した（終了コード $LASTEXITCODE）"
    }
}
finally {
    if (Test-Path -LiteralPath $solutionCopy) {
        Remove-Item -LiteralPath $solutionCopy -Force
    }

    Pop-Location
}
