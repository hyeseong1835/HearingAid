using System.Text.Json.Nodes;

namespace HearingAid.Core.Audio;

/// <summary>
/// 생성할 audioSource 하나를 표현한다 (타입 + 생성자 인자).
/// 이름은 없다 — 이름 역할은 이제 Profile 파일명이 담당한다.
/// </summary>
public record AudioSourceSpec(string Type, JsonObject? Options);
