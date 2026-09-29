using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>프로젝트 루트(Assets 밖)의 .env 에서 API 키를 읽는다. 파일이 바뀌면 다음 조회 때 다시 읽는다.</summary>
    internal static class EnvFile
    {
        public const string DeepLApiKey = "DEEPL_API_KEY";
        public const string GoogleApiKey = "GOOGLE_TRANSLATE_API_KEY";
        public const string PapagoClientId = "PAPAGO_CLIENT_ID";
        public const string PapagoClientSecret = "PAPAGO_CLIENT_SECRET";
        public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
        public const string OpenAIApiKey = "OPENAI_API_KEY";
        public const string GeminiApiKey = "GEMINI_API_KEY";

        public static readonly string[] AllKeys =
        {
            DeepLApiKey, GoogleApiKey, PapagoClientId, PapagoClientSecret, AnthropicApiKey, OpenAIApiKey, GeminiApiKey
        };

        static Dictionary<string, string> _values;
        static DateTime _loadedWriteTime;

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".env"));

        public static bool Exists => File.Exists(FilePath);

        /// <summary>값이 비어 있어도 키 줄 자체가 모두 있는지.</summary>
        public static bool HasAllKeys(IEnumerable<string> keys)
        {
            EnsureLoaded();
            foreach (string key in keys)
                if (!_values.ContainsKey(key))
                    return false;
            return true;
        }

        public static string Get(string key)
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out string value) ? value : string.Empty;
        }

        /// <summary>없는 키만 "KEY=" 줄로 끝에 추가한다. 파일이 없으면 만든다.</summary>
        public static void AddMissingKeys(IEnumerable<string> keys)
        {
            EnsureLoaded();
            var missing = keys.Where(key => !_values.ContainsKey(key)).Select(key => key + "=").ToList();
            if (missing.Count == 0 && Exists)
                return;

            if (Exists)
            {
                string current = File.ReadAllText(FilePath);
                string separator = current.Length > 0 && !current.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty;
                File.AppendAllText(FilePath, separator + string.Join("\n", missing) + "\n");
            }
            else
            {
                missing.Insert(0, "# Quick Translate API 키 (VCS 에 올리지 마세요). 사용하는 엔진의 키만 채우면 됩니다.");
                File.WriteAllText(FilePath, string.Join("\n", missing) + "\n");
            }

            _values = null;
        }

        static void EnsureLoaded()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                _values = new Dictionary<string, string>();
                _loadedWriteTime = default;
                return;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (_values != null && writeTime == _loadedWriteTime)
                return;

            _values = Parse(File.ReadAllLines(path));
            _loadedWriteTime = writeTime;
            NameTranslator.ClearCache();
        }

        /// <summary>KEY=VALUE 형식. #주석, 빈 줄, 'export ' 접두사, 따옴표로 감싼 값을 지원한다.</summary>
        static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (line.StartsWith("export ", StringComparison.Ordinal))
                    line = line.Substring(7).TrimStart();

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                {
                    value = value.Substring(1, value.Length - 2);
                }
                else
                {
                    int comment = value.IndexOf(" #", StringComparison.Ordinal);
                    if (comment >= 0)
                        value = value.Substring(0, comment).TrimEnd();
                }

                values[key] = value;
            }

            return values;
        }
    }
}
