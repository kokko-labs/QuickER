<#
.SYNOPSIS
    公開しようとしている .nupkg の版が nuget.org に公開済みでないかを確かめる（publish.yml の push の前段・ローカルでも同じものを実行できる）。

.DESCRIPTION
    publish.yml は dotnet nuget push に --skip-duplicate を付けている。途中で失敗した公開を
    やり直したとき、公開済みのパッケージを飛ばして残りだけを上げるためである。
    その代わり、VersionPrefix を上げ忘れて公開すると、全部が飛ばされて「何も上げずに成功」になる。
    このスクリプトはその場合だけを止める。

      - 全部の .nupkg が、その版ですでに公開済み → 失敗（VersionPrefix の上げ忘れ）
      - 一部だけが公開済み → 途中で失敗した公開のやり直しとみなし、公開済みの名前を出して続行
      - どれも公開されていない → 続行

    照会は nuget.org の flat container（{FeedBaseUrl}/{id の小文字}/index.json）で行う。
    404 はそのパッケージがまだ 1 つも公開されていないことを表す。
    それ以外の応答と通信の失敗は、判断できないので失敗にする（推測で公開へ進めない）。
    直前に上げたパッケージは反映が遅れて未公開に見えることがあるが、そのときは --skip-duplicate が飛ばす。

.PARAMETER NupkgDirectory
    公開する .nupkg が置かれたディレクトリ。

.PARAMETER FeedBaseUrl
    flat container のベース URL（既定は nuget.org）。

.EXAMPLE
    ./scripts/pack-packages.ps1 -OutputDirectory ./artifacts/nupkg
    ./scripts/check-unpublished-version.ps1 -NupkgDirectory ./artifacts/nupkg
#>
param(
    [Parameter(Mandatory = $true)][string]$NupkgDirectory,
    [string]$FeedBaseUrl = 'https://api.nuget.org/v3-flatcontainer'
)

$ErrorActionPreference = 'Stop'

$packages = @(Get-ChildItem -Path $NupkgDirectory -Filter *.nupkg | Sort-Object Name)

if ($packages.Count -eq 0) {
    Write-Host "確認する .nupkg が $NupkgDirectory に 1 つもありません。"
    exit 1
}

$published = [System.Collections.Generic.List[string]]::new()
$unpublished = [System.Collections.Generic.List[string]]::new()

foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

    try {
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' } | Select-Object -First 1

        if (-not $nuspecEntry) {
            Write-Host "$($package.Name): .nuspec が見つかりません。"
            exit 1
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())

        try {
            $nuspec = [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $zip.Dispose()
    }

    $id = [string]$nuspec.package.metadata.id
    $version = [string]$nuspec.package.metadata.version
    $label = "$id $version"
    $url = "$($FeedBaseUrl.TrimEnd('/'))/$($id.ToLowerInvariant())/index.json"

    $response = Invoke-RestMethod -Uri $url -SkipHttpErrorCheck -StatusCodeVariable statusCode

    if ($statusCode -eq 404) {
        $unpublished.Add($label)
    }
    elseif ($statusCode -eq 200 -and $response.PSObject.Properties.Name -contains 'versions') {
        # flat container の版は小文字の正規化済みの表記で並ぶ
        if (@($response.versions) -contains $version.ToLowerInvariant()) {
            $published.Add($label)
        }
        else {
            $unpublished.Add($label)
        }
    }
    else {
        Write-Host "$label の公開状況を確かめられませんでした（$url の応答: $statusCode・versions の一覧を含む JSON ではない）。"
        exit 1
    }
}

if ($unpublished.Count -eq 0) {
    Write-Host "全 $($published.Count) 本がこの版ですでに公開されています。Directory.Build.props の VersionPrefix を上げてから公開してください。"
    $published | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

if ($published.Count -gt 0) {
    Write-Host "一部がこの版ですでに公開されています。途中で失敗した公開のやり直しとみなし、これらは push で飛ばします:"
    $published | ForEach-Object { Write-Host "  - $_" }
}

Write-Host "未公開の $($unpublished.Count) 本を公開します:"
$unpublished | ForEach-Object { Write-Host "  - $_" }
