using System.Text.Json;
using System.Text.Json.Nodes;
using System.ComponentModel;
using NAudio.Dsp;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;

namespace HearingAid.Core.Audio;

/// <summary>
/// Discord 음성 채널에서 오디오를 수신하는 IAudioSource 구현.
/// 시작하면 봇이 접속한 모든 서버를 뒤져 설정된 유저 ID를 찾고, 그 유저가 있는 음성 채널에
/// 들어가 오디오를 수신한다. 유저가 채널을 옮기거나 나가면 자동으로 따라가거나 대기한다.
/// </summary>
[AudioSource("NetCord",
    DisplayName = "Discord 음성 채널",
    Description = "Discord 봇이 대상 사용자가 있는 음성 채널에 따라 들어가, 말하는 사람별로 나눠 받아 적습니다(화자 구분).\n" +
                  "Discord 개발자 포털에서 만든 봇 토큰이 필요하고, 그 봇이 해당 서버에 초대돼 있어야 합니다. " +
                  "PC 소리와 상관없이 동작하므로 Discord를 음소거해 둬도 됩니다.")]
public class NetCordAudioSource : IAudioSource
{
    /// <summary>SSRC(발신자)별 Opus 디코드 + 48kHz→16kHz 다운샘플 파이프라인.</summary>
    internal sealed class SsrcPipeline : IDisposable
    {
        const int SourceRate = 48000;
        const int TargetRate = AudioPipeline.SampleRate;
        const int Decimation = SourceRate / TargetRate; // 3

        public readonly OpusDecodeStream Decoder;

        readonly Action<float[]> onSamples;
        readonly BiQuadFilter lowPass;
        int phase;

        public SsrcPipeline(float lowPassCutoffHz, float lowPassQ, Action<float[]> onSamples)
        {
            this.onSamples = onSamples;
            lowPass = BiQuadFilter.LowPassFilter(SourceRate, lowPassCutoffHz, lowPassQ);
            Decoder = new OpusDecodeStream(new PcmSinkStream(OnPcm), PcmFormat.Float, VoiceChannels.Mono);
        }

        void OnPcm(ReadOnlySpan<byte> bytes)
        {
            int count = bytes.Length / 4;
            if (count == 0) return;

            var outSamples = new List<float>(count / Decimation + 1);
            for (int i = 0; i < count; i++)
            {
                float raw = BitConverter.ToSingle(bytes.Slice(i * 4, 4));
                float filtered = lowPass.Transform(raw);
                if (phase == 0) outSamples.Add(filtered);
                phase = (phase + 1) % Decimation;
            }

            if (outSamples.Count > 0) onSamples(outSamples.ToArray());
        }

        public void Dispose() => Decoder.Dispose();
    }

    delegate void PcmWriteHandler(ReadOnlySpan<byte> data);

