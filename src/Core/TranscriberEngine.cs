using System.Text.Json.Nodes;

using HearingAid.Core.Stt;
using HearingAid.Core.Audio;

namespace HearingAid.Core;

public class TranscriberEngine : ITranscriber
{
    public event Action<string>? SetStatusAction;
    public event ITranscriber.OnCreatedEvent? OnCreatedText;
    public event ITranscriber.OnEditedEvent? OnEdited;
    public event ITranscriber.OnDeletedEvent? OnDeleted;
    public event Action<ReadOnlyMemory<float>>? RawSamplesAvailable;

    public Config Config { get; }
    public bool IsRunning { get; private set; }

    readonly IAudioSource[] _audioSources;
    readonly Dictionary<IAudioSource, Action> _settingsChangedHandlers = new();
    WhisperModel[]? _whisperModels;

    Dictionary<int, TranscriptRecode> _recodes;
    int _nextRecodeId = 1000;

    readonly AudioChannel.SamplesAvailableHandler _onSamplesHandler =
        async (object param, AudioChannel channel, string? speaker, ReadOnlyMemory<float> samples) =>
        {
            TranscriberEngine engine = (TranscriberEngine)param;

            int recodeId = engine._nextRecodeId++;

            engine.CreateRecode(
                recodeId,
                new TranscriptRecode(
                    DateTime.Now, 
                    speaker, null
                )
            );
            if (channel.whisperModel == null)
                return;

            try
            {
                await channel.whisperModel.TranscribeAsync(
                    samples, 
                    new Tuple<TranscriberEngine, int>(engine, recodeId), 
                    static (param, text) =>
                    {
                        Tuple<TranscriberEngine, int> tuple = (Tuple<TranscriberEngine, int>)param!;
                        TranscriberEngine engine = tuple.Item1;
                        int recodeId = tuple.Item2;
                        engine.CombineRecodeText(recodeId, text);
                    }
                );
            }
            catch (Exception ex)
            {
                engine.SetStatusAction?.Invoke($"전사 오류: {ex.Message}");
            }
        };
    readonly IAudioSource.CreateAudioChannelEventHandler _createAudioChannelEventHandler =
        (object param, IAudioSource source, AudioChannel channel) =>
        {
            TranscriberEngine engine = (TranscriberEngine)param;

            channel.samplesAvailableHandlerParam = param;
            channel.samplesAvailableHandler += engine._onSamplesHandler;
            channel.whisperModel ??= engine._whisperModels?[0];
            channel.rawSamplesAvailable += samples => engine.RawSamplesAvailable?.Invoke(samples);
        };
    readonly IAudioSource.RemoveAudioChannelEventHandler _removeAudioChannelEventHandler =
        (object param, IAudioSource source, AudioChannel channel) =>
        {
            TranscriberEngine engine = (TranscriberEngine)param;

            channel.samplesAvailableHandlerParam = null;
            channel.samplesAvailableHandler -= engine._onSamplesHandler;
            channel.Dispose();
        };
    public TranscriberEngine(Config cfg, IAudioSource[] audioSource)
    {
        Config = cfg;
        this._audioSources = audioSource;
        this._recodes = new Dictionary<int, TranscriptRecode>();
    }

    #region 생명주기

    public async ValueTask StartAsync()
    {
        if (IsRunning) return;

        if (_whisperModels == null)
        {
            AddWhisperModel(new WhisperModel(Config.WhisperModel, Config.Language, Config.Threads));
            SetStatusAction?.Invoke("Whisper 모델 로드 완료");
        }

        foreach (IAudioSource audioSource in _audioSources)
        {
            audioSource.AudioChannelCreatedParam = this;
            audioSource.AudioChannelRemovedParam = this;
            audioSource.AudioChannelCreated += _createAudioChannelEventHandler;
            audioSource.AudioChannelRemoved += _removeAudioChannelEventHandler;
            audioSource.SetStatus += OnAudioSourceStatus;

            Action settingsChangedHandler = () => OnAudioSourceSettingsChanged(audioSource);
            _settingsChangedHandlers[audioSource] = settingsChangedHandler;
            audioSource.SettingsChanged += settingsChangedHandler;

            await audioSource.StartAsync();

            // knownUsers처럼 시작 전에 이미 채널을 갖고 있던 오디오 소스는 AudioChannelCreated가
            // 발생하지 않으므로, 아직 안 엮인 채널을 여기서 직접 훑어 연결한다.
            foreach (AudioChannel channel in audioSource.Channels)
                if (channel.samplesAvailableHandlerParam is null)
                    _createAudioChannelEventHandler(this, audioSource, channel);
        }

        IsRunning = true;
    }
    public async ValueTask StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        foreach (IAudioSource audioSource in _audioSources)
        {
            await audioSource.StopAsync();

            audioSource.AudioChannelCreated -= _createAudioChannelEventHandler;
            audioSource.AudioChannelRemoved -= _removeAudioChannelEventHandler;
            audioSource.SetStatus -= OnAudioSourceStatus;

            if (_settingsChangedHandlers.Remove(audioSource, out Action? settingsChangedHandler))
                audioSource.SettingsChanged -= settingsChangedHandler;
        }

