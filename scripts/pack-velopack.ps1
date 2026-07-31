<#
.SYNOPSIS
    QuickER GUI の配布物（Setup.exe / Portable zip）を Velopack でローカル生成する。

.DESCRIPTION
    release.yml（GitHub Actions）と同じ手順をローカルで実行する:

      1. publish  : QuickER.Gui を win-x64 で publish する（full=ランタイム同梱 / lite=フレームワーク依存）
      2. 同梱     : ライセンス文書 2 種を publish ディレクトリへコピーする（PolyForm NC の Notices 条項）
      3. vpk pack : チャンネル別（win-full / win-lite）に Setup.exe・Portable zip・更新パッケージを生成する

    出力先（既定 artifacts/velopack/releases-{full|lite}）は .gitignore 済み（artifacts/）。
    出力先に前バージョンの nupkg が残っていると差分更新パッケージ（-delta.nupkg）も自動生成される。
    Visual Studio から使う場合は「ツール → 外部ツール」にこのスクリプトを登録すると 1 クリックで実行できる。

    前提: vpk（dotnet tool install --global vpk）がインストール済みであること。
    注意: PublishSingleFile は使わない（Velopack の差分更新はルーズファイル前提）。

.PARAMETER Channel
    生成するチャンネル。full（ランタイム同梱・既定）/ lite（フレームワーク依存）/ both（両方）。

.PARAMETER Version
    パッケージバージョン。省略時は Directory.Build.props の VersionPrefix（CI と同じ単一ソース）。
    自動更新の E2E 検証で仮の新バージョンを作るときだけ明示指定する。

.PARAMETER OutputRoot
    出力先ルート。既定はリポジトリ直下の artifacts/velopack。

.EXAMPLE
    ./scripts/pack-velopack.ps1                       # full を VersionPrefix で生成
    ./scripts/pack-velopack.ps1 -Channel both         # full と lite を両方生成
    ./scripts/pack-velopack.ps1 -Version 0.1.1        # 自動更新 E2E 用に仮バージョンで生成

.NOTES
    自動更新の E2E 検証手順: v(現行) を生成して Setup.exe をインストール → -Version で新バージョンを
    同じ出力先へ生成 → 環境変数 QUICKER_UPDATE_FEED に出力先パスを入れてインストール版を起動する。
#>
[CmdletBinding()]
param(
    [ValidateSet('full', 'lite', 'both')]
    [string]$Channel = 'full',

    [string]$Version,

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

# リポジトリ直下（このスクリプトの親ディレクトリ）を基準に動かす。呼び出し元の作業ディレクトリに依存させない
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    # --- 前提確認: vpk がインストールされているか ---
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "vpk が見つかりません。次でインストールしてください: dotnet tool install --global vpk"
    }

    # --- バージョン解決（省略時は CI と同じく Directory.Build.props の VersionPrefix を単一ソースとする） ---
    if (-not $Version) {
        [xml]$props = Get-Content Directory.Build.props
        $Version = ($props.Project.PropertyGroup.VersionPrefix | Where-Object { $_ }) | Select-Object -First 1

        if (-not $Version) {
            throw "VersionPrefix が Directory.Build.props から取得できませんでした"
        }
    }

    if (-not $OutputRoot) {
        $OutputRoot = Join-Path $repoRoot 'artifacts/velopack'
    }

    # チャンネルごとの publish 設定（release.yml と対応: full=self-contained / lite=フレームワーク依存）
    $targets = switch ($Channel) {
        'full' { @('full') }
        'lite' { @('lite') }
        'both' { @('full', 'lite') }
    }

    foreach ($target in $targets) {
        $selfContained = if ($target -eq 'full') { 'true' } else { 'false' }
        $publishDir = Join-Path $OutputRoot "publish-$target"
        $releaseDir = Join-Path $OutputRoot "releases-$target"

        # --- 1. publish（前回の残骸が混ざらないよう publish ディレクトリは毎回作り直す） ---
        Write-Host "[$target 1/3] publish しています（self-contained=$selfContained）..." -ForegroundColor Cyan

        if (Test-Path $publishDir) {
            Remove-Item $publishDir -Recurse -Force
        }

        dotnet publish src/QuickER.Gui/QuickER.Gui.csproj -c Release -r win-x64 --self-contained $selfContained -o $publishDir

        if ($LASTEXITCODE -ne 0) {
            throw "publish に失敗しました（exit $LASTEXITCODE）"
        }

        # --- 2. ライセンス文書を同梱（PolyForm NC の Notices 条項＝条文をコピーの受領者へ渡す義務。解説ガイド英日も同梱） ---
        Write-Host "[$target 2/3] ライセンス文書を同梱しています..." -ForegroundColor Cyan
        Copy-Item LICENSE, LICENSE-NC.md, LICENSING.md, LICENSING.ja.md -Destination $publishDir

        # --- 3. vpk pack（Setup.exe / Portable zip / 更新パッケージ・メタを生成する） ---
        Write-Host "[$target 3/3] vpk pack しています（channel=win-$target / version=$Version）..." -ForegroundColor Cyan
        vpk pack --packId QuickER --packVersion $Version --packDir $publishDir --mainExe QuickER.exe --channel "win-$target" --outputDir $releaseDir

        if ($LASTEXITCODE -ne 0) {
            throw "vpk pack に失敗しました（exit $LASTEXITCODE）"
        }
    }

    # --- 生成結果の一覧表示 ---
    Write-Host ''
    Write-Host '生成された配布物:' -ForegroundColor Green

    foreach ($target in $targets) {
        Get-ChildItem (Join-Path $OutputRoot "releases-$target") -File |
            Select-Object @{ N = 'ファイル'; E = { $_.Name } }, @{ N = 'サイズ(MB)'; E = { [math]::Round($_.Length / 1MB, 1) } } |
            Format-Table -AutoSize
    }

    Write-Host "出力先: $OutputRoot" -ForegroundColor Green
}
finally {
    Pop-Location
}
