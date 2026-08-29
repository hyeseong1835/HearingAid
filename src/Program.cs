using HearingAid.Core;

namespace HearingAid;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        string baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string profilesDir = Path.Combine(AppContext.BaseDirectory, "profiles");
        Directory.CreateDirectory(profilesDir);

        TranscriberEngine engine = Config.BuildEngine(baseConfigPath, profilesDir, null);

        Application.Run(new MainForm(engine, baseConfigPath, profilesDir));
    }
}
