using HearingAid.Core.Stt;

namespace HearingAid.Core.Audio;

public class AudioChannel : IDisposable
{
    public object? samplesAvailableHandlerParam;
    public delegate ValueTask SamplesAvailableHandler(object param, AudioChannel channel, string? speaker, ReadOnlyMemory<float> samples);
    public SamplesAvailableHandler? samplesAvailableHandler;

    /// <summary>VAD 게이팅 이전의 원시 샘플을 그대로 흘려보낸다(파형 진단 도구 등, 발화 구간 여부와
    /// 무관하게 신호를 항상 보고 싶은 소비자용). samplesAvailableHandler와 달리 매 청크마다 호출된다.</summary>
    public Action<ReadOnlyMemory<float>>? rawSamplesAvailable;

    public WhisperModel? whisperModel;

    public float vadThreshold = 0.7f;
    public int minSilenceMs = 600;
    public int speechPadMs = 200;
    public int minSpeechMs = 500;

    /// <summary>가장 최근에 확정되어 samplesAvailableHandler로 전달된 발화 구간의 최대 VAD 확률.
    /// 그 발화를 전사하는 콜백이 완료 시점에 읽어가도록(TranscriberEngine) 필드로 노출한다.</summary>
    public float lastMaxProb;

    readonly SileroVad vad;
    readonly List<float> pending = new();
    readonly List<float> speechBuf = new();
    readonly List<float> padBuf = new();
    bool speaking;
    int silenceSamples;
    float maxProb;
    bool disposed;

    public AudioChannel(SileroVad vad)
    {
        this.vad = vad;
    }

    /// <summary>원시 샘플을 밀어넣는다. 내부에서 512샘플 윈도우 단위로 VAD를 돌리고,
    /// 발화 구간이 끝났다고 판단될 때만 samplesAvailableHandler를 그 구간 전체와 함께 호출한다.</summary>
    public ValueTask PushSamples(string? speaker, ReadOnlyMemory<float> samples)
    {
        rawSamplesAvailable?.Invoke(samples);

        pending.AddRange(samples.Span);

        while (pending.Count >= SileroVad.WindowSize)
        {
            float[] window = pending.GetRange(0, SileroVad.WindowSize).ToArray();
            pending.RemoveRange(0, SileroVad.WindowSize);
            ProcessWindow(speaker, window);
        }

        return ValueTask.CompletedTask;
    }

    void ProcessWindow(string? speaker, float[] window)
    {
        float prob = vad.Run(window);

        if (!speaking)
        {
            padBuf.AddRange(window);
            int padMax = speechPadMs * AudioPipeline.SampleRate / 1000;
            if (padBuf.Count > padMax)
                padBuf.RemoveRange(0, padBuf.Count - padMax);

            if (prob >= vadThreshold)
            {
                speaking = true;
                silenceSamples = 0;
                maxProb = prob;
                speechBuf.Clear();
                speechBuf.AddRange(padBuf);
                speechBuf.AddRange(window);
                padBuf.Clear();
            }
        }
        else
        {
            speechBuf.AddRange(window);
            maxProb = Math.Max(maxProb, prob);

            if (prob >= vadThreshold) silenceSamples = 0;
            else silenceSamples += SileroVad.WindowSize;

            if (silenceSamples >= minSilenceMs * AudioPipeline.SampleRate / 1000)
            {
                speaking = false;
                float[] audio = speechBuf.ToArray();
                speechBuf.Clear();
                padBuf.Clear();
                vad.Reset();

                if (audio.Length >= minSpeechMs * AudioPipeline.SampleRate / 1000)
                {
                    lastMaxProb = maxProb;
                    _ = samplesAvailableHandler?.Invoke(samplesAvailableHandlerParam!, this, speaker, audio);
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        vad.Dispose();
    }
}