    /// <summary>OpusDecodeStream이 디코딩한 PCM을 콜백으로 흘려보내기만 하는 sink.</summary>
    sealed class PcmSinkStream(PcmWriteHandler onWrite) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => onWrite(new ReadOnlySpan<byte>(buffer, offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => onWrite(buffer);
    }
    
    public class UserData
    {
        public ulong userId;
        public UserConfig config;
        public AudioChannel channel;
        internal SsrcPipeline pipeline;

        public UserData(ulong userId, UserConfig cfg, SileroVad vad, bool autoGain, float vadThreshold, int minSilenceMs, int speechPadMs, int minSpeechMs)
        {
            this.userId = userId;
            this.config = cfg;
            this.channel = new AudioChannel(vad)
            {
                autoGain = autoGain,
                vadThreshold = vadThreshold,
                minSilenceMs = minSilenceMs,
                speechPadMs = speechPadMs,
                minSpeechMs = minSpeechMs,
            };
            this.pipeline = new SsrcPipeline(
                cfg.lowPassCutoffHz, cfg.lowPassQ,
                samples =>
                {
                    string speaker = string.IsNullOrEmpty(this.config.name)? this.userId.ToString() : this.config.name;
                    _ = this.channel.PushSamples(speaker, samples);
                }
            );
        }
    }
    public struct UserConfig
    {
        public UserConfig() { }

        public ulong userId;
        public string? name;
        public float lowPassCutoffHz;
        public float lowPassQ;
        public float gain;
    }
    
    
    public event IAudioSource.SetAudioSourceStateAction? SetStatus;

    public object? AudioChannelCreatedParam { get; set; }
    public event IAudioSource.CreateAudioChannelEventHandler? AudioChannelCreated;
    public object? AudioChannelRemovedParam { get; set; }
    public event IAudioSource.RemoveAudioChannelEventHandler? AudioChannelRemoved;

    public string Description { get; private set; } = "Discord";
    public bool IsRunning { get; private set; }
    public bool SpeakerRecognition => true;

    readonly string token;
    readonly ulong targetUserId;
    readonly float gain;
    readonly bool autoGain;
    readonly float lowPassCutoffHz;
    readonly float lowPassQ;
    readonly float vadThreshold;
    readonly int minSilenceMs;
    readonly int speechPadMs;
    readonly int minSpeechMs;

    GatewayClient? client;
    VoiceClient? voiceClient;
    ulong? currentGuildId;
    ulong? currentChannelId;

    readonly Dictionary<uint, SsrcPipeline> pipelines = new();
    readonly SemaphoreSlim voiceLock = new(1, 1);
    readonly HashSet<ulong> pendingNameRequests = new();
    Task? cleanupTask;

    Dictionary<ulong, UserData> userDataMap = new();
    public IEnumerable<AudioChannel> Channels
    {
        get
        {
            lock (userDataMap)
            {
                foreach (var userData in userDataMap.Values)
                    yield return userData.channel;
            }
        }
    }

    public NetCordAudioSource(
        [Description("Discord 봇 토큰. discord.com/developers/applications 에서 봇을 만들고 Bot 탭의 Reset Token으로 발급. 남에게 보여주지 말 것")]
        string token,
        
        [Description("따라갈 사용자(보통 본인)의 Discord ID(숫자). Discord 설정 > 고급 > 개발자 모드를 켠 뒤 사용자를 우클릭 > 'ID 복사'")]
        ulong targetUserId,
        
        [Description("미리 등록해둘 사용자 목록 (userId/name/gain/lowPassCutoffHz/lowPassQ). name을 비워두면 그 사람이 처음 말할 때 디스코드 닉네임/표시이름으로 자동 채워진다. 비워두면 실제 발화가 감지될 때 자동으로 추가된다.")]
        UserConfig[]? knownUsers = null,

        [Description("입력 증폭 배율 (1.0 = 원본, 2.0 = 2배 증폭 등. 클리핑 방지를 위해 [-1, 1] 범위로 clamp됨)")]
        float gain = 1.0f,
        [Description(AudioPipeline.AutoGainDescription + ". 화자마다 따로 맞춘다")]
        bool autoGain = true,
        [Description("48kHz→16kHz 다운샘플 전 안티앨리어싱 저역통과 필터 차단 주파수 (Hz)")]
        float lowPassCutoffHz = 7000f,
        [Description("저역통과 필터의 Q(공진) 값. 0.707은 표준 Butterworth 특성")]
        float lowPassQ = 0.707f,
        [Description("VAD(음성 감지) 확률 임계값 (0~1). 높을수록 발화로 인식하는 기준이 엄격해짐")]
        float vadThreshold = 0.7f,
        [Description("발화 종료로 판단하기까지 필요한 무음 지속 시간 (ms)")]
        int minSilenceMs = 600,
        [Description("발화 시작 전 포함할 패딩 길이 (ms)")]
        int speechPadMs = 200,
        [Description("이 길이(ms) 미만인 발화 구간은 너무 짧다고 보고 버림")]
        int minSpeechMs = 500)
    {
        this.token = token;
        this.targetUserId = targetUserId;
        this.gain = gain;
        this.autoGain = autoGain;
        this.lowPassCutoffHz = lowPassCutoffHz;
        this.lowPassQ = lowPassQ;
        this.vadThreshold = vadThreshold;
        this.minSilenceMs = minSilenceMs;
        this.speechPadMs = speechPadMs;
        this.minSpeechMs = minSpeechMs;

        this.userDataMap = (knownUsers ?? [])
            .ToDictionary(u => u.userId, u => new UserData(u.userId, u, new SileroVad(AudioPipeline.VadModelPath),
                autoGain, vadThreshold, minSilenceMs, speechPadMs, minSpeechMs));
    }

#region 외부 이벤트
    public async ValueTask StartAsync()
    {
        if (IsRunning) return;

        client = new GatewayClient(new BotToken(token), new GatewayClientConfiguration
        {
            Intents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
        });

        client.Ready += OnReadyAsync;
        client.VoiceStateUpdate += OnVoiceStateUpdateAsync;
        client.GuildUserChunk += OnGuildUserChunkAsync;

        await client.StartAsync();

        IsRunning = true;
        SetStatus?.Invoke("Discord 게이트웨이 연결됨 — 대상 사용자 탐색 중...");
    }
    public async ValueTask StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        await LeaveCurrentVoiceAsync();

        if (client is { } c)
        {
            client = null;
            try { await c.CloseAsync(); } catch { }
            c.Dispose();
        }

        lock (userDataMap)
        {
            foreach (var userData in userDataMap.Values)
                AudioChannelRemoved?.Invoke(AudioChannelRemovedParam!, this, userData.channel);
            userDataMap.Clear();
        }
        lock (pendingNameRequests) pendingNameRequests.Clear();

        SetStatus?.Invoke("정지됨");
    }
    public void Dispose()
    {
        cleanupTask = StopAsync().AsTask();
        try { cleanupTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
        voiceLock.Dispose();
    }
#endregion

#region 디스코드 이벤트
    ValueTask OnReadyAsync(ReadyEventArgs args)
    {
        _ = ScanAndJoinAsync();
        return default;

        async ValueTask ScanAndJoinAsync()
        {
            // 길드 목록(GUILD_CREATE)이 도착할 시간을 잠깐 준다.
            await Task.Delay(2000);

            if (client is not { } c) return;

            foreach (var guild in c.Cache.Guilds.Values)
            {
                if (guild.VoiceStates.TryGetValue(targetUserId, out var vs) && vs.ChannelId is ulong channelId)
                {
                    await JoinAsync(guild.Id, channelId);
                    return;
                }
            }

            SetStatus?.Invoke("대상 사용자가 음성 채널에 없습니다. 대기 중...");
        }
    }
    ValueTask OnVoiceStateUpdateAsync(VoiceState state)
    {
        if (state.UserId != targetUserId) return default;

        _ = HandleVoiceStateChangeAsync(state.GuildId, state.ChannelId);
        return default;

        async ValueTask HandleVoiceStateChangeAsync(ulong guildId, ulong? channelId)
        {
            try
            {
                if (channelId is null)
                {
                    if (currentGuildId == guildId)
                    {
                        await LeaveCurrentVoiceAsync();
                        SetStatus?.Invoke("대상 사용자가 음성 채널을 나갔습니다. 대기 중...");
                    }
                    return;
                }

                if (currentGuildId == guildId && currentChannelId == channelId)
                    return; // 이미 같은 채널에 있음

                await JoinAsync(guildId, channelId.Value);
            }
            catch (Exception ex)
            {
                SetStatus?.Invoke($"음성 채널 추적 오류: {ex.Message}");
            }
        }
    }
    ValueTask OnVoiceReceive(VoiceReceiveEventArgs args)
    {
        if (voiceClient == null) 
            return ValueTask.CompletedTask;

        ulong userId;
        if (false == voiceClient.Cache.SsrcUsers.TryGetValue(args.Ssrc, out userId))
            return ValueTask.CompletedTask;

        UserData userData;
        try { userData = this.GetOrLoadUserData(userId); }
        catch { return ValueTask.CompletedTask; }

        try { userData.pipeline.Decoder.Write(args.Frame); }
        catch { /* 손상된 opus 프레임은 무시 */ }

        return ValueTask.CompletedTask;
    }
#endregion
    
#region 디스코드 도구
    async ValueTask JoinAsync(ulong guildId, ulong channelId)
    {
        if (client is not { } c) return;

        await voiceLock.WaitAsync();
        try
        {
            await LeaveCurrentVoiceAsync();

            SetStatus?.Invoke("음성 채널 참가 중...");

            var vc = await c.JoinVoiceChannelAsync(guildId, channelId);
            vc.VoiceReceive += OnVoiceReceive;

            await vc.StartAsync();

            voiceClient = vc;
            currentGuildId = guildId;
            currentChannelId = channelId;
            Description = $"Discord 음성 채널 (guild {guildId} / channel {channelId})";
            SetStatus?.Invoke($"{Description} — listening...");
        }
        catch (Exception ex)
        {
            SetStatus?.Invoke($"음성 채널 참가 실패: {ex.Message}");
        }
        finally
        {
            voiceLock.Release();
        }
    }
    async ValueTask LeaveCurrentVoiceAsync()
    {
        var vc = voiceClient;
        if (vc is null) return;

        voiceClient = null;
        vc.VoiceReceive -= OnVoiceReceive;

        lock (pipelines)
        {
            foreach (var pipeline in pipelines.Values)
                pipeline.Dispose();
            pipelines.Clear();
        }

        try { await vc.CloseAsync(); } catch { }
        vc.Dispose();

        if (currentGuildId is ulong guildId && client is { } c)
        {
            try { await c.UpdateVoiceStateAsync(new(guildId, null)); } catch { }
        }

        currentGuildId = null;
        currentChannelId = null;
    }
#endregion
    
#region UserData
    UserData GetOrLoadUserData(ulong userId)
    {
        UserData userData;
        bool created;
        lock (userDataMap)
        {
            created = false == userDataMap.TryGetValue(userId, out UserData? existing);
            userData = existing?? new UserData(
                userId, 
                new UserConfig
                {
                    userId = userId,
                    lowPassCutoffHz = lowPassCutoffHz,
                    lowPassQ = lowPassQ,
                    gain = gain
                }, 
                new SileroVad(AudioPipeline.VadModelPath),
                autoGain,
                vadThreshold,
                minSilenceMs, 
                speechPadMs, 
                minSpeechMs
            );
            if (created) 
            {
                userDataMap[userId] = userData;
            }
        }

        if (string.IsNullOrEmpty(userData.config.name))
        {
            string? resolved = ResolveDiscordName(userId);
            if (resolved is not null)
            {
                userData.config.name = resolved;
                SettingsChanged?.Invoke();
            }
            else
            {
                RequestNameAsync(userId);
            }
        }

        if (created)
        {
            AudioChannelCreated?.Invoke(AudioChannelCreatedParam!, this, userData.channel);
            SettingsChanged?.Invoke();
        }

        return userData;
    }

    /// <summary>이 유저가 지금 있는 음성 채널의 GuildUser 정보에서 표시 이름을 얻는다.
    /// GatewayIntents.GuildVoiceStates만으로 채워지는 Guild.VoiceStates를 쓰므로
    /// 특권 인텐트(GuildUsers)가 별도로 필요하지 않다.</summary>
    string? ResolveDiscordName(ulong userId)
    {
        if (client == null || currentGuildId is not ulong guildId) return null;
        if (false == client.Cache.Guilds.TryGetValue(guildId, out var guild)) return null;
        if (false == guild.VoiceStates.TryGetValue(userId, out var vs) || vs.User is not { } u) return null;

        if (false == string.IsNullOrEmpty(u.Nickname)) return u.Nickname;
        if (false == string.IsNullOrEmpty(u.GlobalName)) return u.GlobalName;
        return u.Username;
    }

    /// <summary>봇 준비 이전부터 음성 채널에 있던 유저는 VOICE_STATE_UPDATE 페이로드에
    /// member 정보가 실려오지 않아 ResolveDiscordName만으로는 영원히 이름을 못 얻는다.
    /// 특권 인텐트 없이도 허용되는 user_ids 지정 op 8 요청으로 별도 조회한다.</summary>
    void RequestNameAsync(ulong userId)
    {
        if (client is not { } c || currentGuildId is not ulong guildId) return;

        lock (pendingNameRequests)
        {
            if (false == pendingNameRequests.Add(userId)) return;
        }

        _ = SendRequestAsync();

        async ValueTask SendRequestAsync()
        {
            try
            {
                await c.RequestGuildUsersAsync(new GuildUsersRequestProperties(guildId).WithUserIds([userId]));
            }
            catch (Exception ex)
            {
                lock (pendingNameRequests) pendingNameRequests.Remove(userId);
                SetStatus?.Invoke($"사용자 이름 조회 실패: {ex.Message}");
            }
        }
    }

    ValueTask OnGuildUserChunkAsync(GuildUserChunkEventArgs args)
    {
        foreach (GuildUser user in args.Users)
        {
            ApplyResolvedUser(user);
        }
        return default;
    }

    void ApplyResolvedUser(GuildUser user)
    {
        UserData? userData;
        lock (userDataMap)
            userDataMap.TryGetValue(user.Id, out userData);

        lock (pendingNameRequests) pendingNameRequests.Remove(user.Id);

        if (userData is null || false == string.IsNullOrEmpty(userData.config.name)) return;

        userData.config.name = false == string.IsNullOrEmpty(user.Nickname) ? user.Nickname
            : false == string.IsNullOrEmpty(user.GlobalName) ? user.GlobalName
            : user.Username;
        SettingsChanged?.Invoke();
    }

    public event Action? SettingsChanged;

    /// <summary>지금까지 학습/등록된 화자들을(자동으로 채운 이름 포함) knownUsers 배열로 내보낸다
    /// — TranscriberEngine이 이 값을 설정 파일에 저장한다.</summary>
    public JsonObject? ExportRuntimeOptions()
    {
        UserConfig[] snapshot;
        lock (userDataMap)
            snapshot = userDataMap.Values.Select(u => u.config).ToArray();

        return new JsonObject
        {
            ["knownUsers"] = JsonSerializer.SerializeToNode(snapshot, Config.AudioSourceValueJsonOptions)
        };
    }

    /// <summary>knownUsers 중 이미 추적 중인(userId가 일치하는) 화자의 이름만 그 자리에서
    /// 다시 로드한다 — 캡처를 멈추지 않고도 설정 창에서 고친 이름이 다음 발화부터 화면에
    /// 반영되게 한다. 아직 한 번도 등장하지 않은 새 항목을 추가하거나 필터 값을 바꾸는 건
    /// 범위 밖이다(필터는 SsrcPipeline 생성 시 굳어지므로 재시작이 필요하다).</summary>
    public void ApplyRuntimeOptions(JsonObject updates)
    {
        if (updates["knownUsers"] is not { } node) return;

        UserConfig[]? incoming;
        try { incoming = node.Deserialize<UserConfig[]>(Config.AudioSourceValueJsonOptions); }
        catch { return; }
        if (incoming is null) return;

        lock (userDataMap)
        {
            foreach (var cfg in incoming)
                if (userDataMap.TryGetValue(cfg.userId, out var existing))
                    existing.config.name = cfg.name;
        }
    }
#endregion
}
