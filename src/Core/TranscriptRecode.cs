namespace HearingAid.Core;

public record struct TranscriptRecode(
    DateTime time, 
    string? speaker, 
    string? text
);