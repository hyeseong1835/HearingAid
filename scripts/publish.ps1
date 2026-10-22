# 릴리스용 zip을 만든다. 모델은 넣지 않는다 — 앱이 첫 실행 때 모델 다운로드 화면을 띄운다.
#   .\scripts\publish.ps1            → release\HearingAid-<버전>-win-x64.zip 생성
#   .\scripts\publish.ps1 -Publish   → 위 zip으로 GitHub 릴리스 v<버전> 생성 (gh CLI 로그인 필요)
#   .\scripts\publish.ps1 -BuildNumber 45 → 개발 빌드 HearingAid-<버전>.45-win-x64.zip (CI의 dev-build 워크플로가 씀)
# 버전은 HearingAid.csproj의 <Version>을 따른다. 새 릴리스 전에 그 값을 올릴 것.
# 앱의 업데이트 확인은 태그 v<버전>과 "HearingAid-*-win-x64.zip" 자산 이름에 의존한다.
# 개발 빌드는 버전 4번째 자리에 빌드 번호를 붙여, 같은 정식 버전보다는 새롭고 다음 정식 버전보다는 오래된 것으로 비교된다.
param(
    [switch]$Publish,
    [int]$BuildNumber = 0
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
if ($BuildNumber -gt 0) {
    if ($Publish) { throw "-BuildNumber(개발 빌드)와 -Publish(정식 릴리스)는 함께 쓸 수 없습니다." }
    $Version = "$Version.$BuildNumber"
}
Write-Host "버전 $Version 빌드"

# 정식 릴리스는 GitHub의 main과 똑같은 코드로만 만든다. 다른 브랜치나 고치던 파일로 빌드하면
# 태그가 가리키는 커밋과 zip 내용물이 어긋난다.
if ($Publish) {
    if (git status --porcelain) { throw "커밋하지 않은 변경이 있습니다. 정리한 뒤 다시 실행하세요." }
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne "main") { throw "main 브랜치에서 실행하세요. (지금: $branch)" }
    git fetch --quiet origin main
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
    $Commit = git rev-parse HEAD
    if ($Commit -ne (git rev-parse origin/main)) { throw "로컬 main이 origin/main과 다릅니다. git pull(또는 push)로 맞춘 뒤 다시 실행하세요." }
}

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

dotnet publish HearingAid.csproj -c $Configuration -r $Runtime --self-contained true -p:DebugType=none "-p:Version=$Version" -o $OutDir
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

# 배포 파일 목록. 앱의 자동 업데이트(UpdateApplier)가 이전 버전 목록과 비교해, 새 버전에서 빠진 파일을 지운다.
$outRoot = (Resolve-Path $OutDir).Path.TrimEnd('\') + '\'
$manifest = Get-ChildItem $outRoot -Recurse -File |
    ForEach-Object { $_.FullName.Substring($outRoot.Length).Replace('\', '/') }
$manifest += "app-files.txt"
[System.IO.File]::WriteAllLines((Join-Path $outRoot "app-files.txt"), [string[]]$manifest)

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

    # --target이 없으면 태그가 GitHub 기본 브랜치의 그 순간 HEAD에 붙는다. 빌드한 커밋에 정확히 붙인다.
    gh release create $Tag $zipPath --title $Tag --generate-notes --target $Commit
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
    Write-Host "GitHub 릴리스 $Tag 생성 완료"
}
