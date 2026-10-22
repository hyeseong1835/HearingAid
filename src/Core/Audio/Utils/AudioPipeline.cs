namespace HearingAid.Core.Audio;

/// <summary>모든 AudioSource, TranscriberEngine, SileroVad, WaveformPanel이 공유하는 파이프라인 샘플레이트.
/// Whisper/Silero VAD 모델이 16kHz 모노 입력을 전제로 하므로 AudioSource별로 다르게 설정할 수 없다.</summary>
static class AudioPipeline
{
    public const int SampleRate = 16000;

    /// <summary>모든 오디오 소스의 autoGain 매개변수 설명(속성 인자는 상수여야 하므로 여기 둔다).</summary>
    public const string AutoGainDescription =
        "자동 증폭. 앱·플레이어 볼륨이 낮아 소리가 아주 작게 들어와도 알아서 키운다(최대 100배). " +
        "조용한 방의 마이크처럼 잡음이 큰 환경에서 엉뚱한 문장이 나오면 끄세요";

    public static string VadModelPath => Path.Combine(AppContext.BaseDirectory, "silero_vad.onnx");
}
