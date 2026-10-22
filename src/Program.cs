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
}
