using HearingAid.Core.Audio;

namespace HearingAid;

/// <summary>최근 N초간의 16kHz 모노 샘플을 스크롤 파형으로 그리는 진단용 컨트롤.
/// 클리핑(±1.0 근접) 구간은 색을 다르게 표시하고, 피크/RMS 수치를 좌상단에 표시한다.</summary>
class WaveformPanel : Panel
{
    const int SampleRate = AudioPipeline.SampleRate;

    readonly float[] ring;
    readonly object sync = new();
    int writePos;
    volatile float peak;
    volatile float rms;

    readonly System.Windows.Forms.Timer redrawTimer;

    public WaveformPanel(double secondsVisible = 3.0)
    {
        ring = new float[Math.Max(1, (int)(SampleRate * secondsVisible))];
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 18, 22);

        redrawTimer = new System.Windows.Forms.Timer { Interval = 50 };
        redrawTimer.Tick += (s, e) => Invalidate();
        redrawTimer.Start();
    }

    /// <summary>오디오 콜백 스레드에서 호출된다 (UI 스레드 아님). ring buffer 쓰기만 하므로 lock으로 충분하다.</summary>
    public void Push(float[] samples)
    {
        if (samples.Length == 0) return;

        lock (sync)
        {
            foreach (var s in samples)
            {
                ring[writePos] = s;
                writePos = (writePos + 1) % ring.Length;
            }
        }

        float pk = 0;
        double sumSq = 0;
        foreach (var s in samples)
        {
            float a = Math.Abs(s);
            if (a > pk) pk = a;
            sumSq += (double)s * s;
        }
        peak = pk;
        rms = (float)Math.Sqrt(sumSq / samples.Length);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        int mid = h / 2;

        using (var axisPen = new Pen(Color.FromArgb(60, 60, 66)))
            g.DrawLine(axisPen, 0, mid, w, mid);

        float[] snapshot;
        int startPos;
        lock (sync) { snapshot = (float[])ring.Clone(); startPos = writePos; }
        int n = snapshot.Length;

        bool anyClip = false;
        using var wavePen = new Pen(Color.FromArgb(0, 200, 120));
        using var clipPen = new Pen(Color.FromArgb(255, 90, 60));

        for (int x = 0; x < w; x++)
        {
            int start = x * n / w;
            int end = Math.Max(start + 1, (x + 1) * n / w);
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = start; i < end; i++)
            {
                float v = snapshot[(startPos + i) % n];
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            if (mn > mx) { mn = 0; mx = 0; }

            bool colClip = mx >= 0.999f || mn <= -0.999f;
            if (colClip) anyClip = true;

            int y1 = mid - (int)(mx * (mid - 2));
            int y2 = mid - (int)(mn * (mid - 2));
            g.DrawLine(colClip ? clipPen : wavePen, x, y1, x, Math.Max(y2, y1 + 1));
        }

        string info = $"Peak {peak:F3}{(peak >= 0.999f ? "  ⚠ CLIPPING" : "")}    RMS {rms:F3}";
        using var font = new Font("Segoe UI", 9);
        using var textBrush = new SolidBrush(anyClip ? Color.OrangeRed : Color.Gainsboro);
        g.DrawString(info, font, textBrush, 6, 4);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) redrawTimer.Dispose();
        base.Dispose(disposing);
    }
}
