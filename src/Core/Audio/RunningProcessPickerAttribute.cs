namespace HearingAid.Core.Audio;

/// <summary>이 생성자 매개변수는 설정 UI에서 자유 텍스트 상자 대신, 현재 오디오 세션을 가진
/// 실행 중인 프로그램 목록에서 고르는 콤보박스로 렌더링돼야 함을 표시한다.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class RunningProcessPickerAttribute : Attribute
{
}
