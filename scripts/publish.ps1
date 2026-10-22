# 릴리스용 zip을 만든다. 모델은 넣지 않는다 — 앱이 첫 실행 때 모델 다운로드 화면을 띄운다.
#   .\scripts\publish.ps1            → release\HearingAid-<버전>-win-x64.zip 생성
#   .\scripts\publish.ps1 -Publish   → 위 zip으로 GitHub 릴리스 v<버전> 생성 (gh CLI 로그인 필요)
# 버전은 HearingAid.csproj의 <Version>을 따른다. 새 릴리스 전에 그 값을 올릴 것.
# 앱의 업데이트 확인은 태그 v<버전>과 "HearingAid-*-win-x64.zip" 자산 이름에 의존한다.
param(
    [switch]$Publish
)

$Configuration = "Release"
$Runtime = "win-x64"
$OutDir = "dist"
$ReleaseDir = "release"

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$csproj = Get-Content "HearingAid.csproj"
$Version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $Version) { throw "HearingAid.csproj에 <Version>이 없습니다." }
$Tag = "v$Version"
Write-Host "버전 $Version 빌드"

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

dotnet publish HearingAid.csproj -c $Configuration -r $Runtime --self-contained true -p:DebugType=none -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 배포 폴더에 필수 파일이 다 있는지 확인 — 잘못된 zip을 만들기 전에 여기서 실패시킨다.
$required = @("HearingAid.exe", "appsettings.json", "silero_vad.onnx")
foreach ($rel in $required) {
    $p = Join-Path $OutDir $rel
    if (-not (Test-Path $p)) { throw "배포 결과물에 필수 파일이 없습니다: $p" }
}
# Whisper 네이티브 런타임: Vulkan(GPU)과 CPU 폴백이 둘 다 있어야 GPU 없는 PC에서도 동작한다.
foreach ($rt in @("runtimes\vulkan\win-x64", "runtimes\win-x64")) {
    $p = Join-Path $OutDir $rt
    if (-not (Get-ChildItem $p -Filter "*whisper*.dll" -ErrorAction SilentlyContinue)) {
        throw "배포 결과물에 Whisper 런타임이 없습니다: $p"
    }
}
$models = Get-ChildItem $OutDir -Recurse -Filter "ggml-*.bin"
if ($models) { throw "배포 결과물에 모델 파일이 들어 있습니다: $($models.FullName -join ', ')" }

New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
$zipPath = Join-Path $ReleaseDir "HearingAid-$Version-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# PowerShell 5.1(.NET Framework)의 Compress-Archive와 ZipFile.CreateFromDirectory는 zip 안 경로에
# 역슬래시를 쓴다. zip 표준은 슬래시이므로 항목을 하나씩 슬래시 경로로 추가한다.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$srcRoot = (Resolve-Path $OutDir).Path.TrimEnd('\') + '\'
$zipFull = Join-Path (Resolve-Path $ReleaseDir).Path (Split-Path $zipPath -Leaf)
$zip = [System.IO.Compression.ZipFile]::Open($zipFull, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $srcRoot -Recurse -File) {
        $entryName = $file.FullName.Substring($srcRoot.Length).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $zip.Dispose() }

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "패키징 완료: $zipPath ($sizeMb MB)"

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "gh CLI가 없습니다: https://cli.github.com" }
    # PowerShell 5.1은 Stop 상태에서 네이티브 stderr 리디렉션을 오류로 취급하므로 잠시 풀어 둔다.
    $ErrorActionPreference = "Continue"
    gh release view $Tag *> $null
    $exists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = "Stop"
    if ($exists) { throw "릴리스 $Tag 가 이미 있습니다. csproj의 <Version>을 올리세요." }

    gh release create $Tag $zipPath --title $Tag --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
    Write-Host "GitHub 릴리스 $Tag 생성 완료"
}
