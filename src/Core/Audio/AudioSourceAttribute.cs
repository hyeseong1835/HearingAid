namespace HearingAid.Core.Audio;

/// <summary>Name은 설정 파일의 audioSource.type 값(바꾸면 기존 설정이 깨진다). DisplayName/Description은
/// 프로필 설정 화면에서 사용자가 어떤 소스인지 알아볼 수 있게 보여주는 문구다.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class AudioSourceAttribute : Attribute
{
    public string Name { get; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public AudioSourceAttribute(string name) => Name = name;
}
