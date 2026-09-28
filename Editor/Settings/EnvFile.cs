using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>
    /// 프로젝트 루트(Assets 폴더 밖)의 .env 파일에서 API 키를 읽는다.
    /// Assets 밖이라 Unity 가 임포트하지 않고 빌드에도 포함되지 않는다. VCS 에는 올리지 않는다(.gitignore).
    /// 파일이 바뀌면(수정 시각 기준) 다음 조회 때 다시 읽는다.
    /// </summary>
    internal static class EnvFile
    {
        public const string DeepLApiKey = "DEEPL_API_KEY";
        public const string GoogleApiKey = "GOOGLE_TRANSLATE_API_KEY";
        public const string PapagoClientId = "PAPAGO_CLIENT_ID";
        public const string PapagoClientSecret = "PAPAGO_CLIENT_SECRET";
        public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
        public const string OpenAIApiKey = "OPENAI_API_KEY";
        public const string GeminiApiKey = "GEMINI_API_KEY";

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

        /// <summary>
        /// 값을 채워 넣는다. 파일이 없으면 만들고, 키가 없으면 끝에 추가하고,
        /// 키가 있지만 값이 비어 있으면 그 줄을 채운다. 이미 값이 있는 키는 건드리지 않는다.
        /// </summary>
        public static void FillMissing(IEnumerable<KeyValuePair<string, string>> entries)
        {
            string path = FilePath;
            var lines = File.Exists(path)
                ? new List<string>(File.ReadAllLines(path))
                : new List<string>
                {
                    "# Quick Translate API 키 (VCS 에 올리지 마세요)",
                    "# 사용하는 엔진의 키만 채우면 됩니다."
                };

            bool changed = !File.Exists(path);
            foreach (var entry in entries)
            {
                string value = entry.Value ?? string.Empty;
                int index = lines.FindIndex(line => Parse(new[] { line }).ContainsKey(entry.Key));
                if (index < 0)
                {
                    lines.Add($"{entry.Key}={value}");
                    changed = true;
                }
                else if (value.Length > 0 && Parse(new[] { lines[index] })[entry.Key].Length == 0)
                {
                    lines[index] = $"{entry.Key}={value}";
                    changed = true;
                }
            }

            if (!changed)
                return;

            File.WriteAllText(path, string.Join("\n", lines) + "\n");
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
