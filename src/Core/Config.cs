using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Reflection;
using System.ComponentModel;

using HearingAid.Core.Audio;

namespace HearingAid.Core;

public class Config
{
    #region Static

    static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions DeserializeOptions = new() { PropertyNameCaseInsensitive = true };

    static IReadOnlyList<Type>? cachedAudioSourceTypes;

    /// <summary>오디오 소스 생성자 매개변수 값(예: UserConfig[] 같은 필드 기반 구조체 배열)을
    /// 직렬화/역직렬화할 때 쓰는 옵션. 이 프로젝트의 도메인 타입들은 속성이 아니라 public 필드를
    /// 쓰므로 IncludeFields가 꺼진 기본 옵션으로는 값이 전부 비어버린다.</summary>
    public static readonly JsonSerializerOptions AudioSourceValueJsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void Serialize(string path, Config cfg)
    {
        using FileStream fs = File.Create(path);
        JsonSerializer.Serialize(fs, cfg, SerializeOptions);
    }
    public static Config? Deserialize(string path)
    {
        if (!File.Exists(path))
            return null;

        Config? cfg;
        using (FileStream fs = File.OpenRead(path))
        {
            cfg = JsonSerializer.Deserialize<Config>(fs, DeserializeOptions);
        }
        return cfg;
    }
    
