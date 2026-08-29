using System.Diagnostics;
using System.ComponentModel;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

using HS.Common.Collection;

namespace HearingAid.Core.Audio;

/// <summary>Windows Process Loopback(NAudio 3의 WasapiRecorderBuilder.WithProcessLoopback,
/// Windows 10 Build 19041+)로 특정 프로그램(프로세스)에서 나오는 소리만 골라 캡처하는 IAudioSource 구현.</summary>
[AudioSource("ProcessLoopback")]
public class ProcessLoopbackAudioSource : IAudioSource
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

    readonly string processName;
    readonly bool includeChildProcesses;
    readonly float gain;
    readonly int pumpIntervalMs;
    readonly double bufferSeconds;

    WasapiRecorder? recorder;
    BufferedWaveProvider? buffer;
    ISampleProvider? resampled;
    CancellationTokenSource? cts;
    Thread? pumpThread;

    public ProcessLoopbackAudioSource(
        [RunningProcessPicker, Description("캡처할 프로그램 (실행 중이며 오디오 세션이 있는 프로세스명)")]
        string processName,
        [Description("자식 프로세스(예: 브라우저 렌더러)의 소리도 포함할지 여부")]
        bool includeChildProcesses = true,
        [Description("입력 증폭 배율 (1.0 = 원본, 2.0 = 2배 증폭 등. 클리핑 방지를 위해 [-1, 1] 범위로 clamp됨)")]
        float gain = 1.0f,
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
        this.processName = processName;
        this.includeChildProcesses = includeChildProcesses;
        this.gain = gain;
        this.pumpIntervalMs = pumpIntervalMs;
        this.bufferSeconds = bufferSeconds;

        this.audioChannel = new AudioChannel(new SileroVad(AudioPipeline.VadModelPath))
        {
            vadThreshold = vadThreshold,
            minSilenceMs = minSilenceMs,
            speechPadMs = speechPadMs,
            minSpeechMs = minSpeechMs,
        };
        this.channels = new SingleEnumerable<AudioChannel>(this.audioChannel);
    }

    public async ValueTask StartAsync()
    {
        if (IsRunning) return;

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new Exception("프로세스별 오디오 캡처(Process Loopback)는 Windows 10 Build 19041 이상이 필요합니다.");

        Process proc = FindProcess(processName);
        Description = $"{proc.ProcessName} (process loopback)";

        recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)proc.Id, includeChildProcesses
                ? ProcessLoopbackMode.IncludeTargetProcessTree
                : ProcessLoopbackMode.ExcludeTargetProcessTree)
            .BuildAsync();

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
            if (flags.HasFlag(AudioClientBufferFlags.Silent))
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
        pumpThread = new Thread(() => PumpLoop(cts.Token)) { IsBackground = true, Name = "ProcessLoopbackPump" };
        pumpThread.Start();

        IsRunning = true;
        AudioChannelCreated?.Invoke(AudioChannelCreatedParam!, this, audioChannel);
        SetStatus?.Invoke($"캡처: {Description} — listening...");
    }

    static Process FindProcess(string processName)
    {
        var candidates = Process.GetProcessesByName(processName);
        if (candidates.Length > 0) return candidates[0];

        var active = AudioSessionProcesses.ListActiveAudioProcessNames();
        throw new Exception($"프로세스를 찾을 수 없습니다: '{processName}'. 현재 오디오 세션이 있는 프로그램: {string.Join(", ", active)}");
    }

    void PumpLoop(CancellationToken token)
    {
        BufferedWaveProvider buf = buffer!;
        ISampleProvider source = resampled!;
        int chunkSize = TargetRate * pumpIntervalMs / 1000;
        // chunkSize개(=pumpIntervalMs 분량)의 16kHz 샘플을 만드는 데 필요한 원본 바이트 수.
        // 이만큼 쌓이기 전에 읽으면 리샘플러가 끊긴 구간을 그대로 통과시켜 파형이 뭉개진다.
        int neededBytes = buf.WaveFormat.AverageBytesPerSecond * pumpIntervalMs / 1000;
        float[] chunk = new float[chunkSize];
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
                float[] outChunk = (read == chunk.Length) ? (float[])chunk.Clone() : chunk[..read];
                AudioEffect.Gain(outChunk, gain);
                _ = audioChannel.PushSamples(null, outChunk);
            }
            else
            {
                Thread.Sleep(pumpIntervalMs / 2);
            }
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
        recorder = null; buffer = null; resampled = null; cts = null; pumpThread = null;
        AudioChannelRemoved?.Invoke(AudioChannelRemovedParam!, this, audioChannel);
        SetStatus?.Invoke("정지됨");
        return ValueTask.CompletedTask;
    }

    public void Dispose() => StopAsync();
}
