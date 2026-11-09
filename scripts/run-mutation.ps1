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
    範囲ごとのソリューションと環境変数は、下の表 $targetSettings の 1 か所だけが持つ（ワークフローには書かない）。
    表に無い範囲は、正本 stryker/QuickER.TestsOnly.slnx で、環境をそのまま流す。
    表の変数は Stryker を呼ぶ間だけ立て（外し）、終わったら元へ戻す。

    結果のフォルダには、HTML・JSON の報告と logs/ のログ（-L）が書かれる。要約はそのログから Safe Mode と Dubious を読む。
    このスクリプトは、範囲・ソリューション・立てた変数・外した変数を結果のフォルダの run-environment.json に書く（成否に依らず）。
    要約はそれを読んで「実行の環境」を出す。

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

# 範囲ごとのソリューションと環境変数（表に無い範囲は、テストだけのソリューション・環境はそのまま）。
# 変数の名前は Integration のテストの定数と同じ（ParityTests の行 mutation-script-variables-match-test-constants が照合する）。
# - provider-sync-planner：SyncPlanner の変異を殺すテストの多くは Integration の Docker の要らないテスト（同期計画の総当たり・期待の行）にあるので、
#   Integration を足したソリューションで流す。コンテナのフィクスチャは起動しない（QUICKER_SKIP_DOCKER。厳格モードの宣言 QUICKER_REQUIRE_DOCKER は
#   両立しないので外す）。同期計画の総当たりの種を減らす（既定の数のままだと、総当たりがほぼ全部の変異を通り、変異 1 回の検査が約 100 秒かかる）
$targetSettings = @{
    'provider-sync-planner' = @{
        Solution = 'stryker/QuickER.TestsWithIntegration.slnx'
        Set = [ordered]@{
            QUICKER_SKIP_DOCKER = '1'
            QUICKER_SYNC_FUZZ_SEEDS = '100'
            QUICKER_SYNC_CAPNET_SEEDS = '500'
        }
        Remove = @('QUICKER_REQUIRE_DOCKER')
    }
}

# リポジトリのルート（このスクリプトの親フォルダ）を基準に動かす
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configRelative = "stryker/$Target.json"
$settings = $targetSettings[$Target]
$solutionRelative = if ($settings) { $settings.Solution } else { 'stryker/QuickER.TestsOnly.slnx' }
$variablesToSet = if ($settings) { $settings.Set } else { [ordered]@{} }
# if の値は配列を展開する（1 つなら値・空なら $null）ので、型で配列に保つ（run-environment.json の removed を常に配列で書く）
[string[]]$variablesToRemove = @()

if ($settings) {
    $variablesToRemove = [string[]]$settings.Remove
}
$solutionSource = Join-Path $repositoryRoot $solutionRelative
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

# 表の変数を立てる（外す）前の値を取っておき、終わったら戻す（手元のシェルの環境を変えたまま残さない）
$savedVariables = [ordered]@{}

foreach ($name in @($variablesToSet.Keys) + $variablesToRemove) {
    $savedVariables[$name] = [System.Environment]::GetEnvironmentVariable($name)
}

Push-Location $repositoryRoot

try {
    [System.IO.File]::WriteAllText($solutionCopy, $rewritten)

    foreach ($name in $variablesToRemove) {
        [System.Environment]::SetEnvironmentVariable($name, $null)
    }

    foreach ($name in $variablesToSet.Keys) {
        [System.Environment]::SetEnvironmentVariable($name, $variablesToSet[$name])
    }

    dotnet stryker -f $configRelative -s $solutionCopyName -O $outputFullPath -c $Concurrency --skip-version-check -L

    if ($LASTEXITCODE -ne 0) {
        throw "Stryker が失敗した（終了コード $LASTEXITCODE）"
    }
}
finally {
    foreach ($name in $savedVariables.Keys) {
        [System.Environment]::SetEnvironmentVariable($name, $savedVariables[$name])
    }

    if (Test-Path -LiteralPath $solutionCopy) {
        Remove-Item -LiteralPath $solutionCopy -Force
    }

    # 実行の環境を結果のフォルダへ書く（要約の「実行の環境」。失敗した夜にも残す）
    $environmentRecord = [ordered]@{
        target = $Target
        solution = $solutionRelative
        set = $variablesToSet
        removed = $variablesToRemove
    }
    [System.IO.Directory]::CreateDirectory($outputFullPath) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $outputFullPath 'run-environment.json'),
        (ConvertTo-Json -InputObject $environmentRecord -Depth 3),
        [System.Text.UTF8Encoding]::new($false)
    )

    Pop-Location
}
