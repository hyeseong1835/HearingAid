using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using HearingAid.Core.Stt;

namespace HearingAid.Core.Update;

/// <summary>GitHub 릴리스 한 건. ZipUrl은 HearingAid-&lt;버전&gt;-win-x64.zip 자산의 다운로드 주소.
/// IsDevBuild는 main에 합쳐질 때마다 자동으로 만들어지는 개발 빌드(프리릴리스)인지.</summary>
public record ReleaseInfo(Version Version, string Tag, string Notes, string ZipUrl, long ZipSize, string? Sha256, bool IsDevBuild = false);

/// <summary>GitHub Releases로 새 버전을 확인하고, 받아서 풀어 둔 뒤, 새 버전 exe에게 교체를 맡긴다.
/// 실행 중인 exe는 덮어쓸 수 없으므로 실제 파일 교체는 UpdateApplier(--apply-update 모드)가 한다.</summary>
public static class AppUpdater
{
    public const string Repo = "Hyeseong1835/HearingAid";
    public const string ApplyUpdateArg = "--apply-update";

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public static string WorkDir => Path.Combine(Path.GetTempPath(), "HearingAid-update");

    static readonly HttpClient Http = CreateHttpClient();

    static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub API는 User-Agent가 없으면 403을 돌려준다.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"HearingAid/{CurrentVersion}");
        return http;
    }

    static Version ReadCurrentVersion()
    {
        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return TryParseVersion(info, out Version v) ? v : new Version(0, 0, 0);
    }

    /// <summary>화면에 보일 버전 문자열. 정식 버전은 "1.2.3", 개발 빌드(4번째 자리 = 빌드 번호)는 "1.2.3 개발 빌드 45".</summary>
    public static string Display(Version v) => v.Revision > 0 ? $"{v.ToString(3)} 개발 빌드 {v.Revision}" : v.ToString(3);

    /// <summary>"v1.2.3", "1.2.3-beta+abc" 같은 문자열에서 숫자 부분만 읽는다.</summary>
    static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out Version? parsed)) return false;
        version = parsed;
        return true;
    }

    const string AssetPrefix = "HearingAid-";
    const string AssetSuffix = "-win-x64.zip";

    /// <summary>현재 버전보다 새 버전이 있으면 그 정보를, 없으면 null을 돌려준다.
    /// includeDevBuilds가 false면 정식 릴리스(releases/latest)만 본다. true면 개발 빌드(프리릴리스)까지 포함한
    /// 최근 릴리스 중 버전이 가장 높은 것을 고른다. 개발 빌드는 태그가 고정(dev)이라 버전을 자산 이름에서 읽는다.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(bool includeDevBuilds = false, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        string url = includeDevBuilds
            ? $"https://api.github.com/repos/{Repo}/releases?per_page=20"
            : $"https://api.github.com/repos/{Repo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await Http.SendAsync(request, cts.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // 아직 릴리스가 하나도 없음
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        List<JsonElement> releases = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        ReleaseInfo? best = null;
        foreach (JsonElement release in releases)
        {
            if (release.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean()) continue;
            if (ReadRelease(release) is { } info && (best is null || info.Version > best.Version))
                best = info;
        }
        return best is not null && best.Version > CurrentVersion ? best : null;
    }

    /// <summary>릴리스 JSON 하나에서 zip 자산과 버전을 읽는다. 자산이 없거나 버전을 알 수 없으면 null.</summary>
    static ReleaseInfo? ReadRelease(JsonElement release)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        bool prerelease = release.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean();

        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            // 정식 릴리스는 태그(v1.2.3)에서, 개발 빌드는 자산 이름(HearingAid-1.2.3.45-win-x64.zip)에서 버전을 읽는다.
            string assetVersion = name[AssetPrefix.Length..^AssetSuffix.Length];
            if (!TryParseVersion(assetVersion, out Version version) && !TryParseVersion(tag, out version))
                return null;

            string? sha256 = asset.TryGetProperty("digest", out JsonElement d) && d.GetString() is { } digest
                && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;

            return new ReleaseInfo(
                version, tag,
                release.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                sha256,
                prerelease);
        }
        return null;
    }

    /// <summary>릴리스 zip을 받아 무결성을 확인하고 임시 폴더에 푼다. 풀린 폴더 경로를 돌려준다.</summary>
    public static async Task<string> DownloadAndStageAsync(ReleaseInfo release,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, recursive: true);
        Directory.CreateDirectory(WorkDir);

        string zipPath = Path.Combine(WorkDir, $"{release.Tag}.zip");
        using (var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.ZipSize;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            await ModelStore.CopyWithProgressAsync(src, dst, total, progress, ct);
        }

        if (release.Sha256 is { } expected)
        {
            await using var fs = File.OpenRead(zipPath);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드한 파일의 SHA-256이 릴리스 정보와 다릅니다.");
        }

        string stageDir = Path.Combine(WorkDir, "stage");
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stageDir), ct);
        if (!File.Exists(Path.Combine(stageDir, "HearingAid.exe")))
            throw new InvalidDataException("업데이트 파일에 HearingAid.exe가 없습니다.");
        return stageDir;
    }

    /// <summary>풀어 둔 새 버전 exe를 교체 모드로 실행한다. 호출한 쪽은 곧바로 앱을 종료해야 한다.</summary>
    public static void LaunchApplier(string stageDir)
    {
        var psi = new ProcessStartInfo(Path.Combine(stageDir, "HearingAid.exe")) { UseShellExecute = false };
        psi.ArgumentList.Add(ApplyUpdateArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add(AppContext.BaseDirectory);
        psi.ArgumentList.Add(stageDir);
        Process.Start(psi);
    }

    /// <summary>지난 업데이트가 남긴 임시 폴더를 지운다. 교체 모드로 실행 중이 아닐 때만 호출한다.</summary>
    public static void CleanupLeftovers()
    {
        _ = Task.Run(() =>
        {
            try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, recursive: true); }
            catch { }
        });
    }
}
