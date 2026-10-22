using System.ComponentModel;
using HS.Common.Collection;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HearingAid.Core.Audio;

/// <summary>Windows 기본(또는 지정) 재생 장치의 출력을 WASAPI 루프백으로 캡처하는 IAudioSource 구현.
/// VB-CABLE 같은 가상 오디오 드라이버 설치 없이 시스템 소리를 바로 받아온다.</summary>
[AudioSource("WasapiLoopback",
    DisplayName = "PC 소리 전체",
    Description = "스피커·헤드폰으로 나오는 PC 소리를 모두 받아 적습니다. 유튜브, 회의 앱, 게임 등 " +
                  "PC에서 재생되는 소리를 자막처럼 보고 싶을 때 쓰세요. 별도 설치가 필요 없습니다.\n" +
                  "알림음·음악 같은 다른 소리도 함께 들어가므로, 한 프로그램만 듣고 싶다면 '특정 프로그램 소리만'을 고르세요.")]
public class WasapiLoopbackAudioSource : IAudioSource
{
    const int TargetRate = AudioPipeline.SampleRate;

    public AudioChannel audioChannel;
    IEnumerable<AudioChannel> channels;
    IEnumerable<AudioChannel> IAudioSource.Channels => channels;
    public event IAudioSource.SetAudioSourceStateAction? SetStatus;

    public object? AudioChannelCreatedParam { get; set; }
    public event IAudioSource.CreateAudioChannelEventHandler? AudioChannelCreated;
    public object? AudioChannelRemovedParam { get; set; }
    public event IAudioSource.RemoveAudioChannelEventHandler? AudioChannelRemoved;

    public string Description { get; private set; } = "";
    public bool IsRunning { get; private set; }
    public bool SpeakerRecognition => false;

    readonly string? deviceKeyword;
    readonly float gain;
    readonly int pumpIntervalMs;
    readonly double bufferSeconds;

    MMDeviceEnumerator? enumerator;
    MMDevice? device;
    WasapiRecorder? recorder;
    BufferedWaveProvider? buffer;
    ISampleProvider? resampled;
    CancellationTokenSource? cts;
    Thread? pumpThread;

    public WasapiLoopbackAudioSource(
        [OutputDevicePicker, Description("캡처할 출력 장치. 목록에서 고르거나 이름 일부를 적음 (부분 일치). 비워두면 Windows 기본 출력 장치 사용")]
        string? deviceKeyword = null,
        [Description("입력 증폭 배율 (1.0 = 원본, 2.0 = 2배 증폭 등. 클리핑 방지를 위해 [-1, 1] 범위로 clamp됨)")]
        float gain = 1.0f,
        [Description(AudioPipeline.AutoGainDescription)]
        bool autoGain = true,
        [Description("캡처 버퍼를 읽어 내보내는 주기 (ms). 짧을수록 지연이 줄지만 CPU 사용이 늘어남")]
        int pumpIntervalMs = 100,
        [Description("내부 캡처 버퍼 길이 (초). 처리 지연 등으로 소비가 밀릴 때 얼마나 버틸지 결정")]
        double bufferSeconds = 5.0,
        [Description("VAD(음성 감지) 확률 임계값 (0~1). 높을수록 발화로 인식하는 기준이 엄격해짐")]
        float vadThreshold = 0.7f,
        [Description("발화 종료로 판단하기까지 필요한 무음 지속 시간 (ms)")]
        int minSilenceMs = 600,
        [Description("발화 시작 전 포함할 패딩 길이 (ms)")]
        int speechPadMs = 200,
        [Description("이 길이(ms) 미만인 발화 구간은 너무 짧다고 보고 버림")]
        int minSpeechMs = 500)
    {
        this.deviceKeyword = deviceKeyword;
        this.gain = gain;
        this.pumpIntervalMs = pumpIntervalMs;
        this.bufferSeconds = bufferSeconds;

        this.audioChannel = new AudioChannel(new SileroVad(AudioPipeline.VadModelPath))
        {
            autoGain = autoGain,
            vadThreshold = vadThreshold,
            minSilenceMs = minSilenceMs,
            speechPadMs = speechPadMs,
            minSpeechMs = minSpeechMs,
        };
        this.channels = new SingleEnumerable<AudioChannel>(this.audioChannel);
    }

