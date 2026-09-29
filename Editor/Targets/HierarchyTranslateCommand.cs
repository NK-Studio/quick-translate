using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>
    /// 단축키 진입점. 입력칸에 커서가 있으면 그 글을, 아니면 Project 창의 에셋 이름이나
    /// Hierarchy·Inspector 의 GameObject 이름을 번역한다.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyTranslateCommand
    {
        // 6.5 부터 EntityId 콜백만 쓸 수 있다(int 기반은 6.7 에서 에러). 6.3 이하는 int 기반.
#if UNITY_6000_5_OR_NEWER
        static EntityId ActiveRowKey => Selection.activeEntityId;
        static EntityId _activeRowId;
#else
#pragma warning disable CS0618
        static int ActiveRowKey => Selection.activeInstanceID;
#pragma warning restore CS0618
        static int _activeRowId;
#endif
        static Rect _activeRowScreenRect;

        static HierarchyTranslateCommand()
        {
            // IMGUI Hierarchy(6.0 등)에서 선택된 행의 위치를 기억해 둔다.
#if UNITY_6000_5_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += (id, rowRect) =>
#else
#pragma warning disable CS0618
            EditorApplication.hierarchyWindowItemOnGUI += (id, rowRect) =>
#pragma warning restore CS0618
#endif
            {
                if (id != ActiveRowKey)
                    return;
                _activeRowId = id;
                _activeRowScreenRect = GUIUtility.GUIToScreenRect(rowRect);
            };
        }

        [Shortcut("Quick Translate/Translate Selected Names", KeyCode.X, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void TranslateShortcut()
        {
            var focusedWindow = EditorWindow.focusedWindow;

            if (TextFieldCapture.TryCapture(focusedWindow, out var textTarget, out var textRect))
            {
                var targets = new List<RenameTarget> { textTarget };
                PopupAnchor.Measure(focusedWindow, textRect, () => PopupAnchor.BelowWindowTop(focusedWindow),
                    anchor => TranslationPopup.Open(targets, anchor));
                return;
            }

            if (ProjectWindowTranslate.IsProjectWindow(focusedWindow) ||
                (Selection.gameObjects.Length == 0 && Selection.assetGUIDs.Length > 0))
            {
                ProjectWindowTranslate.TranslateSelection();
                return;
            }

            TranslateGameObjects(focusedWindow);
        }

        static void TranslateGameObjects(EditorWindow focusedWindow)
        {
            var gameObjects = Selection.gameObjects
                .Where(go => !EditorUtility.IsPersistent(go)) // Project 창에서 고른 프리팹 에셋 제외
                .Where(go => NameTranslator.DetectDirection(go.name) != TranslationDirection.None)
                .OrderBy(go => go.scene.handle)
                .ThenBy(go => HierarchyOrderKey(go.transform))
                .ToList();
            if (gameObjects.Count == 0)
                return;

            var targets = gameObjects.Select(go => (RenameTarget)new GameObjectTarget(go)).ToList();
            void Open(Rect anchor) => TranslationPopup.Open(targets, anchor);

            if (focusedWindow != null && focusedWindow.GetType().Name == "InspectorWindow")
                OpenBelowInspectorName(focusedWindow, Open);
            else
                OpenBelowSelectedRow(gameObjects[0].name, Open);
        }

        /// <summary>Inspector GameObject 헤더의 이름 칸 아래. 칸 위치는 Unity 6 기본 레이아웃 기준으로 맞춘 값이다.</summary>
        static void OpenBelowInspectorName(EditorWindow inspector, Action<Rect> open)
        {
            var containers = inspector.rootVisualElement.Query<IMGUIContainer>().ToList();
            var header = containers.FirstOrDefault(c => c.name.EndsWith("Header", StringComparison.Ordinal) && c.worldBound.height >= 30)
                         ?? containers.FirstOrDefault(c => c.worldBound.height >= 30 && c.worldBound.height <= 90 && c.worldBound.width > 100);
            if (header == null)
            {
                open(PopupAnchor.BelowWindowTop(inspector));
                return;
            }

            const float NameLeft = 66, NameTop = 9, NameHeight = 19, RightReserved = 70;
            PopupAnchor.Measure(inspector, () =>
            {
                Rect h = header.worldBound;
                return new Rect(h.x + NameLeft, h.y + NameTop, Mathf.Max(1, h.width - NameLeft - RightReserved), NameHeight);
            }, () => PopupAnchor.BelowWindowTop(inspector), open);
        }

        static void OpenBelowSelectedRow(string rowName, Action<Rect> open)
        {
            if (TryFindSelectedRow(rowName, out var window, out var row, out var nameText))
            {
                PopupAnchor.Measure(window, () =>
                {
                    Rect rowBound = row.worldBound;
                    float x = nameText != null ? nameText.worldBound.x : rowBound.x;
                    return new Rect(x, rowBound.y, Mathf.Max(1, rowBound.xMax - x), rowBound.height);
                }, ImguiRowOrWindowTop, open);
                return;
            }

            open(ImguiRowOrWindowTop());
        }

        static Rect ImguiRowOrWindowTop()
        {
            var window = FindHierarchyWindow() ?? EditorWindow.focusedWindow;
            bool rowKnown = _activeRowId == ActiveRowKey && _activeRowScreenRect.width > 0 &&
                            window != null && window.position.Contains(_activeRowScreenRect.center);
            if (!rowKnown)
                return PopupAnchor.BelowWindowTop(window);

            var r = _activeRowScreenRect;
            return new Rect(r.x + 16, r.y, Mathf.Max(1, r.width - 16), r.height);
        }

        /// <summary>새 Hierarchy(6.3+, UI Toolkit)에서 선택된 행과 그 안의 이름 텍스트를 찾는다.</summary>
        static bool TryFindSelectedRow(string targetName, out EditorWindow window, out VisualElement row, out TextElement nameText)
        {
            row = null;
            nameText = null;
            window = FindHierarchyWindow();
            if (window == null)
                return false;

            foreach (var candidate in window.rootVisualElement.Query(className: "unity-collection-view__item--selected").ToList())
            {
                var text = candidate.Query<TextElement>().Where(t => t.text == targetName).First();
                if (text != null)
                {
                    row = candidate;
                    nameText = text;
                    break;
                }

                row ??= candidate;
            }

            return row != null && row.worldBound.height > 0;
        }

        static EditorWindow FindHierarchyWindow()
        {
            static bool IsHierarchy(EditorWindow w)
            {
                string name = w.GetType().Name;
                return name == "SceneHierarchyWindow" || name == "HierarchyWindow" ||
                       (w.titleContent.text == "Hierarchy" && !name.StartsWith("Builder"));
            }

            var focused = EditorWindow.focusedWindow;
            return focused != null && IsHierarchy(focused)
                ? focused
                : Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(IsHierarchy);
        }

        /// <summary>Hierarchy 표시 순서대로 정렬하기 위한 키(형제 인덱스 경로).</summary>
        static string HierarchyOrderKey(Transform t)
        {
            var path = new List<string>();
            for (; t != null; t = t.parent)
                path.Add(t.GetSiblingIndex().ToString("D6"));
            path.Reverse();
            return string.Join("/", path);
        }
    }
}
