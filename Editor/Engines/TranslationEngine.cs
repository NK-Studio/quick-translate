using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QuickTranslate
{
    internal sealed class TranslationException : Exception
    {
        public TranslationException(string message) : base(message) { }
    }

    public enum TranslationEngineKind
    {
        DeepL = 0,
        [InspectorName("Google (공식 API)")] Google = 1,
        [InspectorName("Google (무료·실험적)")] GoogleFree = 2,
        Papago = 3,
        [InspectorName("Claude (AI)")] Claude = 4,
        [InspectorName("ChatGPT (AI)")] OpenAI = 5,
        [InspectorName("Gemini (AI)")] Gemini = 6
    }

    /// <summary>번역 엔진(Core). 여러 문장을 한 번의 요청으로 번역한다.</summary>
    internal interface ITranslationEngine
    {
        string DisplayName { get; }

        /// <summary>문맥(오브젝트 이름이라는 설명 등)을 반영할 수 있는지.</summary>
        bool SupportsContext { get; }

        bool HasApiKey { get; }

        Task<string[]> TranslateAsync(IReadOnlyList<string> texts, TranslationDirection direction, string context,
            CancellationToken ct);
    }

    internal static class TranslationEngines
    {
        static readonly ITranslationEngine DeepL = new DeepLEngine();
        static readonly ITranslationEngine Google = new GoogleTranslateEngine();
        static readonly ITranslationEngine GoogleFree = new GoogleFreeEngine();
        static readonly ITranslationEngine Papago = new PapagoEngine();
        static readonly ITranslationEngine Claude = new ClaudeEngine();
        static readonly ITranslationEngine OpenAI = new OpenAIEngine();
        static readonly ITranslationEngine Gemini = new GeminiEngine();

        public static ITranslationEngine Current => Get(TranslatorSettings.Engine);

        public static ITranslationEngine Get(TranslationEngineKind kind)
        {
            switch (kind)
            {
                case TranslationEngineKind.Google: return Google;
                case TranslationEngineKind.GoogleFree: return GoogleFree;
                case TranslationEngineKind.Papago: return Papago;
                case TranslationEngineKind.Claude: return Claude;
                case TranslationEngineKind.OpenAI: return OpenAI;
                case TranslationEngineKind.Gemini: return Gemini;
                default: return DeepL;
            }
        }
    }

    /// <summary>엔진 구현이 공유하는 HTTP/JSON 도우미.</summary>
    internal static class HttpJson
    {
        /// <summary>
        /// 요청당 문장 하나만 받는 엔진용. 여러 문장은 줄바꿈으로 묶어 한 번에 보내고,
        /// 결과 줄 수가 안 맞으면 하나씩 다시 보낸다.
        /// </summary>
        public static async Task<string[]> TranslateLinesAsync(IReadOnlyList<string> texts, Func<string, Task<string>> translateOne)
        {
            if (texts.Count == 1)
                return new[] { await translateOne(texts[0]) };

            string[] lines = (await translateOne(string.Join("\n", texts))).Split('\n');
            if (lines.Length == texts.Count)
                return lines;

            var result = new string[texts.Count];
            for (int i = 0; i < texts.Count; i++)
                result[i] = await translateOne(texts[i]);
            return result;
        }

        public static UnityWebRequest CreatePost(string url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        public static async Task SendAsync(UnityWebRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            request.SendWebRequest().completed += _ => tcs.TrySetResult(true);

            using (ct.Register(request.Abort))
                await tcs.Task;

            ct.ThrowIfCancellationRequested();
        }

        public static void AppendStringArray(StringBuilder sb, IReadOnlyList<string> values)
        {
            sb.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendString(sb, values[i]);
            }

            sb.Append(']');
        }

        public static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
