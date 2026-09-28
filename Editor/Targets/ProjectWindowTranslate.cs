using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>
    /// Project 창에서 선택한 에셋/폴더 이름 번역. 단축키는 <see cref="HierarchyTranslateCommand"/> 와 공유하고,
    /// Project 창 우클릭 메뉴에도 항목을 추가한다.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectWindowTranslate
    {
        const string ContextMenuPath = "Assets/Translate Name (KO ↔ EN)";

        static string _activeGuid;
        static string _activeRowGuid;
        static Rect _activeRowScreenRect;

        static ProjectWindowTranslate()
        {
            Selection.selectionChanged += UpdateActiveGuid;
            UpdateActiveGuid();

            // 팝업을 선택된 에셋 바로 아래에 띄우기 위해 활성 에셋의 행(또는 그리드 타일) 위치를 기억한다.
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

        [MenuItem(ContextMenuPath, false, 20)]
        static void TranslateFromContextMenu() => TranslateSelection();

        [MenuItem(ContextMenuPath, true)]
        static bool TranslateFromContextMenuValidate() => Selection.assetGUIDs.Length > 0;

        public static void TranslateSelection()
        {
            var targets = Selection.objects
                .Where(obj => obj != null && AssetDatabase.IsMainAsset(obj))
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => path.StartsWith("Assets/")) // Packages 는 읽기 전용
                .Distinct()
                .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase)
                .Select(path => new AssetTarget(AssetDatabase.AssetPathToGUID(path)))
                .Where(target => NameTranslator.DetectDirection(target.Name) != TranslationDirection.None)
                .Cast<RenameTarget>()
                .ToList();

            // 선택이 없거나 번역할 이름이 없으면 아무것도 띄우지 않는다.
            if (targets.Count == 0)
                return;

            TranslationPopup.Open(targets, GetAnchorRect());
        }

        static Rect GetAnchorRect()
        {
            if (_activeRowGuid != null && _activeRowGuid == _activeGuid && _activeRowScreenRect.width > 0)
            {
                var r = _activeRowScreenRect;
                // 리스트 행이면 아이콘 뒤 이름 위치부터, 그리드 타일이면 타일 아래에 붙는다.
                bool isListRow = r.width > r.height * 2;
                return isListRow ? new Rect(r.x + 18, r.y, Mathf.Max(1, r.width - 18), r.height) : r;
            }

            // 행 위치를 모르면 탭·툴바 아래쪽에 띄운다.
            var window = EditorWindow.focusedWindow;
            Rect area = window != null ? window.position : new Rect(200, 200, 400, 300);
            return new Rect(area.x + 16, area.y + HierarchyTranslateCommand.FallbackTopOffset, Mathf.Max(1, area.width - 32), 1);
        }
    }
}
