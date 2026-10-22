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
    public bool SpeakerRecognition => _audioSources.Any(s => s.SpeakerRecognition);

    readonly IAudioSource[] _audioSources;
    readonly Dictionary<IAudioSource, Action> _settingsChangedHandlers = new();

    /// <summary>모델 id → 로드된 모델. StartAsync에서만 채우고, 그 뒤엔 읽기만 한다.</summary>
    readonly Dictionary<string, WhisperModel> _whisperModels = new(StringComparer.OrdinalIgnoreCase);
    string? _defaultModelId;

    /// <summary>오디오 스레드(생성)·전사 완료 스레드(텍스트 추가)·UI(조회)가 동시에 접근하므로 _recodesLock으로 보호한다.
    /// 이벤트는 잠금 밖에서 발생시켜, 핸들러(UI)가 다시 엔진을 부르더라도 교착되지 않게 한다.</summary>
    readonly Dictionary<int, TranscriptRecode> _recodes;
    readonly object _recodesLock = new();
    int _nextRecodeId = 1000;

    readonly AudioChannel.SamplesAvailableHandler _onSamplesHandler =
        async (object param, AudioChannel channel, string? speaker, ReadOnlyMemory<float> samples) =>
        {
            TranscriberEngine engine = (TranscriberEngine)param;

            int recodeId = Interlocked.Increment(ref engine._nextRecodeId);

            engine.CreateRecode(
                recodeId,
                new TranscriptRecode(
                    DateTime.Now, 
                    speaker, null
                )
            );
            WhisperModel? whisperModel = engine.ResolveModel(speaker);
            if (whisperModel == null)
                return;

            try
            {
                await whisperModel.TranscribeAsync(
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

        // 모델 로드는 수 초 걸리므로 UI 스레드를 막지 않게 백그라운드에서 한다.
        await Task.Run(LoadModels);

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

        foreach (WhisperModel m in _whisperModels.Values)
        {
            m.Dispose();
        }
        _whisperModels.Clear();

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

    /// <summary>호출 시점의 스냅샷. 열거하는 동안 다른 스레드가 기록을 바꿔도 안전하다.</summary>
    public IEnumerable<KeyValuePair<int, TranscriptRecode>> GetRecodes()
    {
        lock (_recodesLock) return this._recodes.ToList();
    }

    public void ApplyRuntimeOptions(JsonObject updates)
    {
        foreach (IAudioSource audioSource in _audioSources)
            audioSource.ApplyRuntimeOptions(updates);
    }
    public TranscriptRecode GetRecode(int id)
    {
        lock (_recodesLock) return this._recodes[id];
    }
    public void CreateRecode(int id, TranscriptRecode recode)
    {
        lock (_recodesLock) this._recodes[id] = recode;
        OnCreatedText?.Invoke(id, recode);
    }
    public void EditRecode(int id, TranscriptRecode recode) =>
        UpdateRecode(id, old => recode);

    public void EditRecodeText(int id, string text) =>
        UpdateRecode(id, old => old with { text = text });

    public void CombineRecodeText(int id, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        UpdateRecode(id, old => old with { text = string.IsNullOrEmpty(old.text) ? text : $"{old.text} {text}" });
    }

    /// <summary>기존 기록을 잠금 안에서 읽고 바꾼 뒤, 실제로 달라졌을 때만 잠금 밖에서 OnEdited를 발생시킨다.
    /// 이미 지워진 기록(예: 전사가 끝나기 전에 삭제)이면 아무 것도 하지 않는다.</summary>
    void UpdateRecode(int id, Func<TranscriptRecode, TranscriptRecode> update)
    {
        TranscriptRecode oldRecode, newRecode;
        lock (_recodesLock)
        {
            if (!this._recodes.TryGetValue(id, out oldRecode)) return;
            newRecode = update(oldRecode);
            if (newRecode.Equals(oldRecode)) return;
            this._recodes[id] = newRecode;
        }
        OnEdited?.Invoke(id, newRecode, oldRecode);
    }

    public void DeleteRecode(int id)
    {
        TranscriptRecode oldRecode;
        lock (_recodesLock)
        {
            if (!this._recodes.Remove(id, out oldRecode)) return;
        }
        OnDeleted?.Invoke(id, oldRecode);
    }

    #endregion

    #region 모델

    /// <summary>기본 모델과 speakerModels가 가리키는 모델 중 아직 안 올린 것을 로드한다.
    /// 화자별 모델이 설치돼 있지 않으면 그 화자는 기본 모델로 대체한다.</summary>
    void LoadModels()
    {
        _defaultModelId = ModelStore.ResolveDefault(Config.Model)
            ?? throw new FileNotFoundException("설치된 Whisper 모델이 없습니다. '모델' 메뉴에서 다운로드하세요.");

        if (!string.IsNullOrEmpty(Config.Model) && !Config.Model.Equals(_defaultModelId, StringComparison.OrdinalIgnoreCase))
            SetStatusAction?.Invoke($"모델 '{Config.Model}'이(가) 설치돼 있지 않아 '{_defaultModelId}'을(를) 사용합니다.");

        var needed = new List<string> { _defaultModelId };
        foreach (var (speaker, modelId) in Config.SpeakerModels ?? [])
        {
            if (ModelStore.IsInstalled(modelId)) needed.Add(modelId);
            else SetStatusAction?.Invoke($"화자 '{speaker}'의 모델 '{modelId}'이(가) 설치돼 있지 않아 기본 모델을 사용합니다.");
        }

        foreach (string id in needed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_whisperModels.ContainsKey(id)) continue;
            SetStatusAction?.Invoke($"Whisper 모델 로드 중: {id}");
            _whisperModels[id] = new WhisperModel(ModelStore.GetPath(id)!, Config.Language, Config.Threads);
        }

        SetStatusAction?.Invoke($"Whisper 모델 로드 완료: {string.Join(", ", _whisperModels.Keys)}");
    }

    WhisperModel? ResolveModel(string? speaker)
    {
        if (speaker is not null
            && Config.SpeakerModels?.TryGetValue(speaker, out string? id) == true
            && _whisperModels.TryGetValue(id, out WhisperModel? speakerModel))
            return speakerModel;

        return _defaultModelId is not null && _whisperModels.TryGetValue(_defaultModelId, out WhisperModel? m) ? m : null;
    }

    #endregion
}
