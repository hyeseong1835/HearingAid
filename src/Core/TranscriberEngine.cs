using System.Collections.Concurrent;
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
    public int AudioSourceCount => _audioSources.Length;

    readonly IAudioSource[] _audioSources;
    readonly Dictionary<IAudioSource, Action> _settingsChangedHandlers = new();
    readonly Dictionary<IAudioSource, IAudioSource.SetAudioSourceStateAction> _statusHandlers = new();

    /// <summary>채널 → 그 채널을 만든 오디오 소스의 순번. 발화 기록에 어느 소스에서 왔는지 남기는 데 쓴다.
    /// 채널은 오디오 스레드에서 생기고 없어지므로 동시성 사전을 쓴다.</summary>
    readonly ConcurrentDictionary<AudioChannel, int> _channelSources = new();

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
                    speaker, null,
                    engine._channelSources.TryGetValue(channel, out int sourceIndex) ? sourceIndex : 0
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

            engine._channelSources[channel] = Array.IndexOf(engine._audioSources, source);
            channel.samplesAvailableHandlerParam = param;
            channel.samplesAvailableHandler += engine._onSamplesHandler;
            channel.rawSamplesAvailable += samples => engine.RawSamplesAvailable?.Invoke(samples);
        };
    readonly IAudioSource.RemoveAudioChannelEventHandler _removeAudioChannelEventHandler =
        (object param, IAudioSource source, AudioChannel channel) =>
        {
            TranscriberEngine engine = (TranscriberEngine)param;

            engine._channelSources.TryRemove(channel, out _);
            channel.samplesAvailableHandlerParam = null;
            channel.samplesAvailableHandler -= engine._onSamplesHandler;
            channel.Dispose();
        };
    /// <summary>오디오 소스를 준비하지 못한 이유(없으면 null). 이 값이 있으면 시작할 수 없다.</summary>
    public string? SetupWarning { get; }

    public TranscriberEngine(Config cfg, IAudioSource[] audioSource, string? setupWarning = null)
    {
        Config = cfg;
        this._audioSources = audioSource;
        this._recodes = new Dictionary<int, TranscriptRecode>();
        SetupWarning = setupWarning;
    }

    #region 생명주기

    public async ValueTask StartAsync()
    {
        if (IsRunning) return;
        if (SetupWarning is not null) throw new InvalidOperationException(SetupWarning);

        // 모델 로드는 수 초 걸리므로 UI 스레드를 막지 않게 백그라운드에서 한다.
        await Task.Run(LoadModels);

        for (int i = 0; i < _audioSources.Length; i++)
        {
            IAudioSource audioSource = _audioSources[i];
            Attach(audioSource, i);
            try
            {
                await audioSource.StartAsync();
            }
            catch (Exception ex)
            {
                // 하나라도 시작하지 못하면 앞서 시작한 소스까지 모두 멈춰, 일부만 도는 어중간한 상태를 남기지 않는다.
                for (int j = 0; j <= i; j++)
                {
                    try { await _audioSources[j].StopAsync(); } catch { }
                    Detach(_audioSources[j]);
                }
                if (_audioSources.Length == 1) throw;
                throw new Exception($"오디오 소스 {i + 1}({Config.GetAudioSourceAlias(i)}): {ex.Message}", ex);
            }

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
            Detach(audioSource);
        }

        SetStatusAction?.Invoke("정지됨");
    }

    void Attach(IAudioSource audioSource, int index)
    {
        audioSource.AudioChannelCreatedParam = this;
        audioSource.AudioChannelRemovedParam = this;
        audioSource.AudioChannelCreated += _createAudioChannelEventHandler;
        audioSource.AudioChannelRemoved += _removeAudioChannelEventHandler;

        // 소스가 여럿이면 상태 표시줄에서 어느 소스의 소식인지 알 수 있게 별명을 붙인다.
        string prefix = _audioSources.Length > 1 ? $"[{Config.GetAudioSourceAlias(index)}] " : "";
        IAudioSource.SetAudioSourceStateAction statusHandler = msg => SetStatusAction?.Invoke(prefix + msg);
        _statusHandlers[audioSource] = statusHandler;
        audioSource.SetStatus += statusHandler;

        Action settingsChangedHandler = () => OnAudioSourceSettingsChanged(audioSource, index);
        _settingsChangedHandlers[audioSource] = settingsChangedHandler;
        audioSource.SettingsChanged += settingsChangedHandler;
    }

    void Detach(IAudioSource audioSource)
    {
        audioSource.AudioChannelCreated -= _createAudioChannelEventHandler;
        audioSource.AudioChannelRemoved -= _removeAudioChannelEventHandler;
        if (_statusHandlers.Remove(audioSource, out var statusHandler))
            audioSource.SetStatus -= statusHandler;
        if (_settingsChangedHandlers.Remove(audioSource, out Action? settingsChangedHandler))
            audioSource.SettingsChanged -= settingsChangedHandler;
    }

    void OnAudioSourceSettingsChanged(IAudioSource source, int index)
    {
        JsonObject? updates = source.ExportRuntimeOptions();
        if (updates is not null)
            Config.PersistAudioSourceOptions(index, updates);
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

    /// <summary>index번째 오디오 소스에 바뀐 설정을 바로 반영한다. 편집 화면에서 그 자리의 종류를 바꿨다면
    /// (아직 새 엔진을 만들기 전이므로) 다른 종류의 소스에 엉뚱한 값을 넣지 않도록 건너뛴다.</summary>
    public void ApplyRuntimeOptions(int index, JsonObject updates)
    {
        if (index < 0 || index >= _audioSources.Length) return;
        if (!string.Equals(Config.GetAudioSourceType(Config.AudioSourceList[index]), Config.GetAudioSourceType(updates),
                StringComparison.OrdinalIgnoreCase))
            return;

        // 별명은 소스가 아니라 화면 표시용이므로 설정 객체에만 바로 반영한다(다음 발화부터 새 별명으로 표시).
        JsonObject current = Config.AudioSourceList[index];
        if (updates[Config.AliasKey] is { } alias) current[Config.AliasKey] = alias.DeepClone();
        else current.Remove(Config.AliasKey);

        _audioSources[index].ApplyRuntimeOptions(updates);
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
