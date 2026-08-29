using System.ComponentModel;
using System.Text;
using HS.Common.Collection;
using NAudio.Wave;

namespace HearingAid.Core.Audio;

/// <summary>Windows 오디오 입력 장치(NAudio WaveIn)로부터 오디오를 수신하는 IAudioSource 구현.</summary>
[AudioSource("WaveIn")]
public class WaveInAudioSource : IAudioSource
{
    const int SampleRate = AudioPipeline.SampleRate;

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

    readonly string deviceKeyword;
    readonly float gain;
    readonly int bufferMilliseconds;
    WaveIn? waveIn;

    public WaveInAudioSource(
        [Description("입력 장치 이름에 포함되어야 하는 문자열 (부분 일치, 대소문자 무시)")]
        string deviceKeyword,
        [Description("입력 증폭 배율 (1.0 = 원본, 2.0 = 2배 증폭 등. 클리핑 방지를 위해 [-1, 1] 범위로 clamp됨)")]
        float gain = 1.0f,
        [Description("드라이버 캡처 버퍼 길이 (ms). 짧을수록 지연이 줄지만 너무 작으면 끊길 수 있음")]
        int bufferMilliseconds = 32,
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
        this.bufferMilliseconds = bufferMilliseconds;

        this.audioChannel = new AudioChannel(new SileroVad(AudioPipeline.VadModelPath))
        {
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

        int device = FindDevice(deviceKeyword);
        if (device < 0)
        {
            var names = new StringBuilder("장치를 찾을 수 없습니다. 목록:");
            for (int i = 0; i < WaveIn.DeviceCount; i++)
                names.Append($"\n  {i}: {WaveIn.GetCapabilities(i).ProductName}");
            throw new Exception(names.ToString());
        }

        Description = WaveIn.GetCapabilities(device).ProductName;

        waveIn = new WaveIn
        {
            DeviceNumber = device,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = bufferMilliseconds
        };
        waveIn.DataAvailable += OnData;
        waveIn.StartRecording();

        IsRunning = true;
        AudioChannelCreated?.Invoke(AudioChannelCreatedParam!, this, audioChannel);
        SetStatus?.Invoke($"캡처: {Description} — listening...");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync()
    {
        if (!IsRunning) return ValueTask.CompletedTask;
        IsRunning = false;
        try { waveIn?.StopRecording(); waveIn?.Dispose(); waveIn = null; } catch { }
        AudioChannelRemoved?.Invoke(AudioChannelRemovedParam!, this, audioChannel);
        SetStatus?.Invoke("정지됨");
        return ValueTask.CompletedTask;
    }

    static int FindDevice(string keyword)
    {
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            if (WaveIn.GetCapabilities(i).ProductName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var samples = new float[e.BytesRecorded / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        AudioEffect.Gain(samples, gain);
        _ = audioChannel.PushSamples(null, samples);
    }

    public void Dispose() => StopAsync();
}
