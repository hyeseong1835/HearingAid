using System.Diagnostics;

namespace HearingAid.Core.Update;

/// <summary>--apply-update &lt;pid&gt; &lt;appDir&gt; &lt;stageDir&gt; 모드. 임시 폴더에 풀린 새 버전 exe가
/// 직접 실행되어, 기존 앱이 끝나기를 기다린 뒤 앱 폴더의 파일을 교체하고 앱을 다시 띄운다.</summary>
public static class UpdateApplier
{
    /// <summary>사용자가 만들거나 고친 파일. 앱 폴더에 이미 있으면 새 버전 것으로 덮어쓰지 않는다.</summary>
    static readonly string[] PreservedFiles = ["appsettings.json"];
    static readonly string[] PreservedDirs = ["profiles", "models", "whisper"];

    public static void Run(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], out int pid))
        {
            MessageBox.Show("업데이트 인자가 올바르지 않습니다.", "HearingAid 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string appDir = args[2];
        string stageDir = args[3];

        try
        {
            WaitForExit(pid);
            CopyTree(stageDir, appDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"업데이트 파일을 복사하지 못했습니다.\n\n{ex.Message}\n\n" +
                $"새 버전 파일은 다음 폴더에 있습니다. 앱 폴더에 직접 복사해도 됩니다.\n{stageDir}",
                "HearingAid 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        Process.Start(new ProcessStartInfo(Path.Combine(appDir, "HearingAid.exe")) { UseShellExecute = true, WorkingDirectory = appDir });
    }

    static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("기존 HearingAid가 30초 안에 종료되지 않았습니다.");
        }
        catch (ArgumentException) { } // 이미 종료됨
    }

    static void CopyTree(string stageDir, string appDir)
    {
        foreach (string src in Directory.EnumerateFiles(stageDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(stageDir, src);
            string dst = Path.Combine(appDir, rel);
            if (IsPreserved(rel) && File.Exists(dst)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            CopyWithRetry(src, dst);
        }
    }

    static bool IsPreserved(string rel)
    {
        if (PreservedFiles.Any(f => rel.Equals(f, StringComparison.OrdinalIgnoreCase))) return true;
        string first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return PreservedDirs.Any(d => first.Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>종료 직후엔 백신 검사나 늦게 풀리는 핸들 때문에 잠시 잠겨 있을 수 있어 몇 번 다시 시도한다.</summary>
    static void CopyWithRetry(string src, string dst)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Copy(src, dst, overwrite: true); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(500); }
        }
    }
}
