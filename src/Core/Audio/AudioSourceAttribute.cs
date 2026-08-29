namespace HearingAid.Core.Audio;

[AttributeUsage(AttributeTargets.Class)]
public class AudioSourceAttribute : Attribute
{
    public string Name { get; }
    public AudioSourceAttribute(string name) => Name = name;
}