    /// <summary>IAudioSource 구현 타입 목록. 어셈블리 스캔은 최초 호출 시 한 번만 수행하고 캐싱한다.</summary>
    static IReadOnlyList<Type> AudioSourceTypes() => cachedAudioSourceTypes ??=
        typeof(IAudioSource).Assembly.GetTypes()
            .Where(t => typeof(IAudioSource).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

    /// <summary>코드에 구현된([AudioSource]로 태그된) 모든 오디오 소스 종류 이름.</summary>
    public static IReadOnlyList<string> GetAvailableAudioSourceTypeNames() =>
        AudioSourceTypes()
            .Select(t => t.GetCustomAttribute<AudioSourceAttribute>()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

    static Type? FindAudioSourceType(string typeName) =>
        AudioSourceTypes().FirstOrDefault(t =>
            t.GetCustomAttribute<AudioSourceAttribute>()?.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);

    static ConstructorInfo SingleConstructor(Type audioSourceType) => audioSourceType.GetConstructors().Single();

    static object? GetEffectiveDefault(ParameterInfo p) => p.HasDefaultValue ? p.DefaultValue : TypeDefault(p.ParameterType);

    static object? TypeDefault(Type t)
    {
        if (t == typeof(string)) return "";
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }

    static object? ResolveConstructorArg(ParameterInfo p, JsonObject? options)
    {
        if (options is not null)
        {
            var prop = options.FirstOrDefault(kv => kv.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (prop.Value is not null)
                return prop.Value.Deserialize(p.ParameterType, AudioSourceValueJsonOptions);
        }

        // 값이 전혀 없으면 (설정 파일에 필드가 누락돼도) 에러 대신 타입 기본값으로 대체한다.
        return GetEffectiveDefault(p);
    }

    #endregion


    #region Instance
    
    [JsonPropertyName("whisperServerExe")]
    public string WhisperServerExe { get; set; } = "";

    [JsonPropertyName("whisperModel")]
    public string WhisperModel { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ko";

    [JsonPropertyName("threads")]
    public int Threads { get; set; } = 4;

    [JsonPropertyName("captureDevice")]
    public string CaptureDevice { get; set; } = "CABLE Output";

    [JsonPropertyName("showServerLog")]
    public bool ShowServerLog { get; set; } = false;

    /// <summary>{ "type": "<AudioSourceAttribute 이름>", 생성자 파라미터... } 형태의 flat 객체.</summary>
    [JsonPropertyName("audioSource")]
    public JsonObject? AudioSource { get; set; }

    /// <summary>appsettings.json에 저장된, 앱 시작 시 자동 적용할 Profile 이름. Profile 파일 자체에는 나타나지 않는다.</summary>
    [JsonPropertyName("activeProfile")]
    public string? ActiveProfile { get; set; }

    /// <summary>이 설정을 로드한 base 파일 경로. 설정 팝업이 다시 읽어들일 때 사용한다.</summary>
    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>이 Config 인스턴스를 만들 때 실제로 적용된 Profile 이름. 없으면(기본값만 사용) null.</summary>
    [JsonIgnore]
    public string? EffectiveProfileName { get; set; }

    /// <summary>profiles/ 디렉터리 경로. PersistAudioSourceOptions가 프로필 파일을 찾을 때 쓴다.</summary>
    [JsonIgnore]
    public string? ProfilesDir { get; set; }

    /// <summary>base appsettings.json과 (있다면) profiles/&lt;profileNameOverride ?? activeProfile&gt;.json을
    /// merge-patch로 합쳐 완전히 새로운 Config를 만든다. 시작 시 1회, Profile 전환 시마다 다시 호출된다.</summary>
    public static Config LoadEffective(string baseConfigPath, string profilesDir, string? profileNameOverride = null)
    {
        var baseNode = JsonNode.Parse(File.ReadAllText(baseConfigPath)) as JsonObject ?? new JsonObject();

        string? profileName = profileNameOverride;
        if (profileName is null && baseNode.TryGetPropertyValue("activeProfile", out var apNode) && apNode is not null)
            profileName = apNode.GetValue<string>();

        JsonObject merged = baseNode;
        string? resolvedProfileName = null;

        if (!string.IsNullOrEmpty(profileName))
        {
            var profilePath = Path.Combine(profilesDir, profileName + ".json");
            if (File.Exists(profilePath))
            {
                var patch = JsonNode.Parse(File.ReadAllText(profilePath)) as JsonObject ?? new JsonObject();
                merged = MergePatch(baseNode.DeepClone().AsObject(), patch);
                resolvedProfileName = profileName;
            }
            // 파일이 없으면(외부에서 지워졌을 수 있음) 조용히 base만 사용한다.
        }

        var cfg = merged.Deserialize<Config>(DeserializeOptions) ?? new Config();
        cfg.WhisperServerExe = ResolvePath(cfg.WhisperServerExe);
        cfg.WhisperModel = ResolvePath(cfg.WhisperModel);
        cfg.FilePath = baseConfigPath;
        cfg.EffectiveProfileName = resolvedProfileName;
        cfg.ProfilesDir = profilesDir;
        return cfg;
    }

    /// <summary>절대 경로가 아니면 실행 파일 기준 상대경로로 간주해 AppContext.BaseDirectory와 합친다.
    /// 번들된 whisper-server.exe/모델을 앱 폴더 기준으로 바로 찾을 수 있게 하기 위함.</summary>
    static string ResolvePath(string path) =>
        string.IsNullOrEmpty(path) || Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);

    static JsonObject MergePatch(JsonObject target, JsonObject patch)
    {
        foreach (var kv in patch.ToList())
        {
            if (kv.Value is null) { target.Remove(kv.Key); continue; }
            if (kv.Value is JsonObject patchObj && target[kv.Key] is JsonObject targetObj)
                target[kv.Key] = MergePatch(targetObj.DeepClone().AsObject(), patchObj.DeepClone().AsObject());
            else
                target[kv.Key] = kv.Value.DeepClone();
        }
        return target;
    }

    /// <summary>base 파일의 activeProfile 키만 갱신한다. Config 전체를 덮어쓰지 않는다 — 그러면
    /// merge된(=profile이 반영된) 값이 base 파일로 새어 들어가 버린다.</summary>
    public static void SetActiveProfile(string baseConfigPath, string? profileName)
    {
        var node = JsonNode.Parse(File.ReadAllText(baseConfigPath)) as JsonObject ?? new JsonObject();
        if (string.IsNullOrEmpty(profileName)) node.Remove("activeProfile");
        else node["activeProfile"] = profileName;
        File.WriteAllText(baseConfigPath, node.ToJsonString(SerializeOptions));
    }

    public static IReadOnlyList<string> ListProfileNames(string profilesDir)
    {
        if (!Directory.Exists(profilesDir)) return Array.Empty<string>();
        return Directory.GetFiles(profilesDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static readonly object FileWriteLock = new();

    /// <summary>오디오 소스가 스스로 학습한 설정 값들을, 지금 활성 설정 파일(프로필이 적용
    /// 중이면 그 프로필, 아니면 base appsettings.json)의 audioSource 객체 안에 병합해 즉시
    /// 저장한다. 다른 키/다른 설정은 건드리지 않는다. 메모리 상의 this.AudioSource도 함께
    /// 갱신해 이후 재조회(예: 설정 화면을 다시 열 때)에 최신 값이 보이게 한다.</summary>
    public void PersistAudioSourceOptions(JsonObject updates)
    {
        if (FilePath is null) return;

        string targetPath = EffectiveProfileName is { } name && ProfilesDir is { } dir
            ? Path.Combine(dir, name + ".json")
            : FilePath;

        lock (FileWriteLock)
        {
            var fileNode = File.Exists(targetPath)
                ? (JsonNode.Parse(File.ReadAllText(targetPath)) as JsonObject ?? new JsonObject())
                : new JsonObject();

            var audioSourceNode = fileNode["audioSource"] as JsonObject;
            if (audioSourceNode is null)
            {
                audioSourceNode = new JsonObject();
                if (AudioSource?["type"] is { } typeNode) audioSourceNode["type"] = typeNode.DeepClone();
                fileNode["audioSource"] = audioSourceNode;
            }
            foreach (var kv in updates)
                audioSourceNode[kv.Key] = kv.Value?.DeepClone();

            File.WriteAllText(targetPath, fileNode.ToJsonString(SerializeOptions));
        }

        AudioSource ??= new JsonObject();
        foreach (var kv in updates)
            AudioSource[kv.Key] = kv.Value?.DeepClone();
    }

    /// <summary>프로필을 로드하고 그 설정으로 오디오 소스와 TranscriberEngine을 새로 만든다.
    /// Program.cs의 최초 기동과 ProfileSettingsForm의 프로필 전환이 이 로직을 공유한다.</summary>
    public static TranscriberEngine BuildEngine(string baseConfigPath, string profilesDir, string? profileName)
    {
        Config cfg = LoadEffective(baseConfigPath, profilesDir, profileName);
        IAudioSource source = cfg.CreateConfiguredAudioSources();
        return new TranscriberEngine(cfg, [source]);
    }

    /// <summary>this.AudioSource에 담긴 type/파라미터로 실제 IAudioSource 인스턴스를 만든다.</summary>
    public IAudioSource CreateConfiguredAudioSources()
    {
        if (AudioSource is null)
            throw new InvalidOperationException("audioSource 설정이 없습니다.");

        string? type = AudioSource.TryGetPropertyValue("type", out var t) ? t?.GetValue<string>() : null;
        if (string.IsNullOrEmpty(type))
            throw new InvalidOperationException("audioSource.type이 없습니다.");

        return CreateAudioSource(new AudioSourceSpec(type, AudioSource));
    }

    public IAudioSource CreateAudioSource(AudioSourceSpec spec)
    {
        var type = FindAudioSourceType(spec.Type);
        if (type is null)
            throw new InvalidOperationException($"알 수 없는 audioSource 종류: '{spec.Type}'");

        var ctor = SingleConstructor(type);
        var args = ctor.GetParameters().Select(p => ResolveConstructorArg(p, spec.Options)).ToArray();
        return (IAudioSource)ctor.Invoke(args);
    }

    /// <summary>주어진 spec에 대해, 그 오디오 소스 타입의 생성자 매개변수마다
    /// 메타데이터(설명/기본값)와 현재 유효값(옵션→기본값 순)을 계산해 반환한다.
    /// UI(ProfileSettingsForm)와 CreateAudioSource가 이 우선순위 로직을 공유한다.</summary>
    public IReadOnlyList<AudioSourceSettingValue> GetSettingValues(AudioSourceSpec spec)
    {
        var type = FindAudioSourceType(spec.Type);
        if (type is null)
            throw new InvalidOperationException($"알 수 없는 audioSource 종류: '{spec.Type}'");

        var ctor = SingleConstructor(type);
        return ctor.GetParameters().Select(p =>
        {
            var settingSpec = new AudioSourceSettingSpec(
                p.Name!, p.ParameterType,
                p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                GetEffectiveDefault(p),
                p.GetCustomAttribute<RunningProcessPickerAttribute>() is not null);
            return new AudioSourceSettingValue(settingSpec, ResolveConstructorArg(p, spec.Options));
        }).ToList();
    }

    #endregion
}
