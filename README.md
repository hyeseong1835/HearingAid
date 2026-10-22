# HearingAid

PC에서 나는 소리를 실시간으로 받아 적어 자막처럼 보여주는 Windows 앱입니다.
유튜브, 회의, 게임, Discord 음성 채널의 말을 글로 볼 수 있습니다.
음성 인식은 [Whisper](https://github.com/ggerganov/whisper.cpp)로 PC 안에서 처리하므로 소리가 외부로 전송되지 않습니다.

## 설치

1. [Releases](https://github.com/Hyeseong1835/HearingAid/releases/latest)에서 `HearingAid-<버전>-win-x64.zip`을 받습니다.
2. 원하는 폴더에 압축을 풀고 `HearingAid.exe`를 실행합니다.
   설정 파일을 앱 폴더에 저장하므로 `C:\Program Files` 같은 쓰기 제한 폴더는 피하세요.
3. 처음 실행하면 음성 인식 모델을 고르는 창이 뜹니다. 잘 모르겠다면 추천 모델(`large-v3-turbo`, 약 1.6GB)을 받으세요.
   저사양 PC라면 `small`이나 이름에 `q5`가 붙은 양자화 모델이 가볍습니다.

> 코드 서명이 없어 처음 실행할 때 "Windows의 PC 보호" 창이 뜰 수 있습니다. **추가 정보 → 실행**을 누르면 됩니다.

Windows 10 이상 x64가 필요합니다. .NET은 앱에 포함돼 있어 따로 설치하지 않아도 됩니다.

## 사용법

- **시작**을 누르면 소리를 듣고 받아 적기 시작합니다.
- **프로필**에서 어떤 소리를 들을지(오디오 소스)와 모델을 정합니다. 프로필은 여러 개 만들어 두고 바꿔 쓸 수 있습니다.
- **모델**에서 모델을 추가로 받거나 지우고, 기본 모델을 바꿉니다.
- **파형**은 들어오는 소리 크기를 보여줍니다. 받아 적기가 안 될 때 소리가 실제로 들어오는지 확인하세요.
- **내보내기**로 TXT/CSV 파일로 저장합니다.

### 오디오 소스

| 이름 | 용도 |
|---|---|
| PC 소리 전체 | 스피커·헤드폰으로 나오는 모든 소리. 기본값 |
| 특정 프로그램 소리만 | 고른 프로그램(chrome, Zoom 등)의 소리만. Windows 10 2004 이상 |
| 마이크 등 입력 장치 | 마이크, 또는 VB-CABLE 같은 가상 오디오 케이블 |
| Discord 음성 채널 | 봇이 음성 채널에 들어가 말하는 사람별로 나눠 받아 적음. 봇 토큰 필요 — [설정 방법](docs/discord.md) |

소리가 아주 작게 들어오면 자동 증폭(`autoGain`, 기본 켜짐)이 키워 줍니다.
조용한 곳의 마이크에서 말하지 않았는데 엉뚱한 문장이 나오면 그 프로필에서 자동 증폭을 끄세요.

프로필별, 그리고 화자(Discord)별로 다른 모델을 지정할 수 있습니다.

### GPU

Vulkan을 지원하는 GPU(NVIDIA, AMD, Intel)가 있으면 GPU로, 없으면 CPU로 자동 실행됩니다.
CPU만 쓰면 큰 모델은 느리므로 `small`이나 양자화 모델을 권장합니다.

### 업데이트

새 버전이 나오면 앱을 켤 때 알려 줍니다. **업데이트 후 재시작**을 누르면 받아서 교체합니다.
설정(`appsettings.json`), 프로필(`profiles/`), 모델(`models/`)은 그대로 유지됩니다.
`appsettings.json`에서 `"checkForUpdates": false`로 확인을 끌 수 있습니다.

> Discord 프로필에는 봇 토큰이 평문으로 저장됩니다. `profiles/` 폴더를 다른 사람과 공유하지 마세요.

## 개발

필요: .NET 10 SDK, Windows

```powershell
dotnet build
dotnet run
```

### 릴리스

1. `HearingAid.csproj`의 `<Version>`을 올립니다.
2. `scripts\publish.ps1 -Publish`
   - `release\HearingAid-<버전>-win-x64.zip`을 만들고 GitHub 릴리스 `v<버전>`을 생성합니다([gh CLI](https://cli.github.com) 필요).
   - `-Publish` 없이 실행하면 zip만 만듭니다.

앱의 업데이트 확인은 태그 이름 `v<버전>`과 자산 이름 `HearingAid-*-win-x64.zip`에 의존하므로 이 형식을 바꾸지 마세요.

### 구조

```
src/
  Program.cs                   진입점, 첫 실행 모델 선택, 업데이트 교체 모드(--apply-update)
  Core/
    Config.cs                  appsettings.json + profiles/*.json 병합, 오디오 소스 생성(리플렉션)
    TranscriberEngine.cs       오디오 소스 → VAD → Whisper 전사, 화자별 모델 선택
    Audio/
      IAudioSource.cs          오디오 소스 인터페이스. [AudioSource]를 붙이면 설정 화면에 자동으로 나타남
      AudioChannel.cs          자동 증폭 + Silero VAD로 발화 구간을 잘라 전사로 넘김
      AudioSources/            WASAPI 루프백, 프로세스 루프백, WaveIn, Discord(NetCord)
    Stt/                       Whisper 모델 래퍼, 모델 카탈로그·다운로드(ModelStore)
    Update/                    GitHub Releases 확인·다운로드·파일 교체
  UI/WinForms/                 메인 창, 프로필 설정, 모델 관리, 업데이트, 파형
```

## 라이선스

[MIT](LICENSE)

사용한 주요 라이브러리와 모델: [Whisper.net](https://github.com/sandrohanea/whisper.net)(MIT), [whisper.cpp](https://github.com/ggerganov/whisper.cpp) 모델(MIT), [Silero VAD](https://github.com/snakers4/silero-vad)(MIT), [NAudio](https://github.com/naudio/NAudio)(MIT), [NetCord](https://github.com/NetCordDev/NetCord)(MIT), ONNX Runtime(MIT)
