using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>개인 설정. API 키는 프로젝트 루트의 .env(<see cref="EnvFile"/>), 나머지는 EditorPrefs 에 둔다.</summary>
    internal static class TranslatorSettings
    {
        public const string PreferencesPath = "Preferences/Quick Translate";

        const string Prefix = "QuickTranslate.";

        public const string DefaultContext =
            "This text is the name of a GameObject in the Unity game engine hierarchy. " +
            "Translate it as a short noun phrase suitable for an object name.";

        static readonly List<string> EnglishVariants = new List<string> { "EN-US", "EN-GB" };
        static readonly List<string> CasePriorityChoices = new List<string> { "대문자 우선 (Banana)", "소문자 우선 (banana)" };

        public static TranslationEngineKind Engine
        {
            get
            {
                // 1.2.0 의 Claude Code 처럼 없어진 엔진이 저장돼 있으면 기본값으로
                var value = (TranslationEngineKind)EditorPrefs.GetInt(Prefix + "Engine", (int)TranslationEngineKind.GoogleFree);
                return System.Enum.IsDefined(typeof(TranslationEngineKind), value) ? value : TranslationEngineKind.GoogleFree;
            }
            set => EditorPrefs.SetInt(Prefix + "Engine", (int)value);
        }

        public static string DeepLApiKey => EnvFile.Get(EnvFile.DeepLApiKey);
        public static string GoogleApiKey => EnvFile.Get(EnvFile.GoogleApiKey);
        public static string PapagoClientId => EnvFile.Get(EnvFile.PapagoClientId);
        public static string PapagoClientSecret => EnvFile.Get(EnvFile.PapagoClientSecret);
        public static string AnthropicApiKey => EnvFile.Get(EnvFile.AnthropicApiKey);
        public static string OpenAIApiKey => EnvFile.Get(EnvFile.OpenAIApiKey);
        public static string GeminiApiKey => EnvFile.Get(EnvFile.GeminiApiKey);

        /// <summary>설정 드롭다운에 보여줄 모델 (ID, 설명). 첫 번째가 기본값.</summary>
        static readonly (string id, string label)[] ClaudeModels =
        {
            ("claude-haiku-4-5-20251001", "Haiku 4.5 — 빠름·저렴 (기본)"),
            ("claude-sonnet-5", "Sonnet 5 — 균형"),
            ("claude-opus-5-5", "Opus 5.5 — 고품질"),
            ("claude-fable-5-1", "Fable 5.1")
        };

        static readonly (string id, string label)[] OpenAIModels =
        {
            ("gpt-6-luna", "GPT-6 Luna — 빠름·저렴 (기본)"),
            ("gpt-6-sol", "GPT-6 Sol — 균형"),
            ("gpt-6-astra", "GPT-6 Astra — 고품질")
        };

        static readonly (string id, string label)[] GeminiModels =
        {
            ("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite — 빠름·저렴 (기본)"),
            ("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite"),
            ("gemini-3.5-flash", "Gemini 3.5 Flash"),
            ("gemini-3.6-flash", "Gemini 3.6 Flash"),
            ("gemini-3.7-flash", "Gemini 3.7 Flash"),
            ("gemini-3.8-flash", "Gemini 3.8 Flash — 고품질")
        };

        public static string DefaultClaudeModel => ClaudeModels[0].id;
        public static string DefaultOpenAIModel => OpenAIModels[0].id;
        public static string DefaultGeminiModel => GeminiModels[0].id;

        public static string ClaudeModel
        {
            get => ModelOrDefault("ClaudeModel", DefaultClaudeModel);
            set => EditorPrefs.SetString(Prefix + "ClaudeModel", value?.Trim() ?? string.Empty);
        }

        public static string OpenAIModel
        {
            get => ModelOrDefault("OpenAIModel", DefaultOpenAIModel);
            set => EditorPrefs.SetString(Prefix + "OpenAIModel", value?.Trim() ?? string.Empty);
        }

        public static string GeminiModel
        {
            get => ModelOrDefault("GeminiModel", DefaultGeminiModel);
            set => EditorPrefs.SetString(Prefix + "GeminiModel", value?.Trim() ?? string.Empty);
        }

        static string ModelOrDefault(string key, string fallback)
        {
            string value = EditorPrefs.GetString(Prefix + key, string.Empty);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        /// <summary>엔진별로 .env 에 있어야 하는 키 이름. 키가 필요 없는 엔진은 빈 배열.</summary>
        static string[] RequiredEnvKeys(TranslationEngineKind engine)
        {
            switch (engine)
            {
                case TranslationEngineKind.DeepL: return new[] { EnvFile.DeepLApiKey };
                case TranslationEngineKind.Google: return new[] { EnvFile.GoogleApiKey };
                case TranslationEngineKind.Papago: return new[] { EnvFile.PapagoClientId, EnvFile.PapagoClientSecret };
                case TranslationEngineKind.Claude: return new[] { EnvFile.AnthropicApiKey };
                case TranslationEngineKind.OpenAI: return new[] { EnvFile.OpenAIApiKey };
                case TranslationEngineKind.Gemini: return new[] { EnvFile.GeminiApiKey };
                default: return System.Array.Empty<string>();
            }
        }

        /// <summary>한→영 번역 시 영어 변형 (EN-US / EN-GB).</summary>
        public static string EnglishVariant
        {
            get => EditorPrefs.GetString(Prefix + "TargetLanguage", EnglishVariants[0]);
            set => EditorPrefs.SetString(Prefix + "TargetLanguage", value);
        }

        public static int MaxCandidates
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(Prefix + "MaxCandidates", 5), 1, 9);
            set => EditorPrefs.SetInt(Prefix + "MaxCandidates", Mathf.Clamp(value, 1, 9));
        }

        /// <summary>한→영 후보에서 첫 글자가 대문자인 버전을 먼저(BEST) 보여줄지. false 면 소문자 버전 우선.</summary>
        public static bool PreferUpperCaseFirst
        {
            get => EditorPrefs.GetBool(Prefix + "PreferUpperCaseFirst", true);
            set => EditorPrefs.SetBool(Prefix + "PreferUpperCaseFirst", value);
        }

        public static bool UseContext
        {
            get => EditorPrefs.GetBool(Prefix + "UseContext", true);
            set => EditorPrefs.SetBool(Prefix + "UseContext", value);
        }

        public static string Context
        {
            get => EditorPrefs.GetString(Prefix + "Context", DefaultContext);
            set => EditorPrefs.SetString(Prefix + "Context", value);
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "Quick Translate",
                activateHandler = (_, root) => BuildUI(root),
                keywords = new[] { "DeepL", "Google", "Translate", "번역", "Hierarchy", ".env" }
            };
        }

        static void Changed() => NameTranslator.ClearCache();

        static void BuildUI(VisualElement root)
        {
            var page = TranslatorStyles.CreateSettingsPage(root, "Quick Translate");

            page.Add(TranslatorStyles.SectionTitle("번역 엔진"));
            var engineField = TranslatorStyles.Aligned(new EnumField("엔진", Engine));
            page.Add(engineField);

            var deepLGroup = new VisualElement();
            var englishVariant = TranslatorStyles.Aligned(new DropdownField("영어 변형", EnglishVariants,
                Mathf.Max(0, EnglishVariants.IndexOf(EnglishVariant))) { tooltip = "한→영 번역 시 사용할 영어 (영→한은 항상 KO)" });
            englishVariant.RegisterValueChangedCallback(e =>
            {
                EnglishVariant = e.newValue;
                Changed();
            });
            deepLGroup.Add(englishVariant);
            deepLGroup.Add(TranslatorStyles.Note("키가 ':fx' 로 끝나면 Free API(api-free.deepl.com), 아니면 Pro API(api.deepl.com)를 사용합니다."));

            var googleGroup = new VisualElement();
            googleGroup.Add(TranslatorStyles.Note(
                "Google Cloud Translation API (Basic, v2) 키가 필요합니다. Google Cloud 콘솔에서 'Cloud Translation API' 를 " +
                "사용 설정한 뒤 API 키를 만드세요. 문맥(context)을 지원하지 않아 DeepL 보다 후보 수가 적을 수 있습니다."));

            var googleFreeGroup = new VisualElement();
            googleFreeGroup.Add(new HelpBox(
                "API 키 없이 비공식 엔드포인트(translate.googleapis.com/translate_a/single)를 사용합니다.\n" +
                "• 공식 API 가 아니라 예고 없이 막히거나 형식이 바뀔 수 있습니다.\n" +
                "• 요청이 많거나 네트워크에 따라 'automated queries' 로 차단될 수 있습니다.\n" +
                "• 번역할 이름이 URL 에 담겨 Google 로 전송됩니다.\n" +
                "업무용으로는 DeepL 또는 Google(공식 API)을 권장합니다.", HelpBoxMessageType.Warning));

            var papagoGroup = new VisualElement();
            papagoGroup.Add(TranslatorStyles.Note(
                "네이버 클라우드 플랫폼 > AI·NAVER API > Papago Translation 을 이용 신청한 뒤, " +
                "Application 의 Client ID / Client Secret 을 .env 에 넣으세요. " +
                "문맥(context)을 지원하지 않아 DeepL 보다 후보 수가 적을 수 있습니다."));

            var claudeGroup = CreateAiGroup("Anthropic 콘솔(console.anthropic.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => ClaudeModel, v => ClaudeModel = v, ClaudeModels);
            var openAIGroup = CreateAiGroup("OpenAI 플랫폼(platform.openai.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => OpenAIModel, v => OpenAIModel = v, OpenAIModels);
            var geminiGroup = CreateAiGroup("Google AI Studio(aistudio.google.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => GeminiModel, v => GeminiModel = v, GeminiModels);

            // 선택한 엔진의 묶음만 보인다.
            var engineGroups = new Dictionary<TranslationEngineKind, VisualElement>
            {
                [TranslationEngineKind.DeepL] = deepLGroup,
                [TranslationEngineKind.Google] = googleGroup,
                [TranslationEngineKind.GoogleFree] = googleFreeGroup,
                [TranslationEngineKind.Papago] = papagoGroup,
                [TranslationEngineKind.Claude] = claudeGroup,
                [TranslationEngineKind.OpenAI] = openAIGroup,
                [TranslationEngineKind.Gemini] = geminiGroup
            };
            foreach (var group in engineGroups.Values)
                page.Add(group);

            var contextGroup = new VisualElement(); // 문맥을 지원하는 엔진(DeepL, AI)일 때만 보인다
            page.Add(contextGroup);

            var keySection = new VisualElement();
            keySection.Add(TranslatorStyles.SectionTitle("API 키 (.env)"));
            var keyStatus = new Label();
            keyStatus.AddToClassList("qt-key-status");
            keySection.Add(keyStatus);
            keySection.Add(TranslatorStyles.Note(
                "키는 프로젝트 루트의 .env 파일에서 읽습니다. 형식: KEY=값 (한 줄에 하나)\n" +
                ".env 는 Assets 밖이라 빌드에 포함되지 않습니다. VCS 에는 올리지 마세요(.gitignore 에 .env 추가)."));
            var keyButtons = new VisualElement();
            keyButtons.AddToClassList("qt-button-row");
            var createButton = new Button { text = ".env 만들기" };
            var fillButton = new Button { text = "빠진 키 항목 추가", tooltip = "현재 엔진에 필요한 키 줄(KEY=)을 .env 끝에 추가합니다. 값은 직접 채우세요." };
            var openButton = new Button { text = ".env 열기" };
            var revealButton = new Button { text = "Finder 에서 보기" };
            var reloadButton = new Button { text = "다시 읽기" };
            keyButtons.Add(createButton);
            keyButtons.Add(fillButton);
            keyButtons.Add(openButton);
            keyButtons.Add(revealButton);
            keyButtons.Add(reloadButton);
            keySection.Add(keyButtons);
            page.Add(keySection);

            page.Add(TranslatorStyles.SectionTitle("후보"));
            var maxCandidates = TranslatorStyles.Aligned(new SliderInt("최대 후보 수", 1, 9) { value = MaxCandidates, showInputField = true });
            maxCandidates.RegisterValueChangedCallback(e =>
            {
                MaxCandidates = e.newValue;
                Changed();
            });
            page.Add(maxCandidates);

            var casePriority = TranslatorStyles.Aligned(new DropdownField("첫 글자 우선", CasePriorityChoices, PreferUpperCaseFirst ? 0 : 1)
            {
                tooltip = "한→영 후보마다 대문자/소문자 시작 버전을 짝으로 보여줄 때 어느 쪽을 먼저(BEST) 둘지"
            });
            casePriority.RegisterValueChangedCallback(_ =>
            {
                PreferUpperCaseFirst = casePriority.index == 0;
                Changed();
            });
            page.Add(casePriority);

            var useContext = TranslatorStyles.Aligned(new Toggle("문맥(context) 사용")
            {
                value = UseContext,
                tooltip = "번역할 글이 '오브젝트 이름'임을 엔진에 알려 이름다운 번역을 유도합니다. " +
                          "DeepL 은 context 파라미터(과금 없음)로, AI 엔진은 지시문에 덧붙여 전달합니다."
            });
            contextGroup.Add(useContext);

            var contextField = new TextField { multiline = true, value = Context };
            contextField.AddToClassList("qt-context-field");
            contextField.RegisterValueChangedCallback(e =>
            {
                Context = e.newValue;
                Changed();
            });
            contextGroup.Add(contextField);
            var resetContext = new Button(() => contextField.value = DefaultContext) { text = "기본 문맥으로 되돌리기" };
            resetContext.AddToClassList("qt-inline-button");
            contextGroup.Add(resetContext);

            page.Add(TranslatorStyles.Note("단축키: Edit > Shortcuts 에서 'Quick Translate' 로 변경할 수 있습니다 (기본 Cmd+Shift+X, Windows 는 Ctrl+Shift+X)."));

            void Refresh()
            {
                var engine = Engine;
                foreach (var pair in engineGroups)
                    SetVisible(pair.Value, pair.Key == engine);

                string[] envKeys = RequiredEnvKeys(engine);
                SetVisible(keySection, envKeys.Length > 0);
                if (envKeys.Length > 0)
                {
                    string[] missing = envKeys.Where(k => EnvFile.Get(k).Length == 0).ToArray();
                    bool hasKeys = missing.Length == 0;
                    keyStatus.text = !EnvFile.Exists ? $".env 파일이 없습니다: {EnvFile.FilePath}"
                        : hasKeys ? $"{string.Join(", ", envKeys)} 확인됨  ({EnvFile.FilePath})"
                        : $".env 에 {string.Join(", ", missing)} 가 없습니다.";
                    keyStatus.EnableInClassList("qt-key-status--ok", hasKeys);
                    keyStatus.EnableInClassList("qt-key-status--missing", !hasKeys);
                }

                SetVisible(createButton, !EnvFile.Exists);
                SetVisible(fillButton, EnvFile.Exists && !EnvFile.HasAllKeys(envKeys));
                openButton.SetEnabled(EnvFile.Exists);
                revealButton.SetEnabled(EnvFile.Exists);

                SetVisible(contextGroup, TranslationEngines.Current.SupportsContext);
                contextField.SetEnabled(UseContext);
                resetContext.SetEnabled(UseContext);
            }

            engineField.RegisterValueChangedCallback(e =>
            {
                Engine = (TranslationEngineKind)e.newValue;
                Changed();
                Refresh();
            });
            useContext.RegisterValueChangedCallback(e =>
            {
                UseContext = e.newValue;
                Changed();
                Refresh();
            });
            createButton.clicked += () =>
            {
                EnvFile.AddMissingKeys(EnvFile.AllKeys);
                Refresh();
            };
            fillButton.clicked += () =>
            {
                EnvFile.AddMissingKeys(RequiredEnvKeys(Engine));
                Refresh();
            };
            openButton.clicked += () => EditorUtility.OpenWithDefaultApp(EnvFile.FilePath);
            revealButton.clicked += () => EditorUtility.RevealInFinder(EnvFile.FilePath);
            reloadButton.clicked += Refresh;

            // 외부 편집기에서 .env 를 고치고 돌아와도 반영되게 주기적으로 갱신한다.
            page.schedule.Execute(Refresh).Every(1000);
            Refresh();
        }

        /// <summary>AI 엔진 공통: 모델 드롭다운. 목록에 없는 모델은 "직접 입력…" 으로 적는다.</summary>
        static VisualElement CreateAiGroup(string keyHint, System.Func<string> getModel, System.Action<string> setModel,
            (string id, string label)[] models)
        {
            const string CustomLabel = "직접 입력…";
            string defaultModel = models[0].id;
            var choices = models.Select(m => m.label).Append(CustomLabel).ToList();

            string current = getModel();
            int index = System.Array.FindIndex(models, m => m.id == current);

            var group = new VisualElement();
            var dropdown = TranslatorStyles.Aligned(new DropdownField("모델", choices, index >= 0 ? index : choices.Count - 1));
            var customField = TranslatorStyles.Aligned(new TextField("모델 ID") { value = current, isDelayed = true });
            customField.tooltip = $"비워 두면 기본값({defaultModel})을 사용합니다.";

            void UpdateCustomVisibility() => SetVisible(customField, dropdown.index == choices.Count - 1);

            dropdown.RegisterValueChangedCallback(_ =>
            {
                if (dropdown.index < models.Length)
                {
                    setModel(models[dropdown.index].id);
                    customField.SetValueWithoutNotify(models[dropdown.index].id);
                }

                UpdateCustomVisibility();
                Changed();
            });
            customField.RegisterValueChangedCallback(e =>
            {
                setModel(e.newValue);
                if (string.IsNullOrWhiteSpace(e.newValue))
                    customField.SetValueWithoutNotify(defaultModel);
                Changed();
            });

            UpdateCustomVisibility();
            group.Add(dropdown);
            group.Add(customField);
            group.Add(TranslatorStyles.Note(
                keyHint + "\n" +
                "AI 가 이름 후보를 순위대로 직접 만들고, 용어집은 지시문에 포함되어 반영됩니다. " +
                "번역할 이름과 용어집이 해당 AI 서비스로 전송되며, 사용량에 따라 요금이 부과됩니다."));
            return group;
        }

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
