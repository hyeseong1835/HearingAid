namespace HearingAid.Core.Stt;

/// <summary>다운로드 가능한 Whisper ggml 모델 하나. Id는 파일 이름에서 "ggml-"와 ".bin"을 뗀 것
/// (예: ggml-large-v3-turbo.bin → large-v3-turbo)이며, 설정 파일의 model/speakerModels 값으로 쓴다.
/// Repo는 모델을 받을 huggingface 저장소 이름이다.</summary>
public record WhisperModelInfo(string Id, long SizeBytes, string Description, string Repo = "ggerganov/whisper.cpp")
{
    public string FileName => $"ggml-{Id}.bin";
    public string DownloadUrl => $"https://huggingface.co/{Repo}/resolve/main/{FileName}";
}

/// <summary>모델 파일의 위치·목록·다운로드·삭제를 담당한다. 모델은 앱 폴더의 models/에 저장하고,
/// 예전 배포본이 쓰던 whisper/ 폴더의 ggml-*.bin도 설치된 모델로 인식한다.</summary>
public static class ModelStore
{
    /// <summary>huggingface에서 받는 모델. 크기는 실제 파일 크기(바이트)다.
    /// batisay-ko-*는 large-v3-turbo를 한국어로 파인튜닝한 모델이며 BatiAI Community License v2를 따른다(앱의 MIT와 별개).</summary>
    public static readonly IReadOnlyList<WhisperModelInfo> Catalog =
    [
        new("tiny",                 77_691_713, "가장 빠름, 정확도 낮음"),
        new("base-q5_1",            59_707_625, "base 양자화. 저사양용"),
        new("base",                147_951_465, "빠름, 정확도 낮음"),
        new("small-q5_1",          190_085_487, "small 양자화"),
        new("small",               487_601_967, "속도·정확도 균형"),
        new("medium-q5_0",         539_212_467, "medium 양자화"),
        new("large-v3-turbo-q5_0", 574_041_195, "large-v3-turbo 양자화. 용량 대비 정확도 좋음"),
        new("large-v3-turbo-q8_0", 874_188_075, "large-v3-turbo 양자화(고품질)"),
        new("large-v3-q5_0",     1_081_140_203, "large-v3 양자화"),
        new("medium",            1_533_763_059, "정확도 높음, 느림"),
        new("large-v3-turbo",    1_624_555_275, "large-v3 수준 정확도에 훨씬 빠름. 한국어 외 언어엔 이 모델"),
        new("large-v3",          3_095_033_483, "가장 정확, 가장 느림"),
        new("batisay-ko-turbo-q5_0", 574_041_195, "한국어 특화 large-v3-turbo 양자화 (BatiAI 라이선스)", "batiai/batisay-ko-turbo"),
        new("batisay-ko-turbo-q8_0", 874_188_075, "한국어 특화. 한국어 정확도가 가장 좋고 빠름 (BatiAI 라이선스)", "batiai/batisay-ko-turbo"),
    ];

    /// <summary>추천 모델. 앱의 기본 언어가 한국어라 한국어 특화 모델을 추천한다. q8_0은 원본(f16)과 정확도가 거의 같고 용량은 절반이다.
    /// 모델 화면이 맨 위에 별표와 강조색으로 보여주고, 첫 실행 때 바로 받을 수 있는 버튼을 둔다.</summary>
    public const string RecommendedId = "batisay-ko-turbo-q8_0";

    public static WhisperModelInfo Recommended => FindInfo(RecommendedId)!;

    public static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");
    static string LegacyDir => Path.Combine(AppContext.BaseDirectory, "whisper");

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static WhisperModelInfo? FindInfo(string id) =>
        Catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>설치된 모델 파일 경로. 없으면 null.</summary>
    public static string? GetPath(string id)
    {
        string fileName = $"ggml-{id}.bin";
        foreach (string dir in new[] { ModelsDir, LegacyDir })
        {
            string path = Path.Combine(dir, fileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool IsInstalled(string id) => GetPath(id) is not null;

    /// <summary>설치된 모델 id 목록(카탈로그에 없는, 사용자가 직접 넣은 ggml-*.bin 포함). 카탈로그 순서 우선.</summary>
    public static IReadOnlyList<string> ListInstalled()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in new[] { ModelsDir, LegacyDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "ggml-*.bin"))
                ids.Add(Path.GetFileNameWithoutExtension(f)["ggml-".Length..]);
        }
        return ids
            .OrderBy(CatalogOrder)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static int CatalogOrder(string id)
    {
        for (int i = 0; i < Catalog.Count; i++)
            if (Catalog[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }

    /// <summary>설정에 지정된 모델이 설치돼 있으면 그것을, 아니면 설치된 첫 모델을 고른다. 하나도 없으면 null.</summary>
    public static string? ResolveDefault(string? preferredId)
    {
        if (!string.IsNullOrEmpty(preferredId) && IsInstalled(preferredId)) return preferredId;
        var installed = ListInstalled();
        return installed.Count > 0 ? installed[0] : null;
    }

    /// <summary>모델을 models/에 내려받는다. .part 파일에 받은 뒤 크기를 확인하고 나서 제 이름으로 옮기므로,
    /// 중간에 취소/실패해도 깨진 모델이 설치된 것으로 보이지 않는다.</summary>
    public static async Task DownloadAsync(WhisperModelInfo info, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        string target = Path.Combine(ModelsDir, info.FileName);
        string part = target + ".part";

        try
        {
            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.SizeBytes;

                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                await CopyWithProgressAsync(src, dst, total, progress, ct);

                if (dst.Length != total)
                    throw new IOException($"다운로드 크기가 맞지 않습니다 ({dst.Length} / {total} 바이트).");
            }
            File.Move(part, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }

    internal static async Task CopyWithProgressAsync(Stream src, Stream dst, long total,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        byte[] buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }
    }

    public static void Delete(string id)
    {
        if (GetPath(id) is { } path) File.Delete(path);
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000.0:0.0} GB" : $"{bytes / 1_000_000.0:0} MB";
}
