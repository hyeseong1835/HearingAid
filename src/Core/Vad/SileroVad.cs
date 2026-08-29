using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using HearingAid.Core.Audio;

namespace HearingAid.Core;

public class SileroVad : IDisposable
{
    public const int WindowSize = 512;

    readonly InferenceSession session;
    float[] state = new float[2 * 1 * 128];

    public SileroVad(string modelPath)
    {
        var opt = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 1 };
        session = new InferenceSession(modelPath, opt);
    }

    public void Reset() => state = new float[2 * 1 * 128];

    public float Run(float[] window)
    {
        var input = new DenseTensor<float>(window, new[] { 1, window.Length });
        var sr = new DenseTensor<long>(new long[] { AudioPipeline.SampleRate }, new int[] { });
        var st = new DenseTensor<float>(state, new[] { 2, 1, 128 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", input),
            NamedOnnxValue.CreateFromTensor("sr", sr),
            NamedOnnxValue.CreateFromTensor("state", st)
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);

        float prob = 0;
        float[]? newState = null;
        foreach (var r in results)
        {
            if (r.Name == "output") prob = r.AsTensor<float>().ToArray()[0];
            else if (r.Name == "stateN") newState = r.AsTensor<float>().ToArray();
        }
        if (newState != null) state = newState;
        return prob;
    }

    public void Dispose() => session.Dispose();
}