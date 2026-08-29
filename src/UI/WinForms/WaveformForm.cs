using HearingAid.Core;

namespace HearingAid;

/// <summary>AudioSource가 공급하는 원시 오디오를 실시간 파형으로 보여주는 진단용 창.
/// gain/클리핑 여부, 실제 신호 크기를 눈으로 확인하기 위한 용도.</summary>
class WaveformForm : Form
{
    readonly WaveformPanel panel = new() { Dock = DockStyle.Fill };
    ITranscriber? engine;

    public WaveformForm()
    {
        Text = "오디오 파형";
        Width = 640;
        Height = 220;
        BackColor = Color.FromArgb(24, 24, 28);
        Controls.Add(panel);
    }

    public void AttachEngine(ITranscriber e)
    {
        if (engine is not null) engine.RawSamplesAvailable -= OnSamples;
        engine = e;
        engine.RawSamplesAvailable += OnSamples;
    }

    void OnSamples(ReadOnlyMemory<float> samples) => panel.Push(samples.ToArray());

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (engine is not null) engine.RawSamplesAvailable -= OnSamples;
        base.OnFormClosed(e);
    }
}
