namespace HearingAid.Core.Audio;

/// <summary>오디오 소스 생성자 매개변수 하나에 대응하는 설정 항목의 메타데이터.
/// Picker는 매개변수에 붙은 [SuggestionPicker] 계열 특성으로, 설정 UI가 후보 목록 콤보박스를 보여준다(예: 프로세스, 입력 장치).
/// IsSecret은 매개변수에 [PasswordPropertyText]가 붙은 경우로, 설정 UI가 값을 가려서 보여준다(예: 봇 토큰).</summary>
public record AudioSourceSettingSpec(string Name, Type ClrType, string Description, object? DefaultValue,
    SuggestionPickerAttribute? Picker = null, bool IsSecret = false);

/// <summary>특정 AudioSourceDefinition에 대해 계산된 설정 항목의 현재 유효값.</summary>
public record AudioSourceSettingValue(AudioSourceSettingSpec Spec, object? CurrentValue);
