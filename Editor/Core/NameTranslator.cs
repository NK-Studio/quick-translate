using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QuickTranslate
{
    public enum TranslationDirection
    {
        None,
        KoreanToEnglish,
        EnglishToKorean
    }

    /// <summary>
    /// 이름 텍스트 → 번역 후보 목록 생성 (한→영 / 영→한 자동 판별). UI 와 독립적이라 다른 도구에서도 재사용할 수 있다.
    /// 번역 엔진(DeepL / Google)은 결과를 하나만 주므로 후보는 아래 순서로 만들고 중복을 제거한다.
    ///   1. 용어집 전체 일치 → 용어집 단어가 들어간 단어별 조합
    ///   2. 문맥(context) 번역   ← 오브젝트 이름임을 알려준 번역 (지원 엔진만)
    ///   3. 일반 번역
    ///   4. (한→영) 2·3 을 이름용으로 정리한 형태 (관사/소유격/문장부호 제거, 단어 첫 글자 대문자)
    ///   5. 단어별 번역 조합 (단어 단위 용어집 적용)
    /// </summary>
    public static class NameTranslator
    {
        // Unity 가 복제 시 붙이는 " (1)" 같은 접미사는 번역하지 않고 그대로 붙인다.
        static readonly Regex DuplicateSuffix = new Regex(@"\s*\(\d+\)$", RegexOptions.Compiled);
        static readonly Regex Separators = new Regex(@"[\s_]+", RegexOptions.Compiled);
        // "PlayerHealthBar" → "Player Health Bar", "UIButton" → "UI Button", "hp2Bar" → "hp2 Bar"
        static readonly Regex CamelBoundary = new Regex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);
        static readonly Regex LeadingArticle = new Regex(@"^(the|a|an)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex Possessive = new Regex(@"['’]s\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex NonNameChars = new Regex(@"[^\p{L}\p{N}\s\-]", RegexOptions.Compiled);

        static readonly Dictionary<string, IReadOnlyList<string>> Cache = new Dictionary<string, IReadOnlyList<string>>();

        // 같은 이름에 대한 요청이 진행 중이면 새로 보내지 않고 그 결과를 함께 기다린다(미리 번역 → 팝업이 이어받음).
        static readonly Dictionary<string, Task<IReadOnlyList<string>>> InFlight =
            new Dictionary<string, Task<IReadOnlyList<string>>>();

        public static void ClearCache()
        {
            Cache.Clear();
            InFlight.Clear();
        }

        /// <summary>팝업을 띄우기 전에 번역을 미리 시작한다. 결과는 캐시에 남고, 팝업의 요청이 이어받는다.</summary>
        public static void Prefetch(string source, bool nameMode = true)
        {
            // 실패는 팝업이 같은 요청을 다시 보낼 때 표시되므로 여기서는 예외만 관찰해 둔다.
            GetCandidatesAsync(source, default, nameMode).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>입력칸 텍스트(UI 문구 등)용 문맥. 이름용 기본 문맥 대신 쓴다.</summary>
        const string TextContext =
            "This text is used in a Unity game project (UI text, labels, descriptions or editor fields). " +
            "Translate it naturally, keeping the meaning, tone, line breaks and placeholders.";

        /// <summary>
        /// 번역 후보를 가져온다. 캐시 → 진행 중인 같은 요청 → 새 요청 순으로 쓴다.
        /// ct 는 기다림만 취소한다(요청 자체는 끝까지 진행되어 캐시에 남는다).
        /// nameMode 가 false 면(입력칸 텍스트) 이름용 가공 없이 문장 그대로 번역한다.
        /// </summary>
        public static async Task<IReadOnlyList<string>> GetCandidatesAsync(string source, CancellationToken ct = default,
            bool nameMode = true)
        {
            source = nameMode ? source?.Trim() ?? string.Empty : source ?? string.Empty;
            string key = (nameMode ? "N|" : "T|") + source;
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            if (!InFlight.TryGetValue(key, out var task))
            {
                task = nameMode ? ComputeCandidatesAsync(source, key) : ComputeTextCandidatesAsync(source, key);
                if (!task.IsCompleted)
                {
                    InFlight[key] = task;
                    _ = task.ContinueWith(_ => InFlight.Remove(key), TaskScheduler.FromCurrentSynchronizationContext());
                }
            }

            if (!ct.CanBeCanceled)
                return await task;

            var cancelled = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                    throw new OperationCanceledException(ct);
            }

            return await task;
        }

        /// <summary>한글이 있으면 한→영, 한글 없이 영문자가 있으면 영→한.</summary>
        public static TranslationDirection DetectDirection(string text)
        {
            if (ContainsHangul(text))
                return TranslationDirection.KoreanToEnglish;
            if (ContainsLatin(text))
                return TranslationDirection.EnglishToKorean;
            return TranslationDirection.None;
        }

        public static bool ContainsHangul(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                if ((c >= '가' && c <= '힣') || // 완성형
                    (c >= 'ᄀ' && c <= 'ᇿ') || // 자모
                    (c >= '㄰' && c <= '㆏'))   // 호환 자모
                    return true;
            }

            return false;
        }

        static bool ContainsLatin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    return true;

            return false;
        }

        /// <summary>
        /// 입력칸 텍스트: 용어집 전체 일치 → 문맥 번역 → 일반 번역만 쓴다(이름 정리형·단어별 조합·대소문자 짝 없음).
        /// 앞뒤 공백·줄바꿈은 원문 그대로 되돌려 붙인다.
        /// </summary>
        static async Task<IReadOnlyList<string>> ComputeTextCandidatesAsync(string source, string key)
        {
            var ct = CancellationToken.None;
            string core = source.Trim();
            string leading = source.Substring(0, source.Length - source.TrimStart().Length);
            string trailing = source.Substring(source.TrimEnd().Length);

            var direction = DetectDirection(core);
            if (direction == TranslationDirection.None)
                return new[] { source };

            var engine = TranslationEngines.Current;
            if (!engine.HasApiKey)
                throw new TranslationException($"{engine.DisplayName} API 키가 설정되지 않았습니다.");

            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            var glossary = TranslatorGlossary.instance;
            string context = TranslatorSettings.UseContext ? TextContext : null;

            var candidates = new List<string>();
            if (toEnglish ? glossary.TryGet(core, out string hit) : glossary.TryGetKorean(core, out hit))
                candidates.Add(hit);

            if (engine is ICandidateEngine candidateEngine)
            {
                var suggestions = await candidateEngine.SuggestAsync(core, direction, TranslatorSettings.MaxCandidates,
                    glossary.Pairs, context, ct, nameMode: false);
                candidates.AddRange(suggestions);
            }
            else
            {
                var plainTask = engine.TranslateAsync(new[] { core }, direction, null, ct);
                var contextTask = context != null && engine.SupportsContext
                    ? engine.TranslateAsync(new[] { core }, direction, context, ct)
                    : Task.FromResult<string[]>(null);
                await Task.WhenAll(plainTask, contextTask);
                candidates.Add(contextTask.Result?[0]);
                candidates.Add(plainTask.Result[0]);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (string candidate in candidates)
            {
                string text = candidate?.Trim();
                if (string.IsNullOrEmpty(text) || !seen.Add(text))
                    continue;
                result.Add(leading + text + trailing);
                if (result.Count >= TranslatorSettings.MaxCandidates)
                    break;
            }

            if (result.Count == 0)
                throw new TranslationException("번역 결과가 비어 있습니다.");
            Cache[key] = result;
            return result;
        }

        static async Task<IReadOnlyList<string>> ComputeCandidatesAsync(string source, string key)
        {
            var ct = CancellationToken.None;
            var direction = DetectDirection(source);
            if (direction == TranslationDirection.None)
                return new[] { source };

            var engine = TranslationEngines.Current;
            if (!engine.HasApiKey)
                throw new TranslationException($"{engine.DisplayName} API 키가 설정되지 않았습니다.");

            if (Cache.TryGetValue(key, out var cached))
                return cached;

            string suffix = string.Empty;
            string core = source;
            var match = DuplicateSuffix.Match(source);
            if (match.Success && match.Index > 0)
            {
                suffix = match.Value;
                core = source.Substring(0, match.Index);
            }

            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            if (!toEnglish)
                core = Humanize(core);

            var glossary = TranslatorGlossary.instance;

            bool TryGlossary(string text, out string hit) =>
                toEnglish ? glossary.TryGet(text, out hit) : glossary.TryGetKorean(text, out hit);

            // AI 엔진: 순위가 매겨진 후보를 직접 받는다(용어집은 프롬프트로 전달).
            if (engine is ICandidateEngine candidateEngine)
            {
                var aiCandidates = new List<string>();
                if (TryGlossary(core, out string hit))
                    aiCandidates.Add(hit);
                var suggestions = await candidateEngine.SuggestAsync(core, direction, TranslatorSettings.MaxCandidates,
                    glossary.Pairs, TranslatorSettings.UseContext ? TranslatorSettings.Context : null, ct);
                foreach (string suggestion in suggestions)
                    aiCandidates.Add(CleanSentence(suggestion));
                return Store(aiCandidates);
            }

            IReadOnlyList<string> Store(List<string> list)
            {
                var finalized = Finalize(list, suffix, TranslatorSettings.MaxCandidates, withCaseVariants: toEnglish);
                if (finalized.Count == 0)
                    throw new TranslationException("번역 결과가 비어 있습니다.");
                Cache[key] = finalized;
                return finalized;
            }

            bool NeedsTranslation(string token) =>
                toEnglish ? ContainsHangul(token) : ContainsLatin(token);

            // 단어별 번역이 필요한 토큰 (용어집에 없는 토큰만)
            string[] tokens = Separators.Split(core);
            var tokenQueries = new List<string>();
            if (tokens.Length > 1)
            {
                foreach (string token in tokens)
                    if (NeedsTranslation(token) && !TryGlossary(token, out _) && !tokenQueries.Contains(token))
                        tokenQueries.Add(token);
            }

            var plainTexts = new List<string>(tokenQueries.Count + 1) { core };
            plainTexts.AddRange(tokenQueries);

            var plainTask = engine.TranslateAsync(plainTexts, direction, null, ct);
            var contextTask = TranslatorSettings.UseContext && engine.SupportsContext
                ? engine.TranslateAsync(new[] { core }, direction, TranslatorSettings.Context, ct)
                : Task.FromResult<string[]>(null);
            await Task.WhenAll(plainTask, contextTask);

            string[] plain = plainTask.Result;
            var tokenTranslations = new Dictionary<string, string>();
            for (int i = 0; i < tokenQueries.Count; i++)
                tokenTranslations[tokenQueries[i]] = plain[i + 1];

            // 단어별 조합. 용어집 단어가 하나라도 들어가면 팀 용어를 반영한 후보이므로 맨 위(BEST)로 올린다.
            string wordByWord = null;
            bool usedGlossaryWord = false;
            if (tokens.Length > 1)
            {
                var parts = new List<string>(tokens.Length);
                foreach (string token in tokens)
                {
                    if (token.Length == 0)
                        continue;

                    if (TryGlossary(token, out string hit))
                    {
                        parts.Add(hit);
                        usedGlossaryWord = true;
                    }
                    else if (tokenTranslations.TryGetValue(token, out string translated))
                    {
                        parts.Add(toEnglish
                            ? LeadingArticle.Replace(CleanSentence(translated) ?? string.Empty, string.Empty)
                            : CleanSentence(translated));
                    }
                    else
                    {
                        parts.Add(token);
                    }
                }

                string joined = string.Join(" ", parts);
                wordByWord = toEnglish ? ToNameForm(joined) : joined;
            }

            // 순서: 용어집 전체 일치 → (용어집 단어가 들어간) 단어별 조합 → 문맥 번역 → 일반 번역 → 이름 정리형 → 단어별 조합
            var candidates = new List<string>();
            if (TryGlossary(core, out string glossaryHit))
                candidates.Add(glossaryHit);
            if (usedGlossaryWord)
                candidates.Add(wordByWord);

            string contextual = CleanSentence(contextTask.Result?[0]);
            string plainFull = CleanSentence(plain[0]);
            candidates.Add(contextual);
            candidates.Add(plainFull);

            if (toEnglish)
            {
                candidates.Add(ToNameForm(contextual));
                candidates.Add(ToNameForm(plainFull));
            }

            if (!usedGlossaryWord)
                candidates.Add(wordByWord);

            return Store(candidates);
        }

        /// <summary>코드식 이름을 문장처럼 풀어 쓴다. "PlayerHealth_Bar" → "Player Health Bar"</summary>
        static string Humanize(string text)
        {
            text = CamelBoundary.Replace(text, " ");
            return Separators.Replace(text, " ").Trim();
        }

        /// <summary>
        /// 중복(대소문자 구분)을 제거하고 최대 개수로 자른다.
        /// withCaseVariants 면 각 후보를 첫 글자 대문자/소문자 두 버전으로 넣는다. (banana → Banana, banana)
        /// 순서는 설정(대문자 우선 / 소문자 우선)을 따른다.
        /// </summary>
        static List<string> Finalize(List<string> candidates, string suffix, int max, bool withCaseVariants)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            void Add(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate) || result.Count >= max)
                    return;

                string name = candidate + suffix;
                if (seen.Add(name))
                    result.Add(name);
            }

            bool upperFirst = TranslatorSettings.PreferUpperCaseFirst;
            foreach (string candidate in candidates)
            {
                if (!withCaseVariants)
                {
                    Add(candidate);
                    continue;
                }

                string upper = ToUpperFirst(candidate);
                string lower = ToLowerFirst(candidate); // 약어로 시작하면 null
                Add(upperFirst ? upper : lower ?? upper);
                Add(upperFirst ? lower : upper);
            }

            return result;
        }

        static string ToUpperFirst(string text)
        {
            if (string.IsNullOrEmpty(text) || !char.IsLower(text[0]))
                return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>첫 글자를 소문자로. "UI Button", "HP Bar" 처럼 약어로 시작하면 바꾸지 않는다(null).</summary>
        static string ToLowerFirst(string text)
        {
            if (string.IsNullOrEmpty(text) || !char.IsUpper(text[0]))
                return text;

            int wordLength = 0;
            while (wordLength < text.Length && char.IsLetter(text[wordLength]))
                wordLength++;
            string firstWord = text.Substring(0, wordLength);
            if (wordLength > 1 && firstWord == firstWord.ToUpperInvariant())
                return null;

            return char.ToLowerInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>앞뒤 공백·따옴표·마침표 제거, 연속 공백 정리.</summary>
        static string CleanSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = Regex.Replace(text.Trim(), @"\s+", " ");
            return text.Trim('"', '\'', '“', '”', '.', '。', ' ');
        }

        /// <summary>"The player's health bar" → "Player Health Bar"</summary>
        static string ToNameForm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = LeadingArticle.Replace(text, string.Empty);
            text = Possessive.Replace(text, string.Empty);
            text = NonNameChars.Replace(text, " ");

            var sb = new StringBuilder();
            foreach (string word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(word[0]));
                sb.Append(word, 1, word.Length - 1); // "UI", "HP" 같은 약어는 유지
            }

            return sb.ToString();
        }
    }
}
