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

    public static void Serialize(string path, Config cfg) =>
        WriteJsonFile(path, JsonSerializer.SerializeToNode(cfg, SerializeOptions)!);

    /// <summary>JSON 객체 파일을 읽는다. 파일이 없으면 빈 객체, 형식이 잘못됐으면 예외(JsonException 등).</summary>
    public static JsonObject ReadJsonFile(string path) =>
        File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject();

    /// <summary>ReadJsonFile과 같지만, 읽을 수 없는 파일도 빈 객체로 본다. 값 하나를 읽거나 고칠 때처럼
    /// 실패해도 앱이 멈추면 안 되는 곳에서 쓴다(망가진 base 파일은 시작할 때 LoadEffective가 따로 보관해 둔다).</summary>
    static JsonObject ReadJsonFileOrEmpty(string path)
    {
        try { return ReadJsonFile(path); }
        catch { return new JsonObject(); }
    }

    /// <summary>설정 파일을 원자적으로 저장한다. 임시 파일에 다 쓴 뒤 바꿔치기하므로, 쓰는 도중 앱이 꺼지거나
    /// 전원이 나가도 파일이 반쯤 쓰인 채로 남지 않는다(망가진 설정 파일은 다음 실행을 막는다).</summary>
    public static void WriteJsonFile(string path, JsonNode node)
    {
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var w = new StreamWriter(fs))
                w.Write(node.ToJsonString(SerializeOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>읽을 수 없는 설정 파일을 이름을 바꿔 옆에 보관한다. 이후 저장이 그 파일을 덮어써 내용을 잃지 않도록.
    /// 보관한 경로를 돌려준다(실패하면 null).</summary>
    static string? BackupBrokenFile(string path)
    {
        try
        {
            string backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
            return backup;
        }
        catch { return null; }
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

    /// <summary>오디오 소스 종류의 표시 이름/설명. 알 수 없는 종류면 null.</summary>
    public static AudioSourceAttribute? GetAudioSourceInfo(string typeName) =>
        FindAudioSourceType(typeName)?.GetCustomAttribute<AudioSourceAttribute>();

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
    
    /// <summary>기본으로 쓸 Whisper 모델 id(ModelStore 참고). 비어 있거나 설치돼 있지 않으면 설치된 첫 모델을 쓴다.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>화자 이름 → 모델 id. 여기 없는 화자는 Model을 쓴다.</summary>
    [JsonPropertyName("speakerModels")]
    public Dictionary<string, string>? SpeakerModels { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ko";

    [JsonPropertyName("threads")]
    public int Threads { get; set; } = 4;

    /// <summary>시작할 때 GitHub에서 새 버전을 확인할지 여부.</summary>
    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; } = true;

    public const string StableChannel = "stable";
    public const string LatestChannel = "latest";

    /// <summary>업데이트 채널. "stable"이면 정식 릴리스만, "latest"면 main에 합쳐질 때마다 자동으로 만들어지는
    /// 개발 빌드까지 포함해 가장 최신 버전을 받는다.</summary>
    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get; set; } = StableChannel;

    [JsonIgnore]
    public bool IncludeDevBuilds => string.Equals(UpdateChannel, LatestChannel, StringComparison.OrdinalIgnoreCase);

    /// <summary>동시에 쓸 오디오 소스 목록. 원소는 { "type": "<AudioSourceAttribute 이름>", 생성자 파라미터... } 형태의 flat 객체.</summary>
    [JsonPropertyName("audioSources")]
    public JsonArray? AudioSources { get; set; }

    /// <summary>예전 형식(오디오 소스 하나). audioSources가 없을 때만 읽고, 저장할 때는 audioSources로 옮긴다.</summary>
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

    /// <summary>설정 파일을 읽지 못해 기본값으로 대신했을 때 그 이유. 정상이면 null. UI가 사용자에게 알리는 데 쓴다.</summary>
    [JsonIgnore]
    public string? LoadWarning { get; set; }

    /// <summary>base appsettings.json과 (있다면) profiles/&lt;profileNameOverride ?? activeProfile&gt;.json을
    /// merge-patch로 합쳐 완전히 새로운 Config를 만든다. 시작 시 1회, Profile 전환 시마다 다시 호출된다.
    /// 설정 파일이 없거나 망가져 있어도 예외를 던지지 않는다 — 앱이 켜지지 않는 대신 기본값으로 시작하고
    /// LoadWarning에 이유를 남긴다. 망가진 base 파일은 옆에 보관해 둔다.</summary>
    public static Config LoadEffective(string baseConfigPath, string profilesDir, string? profileNameOverride = null)
    {
        var warnings = new List<string>();

        JsonObject baseNode;
        try { baseNode = ReadJsonFile(baseConfigPath); }
        catch (Exception ex)
        {
            baseNode = new JsonObject();
            string? backup = BackupBrokenFile(baseConfigPath);
            warnings.Add($"appsettings.json을 읽지 못해 기본 설정으로 시작합니다({ex.Message})." +
                         (backup is null ? "" : $" 원래 파일은 {Path.GetFileName(backup)}(으)로 보관했습니다."));
            // 망가진 채로 두면 다음 실행에도 같은 경고가 나오므로 빈 설정으로 다시 만든다.
            try { WriteJsonFile(baseConfigPath, new JsonObject()); } catch { }
        }

        string? profileName = profileNameOverride;
        if (profileName is null && baseNode["activeProfile"] is JsonValue apValue && apValue.TryGetValue(out string? ap))
            profileName = ap;

        JsonObject merged = baseNode;
        string? resolvedProfileName = null;

        if (!string.IsNullOrEmpty(profileName))
        {
            var profilePath = Path.Combine(profilesDir, profileName + ".json");
            if (File.Exists(profilePath))
            {
                // 읽지 못해도 프로필 이름은 유지해 편집 화면이 그 프로필을 열게 한다. 내용은 base만 쓰므로
                // 오디오 소스가 없어 시작할 수 없고, 경고 바가 이유를 보여준다.
                resolvedProfileName = profileName;
                try { merged = MergePatch(baseNode.DeepClone().AsObject(), ReadJsonFile(profilePath)); }
                catch (Exception ex)
                {
                    warnings.Add($"'{profileName}' 프로필 파일을 읽지 못했습니다({ex.Message}). '⚙ 편집'에서 다시 설정하고 저장하세요.");
                }
            }
            // 파일이 없으면(외부에서 지워졌을 수 있음) 조용히 base만 사용한다.
        }

        Config cfg;
        try { cfg = merged.Deserialize<Config>(DeserializeOptions) ?? new Config(); }
        catch (Exception ex)
        {
            // 형식은 JSON이지만 값의 타입이 틀린 경우(예: "threads": "four").
            cfg = new Config();
            warnings.Add($"설정 값을 해석하지 못해 기본값을 사용합니다({ex.Message}).");
        }
        cfg.FilePath = baseConfigPath;
        cfg.EffectiveProfileName = resolvedProfileName;
        cfg.ProfilesDir = profilesDir;
        cfg.LoadWarning = warnings.Count > 0 ? string.Join(" ", warnings) : null;
        return cfg;
    }

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
    public static void SetActiveProfile(string baseConfigPath, string? profileName) =>
        SetBaseValue(baseConfigPath, "activeProfile", profileName);

    /// <summary>base 파일의 model 키(프로필이 재정의하지 않을 때 쓰는 기본 모델)만 갱신한다.</summary>
    public static void SetDefaultModel(string baseConfigPath, string? modelId) =>
        SetBaseValue(baseConfigPath, "model", modelId);

    /// <summary>base 파일에 적힌 문자열 값을 그대로(프로필 미적용) 돌려준다.</summary>
    public static string? GetBaseValue(string baseConfigPath, string key) =>
        ReadJsonFileOrEmpty(baseConfigPath)[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    static void SetBaseValue(string baseConfigPath, string key, string? value) =>
        SetBaseValue(baseConfigPath, key, string.IsNullOrEmpty(value) ? null : JsonValue.Create(value));

    /// <summary>base 파일의 키 하나만 바꾼다(null이면 지운다). 다른 키와 프로필 내용은 건드리지 않는다.</summary>
    public static void SetBaseValue(string baseConfigPath, string key, JsonNode? value)
    {
        lock (FileWriteLock)
        {
            var node = ReadJsonFileOrEmpty(baseConfigPath);
            if (value is null) node.Remove(key);
            else node[key] = value;
            WriteJsonFile(baseConfigPath, node);
        }
    }

    /// <summary>base 파일(appsettings.json)만 읽어 Config로 만든다. 프로필을 적용하지 않은 앱 전체 설정을 볼 때 쓴다.</summary>
    public static Config LoadBase(string baseConfigPath)
    {
        try { return ReadJsonFileOrEmpty(baseConfigPath).Deserialize<Config>(DeserializeOptions) ?? new Config(); }
        catch { return new Config(); }
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

    /// <summary>설정 파일(base 또는 프로필) 노드에서 오디오 소스 목록을 읽는다. audioSources 배열이 있으면 그것을,
    /// 없으면 예전 형식의 audioSource 하나를 목록으로 돌려준다. type이 없는 원소도 그대로 포함한다(편집 화면용).</summary>
    public static List<JsonObject> ReadAudioSourceList(JsonObject node)
    {
        if (node["audioSources"] is JsonArray arr)
            return arr.OfType<JsonObject>().ToList();
        return node["audioSource"] is JsonObject single ? [single] : [];
    }

    public static string? GetAudioSourceType(JsonObject? source) =>
        source?["type"] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>이 Config에서 실제로 만들 오디오 소스 목록(type이 있는 것만). 순서가 곧 소스 번호다.</summary>
    [JsonIgnore]
    public IReadOnlyList<JsonObject> AudioSourceList
    {
        get
        {
            IEnumerable<JsonObject> all = AudioSources is not null ? AudioSources.OfType<JsonObject>()
                : AudioSource is not null ? new[] { AudioSource }
                : Array.Empty<JsonObject>();
            return all.Where(s => GetAudioSourceType(s) is not null).ToList();
        }
    }

    /// <summary>오디오 소스가 스스로 학습한 설정 값들을, 지금 활성 설정 파일(프로필이 적용 중이면 그 프로필,
    /// 아니면 base appsettings.json)의 index번째 오디오 소스 객체 안에 병합해 즉시 저장한다. 다른 키/다른 설정은
    /// 건드리지 않는다. 예전 형식(audioSource)이면 이 김에 audioSources 배열로 옮긴다.
    /// 파일의 소스는 id로 찾는다(실행 중에 편집 화면에서 순서를 바꿔 저장해도 같은 소스에 쓴다). id가 없는 예전 파일이면
    /// 같은 자리를 쓰되, 그 자리의 종류가 다르면 엉뚱한 소스에 쓰지 않도록 건너뛴다.</summary>
    public void PersistAudioSourceOptions(int index, JsonObject updates)
    {
        if (FilePath is null || index < 0 || index >= AudioSourceList.Count) return;
        JsonObject memorySource = AudioSourceList[index];
        string? type = GetAudioSourceType(memorySource);
        string? id = GetAudioSourceId(memorySource);

        string targetPath = EffectiveProfileName is { } name && ProfilesDir is { } dir
            ? Path.Combine(dir, name + ".json")
            : FilePath;

        lock (FileWriteLock)
        {
            JsonObject? fileNode;
            try { fileNode = ReadJsonFile(targetPath); }
            catch { fileNode = null; } // 망가진 파일에는 쓰지 않는다(덮어쓰면 남은 내용까지 잃는다)

            if (fileNode is not null)
            {
                var sources = ReadAudioSourceList(fileNode).Where(s => GetAudioSourceType(s) is not null)
                    .Select(s => s.DeepClone().AsObject()).ToList();
                int target = id is not null
                    ? sources.FindIndex(s => GetAudioSourceId(s) == id)
                    : index < sources.Count && GetAudioSourceId(sources[index]) is null ? index : -1;
                if (target >= 0
                    && string.Equals(GetAudioSourceType(sources[target]), type, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var kv in updates)
                        sources[target][kv.Key] = kv.Value?.DeepClone();

                    fileNode.Remove("audioSource");
                    fileNode["audioSources"] = new JsonArray(sources.Select(s => (JsonNode)s).ToArray());
                    WriteJsonFile(targetPath, fileNode);
                }
            }
        }

        foreach (var kv in updates)
            memorySource[kv.Key] = kv.Value?.DeepClone();
    }

    /// <summary>프로필을 로드하고 그 설정으로 오디오 소스와 TranscriberEngine을 새로 만든다.
    /// Program.cs의 최초 기동과 설정 창(ProfileSettingsPage)의 프로필 전환이 이 로직을 공유한다.
    /// 오디오 소스가 설정되지 않았거나 만들 수 없어도 예외를 던지지 않는다 — 앱이 켜지지 않는 대신,
    /// 오디오 소스 없이 엔진을 만들고 SetupWarning에 이유를 담아 UI가 사용자에게 설정을 안내하게 한다.</summary>
    public static TranscriberEngine BuildEngine(string baseConfigPath, string profilesDir, string? profileName)
    {
        Config cfg = LoadEffective(baseConfigPath, profilesDir, profileName);

        if (!cfg.HasAudioSource)
            return new TranscriberEngine(cfg, [],
                cfg.LoadWarning
                ?? (cfg.EffectiveProfileName is not null
                    ? $"'{cfg.EffectiveProfileName}' 프로필에 오디오 소스가 없습니다. '⚙ 편집'에서 오디오 소스를 고르세요."
                : ListProfileNames(profilesDir).Count > 0
                    ? "사용할 프로필을 고르세요. 프로필마다 무엇을 들을지(오디오 소스)가 정해져 있습니다."
                    : "프로필이 없습니다. '⚙ 편집'에서 프로필을 만들고 오디오 소스를 고르세요."));

        var created = new List<IAudioSource>();
        var list = cfg.AudioSourceList;
        for (int i = 0; i < list.Count; i++)
        {
            try
            {
                created.Add(cfg.CreateAudioSource(new AudioSourceSpec(GetAudioSourceType(list[i])!, list[i])));
            }
            catch (Exception ex)
            {
                foreach (var s in created) s.Dispose();
                // 리플렉션 생성자 호출 실패는 TargetInvocationException으로 감싸져 오므로 안쪽 메시지를 보여준다.
                string reason = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
                string which = list.Count > 1 ? $" {i + 1}({GetAudioSourceAlias(list[i])})" : "";
                return new TranscriberEngine(cfg, [], $"오디오 소스{which}를 준비하지 못했습니다: {reason}");
            }
        }
        return new TranscriberEngine(cfg, created.ToArray());
    }

    /// <summary>오디오 소스가 하나 이상 지정돼 있는지.</summary>
    [JsonIgnore]
    public bool HasAudioSource => AudioSourceList.Count > 0;

    /// <summary>오디오 소스 객체에서 별명을 저장하는 키. 생성자 매개변수가 아니므로 소스를 만들 때는 무시된다.</summary>
    public const string AliasKey = "alias";

    /// <summary>오디오 소스 객체에서 고유 id를 저장하는 키. 순서를 바꿔도 같은 소스를 찾는 데 쓴다(생성자 매개변수가 아니다).
    /// 편집 화면이 저장할 때 붙이므로 예전 파일의 소스에는 없을 수 있다.</summary>
    public const string IdKey = "id";

    public static string? GetAudioSourceId(JsonObject? source) =>
        source?[IdKey] is JsonValue v && v.TryGetValue(out string? id) && !string.IsNullOrWhiteSpace(id) ? id : null;

    public static string NewAudioSourceId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>오디오 소스의 별명. 지정하지 않았으면 종류의 시스템 이름(예: WasapiLoopback)을 쓴다.</summary>
    public static string GetAudioSourceAlias(JsonObject? source) =>
        source?[AliasKey] is JsonValue v && v.TryGetValue(out string? alias) && !string.IsNullOrWhiteSpace(alias)
            ? alias.Trim()
            : GetAudioSourceType(source) ?? "?";

    /// <summary>index번째 오디오 소스의 별명. 목록의 소스 칼럼, 상태 표시줄, 내보내기에 쓴다.</summary>
    public string GetAudioSourceAlias(int index) =>
        index >= 0 && index < AudioSourceList.Count ? GetAudioSourceAlias(AudioSourceList[index]) : $"{index + 1}";

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
    /// UI(ProfileSettingsPage)와 CreateAudioSource가 이 우선순위 로직을 공유한다.</summary>
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
                p.GetCustomAttribute<SuggestionPickerAttribute>(),
                p.GetCustomAttribute<PasswordPropertyTextAttribute>()?.Password == true);
            return new AudioSourceSettingValue(settingSpec, ResolveConstructorArg(p, spec.Options));
        }).ToList();
    }

    #endregion
}
