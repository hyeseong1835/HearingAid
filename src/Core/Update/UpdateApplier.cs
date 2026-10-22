using System.Diagnostics;

namespace HearingAid.Core.Update;

/// <summary>--apply-update &lt;pid&gt; &lt;appDir&gt; &lt;stageDir&gt; 모드. 임시 폴더에 풀린 새 버전 exe가
/// 직접 실행되어, 기존 앱이 끝나기를 기다린 뒤 앱 폴더의 파일을 교체하고 앱을 다시 띄운다.</summary>
public static class UpdateApplier
{
    /// <summary>사용자가 만들거나 고친 파일. 앱 폴더에 이미 있으면 새 버전 것으로 덮어쓰지 않는다.</summary>
    static readonly string[] PreservedFiles = ["appsettings.json"];
    static readonly string[] PreservedDirs = ["profiles", "models", "whisper", "logs"];

    /// <summary>배포 zip에 든 파일 목록(scripts/publish.ps1이 만든다). 새 버전에 없는 예전 파일을 지우는 데 쓴다.
    /// 목록에 적힌 파일만 지우므로 사용자가 앱 폴더에 넣은 파일은 건드리지 않는다.</summary>
    public const string ManifestFile = "app-files.txt";

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
            // 복사가 예전 목록을 덮어쓰기 전에 읽어 둔다.
            HashSet<string> oldFiles = ReadManifest(appDir);
            CopyTree(stageDir, appDir);
            RemoveObsoleteFiles(appDir, oldFiles, ReadManifest(stageDir));
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

    /// <summary>폴더의 파일 목록을 읽는다. 목록이 없으면(목록을 만들기 전 버전) 빈 집합.
    /// 앱 폴더 밖을 가리키는 항목은 버린다.</summary>
    static HashSet<string> ReadManifest(string dir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(dir, ManifestFile);
        if (!File.Exists(path)) return set;
        foreach (string line in File.ReadAllLines(path))
        {
            string rel = line.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Split(Path.DirectorySeparatorChar).Contains("..")) continue;
            set.Add(rel);
        }
        return set;
    }

    /// <summary>예전 버전 목록에는 있고 새 버전 목록에는 없는 파일(이름이 바뀌거나 빠진 DLL 등)을 지운다.
    /// 새 목록이 비어 있으면(잘못 만든 배포본) 아무것도 지우지 않는다. 지우지 못한 파일은 그냥 둔다.</summary>
    static void RemoveObsoleteFiles(string appDir, HashSet<string> oldFiles, HashSet<string> newFiles)
    {
        if (oldFiles.Count == 0 || newFiles.Count == 0) return;
        foreach (string rel in oldFiles)
        {
            if (newFiles.Contains(rel) || IsPreserved(rel)) continue;
            try { File.Delete(Path.Combine(appDir, rel)); } catch { }
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
