using System.Text.Json.Nodes;

namespace HearingAid.Core.Audio;

/// <summary>
/// 16kHz mono float PCM 오디오를 공급하는 소스. NAudio 캡처, NetCord 등 어떤 방식으로
/// 오디오를 수신하든 이 인터페이스만 구현하면 TranscriberEngine과 연결할 수 있다.
/// </summary>
public interface IAudioSource : IDisposable
{
    delegate void SetAudioSourceStateAction(string msg);
    event SetAudioSourceStateAction? SetStatus;

    delegate void CreateAudioChannelEventHandler(object param, IAudioSource source, AudioChannel channel);
    public object? AudioChannelCreatedParam { get; set; }
    event CreateAudioChannelEventHandler? AudioChannelCreated;

    delegate void RemoveAudioChannelEventHandler(object param, IAudioSource source, AudioChannel channel);
    public object? AudioChannelRemovedParam { get; set; }
    event RemoveAudioChannelEventHandler? AudioChannelRemoved;

    string Description { get; }
    bool IsRunning { get; }
    bool SpeakerRecognition { get; }

    IEnumerable<AudioChannel> Channels { get; }

    ValueTask StartAsync();
    ValueTask StopAsync();

    /// <summary>이 오디오 소스가 실행 중에 스스로 학습한 설정(예: 화자 이름 자동 채움)이 생겨
    /// 저장이 필요할 때 발생한다. 기본 구현은 절대 발생시키지 않는다.</summary>
    event Action? SettingsChanged
    {
        add { }
        remove { }
    }

    /// <summary>런타임 중 스스로 학습/변경한 생성자 설정 값을, 그 매개변수 이름을 key로 하는
    /// JSON 객체로 돌려준다. 저장할 게 없으면 null. 기본 구현은 항상 null.</summary>
    JsonObject? ExportRuntimeOptions() => null;

    /// <summary>UI 등에서 사용자가 직접 고친 설정을, 재시작 없이 지금 실행 중인 소스에
    /// 바로 반영한다. 기본 구현은 아무 것도 하지 않는다.</summary>
    void ApplyRuntimeOptions(JsonObject updates) { }
}
