using System.IO;
using UnityEditor;

namespace QuickTranslate
{
    /// <summary>
    /// 이전 이름(Hierarchy Translator)으로 저장된 설정을 새 이름으로 한 번 옮긴다.
    /// 새 쪽에 이미 값이 있으면 건드리지 않는다. API 키는 .env 로 옮기는 버튼(설정 화면)에서 따로 처리한다.
    /// </summary>
    [InitializeOnLoad]
    internal static class LegacyMigration
    {
        const string NewPrefix = "QuickTranslate.";
        const string LegacyGlossaryPath = "ProjectSettings/HierarchyTranslatorGlossary.asset";
        const string GlossaryPath = "ProjectSettings/QuickTranslateGlossary.asset";

        static LegacyMigration()
        {
            MoveInt("Engine");
            MoveInt("MaxCandidates");
            MoveString("TargetLanguage");
            MoveString("Context");
            MoveBool("PreferUpperCaseFirst");
            MoveBool("UseContext");
            MoveGlossaryFile();
        }

        static bool ShouldMove(string key, out string oldKey, out string newKey)
        {
            oldKey = TranslatorSettings.LegacyPrefix + key;
            newKey = NewPrefix + key;
            return EditorPrefs.HasKey(oldKey) && !EditorPrefs.HasKey(newKey);
        }

        static void MoveInt(string key)
        {
            if (!ShouldMove(key, out string oldKey, out string newKey))
                return;
            EditorPrefs.SetInt(newKey, EditorPrefs.GetInt(oldKey));
            EditorPrefs.DeleteKey(oldKey);
        }

        static void MoveString(string key)
        {
            if (!ShouldMove(key, out string oldKey, out string newKey))
                return;
            EditorPrefs.SetString(newKey, EditorPrefs.GetString(oldKey));
            EditorPrefs.DeleteKey(oldKey);
        }

        static void MoveBool(string key)
        {
            if (!ShouldMove(key, out string oldKey, out string newKey))
                return;
            EditorPrefs.SetBool(newKey, EditorPrefs.GetBool(oldKey));
            EditorPrefs.DeleteKey(oldKey);
        }

        /// <summary>용어집은 ScriptableSingleton 이 처음 접근될 때 읽으므로, 그 전에 파일 이름만 바꿔 둔다.</summary>
        static void MoveGlossaryFile()
        {
            if (File.Exists(LegacyGlossaryPath) && !File.Exists(GlossaryPath))
                File.Move(LegacyGlossaryPath, GlossaryPath);
        }
    }
}
