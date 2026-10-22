using System.Reflection;

namespace HearingAid.Core;

/// <summary>처리되지 않은 예외를 앱 폴더의 logs\에 남긴다. 앱이 갑자기 꺼졌을 때 원인을 알 수 있게 하고,
/// 다음 실행 때 "지난번에 비정상 종료됐다"고 알려 주는 데 쓴다.</summary>
public static class CrashLog
{
    public static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");
    static string SeenMarker => Path.Combine(LogDir, ".seen");

    static readonly object WriteLock = new();

    /// <summary>예외를 새 로그 파일로 남기고 그 경로를 돌려준다. 기록 자체가 실패해도 예외를 던지지 않는다.</summary>
    public static string? Write(string where, Exception ex)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(LogDir);
                string path = Path.Combine(LogDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
                string version = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
                File.WriteAllText(path,
                    $"시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"버전: {version}\n" +
                    $"OS: {Environment.OSVersion}\n" +
                    $"위치: {where}\n\n" +
                    ex);
                return path;
            }
        }
        catch { return null; }
    }

    /// <summary>지난번 확인 이후에 새로 생긴 가장 최근 로그. 없으면 null. 확인했다고 표시하는 것은 MarkSeen.</summary>
    public static string? FindUnseen()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return null;
            DateTime seen = File.Exists(SeenMarker) ? File.GetLastWriteTimeUtc(SeenMarker) : DateTime.MinValue;
            return Directory.GetFiles(LogDir, "crash-*.txt")
                .Where(f => File.GetLastWriteTimeUtc(f) > seen)
                .OrderByDescending(f => f)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    public static void MarkSeen()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(SeenMarker, "");
            File.SetLastWriteTimeUtc(SeenMarker, DateTime.UtcNow);
        }
        catch { }
    }
}
