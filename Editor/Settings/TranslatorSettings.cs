using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>
    /// 개인 설정. API 키는 프로젝트 루트의 .env(<see cref="EnvFile"/>)에서, 나머지는 EditorPrefs 에서 읽는다.
    /// 둘 다 프로젝트 에셋이 아니므로 VCS 에 포함되지 않는다.
    /// </summary>
    internal static class TranslatorSettings
    {
        public const string PreferencesPath = "Preferences/Quick Translate";

        const string Prefix = "QuickTranslate.";

        // 이전 이름(Quick Translate) 시절의 EditorPrefs 접두사. 설정 이전에만 쓴다.
        internal const string LegacyPrefix = "HierarchyTranslator" + ".";

        // 이전 버전에서 EditorPrefs 에 저장하던 API 키. .env 로 옮기는 버튼에서만 사용한다.
        const string LegacyDeepLKeyPref = LegacyPrefix + "DeepLApiKey";
        const string LegacyGoogleKeyPref = LegacyPrefix + "GoogleApiKey";

        public const string DefaultContext =
            "This text is the name of a GameObject in the Unity game engine hierarchy. " +
            "Translate it as a short noun phrase suitable for an object name.";

        static readonly List<string> EnglishVariants = new List<string> { "EN-US", "EN-GB" };
        static readonly List<string> CasePriorityChoices = new List<string> { "대문자 우선 (Banana)", "소문자 우선 (banana)" };

        public static TranslationEngineKind Engine
        {
            get
            {
                // 제거된 엔진(예: 1.2.0 의 Claude Code)이 저장돼 있으면 기본 엔진으로 되돌린다.
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

            // ── 번역 엔진 ──
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
            page.Add(deepLGroup);

            var googleGroup = new VisualElement();
            googleGroup.Add(TranslatorStyles.Note(
                "Google Cloud Translation API (Basic, v2) 키가 필요합니다. Google Cloud 콘솔에서 'Cloud Translation API' 를 " +
                "사용 설정한 뒤 API 키를 만드세요. 문맥(context)을 지원하지 않아 DeepL 보다 후보 수가 적을 수 있습니다."));
            page.Add(googleGroup);

            var googleFreeGroup = new VisualElement();
            googleFreeGroup.Add(new HelpBox(
                "API 키 없이 비공식 엔드포인트(translate.googleapis.com/translate_a/single)를 사용합니다.\n" +
                "• 공식 API 가 아니라 예고 없이 막히거나 형식이 바뀔 수 있습니다.\n" +
                "• 요청이 많거나 네트워크에 따라 'automated queries' 로 차단될 수 있습니다.\n" +
                "• 번역할 이름이 URL 에 담겨 Google 로 전송됩니다.\n" +
                "업무용으로는 DeepL 또는 Google(공식 API)을 권장합니다.", HelpBoxMessageType.Warning));
            page.Add(googleFreeGroup);

            var papagoGroup = new VisualElement();
            papagoGroup.Add(TranslatorStyles.Note(
                "네이버 클라우드 플랫폼 > AI·NAVER API > Papago Translation 을 이용 신청한 뒤, " +
                "Application 의 Client ID / Client Secret 을 .env 에 넣으세요. " +
                "문맥(context)을 지원하지 않아 DeepL 보다 후보 수가 적을 수 있습니다."));
            page.Add(papagoGroup);

            var claudeGroup = CreateAiGroup("Anthropic 콘솔(console.anthropic.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => ClaudeModel, v => ClaudeModel = v, ClaudeModels);
            var openAIGroup = CreateAiGroup("OpenAI 플랫폼(platform.openai.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => OpenAIModel, v => OpenAIModel = v, OpenAIModels);
            var geminiGroup = CreateAiGroup("Google AI Studio(aistudio.google.com)에서 API 키를 만들어 .env 에 넣으세요.",
                () => GeminiModel, v => GeminiModel = v, GeminiModels);
            page.Add(claudeGroup);
            page.Add(openAIGroup);
            page.Add(geminiGroup);

            // 문맥: 지원하는 엔진(DeepL, AI)을 고를 때만 보인다. 항목은 아래에서 채운다.
            var contextGroup = new VisualElement();
            page.Add(contextGroup);

            // ── API 키 (.env) ──
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
            var migrateButton = new Button { text = "기존 키를 .env 로 옮기기", tooltip = "이전 버전에서 Preferences 에 저장한 키를 .env 로 옮기고 Preferences 에서는 지웁니다." };
            keyButtons.Add(createButton);
            keyButtons.Add(fillButton);
            keyButtons.Add(openButton);
            keyButtons.Add(revealButton);
            keyButtons.Add(reloadButton);
            keyButtons.Add(migrateButton);
            keySection.Add(keyButtons);
            page.Add(keySection);

            // ── 후보 ──
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
                deepLGroup.style.display = engine == TranslationEngineKind.DeepL ? DisplayStyle.Flex : DisplayStyle.None;
                googleGroup.style.display = engine == TranslationEngineKind.Google ? DisplayStyle.Flex : DisplayStyle.None;
                googleFreeGroup.style.display = engine == TranslationEngineKind.GoogleFree ? DisplayStyle.Flex : DisplayStyle.None;
                papagoGroup.style.display = engine == TranslationEngineKind.Papago ? DisplayStyle.Flex : DisplayStyle.None;
                claudeGroup.style.display = engine == TranslationEngineKind.Claude ? DisplayStyle.Flex : DisplayStyle.None;
                openAIGroup.style.display = engine == TranslationEngineKind.OpenAI ? DisplayStyle.Flex : DisplayStyle.None;
                geminiGroup.style.display = engine == TranslationEngineKind.Gemini ? DisplayStyle.Flex : DisplayStyle.None;

                // 키가 필요한 엔진만 .env 상태를 보여준다.
                string[] envKeys = RequiredEnvKeys(engine);
                keySection.style.display = envKeys.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
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

                createButton.style.display = EnvFile.Exists ? DisplayStyle.None : DisplayStyle.Flex;
                fillButton.style.display = EnvFile.Exists && !EnvFile.HasAllKeys(envKeys) ? DisplayStyle.Flex : DisplayStyle.None;
                openButton.SetEnabled(EnvFile.Exists);
                revealButton.SetEnabled(EnvFile.Exists);
                migrateButton.style.display = HasLegacyKeys() ? DisplayStyle.Flex : DisplayStyle.None;

                contextGroup.style.display = TranslationEngines.Current.SupportsContext ? DisplayStyle.Flex : DisplayStyle.None;
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
                EnvFile.FillMissing(new[]
                {
                    new KeyValuePair<string, string>(EnvFile.DeepLApiKey, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.GoogleApiKey, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.PapagoClientId, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.PapagoClientSecret, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.AnthropicApiKey, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.OpenAIApiKey, string.Empty),
                    new KeyValuePair<string, string>(EnvFile.GeminiApiKey, string.Empty)
                });
                MigrateLegacyKeys();
                Refresh();
            };
            fillButton.clicked += () =>
            {
                EnvFile.FillMissing(RequiredEnvKeys(Engine).Select(k => new KeyValuePair<string, string>(k, string.Empty)));
                Refresh();
            };
            openButton.clicked += () => EditorUtility.OpenWithDefaultApp(EnvFile.FilePath);
            revealButton.clicked += () => EditorUtility.RevealInFinder(EnvFile.FilePath);
            reloadButton.clicked += Refresh;
            migrateButton.clicked += () =>
            {
                MigrateLegacyKeys();
                Refresh();
            };

            // 외부 편집기에서 .env 를 고치고 돌아왔을 때 반영되도록 주기적으로 갱신한다.
            page.schedule.Execute(Refresh).Every(1000);
            Refresh();
        }

        /// <summary>
        /// AI 엔진 공통: 모델 드롭다운 + 안내. 목록 끝의 "직접 입력…" 을 고르면 모델 ID 를 직접 적을 수 있다.
        /// 저장된 모델이 목록에 없으면 "직접 입력…" 상태로 보여준다.
        /// </summary>
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

            void UpdateCustomVisibility() =>
                customField.style.display = dropdown.index == choices.Count - 1 ? DisplayStyle.Flex : DisplayStyle.None;

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

        static bool HasLegacyKeys() =>
            EditorPrefs.GetString(LegacyDeepLKeyPref, string.Empty).Length > 0 ||
            EditorPrefs.GetString(LegacyGoogleKeyPref, string.Empty).Length > 0;

        /// <summary>이전 버전에서 EditorPrefs 에 저장한 키를 .env 로 옮긴다. (.env 에 이미 값이 있으면 그 값을 유지)</summary>
        static void MigrateLegacyKeys()
        {
            var legacy = new[]
            {
                new KeyValuePair<string, string>(EnvFile.DeepLApiKey, EditorPrefs.GetString(LegacyDeepLKeyPref, string.Empty)),
                new KeyValuePair<string, string>(EnvFile.GoogleApiKey, EditorPrefs.GetString(LegacyGoogleKeyPref, string.Empty))
            }.Where(pair => pair.Value.Length > 0).ToArray();

            if (legacy.Length == 0)
                return;

            EnvFile.FillMissing(legacy);
            EditorPrefs.DeleteKey(LegacyDeepLKeyPref);
            EditorPrefs.DeleteKey(LegacyGoogleKeyPref);
            Changed();
        }
    }
}
