namespace HearingAid.Core.Audio;

/// <summary>오디오 소스 생성자 매개변수 하나에 대응하는 설정 항목의 메타데이터.</summary>
public record AudioSourceSettingSpec(string Name, Type ClrType, string Description, object? DefaultValue, bool IsProcessPicker = false);

/// <summary>특정 AudioSourceDefinition에 대해 계산된 설정 항목의 현재 유효값.</summary>
public record AudioSourceSettingValue(AudioSourceSettingSpec Spec, object? CurrentValue);