        SetStatusAction?.Invoke("정지됨");
    }
    void OnAudioSourceStatus(string msg) => SetStatusAction?.Invoke(msg);

    void OnAudioSourceSettingsChanged(IAudioSource source)
    {
        JsonObject? updates = source.ExportRuntimeOptions();
        if (updates is not null)
            Config.PersistAudioSourceOptions(updates);
    }

    public void Dispose()
    {
        Task pendingStop = StopAsync().AsTask();
        try { pendingStop.Wait(TimeSpan.FromSeconds(5)); } catch { }

        if (_whisperModels != null)
        {
            foreach (WhisperModel m in _whisperModels) 
            {
                m.Dispose();
            }
        }

        foreach (IAudioSource a in _audioSources)
        {
            foreach (AudioChannel channel in a.Channels) 
            {
                channel.Dispose();
            }
            a.Dispose();
        }
    }

    #endregion

    #region Recodes

    public IEnumerable<KeyValuePair<int, TranscriptRecode>> GetRecodes()
    {
        return this._recodes;
    }

    public void ApplyRuntimeOptions(JsonObject updates)
    {
        foreach (IAudioSource audioSource in _audioSources)
            audioSource.ApplyRuntimeOptions(updates);
    }
    public TranscriptRecode GetRecode(int id)
    {
        return this._recodes[id];
    }
    public void CreateRecode(int id, TranscriptRecode recode)
    {
        this._recodes[id] = recode;
        OnCreatedText?.Invoke(id, recode);
    }
    public void EditRecode(int id, TranscriptRecode recode)
    {
        TranscriptRecode oldRecode = this._recodes[id];
        
        if (false == recode.Equals(oldRecode))
        {
            this._recodes[id] = recode;
            OnEdited?.Invoke(id, recode, oldRecode);
        }
    }
    public void EditRecodeText(int id, string text)
    {
        TranscriptRecode oldRecode = this._recodes[id];
        
        if (text != oldRecode.text)
        {
            TranscriptRecode newRecode = oldRecode with { text = text };
            this._recodes[id] = newRecode;

            OnEdited?.Invoke(id, newRecode, oldRecode);
        }
    }
    public void CombineRecodeText(int id, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        TranscriptRecode oldRecode = this._recodes[id];
        TranscriptRecode newRecode;
        if (string.IsNullOrEmpty(oldRecode.text))
        {
            newRecode = oldRecode with { text = text };
        }
        else
        {
            Span<char> newTextSpan = stackalloc char[oldRecode.text.Length + 1 + text.Length];
            oldRecode.text.AsSpan().CopyTo(newTextSpan);
            newTextSpan[oldRecode.text.Length] = ' ';
            text.AsSpan().CopyTo(newTextSpan.Slice(oldRecode.text.Length + 1));

            newRecode = oldRecode with { text = new string(newTextSpan) };
        }
        this._recodes[id] = newRecode;
        this.OnEdited?.Invoke(id, newRecode, oldRecode);
    }
    public void DeleteRecode(int id)
    {
        TranscriptRecode oldRecode = this._recodes[id];
        this._recodes.Remove(id);
        OnDeleted?.Invoke(id, oldRecode);
    }

    #endregion

    public bool AddWhisperModel(WhisperModel model)
    {
        if (_whisperModels == null)
        {
            _whisperModels = [model];
            return true;
        }
        else
        {
            Array.Resize(ref _whisperModels, _whisperModels.Length + 1);
            _whisperModels[^1] = model;
            return true;
        }
    }
}
