using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HearingAid.Core.Audio;

/// <summary>이 생성자 매개변수는 설정 UI에서 자유 텍스트 상자 대신, 후보 목록에서 고르거나 직접 입력할 수 있는
/// 콤보박스로 렌더링돼야 함을 표시한다. 후보는 콤보를 펼칠 때마다 다시 조회한다.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public abstract class SuggestionPickerAttribute : Attribute
{
    public abstract IReadOnlyList<string> GetSuggestions();
}

/// <summary>현재 오디오 세션을 가진 실행 중인 프로그램 목록.</summary>
public class RunningProcessPickerAttribute : SuggestionPickerAttribute
{
    public override IReadOnlyList<string> GetSuggestions() => AudioSessionProcesses.ListActiveAudioProcessNames().ToList();
}

/// <summary>WaveIn 입력 장치(마이크 등) 이름 목록. WaveInAudioSource가 같은 이름으로 장치를 찾는다.</summary>
public class InputDevicePickerAttribute : SuggestionPickerAttribute
{
    public override IReadOnlyList<string> GetSuggestions()
    {
        var names = new List<string>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            names.Add(WaveIn.GetCapabilities(i).ProductName);
        return names;
    }
}

/// <summary>활성 재생 장치 이름 목록.</summary>
public class OutputDevicePickerAttribute : SuggestionPickerAttribute
{
    public override IReadOnlyList<string> GetSuggestions()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => { using (d) return d.FriendlyName; })
            .ToList();
    }
}
