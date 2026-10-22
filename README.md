<div align="center">

# HearingAid

### *PC에서 나는 소리를 실시간으로 받아 적는 Windows 자막 앱*

[![Latest Release](https://img.shields.io/github/v/release/Hyeseong1835/HearingAid?label=Latest&color=yellow)](https://github.com/Hyeseong1835/HearingAid/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Hyeseong1835/HearingAid/total)](https://github.com/Hyeseong1835/HearingAid/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B%20x64-1E9BFA?logo=windows)](#-설치)
[![License](https://img.shields.io/github/license/Hyeseong1835/HearingAid)](LICENSE)

[설치](#-설치) · [사용법](#-사용법) · [Discord 봇 설정](#-discord-봇-설정) · [문서](docs/app/) · [개발](#-개발)

</div>

## 소개

유튜브, 회의, 게임, Discord 음성 채널에서 나오는 말을 **실시간 자막처럼** 글로 보여줍니다.
음성 인식은 [Whisper](https://github.com/ggerganov/whisper.cpp)로 **PC 안에서** 처리하므로 소리가 외부 서버로 전송되지 않습니다.

**🚀 빠른 시작:** [Releases](https://github.com/Hyeseong1835/HearingAid/releases/latest)에서 zip을 받아 풀고 `HearingAid.exe`를 실행하세요. 처음 실행하면 모델 다운로드 화면이 안내합니다.

<div align="center">
  <img src="docs/images/main.png" alt="HearingAid 메인 화면" width="80%" />
  <br><em style="font-size:80%">메인 화면</em>
</div>

## ✨ 기능

- **🔊 여러 오디오 소스**

  | 오디오 소스 | 용도 |
  |---|---|
  | PC 소리 전체 | 스피커·헤드폰으로 나오는 모든 소리 |
  | 특정 프로그램 소리만 | 고른 프로그램(chrome, Zoom 등)의 소리만. 알림음이 섞이지 않음 |
  | 마이크 등 입력 장치 | 마이크, 또는 VB-CABLE 같은 가상 오디오 케이블 |
  | Discord 음성 채널 | 사용자가 봇이 참가한 서버에 있다면 자동으로 음성 채널에 들어가 **말하는 사람별로** 받아 적음 → [설정 방법](docs/discord.md) |

  한 프로필에 여러 소스를 넣어 **동시에** 받아 적을 수도 있습니다(예: 회의 앱 소리 + 마이크). 소스마다 별명을 붙여 목록과 내보내기에서 구분할 수 있습니다.

- **🗂️ 프로필**

  오디오 소스와 설정을 프로필로 저장해 두고 클릭 한 번으로 바꿔 씁니다.
  설정마다 설명이 붙어 있어 각 값이 무엇인지 화면에서 바로 알 수 있습니다.

  <div align="center">
    <img src="docs/images/profile.png" alt="프로필 설정 화면" width="70%" />
    <br><em style="font-size:80%">프로필 설정</em>
  </div>

- **🧠 모델 선택**

  가벼운 `tiny`부터 정확한 `large-v3`까지 앱 안에서 받아 둘 수 있습니다.
  **프로필별**, **화자별**로 다른 모델을 지정할 수 있습니다.

  <div align="center">
    <img src="docs/images/models.png" alt="모델 관리 화면" width="70%" />
    <br><em style="font-size:80%">모델 관리</em>
  </div>

- **⚡ GPU 가속**

  Vulkan을 지원하는 GPU(NVIDIA·AMD·Intel)를 자동으로 사용하고, 없으면 CPU로 동작합니다.

- **🔉 자동 증폭**

  앱이나 플레이어 볼륨이 낮아 소리가 아주 작게 들어와도 자동으로 키워서 받아 적습니다.

- **🔄 자동 업데이트**

  새 버전이 나오면 실행할 때 알려 주고, 버튼 하나로 업데이트합니다. 설정·프로필·모델은 그대로 유지됩니다.

- **📄 내보내기**

  받아 적은 내용을 TXT 또는 CSV로 저장합니다.

- **📖 내장 문서**

  설정 방법과 문제 해결을 앱 안의 **문서**(F1)에서 바로 볼 수 있습니다. 시작에 실패하면 문제 해결 문서로 안내합니다.

## 📦 설치

1. [Releases](https://github.com/Hyeseong1835/HearingAid/releases/latest)에서 `HearingAid-<버전>-win-x64.zip`을 받습니다.
2. 원하는 폴더에 압축을 풀고 `HearingAid.exe`를 실행합니다.
3. 처음 실행하면 음성 인식 모델을 고르는 창이 뜹니다. 잘 모르겠다면 **추천 모델 `large-v3-turbo`** (약 1.6GB)를 받으세요.

> [!TIP]
> 설정을 앱 폴더에 저장하므로 `C:\Program Files`처럼 쓰기가 제한된 폴더는 피하세요. 바탕 화면이나 문서 폴더면 충분합니다.

<details>
<summary><strong>설치 문제 해결</strong></summary>
<br/>

**"Windows의 PC 보호" 창이 떠요**

코드 서명이 없는 앱이라 처음 실행할 때 SmartScreen 경고가 뜹니다. **추가 정보 → 실행**을 누르세요.
압축을 풀기 전에 zip 파일을 **우클릭 → 속성 → "차단 해제" 체크 → 확인**하면 이 경고가 뜨지 않습니다.

**실행해도 아무 창도 뜨지 않아요**

64비트 Windows 10 이상인지 확인하세요. 백신 프로그램이 처음 보는 실행 파일을 격리했을 수도 있으니 백신의 검역소(격리) 목록도 확인하세요.

**설정이 저장되지 않거나 업데이트가 실패해요**

앱 폴더에 쓰기 권한이 없는 경우입니다. `C:\Program Files` 등에 풀었다면 바탕 화면이나 문서 폴더로 옮기세요.

**첫 실행 화면에서 모델 다운로드가 안 돼요**

모델은 `huggingface.co`에서 받습니다. 회사·학교 네트워크에서 막혀 있다면 다른 곳에서 `ggml-large-v3-turbo.bin`을 받아 앱 폴더의 `models\`에 넣고 실행하세요. 모델 목록은 [whisper.cpp 모델 저장소](https://huggingface.co/ggerganov/whisper.cpp/tree/main)에 있습니다.

**이전 버전에서 옮겨 오고 싶어요**

새 폴더에 푼 뒤 예전 폴더의 `appsettings.json`, `profiles\`, `models\`를 복사해 넣으면 설정과 모델이 그대로 이어집니다.

</details>

<details>
<summary><strong>시스템 요구 사항</strong></summary>
<br/>

| 항목 | 요구 사항 |
|---|---|
| OS | Windows 10 이상, 64비트 ("특정 프로그램 소리만"은 Windows 10 2004 이상) |
| .NET | 앱에 포함됨. 따로 설치하지 않아도 됨 |
| GPU | 선택. Vulkan 지원 GPU가 있으면 훨씬 빠름 |
| 디스크 | 앱 약 200MB + 모델 60MB ~ 3GB |

</details>

<details>
<summary><strong>모델 고르기</strong></summary>
<br/>

| 모델 | 크기 | 추천 상황 |
|---|---|---|
| `large-v3-turbo` | 1.6GB | **추천.** GPU가 있는 PC. 정확하고 빠름 |
| `large-v3-turbo-q5_0` | 574MB | 용량을 아끼고 싶을 때. 정확도는 거의 비슷 |
| `small`, `small-q5_1` | 190~490MB | GPU가 없거나 저사양 PC |
| `tiny`, `base` | 60~150MB | 빠른 테스트용. 정확도 낮음 |
| `large-v3` | 3.1GB | 가장 정확하지만 가장 느림 |
| `batisay-ko-turbo-q5_0` | 574MB | 한국어 특화 모델 ([BatiAI 라이선스](https://huggingface.co/batiai/batisay-ko-turbo), 개인·비영리 무료) |

모델은 언제든 메인 화면의 **모델** 버튼에서 추가하거나 지울 수 있습니다.

</details>

## 🧭 사용법

| 버튼 | 하는 일 |
|---|---|
| **시작 / 정지** | 받아 적기를 시작하거나 멈춤 |
| **프로필** (목록) | 사용할 프로필을 골라 바로 전환 (캡처를 정지한 상태에서) |
| **⚙ 편집** | 프로필 추가·삭제, 오디오 소스·모델·화자별 모델 설정 |
| **모델** | 모델 다운로드·삭제, 기본 모델 지정 |
| **파형** | 들어오는 소리 크기를 실시간으로 표시. 받아 적기가 안 될 때 확인용 |
| **내보내기** | TXT / CSV로 저장 |
| **문서** | 설정 방법과 문제 해결 (F1) |
| **업데이트** | 새 버전 확인 및 설치 |

### 프로필 만들기

1. **⚙ 편집 → + 새 프로필**로 프로필을 만듭니다.
2. **오디오 소스** 섹션에서 무엇을 들을지 고릅니다. 각 설정 아래에 설명이 표시됩니다.
3. 필요하면 **모델 재정의**나 **화자별 모델**을 지정합니다.
4. **저장 → 이 프로필 사용**을 누른 뒤 메인 화면에서 **시작**합니다.
   이후에는 메인 화면의 프로필 목록에서 바로 바꿀 수 있습니다.

처음 실행하면 프로필이 없으므로, 메인 화면의 안내에 따라 프로필을 만들고 오디오 소스를 고르세요.
오디오 소스가 정해지지 않은 프로필은 목록에 ⚠ 표시가 붙고 시작할 수 없습니다.

### 문서

각 설정의 뜻, 상황별 조정 방법, 문제 해결은 앱의 **문서**(F1)에 정리돼 있습니다. 같은 내용을 GitHub에서도 볼 수 있습니다.

| 문서 | 내용 |
|---|---|
| [시작하기](docs/app/getting-started.md) | 첫 설정, 화면 구성, 업데이트, 설정 파일 |
| [오디오 소스 설정](docs/app/audio-sources.md) | 오디오 소스별 설정, 자동 증폭, 말소리 감지 조정 |
| [모델](docs/app/models.md) | 모델 고르기, 프로필별·화자별 모델, GPU |
| [Discord 음성 채널 받아 적기](docs/discord.md) | 봇 만들기부터 사용까지 |
| [문제 해결](docs/app/troubleshooting.md) | 시작 오류, 받아 적지 않음, 엉뚱한 문장, 느림 등 |

## 🤖 Discord 봇 설정

Discord 음성 채널을 받아 적으려면 내 봇을 하나 만들어 서버에 초대합니다. 처음 한 번, 10분 정도 걸립니다.
화면을 따라 하는 단계별 안내는 **[Discord 가이드](docs/discord.md)** 를 보세요.

1. [Discord 개발자 포털](https://discord.com/developers/applications)에서 **New Application**을 만듭니다.
2. **Bot** 메뉴에서 **Reset Token**으로 토큰을 복사합니다.
3. **OAuth2 → OAuth2 URL Generator**에서 Scopes의 **`bot`**, Bot Permissions의 **View Channels**와 **Connect**를 체크하고 링크를 복사합니다.
4. 그 링크로 봇을 서버에 초대합니다.
5. Discord **설정 → 고급 → 개발자 모드**를 켜고, 내 프로필 우클릭 → **사용자 ID 복사**.
6. HearingAid 프로필의 오디오 소스를 **Discord 음성 채널**로 고르고 `token`과 `targetUserId`를 넣습니다.

권한이 필요한 이유, 인텐트, 설정 항목은 [Discord 상세 정보](docs/discord-details.md)에 정리했습니다.

> [!WARNING]
> 봇 토큰은 봇의 비밀번호입니다. 앱 폴더의 `profiles\` 폴더에 평문으로 저장되므로 이 폴더를 다른 사람과 공유하지 마세요.

## 🛠️ 개발

**필요:** Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
git clone https://github.com/Hyeseong1835/HearingAid.git
cd HearingAid
dotnet run
```

<details>
<summary><strong>브랜치와 기여 방식</strong></summary>
<br/>

작업용 `develop`과 배포용 `main` 두 브랜치를 씁니다. **`main`에 반영하는 것이 곧 배포**입니다.

```
feat/… ─PR(squash)→ develop ─PR(merge commit)→ main ─→ 개발 빌드 자동 배포
```

| 브랜치 | 역할 | 합칠 때 |
|---|---|---|
| `develop` (기본 브랜치) | 평소 작업을 합치는 곳 | CI 빌드 검사만. 배포 없음 |
| `main` | 배포한 버전 | [개발 빌드](.github/workflows/dev-build.yml)가 `dev` 프리릴리스로 올라감. 앱의 업데이트 채널을 "최신 개발 버전"으로 고른 사용자만 받음 |

- 작업은 `develop`에서 짧은 브랜치를 만들어 하고 PR로 합칩니다. 이름은 `feat/…`, `fix/…`, `docs/…`, `ci/…`처럼 종류를 앞에 붙입니다.
- 기능 PR(`… → develop`)은 **squash merge**로 합치고, 합친 브랜치는 지웁니다.
- 배포 PR(`develop → main`)은 **merge commit**으로 합칩니다. squash로 합치면 두 브랜치의 이력이 갈라져 다음 배포 때 충돌이 납니다.
- 두 브랜치 모두 직접 푸시하지 않고, PR마다 [CI](.github/workflows/ci.yml)가 Windows에서 빌드해 봅니다. 통과해야 합칠 수 있습니다.

</details>

<details>
<summary><strong>릴리스 만들기</strong></summary>
<br/>

1. `HearingAid.csproj`의 `<Version>`을 올리는 PR을 `develop`에 합칩니다.
2. `develop → main` 배포 PR을 merge commit으로 합칩니다(이때 개발 빌드도 함께 올라갑니다).
3. `main`을 체크아웃한 상태에서 아래 명령을 실행합니다. [gh CLI](https://cli.github.com) 로그인이 필요합니다.
   스크립트는 지금 작업 폴더의 코드를 빌드하므로, 다른 브랜치에서 실행하면 태그와 내용물이 어긋납니다.

   ```powershell
   .\scripts\publish.ps1 -Publish
   ```

   `release\HearingAid-<버전>-win-x64.zip`을 만들고 GitHub 릴리스 `v<버전>`을 생성합니다. `-Publish` 없이 실행하면 zip만 만듭니다.

개발 빌드의 버전은 `<Version>.<빌드 번호>`(예: `1.0.0.45`)입니다. 같은 정식 버전보다는 새롭고, 다음 정식 버전(`1.0.1`)보다는 오래된 것으로 비교됩니다.

> [!IMPORTANT]
> 앱의 업데이트 확인은 태그 `v<버전>`과 자산 이름 `HearingAid-*-win-x64.zip`에 의존합니다. 이 형식을 바꾸지 마세요.

</details>

<details>
<summary><strong>프로젝트 구조</strong></summary>
<br/>

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
  UI/WinForms/                 메인 창, 프로필 설정, 모델 관리, 업데이트, 파형, 문서(Markdig → WebBrowser)
docs/app/                      앱 '문서' 창에 내장되는 설명서 (EmbeddedResource)
docs/discord.md                Discord 봇 가이드 (앱에도 내장)
scripts/publish.ps1            릴리스 zip 생성 / GitHub 릴리스
```

새 오디오 소스는 `IAudioSource`를 구현하고 `[AudioSource("이름", DisplayName = ..., Description = ...)]`만 붙이면 됩니다.
생성자 매개변수가 그대로 설정 화면의 항목이 되고, `[Description]`은 항목 설명으로, `[PasswordPropertyText(true)]`는 가려진 입력란으로 표시됩니다.

</details>

## 🙏 감사

- [Whisper.net](https://github.com/sandrohanea/whisper.net) / [whisper.cpp](https://github.com/ggerganov/whisper.cpp) — 음성 인식
- [Silero VAD](https://github.com/snakers4/silero-vad) — 발화 구간 감지
- [NAudio](https://github.com/naudio/NAudio) — Windows 오디오 캡처
- [NetCord](https://github.com/NetCordDev/NetCord) — Discord 음성 수신
- [ONNX Runtime](https://github.com/microsoft/onnxruntime)
- [batisay-ko-turbo](https://huggingface.co/batiai/batisay-ko-turbo) — 한국어 특화 Whisper 모델 (선택 다운로드, BatiAI Community License v2)

## 📜 라이선스

[MIT](LICENSE) © Hyeseong1835
