namespace HearingAid.Core.Audio;

/// <summary>모든 AudioSource가 공유하는 증폭(gain) 적용 로직.
/// 디지털 클리핑으로 인한 왜곡(그리고 그로 인한 VAD/전사 오작동)을 막기 위해 [-1, 1] 범위로 clamp한다.</summary>
static class AudioEffect
{
    public static void Gain(float[] samples, float gain)
    {
        if (gain == 1f) return;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
        }
    }
}
