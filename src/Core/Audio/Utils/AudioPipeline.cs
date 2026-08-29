namespace HearingAid.Core.Audio;

/// <summary>모든 AudioSource, TranscriberEngine, SileroVad, WaveformPanel이 공유하는 파이프라인 샘플레이트.
/// Whisper/Silero VAD 모델이 16kHz 모노 입력을 전제로 하므로 AudioSource별로 다르게 설정할 수 없다.</summary>
static class AudioPipeline
{
    public const int SampleRate = 16000;

    public static string VadModelPath => Path.Combine(AppContext.BaseDirectory, "silero_vad.onnx");
}
