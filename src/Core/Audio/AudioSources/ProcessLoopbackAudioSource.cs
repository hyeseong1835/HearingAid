using System.Diagnostics;
using System.ComponentModel;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

using HS.Common.Collection;

namespace HearingAid.Core.Audio;

/// <summary>Windows Process Loopback(NAudio 3의 WasapiRecorderBuilder.WithProcessLoopback,
/// Windows 10 Build 19041+)로 특정 프로그램(프로세스)에서 나오는 소리만 골라 캡처하는 IAudioSource 구현.</summary>
[AudioSource("ProcessLoopback",
    DisplayName = "특정 프로그램 소리만",
    Description = "고른 프로그램(예: chrome, Zoom, Discord 앱)에서 나오는 소리만 받아 적습니다. " +
                  "다른 프로그램의 알림음이나 음악은 섞이지 않습니다.\n" +
                  "프로그램이 실행 중이고 소리를 낸 적이 있어야 목록에 나타납니다. Windows 10 2004 이상이 필요합니다.")]
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

    /// <summary>캡처 패킷이 이만큼(ms) 끊기면 대상이 소리를 멈춘 것으로 보고 무음을 직접 흘려보낸다.</summary>
    const int StarvedSilenceMs = 300;

    readonly string processName;
    readonly float gain;
    readonly int pumpIntervalMs;
    readonly double bufferSeconds;

    WasapiRecorder? recorder;
    BufferedWaveProvider? buffer;
    ISampleProvider? resampled;
    CancellationTokenSource? cts;
    Thread? pumpThread;
    long lastDataTick;

    // includeChildProcesses 매개변수는 없앴다: false일 때 쓰던 ExcludeTargetProcessTree는 "자식 제외"가 아니라
    // "이 프로그램을 뺀 나머지 전부"를 캡처하는 모드라 정반대로 동작했다. Windows에는 자식만 빼는 모드가 없다.
    // 예전 프로필 파일에 키가 남아 있어도 생성자 매개변수와 매칭되지 않을 뿐이라 무시된다.
    public ProcessLoopbackAudioSource(
        [RunningProcessPicker, Description("캡처할 프로그램 (실행 중이며 오디오 세션이 있는 프로세스명). 자식 프로세스의 소리도 함께 캡처한다")]
        string processName,
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
        this.processName = processName;
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

    public async ValueTask StartAsync()
    {
        if (IsRunning) return;

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new Exception("프로세스별 오디오 캡처(Process Loopback)는 Windows 10 Build 19041 이상이 필요합니다.");

        Process proc = FindProcess(processName);
        Description = $"{proc.ProcessName} (PID {proc.Id}, process loopback)";

        recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)proc.Id, ProcessLoopbackMode.IncludeTargetProcessTree)
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

        Interlocked.Exchange(ref lastDataTick, Environment.TickCount64);
        // 필드(buffer)가 아니라 지역 변수를 캡처한다. 정지할 때 필드를 null로 비우는 순간 캡처 스레드의 콜백이
        // 아직 돌고 있으면 NullReferenceException이 나고, 이 스레드의 예외는 앱 전체를 끈다.
        BufferedWaveProvider buf = buffer;
        recorder.DataAvailable += (data, flags, _, _) =>
        {
            try
            {
                Interlocked.Exchange(ref lastDataTick, Environment.TickCount64);
                if (flags.HasFlag(AudioClientBufferFlags.Silent))
                    buf.AddSamples(new byte[data.Length]);
                else
                    buf.AddSamples(data);
            }
            catch (Exception ex) { ReportFailure("캡처 콜백", ex); }
        };
        recorder.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null) SetStatus?.Invoke($"캡처 오류: {e.Exception.Message}");
        };
        recorder.StartRecording();

        cts = new CancellationTokenSource();
        pumpThread = new Thread(() => RunPump(cts.Token)) { IsBackground = true, Name = "ProcessLoopbackPump" };
        pumpThread.Start();

        IsRunning = true;
        AudioChannelCreated?.Invoke(AudioChannelCreatedParam!, this, audioChannel);
        SetStatus?.Invoke($"캡처: {Description} — listening...");
    }

    /// <summary>캡처 대상 프로세스를 고른다. 크롬·엣지·디스코드처럼 같은 이름의 프로세스가 여러 개인 프로그램은
    /// 소리를 내는 프로세스(오디오 서비스)가 따로 있고, 아무 하나(예: 렌더러)를 고르면 그 트리에는 소리가 없어
    /// 아무것도 캡처되지 않는다. 그래서 오디오 세션을 가진 프로세스를 우선하고, 애매하면 가장 먼저 시작된
    /// 프로세스(보통 나머지 전부의 부모)를 골라 자식 트리째 캡처한다.</summary>
    static Process FindProcess(string processName)
    {
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];

        var candidates = Process.GetProcessesByName(processName);
        if (candidates.Length == 0)
        {
            var active = AudioSessionProcesses.ListActiveAudioProcessNames();
            throw new Exception($"프로세스를 찾을 수 없습니다: '{processName}'. 현재 오디오 세션이 있는 프로그램: {string.Join(", ", active)}");
        }
        if (candidates.Length == 1) return candidates[0];

        var sessionPids = AudioSessionProcesses.ListSessionProcessIds(processName);
        if (sessionPids.Count == 1 && candidates.FirstOrDefault(p => p.Id == sessionPids[0]) is { } audioProcess)
            return audioProcess;

        return candidates.OrderBy(StartTimeOrMax).First();
    }

    static DateTime StartTimeOrMax(Process p)
    {
        try { return p.StartTime; }
        catch { return DateTime.MaxValue; } // 권한이 없어 시작 시각을 못 읽는 프로세스는 뒤로
    }

    /// <summary>펌프 스레드의 예외는 잡지 않으면 앱 전체를 끈다. 기록하고 상태 표시줄에 알린 뒤 이 소스만 멈춘다.</summary>
    void RunPump(CancellationToken token)
    {
        try { PumpLoop(token); }
        catch (Exception ex) { ReportFailure("오디오 처리", ex); }
    }

    void ReportFailure(string where, Exception ex)
    {
        CrashLog.Write($"ProcessLoopback {where}", ex);
        SetStatus?.Invoke($"캡처 오류({where}): {ex.Message} — 정지 후 다시 시작하세요.");
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
                // 대상 프로그램이 소리를 멈추면 캡처 패킷 자체가 끊길 수 있다. 그러면 VAD가 발화 끝(무음)을 보지 못해
                // 마지막 문장이 다음 소리가 날 때까지 전사되지 않으므로, 패킷이 한동안 없으면 무음을 직접 흘려보낸다.
                if (Environment.TickCount64 - Interlocked.Read(ref lastDataTick) >= StarvedSilenceMs)
                {
                    _ = audioChannel.PushSamples(null, new float[chunkSize]);
                    Thread.Sleep(pumpIntervalMs);
                }
                else
                {
                    Thread.Sleep(pumpIntervalMs / 2);
                }
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