    public ValueTask StartAsync()
    {
        if (IsRunning) return ValueTask.CompletedTask;

        enumerator = new MMDeviceEnumerator();
        device = FindDevice(enumerator, deviceKeyword);
        Description = $"{device.FriendlyName} (loopback)";

        recorder = new WasapiRecorderBuilder().WithLoopbackCapture().WithDevice(device).Build();
        buffer = new BufferedWaveProvider(recorder.WaveFormat, TimeSpan.FromSeconds(bufferSeconds))
        {
            DiscardOnBufferOverflow = true,
            // ReadFully(기본값 true)이면 버퍼가 비어 있어도 0으로 채워 "요청한 만큼 다 읽었다"고 돌려준다.
            // 그러면 아래 PumpLoop이 한 번도 쉬지 않고 돌면서 무음을 실시간의 수백 배 속도로 뿜어내고,
            // 실제 소리는 몇 샘플짜리 스파이크로 압축돼 버린다.
            ReadFully = false
        };

        ISampleProvider floatSamples = buffer.ToSampleProvider();
        ISampleProvider mono = new DownmixToMonoSampleProvider(floatSamples);
        resampled = new WdlResamplingSampleProvider(mono, TargetRate);

        recorder.DataAvailable += (data, flags, _, _) =>
        {
            // AUDCLNT_BUFFERFLAGS_SILENT가 설정된 구간은 버퍼 내용이 정의되지 않은 값(이전에 쓰인 잔여
            // 데이터)일 수 있어 그대로 오디오로 취급하면 안 된다 — 0으로 채워 무음으로 처리해야 한다.
            if ((flags & AudioClientBufferFlags.Silent) != 0)
                buffer.AddSamples(new byte[data.Length]);
            else
                buffer.AddSamples(data);
        };
        recorder.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null) SetStatus?.Invoke($"캡처 오류: {e.Exception.Message}");
        };
        recorder.StartRecording();

        cts = new CancellationTokenSource();
        pumpThread = new Thread(() => PumpLoop(cts.Token)) { IsBackground = true, Name = "WasapiLoopbackPump" };
        pumpThread.Start();

        IsRunning = true;
        AudioChannelCreated?.Invoke(AudioChannelCreatedParam!, this, audioChannel);
        SetStatus?.Invoke($"캡처: {Description} — listening...");
        return ValueTask.CompletedTask;
    }

    void PumpLoop(CancellationToken token)
    {
        var buf = buffer!;
        var source = resampled!;
        int chunkSize = TargetRate * pumpIntervalMs / 1000;
        // chunkSize개(=pumpIntervalMs 분량)의 16kHz 샘플을 만드는 데 필요한 원본 바이트 수.
        // 이만큼 쌓이기 전에 읽으면 리샘플러가 끊긴 구간을 그대로 통과시켜 파형이 뭉개진다.
        int neededBytes = buf.WaveFormat.AverageBytesPerSecond * pumpIntervalMs / 1000;
        var chunk = new float[chunkSize];
        while (!token.IsCancellationRequested)
        {
            if (buf.BufferedBytes < neededBytes)
            {
                Thread.Sleep(pumpIntervalMs / 2);
                continue;
            }

            int read = source.Read(chunk);
            if (read > 0)
            {
                var outChunk = read == chunk.Length ? (float[])chunk.Clone() : chunk[..read];
                AudioEffect.Gain(outChunk, gain);
                _ = audioChannel.PushSamples(null, outChunk);
            }
            else
                Thread.Sleep(pumpIntervalMs / 2);
        }
    }

    public ValueTask StopAsync()
    {
        if (!IsRunning) return ValueTask.CompletedTask;
        IsRunning = false;
        try { cts?.Cancel(); } catch { }
        try { pumpThread?.Join(1000); } catch { }
        try { recorder?.StopRecording(); } catch { }
        try { recorder?.Dispose(); } catch { }
        try { device?.Dispose(); } catch { }
        try { enumerator?.Dispose(); } catch { }
        recorder = null; device = null; enumerator = null; buffer = null; resampled = null; cts = null; pumpThread = null;
        AudioChannelRemoved?.Invoke(AudioChannelRemovedParam!, this, audioChannel);
        SetStatus?.Invoke("정지됨");
        return ValueTask.CompletedTask;
    }

    static MMDevice FindDevice(MMDeviceEnumerator enumerator, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var names = new List<string>();
        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (dev.FriendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return dev;
            names.Add(dev.FriendlyName);
            dev.Dispose();
        }

        throw new Exception($"출력 장치 '{keyword}'를 찾을 수 없습니다. 프로필 편집에서 장치를 다시 고르세요.\n\n현재 출력 장치:"
            + string.Concat(names.Select(n => $"\n  • {n}")));
    }

    public void Dispose() => StopAsync();
}
