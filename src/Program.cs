using HearingAid.Core;
using HearingAid.Core.Stt;
using HearingAid.Core.Update;

namespace HearingAid;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 업데이트 교체 모드: 임시 폴더에 풀린 새 버전 exe로 실행되어 앱 폴더의 파일만 바꾸고 끝낸다.
        if (args.Length > 0 && args[0] == AppUpdater.ApplyUpdateArg)
        {
            UpdateApplier.Run(args);
            return;
        }
        AppUpdater.CleanupLeftovers();
        InstallCrashHandlers();

        string baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string profilesDir = Path.Combine(AppContext.BaseDirectory, "profiles");
        Directory.CreateDirectory(profilesDir);

        // 첫 실행: 모델이 하나도 없으면 먼저 받게 한다. 받지 않고 닫아도 메인 창은 열리며,
        // 시작 버튼을 누르면 엔진이 "모델 없음"을 안내한다.
        if (ModelStore.ListInstalled().Count == 0)
        {
            using var dlg = new ModelManagerDialog(baseConfigPath);
            dlg.ShowDialog();
        }

        TranscriberEngine engine = Config.BuildEngine(baseConfigPath, profilesDir, null);

        Application.Run(new MainForm(engine, baseConfigPath, profilesDir));
    }

    /// <summary>처리되지 않은 예외를 logs\에 남긴다.
    /// UI 스레드의 예외는 앱을 끄지 않고 알리기만 하고, 다른 스레드의 예외는 (.NET이 프로세스를 끝내기 전에) 기록만 남긴다.</summary>
    static void InstallCrashHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            string? path = CrashLog.Write("UI 스레드", e.Exception);
            MessageBox.Show($"예기치 못한 오류가 발생했습니다.\n\n{e.Exception.Message}" +
                            (path is null ? "" : $"\n\n기록: {path}"),
                "HearingAid 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) CrashLog.Write("백그라운드 스레드 (앱 종료)", ex);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            CrashLog.Write("관찰되지 않은 작업", e.Exception);
            e.SetObserved(); // 기록만 하고 앱은 계속 실행
        };
    }
}
