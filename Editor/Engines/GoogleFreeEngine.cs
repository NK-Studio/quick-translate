using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace QuickTranslate
{
    /// <summary>
    /// 비공식 Google Translate 엔드포인트(translate_a/single, client=gtx). 키가 필요 없지만 예고 없이 막히거나
    /// 요청이 많으면 IP 단위로 차단("Sorry..." 페이지)될 수 있다.
    /// </summary>
    internal sealed class GoogleFreeEngine : ITranslationEngine
    {
        const string Endpoint = "https://translate.googleapis.com/translate_a/single";

        public string DisplayName => "Google (무료·실험적)";
        public bool SupportsContext => false;
        public bool HasApiKey => true;

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct)
        {
            bool toEnglish = direction == TranslationDirection.KoreanToEnglish;
            string source = toEnglish ? "ko" : "en";
            string target = toEnglish ? "en" : "ko";
            return await HttpJson.TranslateLinesAsync(texts, text => TranslateOneAsync(text, source, target, ct));
        }

        static async Task<string> TranslateOneAsync(string text, string source, string target, CancellationToken ct)
        {
            string url = $"{Endpoint}?client=gtx&sl={source}&tl={target}&dt=t&q={Uri.EscapeDataString(text)}";

            using var request = UnityWebRequest.Get(url);
            request.timeout = 15;
            await HttpJson.SendAsync(request, ct);

            string body = request.downloadHandler?.text ?? string.Empty;
            bool blocked = request.responseCode == 429 || body.TrimStart().StartsWith("<", StringComparison.Ordinal);
            if (blocked)
                throw new TranslationException(
                    "Google 이 자동 요청으로 판단해 차단했습니다 (비공식 엔드포인트).\n" +
                    "잠시 후 다시 시도하거나 설정에서 DeepL / Google(공식 API)로 바꾸세요.");

            if (request.result != UnityWebRequest.Result.Success)
                throw new TranslationException(request.responseCode > 0
                    ? $"Google(무료) 요청 실패 ({request.responseCode}) {request.error}"
                    : $"네트워크 오류: {request.error}");

            // 응답: [[["번역 조각","원문 조각",...], ...], null, "ko", ...] → 첫 배열의 조각들을 이어 붙인다.
            try
            {
                var root = MiniJson.Parse(body) as List<object>;
                var segments = root?.Count > 0 ? root[0] as List<object> : null;
                if (segments == null)
                    throw new FormatException();

                var sb = new StringBuilder();
                foreach (var segment in segments)
                    if (segment is List<object> parts && parts.Count > 0 && parts[0] is string piece)
                        sb.Append(piece);

                if (sb.Length == 0)
                    throw new FormatException();
                return sb.ToString().TrimEnd('\n');
            }
            catch (FormatException)
            {
                throw new TranslationException("Google(무료) 응답 형식을 해석할 수 없습니다. 엔드포인트가 바뀌었을 수 있습니다.");
            }
        }
    }
}
