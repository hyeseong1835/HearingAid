using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace HearingAid.Core.Audio;

/// <summary>현재 오디오 세션(재생 스트림)을 가진 실행 중인 프로그램을 나열한다.
/// ProcessLoopbackAudioSource의 대상 선택 UI와 캡처 대상 프로세스 결정, 그리고 대상을 찾지 못했을 때의 오류 메시지에 쓰인다.</summary>
public static class AudioSessionProcesses
{
    public static IReadOnlyList<string> ListActiveAudioProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int pid in EnumerateSessionProcessIds())
        {
            try { names.Add(Process.GetProcessById(pid).ProcessName); }
            catch { /* 세션 프로세스가 이미 종료됐거나 접근할 수 없음 */ }
        }
        return names.ToList();
    }

    /// <summary>이름이 processName인 프로세스 중 실제로 오디오 세션을 가진 것의 PID.
    /// 크롬·디스코드처럼 같은 이름의 프로세스가 수십 개인 프로그램은 소리를 내는 프로세스가 따로 있다.</summary>
    public static IReadOnlyList<int> ListSessionProcessIds(string processName)
    {
        var pids = new SortedSet<int>();
        foreach (int pid in EnumerateSessionProcessIds())
        {
            try
            {
                if (Process.GetProcessById(pid).ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    pids.Add(pid);
            }
            catch { }
        }
        return pids.ToList();
    }

    static IEnumerable<int> EnumerateSessionProcessIds()
    {
        var pids = new HashSet<int>();

        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        if (session.IsSystemSoundsSession) continue;
                        int pid = (int)session.GetProcessID;
                        if (pid > 0) pids.Add(pid);
                    }
                    catch { /* 세션이 사라졌거나 접근할 수 없음 */ }
                }
            }
            catch { /* 이 장치의 세션 목록을 가져올 수 없음 */ }
            finally
            {
                device.Dispose();
            }
        }

        return pids;
    }
}
