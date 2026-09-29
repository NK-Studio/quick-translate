using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>
    /// 설치된 Claude Code CLI 를 헤드리스(-p)로 실행해 번역한다. API 키 없이 CLI 에 로그인된 계정(요금제)을 쓴다.
    /// 호출마다 CLI 를 새로 띄우므로 API 방식보다 느리다(약 3~4초).
    /// - 지시문은 임시 파일(--system-prompt-file), 번역할 이름은 표준 입력으로 넘겨 인자 따옴표 문제를 피한다.
    /// - 도구·MCP·설정·세션 저장을 끄고, 생각하기(thinking)도 꺼서 응답만 빠르게 받는다.
    /// - 프로젝트의 CLAUDE.md 를 읽지 않도록 임시 폴더에서 실행한다.
    /// </summary>
    internal sealed class ClaudeCodeEngine : LlmEngine
    {
        const int TimeoutMs = 60_000;

        public override string DisplayName => "Claude Code";

        /// <summary>키 대신 CLI 실행 파일이 있는지로 판단한다.</summary>
        public override bool HasApiKey => ResolveExecutable() != null;

        protected override async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            string executable = ResolveExecutable();
            if (executable == null)
                throw new TranslationException(
                    "Claude Code CLI 를 찾을 수 없습니다. 설치했다면 Preferences > Quick Translate 에서 경로를 지정하세요.");

            string workDir = Path.Combine(Path.GetTempPath(), "QuickTranslate");
            Directory.CreateDirectory(workDir);
            string systemFile = Path.Combine(workDir, $"system-{Guid.NewGuid():N}.txt");
            File.WriteAllText(systemFile, system, new UTF8Encoding(false));

            try
            {
                string model = TranslatorSettings.ClaudeCodeModel;
                string arguments = string.Join(" ", new[]
                {
                    "-p",
                    "--model", Quote(model),
                    "--output-format", "json",
                    "--tools", Quote(string.Empty),
                    "--no-session-persistence",
                    "--strict-mcp-config",
                    "--setting-sources", Quote(string.Empty),
                    "--system-prompt-file", Quote(systemFile)
                });

                string json = await Task.Run(() => Run(executable, arguments, user, workDir));
                return ParseResult(json);
            }
            finally
            {
                try
                {
                    File.Delete(systemFile);
                }
                catch (IOException)
                {
                    // 임시 파일 정리 실패는 무시
                }
            }
        }

        static string Run(string executable, string arguments, string stdin, string workDir)
        {
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workDir
            };
            info.EnvironmentVariables["MAX_THINKING_TOKENS"] = "0";

            // Unity 는 터미널과 PATH 가 달라 CLI 가 보조 프로그램을 못 찾을 수 있어 실행 파일 폴더를 앞에 붙인다.
            string dir = Path.GetDirectoryName(executable);
            string path = info.EnvironmentVariables["PATH"] ?? string.Empty;
            info.EnvironmentVariables["PATH"] = string.IsNullOrEmpty(dir) ? path : dir + Path.PathSeparator + path;

            using var process = new Process { StartInfo = info };
            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                throw new TranslationException($"Claude Code CLI 를 실행하지 못했습니다: {e.Message}");
            }

            // stdout/stderr 를 동시에 읽어 버퍼가 차서 멈추는 것을 막는다.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                input.Write(stdin);

            if (!process.WaitForExit(TimeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // 이미 종료됨
                }

                throw new TranslationException("Claude Code 응답이 너무 오래 걸려 중단했습니다.");
            }

            string stdout = stdoutTask.Result;
            string stderr = stderrTask.Result;
            if (string.IsNullOrWhiteSpace(stdout))
                throw new TranslationException(
                    $"Claude Code 실행 실패 (종료 코드 {process.ExitCode}). 터미널에서 claude 로그인 상태를 확인하세요.\n{stderr.Trim()}");
            return stdout;
        }

        /// <summary>--output-format json 결과: {"is_error":false,"result":"...", ...}</summary>
        static string ParseResult(string json)
        {
            var root = AiJson.Parse(json.Trim());
            string result = AiJson.String(root, "result");
            if (AiJson.Object(root, "is_error") is true)
                throw new TranslationException($"Claude Code 오류: {result ?? AiJson.String(root, "subtype")}");
            return result;
        }

        /// <summary>인자 하나를 따옴표로 감싼다(공백·빈 문자열 대응, 내부 따옴표/역슬래시 이스케이프).</summary>
        static string Quote(string value)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }

                backslashes = 0;
            }

            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        // ───────── 실행 파일 찾기 ─────────

        static string _detectedPath;
        static bool _detected;

        /// <summary>설정한 경로 → 흔한 설치 위치 → (macOS/Linux) 로그인 셸의 command -v 순으로 찾는다.</summary>
        public static string ResolveExecutable()
        {
            string configured = TranslatorSettings.ClaudeCodePath;
            if (!string.IsNullOrWhiteSpace(configured))
                return File.Exists(configured) ? configured : null;

            if (!_detected)
            {
                _detected = true;
                _detectedPath = Detect();
            }

            return _detectedPath;
        }

        public static void ResetDetection() => _detected = false;

        static string Detect()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>();
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                candidates.Add(Path.Combine(home, ".local", "bin", "claude.exe"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"));
            }
            else
            {
                candidates.Add("/opt/homebrew/bin/claude");
                candidates.Add("/usr/local/bin/claude");
                candidates.Add(Path.Combine(home, ".local", "bin", "claude"));
                candidates.Add(Path.Combine(home, ".claude", "local", "claude"));
            }

            foreach (string candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            return Application.platform == RuntimePlatform.WindowsEditor ? null : FindWithLoginShell();
        }

        /// <summary>Unity 는 로그인 셸의 PATH 를 모르므로, 셸을 한 번 띄워 claude 위치를 물어본다.</summary>
        static string FindWithLoginShell()
        {
            try
            {
                string shell = Environment.GetEnvironmentVariable("SHELL");
                if (string.IsNullOrEmpty(shell) || !File.Exists(shell))
                    shell = "/bin/zsh";

                var info = new ProcessStartInfo(shell, "-lc \"command -v claude\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(info);
                string output = process?.StandardOutput.ReadToEnd().Trim();
                process?.WaitForExit(5000);
                if (string.IsNullOrEmpty(output))
                    return null;

                string[] lines = output.Split('\n');
                string last = lines[lines.Length - 1].Trim();
                return File.Exists(last) ? last : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
