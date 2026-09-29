using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>Project 창에서 선택한 에셋·폴더 이름 번역.</summary>
    [InitializeOnLoad]
    internal static class ProjectWindowTranslate
    {
        static string _activeGuid;
        static string _activeRowGuid;
        static Rect _activeRowScreenRect;

        static ProjectWindowTranslate()
        {
            Selection.selectionChanged += UpdateActiveGuid;
            UpdateActiveGuid();

            // 선택된 에셋의 행(또는 그리드 타일) 위치를 기억해 팝업을 그 아래에 띄운다.
            EditorApplication.projectWindowItemOnGUI += (guid, rect) =>
            {
                if (guid != _activeGuid || Event.current.type != EventType.Repaint || rect.width <= 0)
                    return;

                _activeRowGuid = guid;
                _activeRowScreenRect = GUIUtility.GUIToScreenRect(rect);
            };
        }

        static void UpdateActiveGuid()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            _activeGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        public static bool IsProjectWindow(EditorWindow window) =>
            window != null && window.GetType().Name == "ProjectBrowser";

        public static void TranslateSelection()
        {
            var targets = Selection.objects
                .Where(obj => obj != null && AssetDatabase.IsMainAsset(obj))
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => path.StartsWith("Assets/")) // Packages 는 읽기 전용이다
                .Distinct()
                .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
                .Select(path => new AssetTarget(AssetDatabase.AssetPathToGUID(path)))
                .Where(target => NameTranslator.DetectDirection(target.Name) != TranslationDirection.None)
                .Cast<RenameTarget>()
                .ToList();
            if (targets.Count == 0)
                return;

            TranslationPopup.Open(targets, GetAnchorRect());
        }

        static Rect GetAnchorRect()
        {
            if (_activeRowGuid == null || _activeRowGuid != _activeGuid || _activeRowScreenRect.width <= 0)
                return PopupAnchor.BelowWindowTop(EditorWindow.focusedWindow);

            // 리스트 행이면 아이콘 뒤부터, 그리드 타일이면 타일 전체 아래.
            var r = _activeRowScreenRect;
            bool isListRow = r.width > r.height * 2;
            return isListRow ? new Rect(r.x + 18, r.y, Mathf.Max(1, r.width - 18), r.height) : r;
        }
    }
}
