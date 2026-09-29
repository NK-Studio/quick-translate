using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuickTranslate
{
    /// <summary>
    /// 번역 결과 하나가 아니라 순위가 매겨진 이름 후보 여러 개를 직접 만들 수 있는 엔진(AI).
    /// <see cref="NameTranslator"/> 는 이 인터페이스가 있으면 단어별 조합 대신 이 결과를 후보로 쓴다.
    /// </summary>
    internal interface ICandidateEngine
    {
        Task<IReadOnlyList<string>> SuggestAsync(string text, TranslationDirection direction, int count,
            IReadOnlyList<KeyValuePair<string, string>> glossary, string context, CancellationToken ct, bool nameMode = true);
    }

    /// <summary>
    /// AI(LLM) 번역 엔진 공통부: 프롬프트 작성과 응답(JSON 배열) 해석. 공급자별 차이는 <see cref="CompleteAsync"/> 만 구현한다.
    /// </summary>
    internal abstract class LlmEngine : ITranslationEngine, ICandidateEngine
    {
#pragma warning disable 0649
        [Serializable] class ErrorResponse { public ErrorBody error; }
        [Serializable] class ErrorBody { public string message; }
#pragma warning restore 0649

        public abstract string DisplayName { get; }
        /// <summary>문맥은 지시문에 덧붙여 전달한다.</summary>
        public bool SupportsContext => true;
        public abstract bool HasApiKey { get; }

        /// <summary>시스템 지시문과 사용자 메시지를 보내고 모델의 텍스트 응답을 받는다.</summary>
        protected abstract Task<string> CompleteAsync(string system, string user, CancellationToken ct);

        public async Task<IReadOnlyList<string>> SuggestAsync(string text, TranslationDirection direction, int count,
            IReadOnlyList<KeyValuePair<string, string>> glossary, string context, CancellationToken ct, bool nameMode = true)
        {
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            string from = toEnglish ? "Korean" : "English";
            string to = toEnglish ? "English" : "Korean";

            var system = new StringBuilder();
            if (nameMode)
            {
                system.Append("You translate names of GameObjects and assets in a Unity game project from ")
                    .Append(from).Append(" to ").Append(to).Append(".\n")
                    .Append("Return ONLY a JSON array of up to ").Append(count)
                    .Append(" distinct candidate names, best first. No explanations, no code fences.\n")
                    .Append("Each candidate must be a short noun phrase usable as an object name: no trailing punctuation, no quotes");
                system.Append(toEnglish ? ", no articles (a/an/the).\n" : ".\n");
            }
            else
            {
                // 입력칸 텍스트: 문장 그대로의 자연스러운 번역.
                system.Append("You translate text used in a Unity game project (UI text, labels, descriptions, editor fields) from ")
                    .Append(from).Append(" to ").Append(to).Append(".\n")
                    .Append("Return ONLY a JSON array of up to ").Append(count)
                    .Append(" distinct candidate translations, best first. No explanations, no code fences.\n")
                    .Append("Keep line breaks, placeholders such as {0} or %s, and rich text tags such as <b> unchanged.\n");
            }

            system.Append("Keep numbers, symbols and words that are already in ").Append(to).Append(" unchanged.\n");

            if (!string.IsNullOrWhiteSpace(context))
                system.Append("Additional context from the user: ").Append(context.Trim()).Append('\n');

            var terms = glossary
                .Where(pair => pair.Key.Length > 0 && pair.Value.Length > 0)
                .Select(pair => toEnglish ? $"{pair.Key} → {pair.Value}" : $"{pair.Value} → {pair.Key}")
                .ToList();
            if (terms.Count > 0)
            {
                system.Append("Always use these project glossary terms when they apply:\n");
                foreach (string term in terms)
                    system.Append("- ").Append(term).Append('\n');
            }

            string response = await CompleteAsync(system.ToString(), text, ct);
            var candidates = ParseStringArray(response);
            if (candidates.Count == 0)
                throw new TranslationException($"{DisplayName} 응답에서 후보를 찾지 못했습니다.");
            return candidates;
        }

        /// <summary>일반 번역 경로(단어별 번역 등)용. 입력과 같은 순서·개수의 JSON 배열로 받는다.</summary>
        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct)
        {
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            var input = new StringBuilder();
            HttpJson.AppendStringArray(input, texts);

            string system =
                $"Translate each item of the given JSON array from {(toEnglish ? "Korean" : "English")} to " +
                $"{(toEnglish ? "English" : "Korean")}. The items are names of GameObjects and assets in a Unity project. " +
                "Return ONLY a JSON array of strings with the same length and order. No explanations, no code fences.";

            var result = ParseStringArray(await CompleteAsync(system, input.ToString(), ct));
            if (result.Count != texts.Count)
                throw new TranslationException($"{DisplayName} 응답의 개수가 요청과 다릅니다.");
            return result.ToArray();
        }

        /// <summary>응답에서 JSON 문자열 배열을 꺼낸다. 코드 블록 등으로 감싸져 있어도 첫 '[' ~ 마지막 ']' 를 읽는다.</summary>
        static List<string> ParseStringArray(string response)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(response))
                return result;

            int start = response.IndexOf('[');
            int end = response.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                try
                {
                    if (MiniJson.Parse(response.Substring(start, end - start + 1)) is List<object> items)
                    {
                        foreach (var item in items)
                            if (item is string s && !string.IsNullOrWhiteSpace(s))
                                result.Add(s.Trim());
                        return result;
                    }
                }
                catch (FormatException)
                {
                    // 아래 줄 단위 해석으로 넘어간다.
                }
            }

            // JSON 이 아니면 "1. 이름" / "- 이름" 같은 줄 목록으로 해석한다.
            foreach (string raw in response.Split('\n'))
            {
                string line = raw.Trim().TrimStart('-', '*', '•').Trim();
                int dot = line.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot <= 3 && line.Substring(0, dot).All(char.IsDigit))
                    line = line.Substring(dot + 2);
                line = line.Trim('"', '\'', '`', ' ');
                if (line.Length > 0 && !line.StartsWith("```", StringComparison.Ordinal))
                    result.Add(line);
            }

            return result;
        }

        protected async Task<string> PostJsonAsync(string url, string body, IEnumerable<KeyValuePair<string, string>> headers,
            CancellationToken ct)
        {
            using var request = HttpJson.CreatePost(url, body);
            request.timeout = 30;
            foreach (var header in headers)
                request.SetRequestHeader(header.Key, header.Value);
            await HttpJson.SendAsync(request, ct);

            if (request.result != UnityWebRequest.Result.Success)
                throw new TranslationException(Describe(request));
            return request.downloadHandler.text;
        }

        string Describe(UnityWebRequest request)
        {
            string detail = null;
            try
            {
                // Anthropic / OpenAI / Gemini 모두 {"error":{"message":...}} 형태를 쓴다.
                detail = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler?.text ?? string.Empty)?.error?.message;
            }
            catch (ArgumentException)
            {
                // 본문이 JSON 이 아니면 무시
            }

            switch (request.responseCode)
            {
                case 401:
                case 403:
                    return $"{DisplayName} API 키가 올바르지 않거나 권한이 없습니다. ({request.responseCode})\n{detail}";
                case 404:
                    return $"{DisplayName} 모델을 찾을 수 없습니다. 설정의 모델 이름을 확인하세요. (404)\n{detail}";
                case 429:
                    return $"{DisplayName} 사용량 한도를 초과했거나 요청이 너무 많습니다. (429)\n{detail}";
            }

            return request.responseCode > 0
                ? $"{DisplayName} 요청 실패 ({request.responseCode}) {detail ?? request.error}"
                : $"네트워크 오류: {request.error}";
        }

        protected static string JsonString(string value)
        {
            var sb = new StringBuilder();
            HttpJson.AppendString(sb, value);
            return sb.ToString();
        }
    }
}
