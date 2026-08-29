using Whisper.net;

namespace HearingAid.Core.Stt;

public class WhisperModel : IDisposable
{
    readonly WhisperFactory factory;
    readonly WhisperProcessor processor;
    readonly SemaphoreSlim gate = new(1, 1);

    public WhisperModel(string modelPath, string language = "auto", int threads = 4)
    {
        if (false == File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper 모델 없음: {modelPath}");

        factory = WhisperFactory.FromPath(
            modelPath, 
            new WhisperFactoryOptions { UseGpu = true }
        );
        processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(threads)
            .Build();
    }

    public async ValueTask TranscribeAsync(ReadOnlyMemory<float> audio,
        object? onTextTranscribeParam, Action<object?, string> onTextTranscribe,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await foreach (SegmentData segment in processor.ProcessAsync(audio, cancellationToken))
            {
                onTextTranscribe.Invoke(onTextTranscribeParam, segment.Text);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        processor.Dispose();
        factory.Dispose();
        gate.Dispose();
    }
}
