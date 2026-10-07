<#
.SYNOPSIS
    公開する NuGet パッケージ（ランタイム 7 種＋CLI）を pack し、続けて検証する（ci.yml / publish.yml の pack ステップ本体・ローカルでも同じものを実行できる）。

.DESCRIPTION
    公開するパッケージの一覧の正本は、このスクリプトの $projects だけに置く。
    ci.yml・publish.yml はこのスクリプトを呼ぶだけで、一覧を写さない（写しは片方だけ更新されて食い違う）。
    LicenseDocumentParityTests もこの一覧を公開パッケージの正本として読む。

    dotnet pack は 1 本ずつ実行し、その都度終了コードを見る。
    GitHub Actions の pwsh はネイティブコマンドの非 0 で止まらないため、
    検査しないと最後の 1 本以外の失敗がステップの成否に表れない。

    pack を終えたら verify-packages.ps1 を、期待本数＝この一覧の本数で呼ぶ。
    本数を別の場所に書かないので、一覧を増減させれば検証も追従する。

.PARAMETER OutputDirectory
    .nupkg の出力先ディレクトリ。

.PARAMETER ContinuousIntegrationBuild
    指定すると -p:ContinuousIntegrationBuild=true を付ける（ソースパスを /_/ へ正規化する決定的ビルド。公開用）。

.EXAMPLE
    ./scripts/pack-packages.ps1 -OutputDirectory ./artifacts/nupkg
#>
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$ContinuousIntegrationBuild
)

$ErrorActionPreference = 'Stop'

# 公開するパッケージ（ランタイム 7 種＋CLI）。パスはリポジトリのルートからの相対パス
$projects = @(
    'src/QuickER.Runtime/QuickER.Runtime.csproj',
    'src/QuickER.Runtime.SqlServer/QuickER.Runtime.SqlServer.csproj',
    'src/QuickER.Runtime.Sqlite/QuickER.Runtime.Sqlite.csproj',
    'src/QuickER.Runtime.EntityFrameworkCore/QuickER.Runtime.EntityFrameworkCore.csproj',
    'src/QuickER.Runtime.InMemory/QuickER.Runtime.InMemory.csproj',
    'src/QuickER.Runtime.AspNetCore/QuickER.Runtime.AspNetCore.csproj',
    'src/QuickER.Runtime.Sync/QuickER.Runtime.Sync.csproj',
    'src/QuickER.Cli/QuickER.Cli.csproj'
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packArguments = @('-c', 'Release', '--output', $OutputDirectory)

if ($ContinuousIntegrationBuild) {
    $packArguments += '-p:ContinuousIntegrationBuild=true'
}

foreach ($project in $projects) {
    # dotnet pack は .NET 8 以降の SDK 既定で Release を対象にするため、事前ビルドは pack に任せる
    # （--no-build だと Debug でしかビルドしていない CI では Release バイナリ不在で NU5026 になる）
    dotnet pack (Join-Path $repositoryRoot $project) @packArguments

    if ($LASTEXITCODE -ne 0) {
        Write-Host "pack に失敗しました: $project（終了コード: $LASTEXITCODE）"
        exit 1
    }
}

& (Join-Path $PSScriptRoot 'verify-packages.ps1') -NupkgDirectory $OutputDirectory -ExpectedPackageCount $projects.Count
exit $LASTEXITCODE
