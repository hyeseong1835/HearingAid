$Configuration = "Release"
$Runtime = "win-x64"
$OutDir = "dist"
$ModelSource = "C:\Users\hyese\Documents\Dev\C#\HearingAid\whisper\ggml-large-v3-turbo.bin"

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

dotnet publish HearingAid.csproj -c $Configuration -r $Runtime --self-contained true -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 모델 파일은 용량이 커서(약 1.6GB) MSBuild 항목으로 다루지 않는다 — 빌드마다 글롭/해시 검사 비용이 들기 때문.
# 패키징 시점에만 여기서 직접 복사한다.
$whisperOut = Join-Path $OutDir "whisper"
New-Item -ItemType Directory -Force -Path $whisperOut | Out-Null
if (-not (Test-Path $ModelSource)) { throw "모델 파일을 찾을 수 없습니다: $ModelSource" }
Copy-Item $ModelSource -Destination (Join-Path $whisperOut "ggml-large-v3-turbo.bin") -Force

# 배포 폴더에 필수 파일이 다 있는지 확인 — 잘못된 zip을 만들기 전에 여기서 실패시킨다.
$required = @(
    "appsettings.json", "silero_vad.onnx",
    "whisper\whisper-server.exe", "whisper\ggml-large-v3-turbo.bin"
)
foreach ($rel in $required) {
    $p = Join-Path $OutDir $rel
    if (-not (Test-Path $p)) { throw "배포 결과물에 필수 파일이 없습니다: $p" }
}
if (-not (Test-Path (Join-Path $OutDir "profiles"))) {
    Write-Warning "profiles\ 폴더가 배포 결과물에 없습니다 (repo에 profiles가 없다면 정상)"
}

# 번들된 whisper-server.exe가 DLL 누락 없이 단독 실행되는지 간단히 확인.
Write-Host "whisper-server.exe 단독 실행 확인 중..."
$exe = Join-Path $whisperOut "whisper-server.exe"
$p = Start-Process -FilePath $exe -ArgumentList @("--help") -PassThru -NoNewWindow -Wait
if ($p.ExitCode -ne 0) { Write-Warning "whisper-server.exe --help 종료 코드 $($p.ExitCode) — DLL 누락 여부를 확인하세요" }

$zipPath = "$OutDir.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$OutDir\*" -DestinationPath $zipPath
Write-Host "배포 완료: $zipPath"
