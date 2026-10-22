namespace HearingAid.Core.Audio;

/// <summary>입력이 작을 때 자동으로 키워 주는 증폭기(AGC). 앱 볼륨·플레이어 볼륨이 낮아 신호가 아주 작으면
/// VAD가 발화로 인식하지 못하므로, 최근 peak를 목표 크기까지 끌어올린다. 키우기만 하고 줄이지는 않는다.
/// 큰 소리가 오면 즉시 배율을 낮추고(클리핑 방지), 작아지면 천천히 다시 올린다.</summary>
public sealed class AutoGain
{
    /// <summary>증폭 후 peak가 이 정도가 되도록 맞춘다.</summary>
    const float TargetPeak = 0.5f;
    /// <summary>최대 배율(40dB). 너무 크면 조용할 때 배경 잡음까지 발화처럼 커진다.</summary>
    const float MaxGain = 100f;
    /// <summary>이보다 작은 레벨(-80dB)은 무음으로 보고 배율을 올리지 않는다.</summary>
    const float SilenceFloor = 1e-4f;
    /// <summary>peak 기억이 줄어드는 시간 상수(초). 말 사이 짧은 쉼에 배율이 출렁이지 않게 길게 둔다.</summary>
    const float ReleaseSeconds = 3f;
    /// <summary>배율이 올라가는 속도(초당 배수).</summary>
    const float RisePerSecond = 2f;

    float envelope;
    float gain = 1f;

    public float CurrentGain => gain;

    public void Process(Span<float> samples)
    {
        if (samples.Length == 0) return;
        float dt = samples.Length / (float)AudioPipeline.SampleRate;

        float peak = 0f;
        foreach (float s in samples) peak = Math.Max(peak, Math.Abs(s));

        envelope = Math.Max(peak, envelope * MathF.Exp(-dt / ReleaseSeconds));

        if (envelope >= SilenceFloor)
        {
            float desired = Math.Clamp(TargetPeak / envelope, 1f, MaxGain);
            gain = desired < gain
                ? desired                                                   // 큰 소리: 즉시 낮춤
                : Math.Min(desired, gain * MathF.Pow(RisePerSecond, dt));   // 작은 소리: 천천히 올림
        }

        if (gain == 1f) return;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
    }

    public void Reset()
    {
        envelope = 0f;
        gain = 1f;
    }
}
