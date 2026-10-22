using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using HearingAid.Core.Stt;

namespace HearingAid.Core.Update;

/// <summary>GitHub 최신 릴리스 한 건. ZipUrl은 HearingAid-&lt;버전&gt;-win-x64.zip 자산의 다운로드 주소.</summary>
public record ReleaseInfo(Version Version, string Tag, string Notes, string ZipUrl, long ZipSize, string? Sha256);

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

    /// <summary>최신 릴리스가 현재 버전보다 새로우면 그 정보를, 아니면 null을 돌려준다.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await Http.SendAsync(request, cts.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // 아직 릴리스가 하나도 없음
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        JsonElement root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out Version latest) || latest <= CurrentVersion)
            return null;

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("HearingAid-", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                continue;

            string? sha256 = asset.TryGetProperty("digest", out JsonElement d) && d.GetString() is { } digest
                && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;

            return new ReleaseInfo(
                latest, tag,
                root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                sha256);
        }

        throw new InvalidOperationException($"릴리스 {tag}에 HearingAid-*-win-x64.zip 파일이 없습니다.");
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
