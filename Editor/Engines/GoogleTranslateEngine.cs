using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuickTranslate
{
    /// <summary>Google Cloud Translation API (Basic, v2). 키는 URL 이 아니라 X-Goog-Api-Key 헤더로 보낸다.</summary>
    internal sealed class GoogleTranslateEngine : ITranslationEngine
    {
        const string Endpoint = "https://translation.googleapis.com/language/translate/v2";

#pragma warning disable 0649
        [Serializable] class Response { public Data data; }
        [Serializable] class Data { public Translation[] translations; }
        [Serializable] class Translation { public string translatedText; }
        [Serializable] class ErrorResponse { public ErrorBody error; }
        [Serializable] class ErrorBody { public string message; }
#pragma warning restore 0649

        public string DisplayName => "Google Translate";
        public bool SupportsContext => false;
        public bool HasApiKey => !string.IsNullOrEmpty(TranslatorSettings.GoogleApiKey);

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct)
        {
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;

            var body = new StringBuilder();
            body.Append("{\"q\":");
            HttpJson.AppendStringArray(body, texts);
            body.Append(",\"source\":");
            HttpJson.AppendString(body, toEnglish ? "ko" : "en");
            body.Append(",\"target\":");
            HttpJson.AppendString(body, toEnglish ? "en" : "ko");
            body.Append(",\"format\":\"text\"}"); // html 이면 &#39; 같은 엔티티가 섞여 온다

            using var request = HttpJson.CreatePost(Endpoint, body.ToString());
            request.SetRequestHeader("X-Goog-Api-Key", TranslatorSettings.GoogleApiKey);
            await HttpJson.SendAsync(request, ct);

            if (request.result != UnityWebRequest.Result.Success)
                throw new TranslationException(Describe(request));

            var response = JsonUtility.FromJson<Response>(request.downloadHandler.text);
            var translations = response?.data?.translations;
            if (translations == null || translations.Length != texts.Count)
                throw new TranslationException("Google 응답을 해석할 수 없습니다.");

            var result = new string[texts.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = translations[i].translatedText;
            return result;
        }

        static string Describe(UnityWebRequest request)
        {
            string detail = null;
            try
            {
                detail = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler?.text ?? string.Empty)?.error?.message;
            }
            catch (ArgumentException)
            {
                // 본문이 JSON 이 아니면 무시
            }

            switch (request.responseCode)
            {
                case 400 when detail != null && detail.Contains("API key"):
                    return "Google API 키가 올바르지 않습니다. (400)";
                case 403:
                    return $"Google 요청이 거부되었습니다. 키 제한 또는 Cloud Translation API 활성화 여부를 확인하세요. (403)\n{detail}";
                case 429:
                    return "Google 사용량 한도를 초과했거나 요청이 너무 많습니다. (429)";
            }

            return request.responseCode > 0
                ? $"Google 요청 실패 ({request.responseCode}) {detail ?? request.error}"
                : $"네트워크 오류: {request.error}";
        }
    }
}
