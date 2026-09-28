using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickTranslate
{
    /// <summary>Anthropic Messages API (Claude).</summary>
    internal sealed class ClaudeEngine : LlmEngine
    {
        const string Endpoint = "https://api.anthropic.com/v1/messages";

        public override string DisplayName => "Claude";
        public override bool HasApiKey => !string.IsNullOrEmpty(TranslatorSettings.AnthropicApiKey);

        protected override async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            string body =
                "{\"model\":" + JsonString(TranslatorSettings.ClaudeModel) +
                ",\"max_tokens\":1024" +
                ",\"system\":" + JsonString(system) +
                ",\"messages\":[{\"role\":\"user\",\"content\":" + JsonString(user) + "}]}";

            string json = await PostJsonAsync(Endpoint, body, new[]
            {
                new KeyValuePair<string, string>("x-api-key", TranslatorSettings.AnthropicApiKey),
                new KeyValuePair<string, string>("anthropic-version", "2023-06-01")
            }, ct);

            // {"content":[{"type":"text","text":"..."}], ...}
            var text = new StringBuilder();
            foreach (var block in AiJson.Array(AiJson.Parse(json), "content"))
                if (AiJson.String(block, "type") == "text")
                    text.Append(AiJson.String(block, "text"));
            return text.ToString();
        }
    }

    /// <summary>OpenAI Responses API (ChatGPT).</summary>
    internal sealed class OpenAIEngine : LlmEngine
    {
        const string Endpoint = "https://api.openai.com/v1/responses";

        public override string DisplayName => "ChatGPT";
        public override bool HasApiKey => !string.IsNullOrEmpty(TranslatorSettings.OpenAIApiKey);

        protected override async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            // temperature / max_output_tokens 는 모델 종류(추론 모델 등)에 따라 거부될 수 있어 보내지 않는다.
            string body =
                "{\"model\":" + JsonString(TranslatorSettings.OpenAIModel) +
                ",\"instructions\":" + JsonString(system) +
                ",\"input\":" + JsonString(user) + "}";

            string json = await PostJsonAsync(Endpoint, body, new[]
            {
                new KeyValuePair<string, string>("Authorization", "Bearer " + TranslatorSettings.OpenAIApiKey)
            }, ct);

            // {"output":[{"type":"message","content":[{"type":"output_text","text":"..."}]}], ...}
            var text = new StringBuilder();
            foreach (var item in AiJson.Array(AiJson.Parse(json), "output"))
            {
                if (AiJson.String(item, "type") != "message")
                    continue;
                foreach (var content in AiJson.Array(item, "content"))
                    if (AiJson.String(content, "type") == "output_text")
                        text.Append(AiJson.String(content, "text"));
            }

            return text.ToString();
        }
    }

    /// <summary>Google Gemini API (generateContent).</summary>
    internal sealed class GeminiEngine : LlmEngine
    {
        const string EndpointFormat = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        public override string DisplayName => "Gemini";
        public override bool HasApiKey => !string.IsNullOrEmpty(TranslatorSettings.GeminiApiKey);

        protected override async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            string url = string.Format(EndpointFormat, Uri.EscapeDataString(TranslatorSettings.GeminiModel));
            string body =
                "{\"systemInstruction\":{\"parts\":[{\"text\":" + JsonString(system) + "}]}" +
                ",\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":" + JsonString(user) + "}]}]" +
                ",\"generationConfig\":{\"responseMimeType\":\"application/json\"}}";

            // 키는 URL 이 아닌 헤더로 보낸다.
            string json = await PostJsonAsync(url, body, new[]
            {
                new KeyValuePair<string, string>("x-goog-api-key", TranslatorSettings.GeminiApiKey)
            }, ct);

            // {"candidates":[{"content":{"parts":[{"text":"..."}]}}], ...}  (thought:true 인 부분은 건너뛴다)
            var candidates = AiJson.Array(AiJson.Parse(json), "candidates");
            if (candidates.Count == 0)
                return null;

            var text = new StringBuilder();
            foreach (var part in AiJson.Array(AiJson.Object(candidates[0], "content"), "parts"))
                if (!(part is Dictionary<string, object> map && map.TryGetValue("thought", out var thought) && thought is true))
                    text.Append(AiJson.String(part, "text"));
            return text.ToString();
        }
    }

    /// <summary>MiniJson 결과(Dictionary/List) 탐색 도우미. 형식이 다르면 빈 값을 돌려준다.</summary>
    internal static class AiJson
    {
        public static object Parse(string json)
        {
            try
            {
                return MiniJson.Parse(json);
            }
            catch (FormatException)
            {
                throw new TranslationException("AI 응답을 해석할 수 없습니다.");
            }
        }

        public static object Object(object node, string key) =>
            node is Dictionary<string, object> map && map.TryGetValue(key, out var value) ? value : null;

        public static List<object> Array(object node, string key) =>
            Object(node, key) as List<object> ?? new List<object>();

        public static string String(object node, string key) => Object(node, key) as string;
    }
}
