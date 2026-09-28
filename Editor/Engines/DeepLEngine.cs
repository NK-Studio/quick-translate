using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuickTranslate
{
    /// <summary>DeepL /v2/translate. 키가 ':fx' 로 끝나면 Free API 를 사용한다.</summary>
    internal sealed class DeepLEngine : ITranslationEngine
    {
        const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";
        const string ProEndpoint = "https://api.deepl.com/v2/translate";

#pragma warning disable 0649
        [Serializable] class Response { public Translation[] translations; }
        [Serializable] class Translation { public string text; }
        [Serializable] class ErrorBody { public string message; }
#pragma warning restore 0649

        public string DisplayName => "DeepL";
        public bool SupportsContext => true;
        public bool HasApiKey => !string.IsNullOrEmpty(TranslatorSettings.DeepLApiKey);

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct)
        {
            string apiKey = TranslatorSettings.DeepLApiKey;
            string endpoint = apiKey.EndsWith(":fx", StringComparison.Ordinal) ? FreeEndpoint : ProEndpoint;
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;

            var body = new StringBuilder();
            body.Append("{\"text\":");
            HttpJson.AppendStringArray(body, texts);
            body.Append(",\"source_lang\":");
            HttpJson.AppendString(body, toEnglish ? "KO" : "EN");
            body.Append(",\"target_lang\":");
            HttpJson.AppendString(body, toEnglish ? TranslatorSettings.EnglishVariant : "KO");
            if (!string.IsNullOrWhiteSpace(context))
            {
                body.Append(",\"context\":");
                HttpJson.AppendString(body, context);
            }

            body.Append('}');

            using var request = HttpJson.CreatePost(endpoint, body.ToString());
            request.SetRequestHeader("Authorization", "DeepL-Auth-Key " + apiKey);
            await HttpJson.SendAsync(request, ct);

            if (request.result != UnityWebRequest.Result.Success)
                throw new TranslationException(Describe(request));

            var response = JsonUtility.FromJson<Response>(request.downloadHandler.text);
            if (response?.translations == null || response.translations.Length != texts.Count)
                throw new TranslationException("DeepL 응답을 해석할 수 없습니다.");

            var result = new string[texts.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = response.translations[i].text;
            return result;
        }

        static string Describe(UnityWebRequest request)
        {
            switch (request.responseCode)
            {
                case 403: return "DeepL API 키가 올바르지 않습니다. (403)";
                case 456: return "DeepL 사용량 한도를 초과했습니다. (456)";
                case 429: return "요청이 너무 많습니다. 잠시 후 다시 시도하세요. (429)";
            }

            string detail = null;
            try
            {
                detail = JsonUtility.FromJson<ErrorBody>(request.downloadHandler?.text ?? string.Empty)?.message;
            }
            catch (ArgumentException)
            {
                // 본문이 JSON 이 아니면 무시
            }

            return request.responseCode > 0
                ? $"DeepL 요청 실패 ({request.responseCode}) {detail ?? request.error}"
                : $"네트워크 오류: {request.error}";
        }
    }
}
