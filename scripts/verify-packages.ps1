<#
.SYNOPSIS
    公開前の .nupkg を検証する（publish.yml の検証ステップ本体・ローカルでも同じものを実行できる）。

.DESCRIPTION
    dotnet pack 自身が守らない 3 点だけを見る。重複した検査は置かない。

      1. README / アイコンの「宣言」が生きているか
         PackageReadmeFile / PackageIcon が指すファイルが無ければ pack は NU5039 / NU5046 で落ちるが、
         プロパティごと消えた場合は黙って成功し、README もアイコンも無いパッケージが出てしまう。
      2. dotnet tool へ .pdb が混入していないか
         ExcludePdbsFromToolPackage ターゲットが壊れても pack は成功する。
      3. 全パッケージのバージョンが一致しているか（ロックステップ）
         版は Directory.Build.props の VersionPrefix 1 箇所で管理する規約だけで成立しており、
         どれか 1 つの csproj へ Version を書けば pack は成功して版だけがずれる。同梱 README は
         「コードを生成したツールと同じ版を参照せよ」と案内しているため、ずれると案内が破綻する。

    パッケージ本数は既知の 8 本（ランタイム 7＋CLI）で固定する。増減させたときは本スクリプトの
    ExpectedPackageCount も更新する（黙って通り抜けるより、更新を強制するほうが安全）。

.PARAMETER NupkgDirectory
    検証対象の .nupkg が置かれたディレクトリ。

.PARAMETER ExpectedPackageCount
    期待するパッケージ本数（既定 8）。

.EXAMPLE
    dotnet pack src/QuickER.Runtime/QuickER.Runtime.csproj -c Release --output ./artifacts/nupkg
    ./scripts/verify-packages.ps1 -NupkgDirectory ./artifacts/nupkg
#>
param(
    [Parameter(Mandatory = $true)][string]$NupkgDirectory,
    [int]$ExpectedPackageCount = 8
)

$ErrorActionPreference = 'Stop'

$packages = @(Get-ChildItem -Path $NupkgDirectory -Filter *.nupkg | Sort-Object Name)
$failures = [System.Collections.Generic.List[string]]::new()
$versions = [ordered]@{}

if ($packages.Count -ne $ExpectedPackageCount) {
    $failures.Add("パッケージ数が $ExpectedPackageCount 本ではありません（実際: $($packages.Count) 本）。pack 対象を増減させたなら ExpectedPackageCount も更新すること。")
}

foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' } | Select-Object -First 1

        if (-not $nuspecEntry) {
            $failures.Add("$($package.Name): .nuspec が見つかりません。")
            continue
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())

        try {
            $nuspec = [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.package.metadata
        $id = [string]$metadata.id
        $versions[$id] = [string]$metadata.version

        # ① README とアイコンの宣言（消してもパックは通ってしまう）
        foreach ($declaration in @(
                @{ Name = 'readme'; Value = [string]$metadata.readme },
                @{ Name = 'icon'; Value = [string]$metadata.icon })) {

            if ([string]::IsNullOrWhiteSpace($declaration.Value)) {
                $failures.Add("${id}: .nuspec に <$($declaration.Name)> の宣言がありません（csproj の PackageReadmeFile / PackageIcon が消えると、ファイル不在のエラーにならずに欠落したまま公開される）。")
            }
            elseif ($entryNames -notcontains $declaration.Value) {
                $failures.Add("${id}: <$($declaration.Name)> が指す $($declaration.Value) がパッケージに含まれていません。")
            }
        }

        # ② dotnet tool への .pdb 混入（ExcludePdbsFromToolPackage の回帰検知）
        $toolPdbs = @($entryNames | Where-Object { $_ -like 'tools/*' -and $_ -like '*.pdb' })

        if ($toolPdbs.Count -gt 0) {
            $failures.Add("${id}: tools 配下に .pdb が $($toolPdbs.Count) 個含まれています（例: $($toolPdbs[0])）。ExcludePdbsFromToolPackage が効いていない可能性があります。")
        }
    }
    finally {
        $zip.Dispose()
    }
}

# ③ ロックステップ（全パッケージが同一バージョン）
$distinctVersions = @($versions.Values | Sort-Object -Unique)

if ($distinctVersions.Count -gt 1) {
    $detail = ($versions.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
    $failures.Add("バージョンがロックステップになっていません（$detail）。版は Directory.Build.props の VersionPrefix 1 箇所で管理し、同梱 README は『コードを生成したツールと同じ版を参照せよ』と案内しているため、ずれると案内が破綻します。")
}

Write-Host '--- パッケージ検証 ---'
$versions.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-40} {1}" -f $_.Key, $_.Value) }

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "検証に失敗しました（$($failures.Count) 件）:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host ''
Write-Host "$($packages.Count) 本すべて検証を通過しました（README / アイコンの宣言・tools の .pdb 不在・バージョンの一致）。"
