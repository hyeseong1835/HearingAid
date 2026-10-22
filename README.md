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

Discord 음성 채널을 받아 적으려면 내 봇을 하나 만들어 서버에 초대해야 합니다. 봇은 음성 채널에 들어가 **듣기만** 하므로 권한은 최소한만 줍니다.
단계별 화면 안내는 [Discord 가이드](docs/discord.md)를 보세요.

**1. 봇 만들기** — [Discord 개발자 포털](https://discord.com/developers/applications)에서 **New Application**을 만들고, **Bot** 메뉴에서 **Reset Token**으로 토큰을 받습니다.

**2. Bot 메뉴 설정**

| 항목 | 설정 | 이유 |
|---|---|---|
| Public Bot | 끔 (권장) | 다른 사람이 내 봇을 자기 서버에 초대하지 못하게 |
| Requires OAuth2 Code Grant | 끔 | 켜면 일반 초대 링크로 초대할 수 없음 |
| Presence Intent | 끔 | 쓰지 않음 |
| Server Members Intent | 끔 | 쓰지 않음. 화자 이름은 음성 채널 정보에서 가져옴 |
| Message Content Intent | 끔 | 메시지를 읽지 않음 |

HearingAid가 쓰는 Gateway 인텐트는 `Guilds`, `GuildVoiceStates` 두 가지이고, 둘 다 특권 인텐트가 아니라 따로 켤 필요가 없습니다.

**3. 초대 링크 권한** — **OAuth2 → URL Generator**에서 아래만 체크합니다.

| 구분 | 권한 | 필요 여부 | 이유 |
|---|---|---|---|
| Scopes | `bot` | ✅ 필요 | 봇으로 서버에 참가 |
| Scopes | `applications.commands` 등 | ❌ | 슬래시 명령을 쓰지 않음 |
| Bot Permissions | `View Channels` | ✅ 필요 | 음성 채널과 대상 사용자의 위치를 봄 |
| Bot Permissions | `Connect` | ✅ 필요 | 음성 채널에 들어감 |
| Bot Permissions | `Speak`, `Use Voice Activity` | ❌ | 말하지 않음 |
| Bot Permissions | `Send Messages`, `Read Message History` | ❌ | 채팅을 쓰거나 읽지 않음 |
| Bot Permissions | `Administrator`, `Move Members`, `Mute/Deafen Members` | ❌ | 주지 마세요 |

권한 정수는 `1049600`입니다. Application ID만 바꿔 아래 주소로 바로 초대할 수도 있습니다.

```
https://discord.com/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot&permissions=1049600
```

**4. 서버에서 확인**

- 봇을 초대하려면 그 서버의 **서버 관리하기** 권한이 필요합니다. 내 서버가 아니면 관리자에게 링크를 전달하세요.
- 음성 채널이나 카테고리에서 `@everyone`의 **채널 보기**·**연결**을 막아 두었다면, 채널 권한에 봇(또는 봇 역할)을 추가해 두 권한을 허용하세요.
- 봇이 **서버 헤드셋 음소거(Server Deafen)** 상태면 소리를 받을 수 없습니다.
- 인원 제한이 꽉 찬 채널에는 들어갈 수 없습니다.

**5. HearingAid 설정** — 프로필의 오디오 소스를 **Discord 음성 채널**로 고르고, `token`에 봇 토큰, `targetUserId`에 봇이 따라다닐 사용자(보통 나)의 ID를 넣습니다.
사용자 ID는 Discord **설정 → 고급 → 개발자 모드**를 켠 뒤 내 프로필을 우클릭 → **사용자 ID 복사**로 얻습니다.

> [!WARNING]
> 봇 토큰은 봇의 비밀번호입니다. 토큰은 앱 폴더의 `profiles\` 폴더에 평문으로 저장되므로 이 폴더를 다른 사람과 공유하지 마세요. 유출이 의심되면 개발자 포털에서 즉시 **Reset Token**을 누르세요.

## 🛠️ 개발

**필요:** Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
git clone https://github.com/Hyeseong1835/HearingAid.git
cd HearingAid
dotnet run
```

<details>
<summary><strong>릴리스 만들기</strong></summary>
<br/>

1. `HearingAid.csproj`의 `<Version>`을 올립니다.
2. 아래 명령을 실행합니다. [gh CLI](https://cli.github.com) 로그인이 필요합니다.

   ```powershell
   .\scripts\publish.ps1 -Publish
   ```

   `release\HearingAid-<버전>-win-x64.zip`을 만들고 GitHub 릴리스 `v<버전>`을 생성합니다. `-Publish` 없이 실행하면 zip만 만듭니다.

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

## 📜 라이선스

[MIT](LICENSE) © Hyeseong1835
