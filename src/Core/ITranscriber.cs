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

    ValueTask StartAsync();
    ValueTask StopAsync();
    IEnumerable<KeyValuePair<int, TranscriptRecode>> GetRecodes();
    void ApplyRuntimeOptions(JsonObject updates);
}