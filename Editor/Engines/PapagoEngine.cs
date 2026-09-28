using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuickTranslate
{
    /// <summary>
    /// 네이버 클라우드 플랫폼 Papago Translation (NMT). Client ID / Client Secret 을 헤더로 보낸다.
    /// 요청당 문장 하나만 받으므로 여러 문장은 줄바꿈으로 묶어 한 번에 보내고, 줄 수가 안 맞으면 하나씩 다시 보낸다.
    /// 문맥(context) 파라미터는 없다.
    /// </summary>
    internal sealed class PapagoEngine : ITranslationEngine
    {
        const string Endpoint = "https://papago.apigw.ntruss.com/nmt/v1/translation";

#pragma warning disable 0649
        [Serializable] class Response { public Message message; }
        [Serializable] class Message { public Result result; }
        [Serializable] class Result { public string translatedText; }
        [Serializable] class GatewayError { public ErrorBody error; public string errorMessage; public string errorCode; }
        [Serializable] class ErrorBody { public string errorCode; public string message; public string details; }
#pragma warning restore 0649

        public string DisplayName => "Papago";
        public bool SupportsContext => false;

        public bool HasApiKey =>
            !string.IsNullOrEmpty(TranslatorSettings.PapagoClientId) &&
            !string.IsNullOrEmpty(TranslatorSettings.PapagoClientSecret);

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct)
        {
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            string source = toEnglish ? "ko" : "en";
            string target = toEnglish ? "en" : "ko";

            if (texts.Count == 1)
                return new[] { await TranslateOneAsync(texts[0], source, target, ct) };

            string joined = await TranslateOneAsync(string.Join("\n", texts), source, target, ct);
            string[] lines = joined.Split('\n');
            if (lines.Length == texts.Count)
                return lines;

            var result = new string[texts.Count];
            for (int i = 0; i < texts.Count; i++)
                result[i] = await TranslateOneAsync(texts[i], source, target, ct);
            return result;
        }

        static async Task<string> TranslateOneAsync(string text, string source, string target, CancellationToken ct)
        {
            var body = new StringBuilder();
            body.Append("{\"source\":");
            HttpJson.AppendString(body, source);
            body.Append(",\"target\":");
            HttpJson.AppendString(body, target);
            body.Append(",\"text\":");
            HttpJson.AppendString(body, text);
            body.Append('}');

            using var request = HttpJson.CreatePost(Endpoint, body.ToString());
            request.SetRequestHeader("X-NCP-APIGW-API-KEY-ID", TranslatorSettings.PapagoClientId);
            request.SetRequestHeader("X-NCP-APIGW-API-KEY", TranslatorSettings.PapagoClientSecret);
            await HttpJson.SendAsync(request, ct);

            if (request.result != UnityWebRequest.Result.Success)
                throw new TranslationException(Describe(request));

            var response = JsonUtility.FromJson<Response>(request.downloadHandler.text);
            string translated = response?.message?.result?.translatedText;
            if (translated == null)
                throw new TranslationException("Papago 응답을 해석할 수 없습니다.");
            return translated.TrimEnd('\n');
        }

        static string Describe(UnityWebRequest request)
        {
            string detail = null;
            try
            {
                var error = JsonUtility.FromJson<GatewayError>(request.downloadHandler?.text ?? string.Empty);
                detail = error?.error?.details ?? error?.error?.message ?? error?.errorMessage;
            }
            catch (ArgumentException)
            {
                // 본문이 JSON 이 아니면 무시
            }

            switch (request.responseCode)
            {
                case 401:
                    return "Papago 인증에 실패했습니다. Client ID / Client Secret 을 확인하세요. (401)";
                case 403:
                    return $"Papago 요청이 거부되었습니다. 콘솔에서 Papago Translation 이용 신청 여부를 확인하세요. (403)\n{detail}";
                case 429:
                    return "Papago 사용량 한도를 초과했거나 요청이 너무 많습니다. (429)";
            }

            return request.responseCode > 0
                ? $"Papago 요청 실패 ({request.responseCode}) {detail ?? request.error}"
                : $"네트워크 오류: {request.error}";
        }
    }
}
