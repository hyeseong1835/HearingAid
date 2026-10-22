namespace HearingAid.Core;

/// <param name="source">이 발화를 보낸 오디오 소스의 순번(프로필의 audioSources 배열 인덱스, 0부터).</param>
public record struct TranscriptRecode(
    DateTime time,
    string? speaker,
    string? text,
    int source = 0
);
