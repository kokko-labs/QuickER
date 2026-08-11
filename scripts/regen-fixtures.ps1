<#
.SYNOPSIS
    Scriban テンプレート由来の生成物（ランタイムパッケージ用ソース・固定フィクスチャ・サンプル生成物）を再生成する。

.DESCRIPTION
    テンプレート（src/QuickER.CodeGen.CSharp/Templates/*.scriban）を変更したあとに実行する。
    処理は 3 段階で、途中で失敗したらそこで止まる:

      1. 再生成  : 環境変数 QUICKER_REGEN_FIXTURES=1 でドリフトテストを走らせ、チェックイン済み生成物を上書きする
      2. 検証    : 環境変数なしで同じテストを流し、ドリフトなし（緑）を確認する
      3. 差分表示: 変更された生成物を一覧表示する（意図した差分だけかを人間が確認するため）

    再生成の実処理はテストと同一経路（FixtureDriftHarness）に集約されているため、
    「ドリフト検知の期待値を作る経路」と「再生成の経路」が構造上ずれない。

    生成物（*.g.cs 等）は手で編集しないこと。編集してもドリフトテストで検出され、次の再生成で失われる。

.PARAMETER SkipVerify
    再生成後の検証（手順 2）を省略する。差分を先に確認したい場合などに使う（通常は指定しない）。

.EXAMPLE
    ./scripts/regen-fixtures.ps1

.NOTES
    再生成後は必ず `dotnet build QuickER.slnx` と `csharpier format .`、全テストの実行まで行うこと。
#>
[CmdletBinding()]
param(
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'

# リポジトリ直下（このスクリプトの親ディレクトリ）を基準に動かす。呼び出し元の作業ディレクトリに依存させない
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

# 再生成対象（ドリフトテストが書き出すパス。差分表示の対象を絞るために列挙する）
$generatedPaths = @(
    'src/QuickER.Runtime',
    'src/QuickER.Runtime.SqlServer',
    'src/QuickER.Runtime.Sqlite',
    'src/QuickER.Runtime.EntityFrameworkCore',
    'src/QuickER.Runtime.InMemory',
    'src/QuickER.Runtime.AspNetCore',
    'tests/QuickER.Tests/GeneratedFixture',
    'samples'
)

$testProject = 'tests/QuickER.Tests/QuickER.Tests.csproj'
$driftFilter = 'FullyQualifiedName~Drift'

try {
    # --- 1. 再生成（環境変数を立てるとドリフト検知が上書きモードに切り替わる） ---
    Write-Host '[1/3] 生成物を再生成しています...' -ForegroundColor Cyan

    try {
        $env:QUICKER_REGEN_FIXTURES = '1'
        dotnet test $testProject --filter $driftFilter
    }
    finally {
        # 例外時も必ず環境変数を戻す（立てたままだと以降のテストがドリフトを検知できなくなる）
        Remove-Item Env:QUICKER_REGEN_FIXTURES -ErrorAction SilentlyContinue
    }

    if ($LASTEXITCODE -ne 0) {
        throw "再生成に失敗しました（exit $LASTEXITCODE）。テンプレートの構文エラー等を確認してください。"
    }

    # --- 2. 検証（環境変数なし＝照合モードで緑になることを確認する） ---
    if ($SkipVerify) {
        Write-Host '[2/3] 検証をスキップしました（-SkipVerify）。' -ForegroundColor Yellow
    }
    else {
        Write-Host '[2/3] 再生成後のドリフトなしを検証しています...' -ForegroundColor Cyan
        dotnet test $testProject --filter $driftFilter

        if ($LASTEXITCODE -ne 0) {
            throw "再生成後もドリフトが検出されました（exit $LASTEXITCODE）。生成が非決定的になっている可能性があります。"
        }
    }

    # --- 3. 差分表示（意図した生成物だけが変わったかを人間が確認する） ---
    Write-Host '[3/3] 生成物の差分:' -ForegroundColor Cyan
    $diff = git diff --stat -- $generatedPaths

    if ([string]::IsNullOrWhiteSpace($diff)) {
        Write-Host '  変更なし（テンプレート変更が生成物へ影響していません）' -ForegroundColor Green
    }
    else {
        $diff
        Write-Host ''
        Write-Host '  上記が意図した差分かを必ず確認してください（想定外のパッケージが変わっていないか）。' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host '完了。次に実行してください: dotnet build QuickER.slnx / csharpier format . / dotnet test QuickER.slnx' -ForegroundColor Green
}
finally {
    Pop-Location
}
