using System.Text.Json.Nodes;

namespace HearingAid.Core;

public interface ITranscriber : IDisposable
{
    public delegate void OnCreatedEvent(int id, TranscriptRecode recode);
    public delegate void OnEditedEvent(int id, TranscriptRecode newRecode, TranscriptRecode oldRecode);
    public delegate void OnDeletedEvent(int id, TranscriptRecode oldRecode);
    event Action<string>? SetStatusAction;
    event OnCreatedEvent? OnCreatedText;
    event OnEditedEvent? OnEdited;
    event OnDeletedEvent? OnDeleted;
    event Action<ReadOnlyMemory<float>>? RawSamplesAvailable;

    Config Config { get; }
    bool IsRunning { get; }
    /// <summary>오디오 소스 중 하나라도 발화자(화자)를 구분해 보내는지. 아니면 UI가 화자 칼럼을 숨긴다.</summary>
    bool SpeakerRecognition { get; }

    ValueTask StartAsync();
    ValueTask StopAsync();
    IEnumerable<KeyValuePair<int, TranscriptRecode>> GetRecodes();
    void ApplyRuntimeOptions(JsonObject updates);
}