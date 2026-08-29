using NAudio.Wave;

namespace HearingAid.Core.Audio;

/// <summary>임의 채널 수(모노/스테레오/5.1/7.1...)를 평균으로 모노 다운믹스한다.
/// NAudio 내장 ToMono()는 스테레오 전용이라 사용하지 않는다.</summary>
sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    readonly ISampleProvider source;
    readonly int channels;
    float[] scratch = Array.Empty<float>();

    public DownmixToMonoSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = Math.Max(1, source.WaveFormat.Channels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        if (channels == 1) return source.Read(buffer);

        int needed = buffer.Length * channels;
        if (scratch.Length < needed) scratch = new float[needed];
        int read = source.Read(scratch.AsSpan(0, needed));
        int frames = read / channels;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++) sum += scratch[i * channels + ch];
            buffer[i] = sum / channels;
        }
        return frames;
    }
}
