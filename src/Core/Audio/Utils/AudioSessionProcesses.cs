using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace HearingAid.Core.Audio;

/// <summary>현재 오디오 세션(재생 스트림)을 가진 실행 중인 프로그램의 이름을 나열한다.
/// ProcessLoopbackAudioSource의 대상 선택 UI, 그리고 대상을 찾지 못했을 때의 오류 메시지에 쓰인다.</summary>
public static class AudioSessionProcesses
{
    public static IReadOnlyList<string> ListActiveAudioProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

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
                        if (pid <= 0) continue;
                        names.Add(Process.GetProcessById(pid).ProcessName);
                    }
                    catch { /* 세션 프로세스가 이미 종료됐거나 접근할 수 없음 */ }
                }
            }
            catch { /* 이 장치의 세션 목록을 가져올 수 없음 */ }
            finally
            {
                device.Dispose();
            }
        }

        return names.ToList();
    }
}
