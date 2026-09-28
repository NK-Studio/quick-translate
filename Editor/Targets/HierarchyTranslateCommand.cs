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
    /// 단축키(기본 Cmd+Shift+X, Windows 는 Ctrl+Shift+X) / 메뉴 진입점. 선택한 이름을 한→영 / 영→한 으로 번역한다.
    /// Project 창에 포커스가 있으면 에셋 이름(<see cref="ProjectWindowTranslate"/>), 아니면 Hierarchy 의 GameObject 이름.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyTranslateCommand
    {
        const string MenuPath = "Tools/Quick Translate/Translate Selected Names";

        // 6.5+ 는 EntityId 기반 API 만 쓸 수 있고(int 기반은 6.7 에서 컴파일 에러), 6.3 에는 EntityId 콜백이 없다.
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

        /// <summary>행 위치를 모를 때 창 위쪽에서 이만큼 내려서 띄운다(탭 + 툴바 + 열 머리글).</summary>
        internal const float FallbackTopOffset = 72;

        static HierarchyTranslateCommand()
        {
            // 팝업을 선택된 행 바로 아래에 띄우기 위해 활성 오브젝트의 행 위치를 기억한다. (IMGUI Hierarchy 전용)
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
        static void TranslateShortcut() => TranslateSelection();

        [MenuItem(MenuPath)]
        static void TranslateMenu() => TranslateSelection();

        [MenuItem(MenuPath, true)]
        static bool TranslateMenuValidate() => Selection.objects.Length > 0;

        static void TranslateSelection()
        {
            if (ProjectWindowTranslate.IsProjectWindow(EditorWindow.focusedWindow) ||
                (Selection.gameObjects.Length == 0 && Selection.assetGUIDs.Length > 0))
            {
                ProjectWindowTranslate.TranslateSelection();
                return;
            }

            var gameObjects = Selection.gameObjects
                .Where(go => !EditorUtility.IsPersistent(go)) // Project 창에서 고른 프리팹 에셋 제외
                .Where(go => NameTranslator.DetectDirection(go.name) != TranslationDirection.None)
                .OrderBy(go => go.scene.handle)
                .ThenBy(go => HierarchyOrderKey(go.transform))
                .ToList();

            // 선택이 없거나 번역할 이름이 없으면 아무것도 띄우지 않는다.
            if (gameObjects.Count == 0)
                return;

            var targets = gameObjects.Select(go => (RenameTarget)new GameObjectTarget(go)).ToList();
            void Open(Rect anchor) => TranslationPopup.Open(targets, anchor);

            // [실험] Inspector 에 포커스가 있으면 GameObject 헤더의 이름 칸 바로 아래에 띄운다.
            if (IsInspectorWindow(EditorWindow.focusedWindow))
            {
                OpenAtInspectorNameField(EditorWindow.focusedWindow, Open);
                return;
            }

            OpenAtSelectedRow(gameObjects[0].name, Open);
        }

        static bool IsInspectorWindow(EditorWindow window) =>
            window != null && window.GetType().Name == "InspectorWindow";

        /// <summary>
        /// [실험] Inspector 의 GameObject 헤더(IMGUI)에서 이름 칸 위치를 추정해 그 아래에 연다.
        /// 헤더 요소는 이름이 "...Header" 인 IMGUIContainer 이며, 이름 칸은 헤더 첫 줄(아이콘·활성 토글 오른쪽)에 있다.
        /// </summary>
        static void OpenAtInspectorNameField(EditorWindow inspector, Action<Rect> open)
        {
            // 이름 칸을 편집 중이었다면 편집을 끝내서, 팝업으로 바꾼 이름을 필드가 되돌려 쓰지 않게 한다.
            if (EditorGUIUtility.editingTextField)
            {
                EditorGUIUtility.editingTextField = false;
                GUIUtility.keyboardControl = 0;
            }

            var containers = inspector.rootVisualElement.Query<IMGUIContainer>().ToList();
            var header = containers.FirstOrDefault(c => c.name.EndsWith("Header", StringComparison.Ordinal) &&
                                                        c.worldBound.height >= 30)
                         ?? containers.FirstOrDefault(c => c.worldBound.height >= 30 && c.worldBound.height <= 90 &&
                                                           c.worldBound.width > 100);

            Rect Fallback()
            {
                Rect area = inspector.position;
                return new Rect(area.x + 16, area.y + FallbackTopOffset, Mathf.Max(1, area.width - 32), 1);
            }

            if (header == null)
            {
                open(Fallback());
                return;
            }

            // 헤더 기준 이름 칸 위치(Unity 6 기본 레이아웃): 왼쪽에서 약 66pt, 위에서 5pt, 높이 19pt, 오른쪽 Static 토글 앞까지.
            const float NameLeft = 66, NameTop = 5, NameHeight = 19, RightReserved = 70;
            MeasureElement(inspector, () =>
            {
                Rect h = header.worldBound;
                return new Rect(h.x + NameLeft, h.y + NameTop, Mathf.Max(1, h.width - NameLeft - RightReserved), NameHeight);
            }, Fallback, open);
        }

        /// <summary>선택된 행 바로 아래(모르면 Hierarchy 창 왼쪽 위)를 기준으로 팝업을 연다.</summary>
        static void OpenAtSelectedRow(string rowName, Action<Rect> open)
        {
            // Unity 6.3+ 새 Hierarchy (UI Toolkit): 행 위치를 실제로 측정한 뒤 연다.
            if (rowName != null && TryFindSelectedRow(rowName, out var window, out var row, out var nameText))
            {
                MeasureRow(window, row, nameText, open);
                return;
            }

            open(GetFallbackAnchorRect());
        }

        /// <summary>팝업을 띄울 기준 영역(화면 좌표). 드롭다운은 이 영역 바로 아래에 열린다.</summary>
        static Rect GetFallbackAnchorRect()
        {
            var window = FindHierarchyWindow() ?? EditorWindow.focusedWindow;
            Rect area = window != null ? window.position : new Rect(200, 200, 400, 300);

            // 기존(IMGUI) Hierarchy. 기억한 행 위치가 창 안에 있을 때만 쓴다.
            if (_activeRowId == ActiveRowKey && _activeRowScreenRect.width > 0 &&
                area.Contains(_activeRowScreenRect.center))
            {
                var r = _activeRowScreenRect;
                return new Rect(r.x + 16, r.y, Mathf.Max(1, r.width - 16), r.height);
            }

            // 행 위치를 모르면(스크롤로 가려짐 등) 탭·툴바·열 머리글 아래쪽에 띄운다.
            return new Rect(area.x + 16, area.y + FallbackTopOffset, Mathf.Max(1, area.width - 32), 1);
        }

        /// <summary>
        /// 새 Hierarchy 는 MultiColumnListView 라 hierarchyWindowItemOnGUI 가 호출되지 않는다.
        /// 선택된 행 요소(unity-collection-view__item--selected)와 그 안의 이름 텍스트를 찾는다.
        /// </summary>
        static bool TryFindSelectedRow(string targetName, out EditorWindow window, out VisualElement row, out TextElement nameText)
        {
            row = null;
            nameText = null;
            window = FindHierarchyWindow();
            if (window == null)
                return false;

            VisualElement foundRow = null;
            TextElement foundText = null;
            window.rootVisualElement.Query(className: "unity-collection-view__item--selected").ForEach(candidate =>
            {
                if (foundText != null)
                    return;

                var text = candidate.Query<TextElement>().Where(t => t.text == targetName).First();
                if (text != null)
                {
                    foundRow = candidate;
                    foundText = text;
                }
                else if (foundRow == null)
                {
                    foundRow = candidate;
                }
            });

            row = foundRow;
            nameText = foundText;
            return row != null && row.worldBound.height > 0;
        }

        /// <summary>
        /// EditorWindow.position 과 패널 좌표의 기준점은 도킹 상태(탭 높이 등)에 따라 어긋난다.
        /// 1x1 IMGUIContainer 를 잠깐 넣어 GUIToScreenPoint 로 패널 좌표 → 화면 좌표를 정확히 잰다.
        /// </summary>
        static void MeasureRow(EditorWindow window, VisualElement row, TextElement nameText, Action<Rect> open)
        {
            MeasureElement(window, () =>
            {
                Rect rowBound = row.worldBound;
                float x = nameText != null ? nameText.worldBound.x : rowBound.x;
                return new Rect(x, rowBound.y, Mathf.Max(1, rowBound.xMax - x), rowBound.height);
            }, GetFallbackAnchorRect, open);
        }

        /// <summary>
        /// window 패널 좌표계의 영역(panelRect)을 화면 좌표로 바꿔 open 에 넘긴다.
        /// 측정값이 창 밖이거나 창이 다시 그려지지 않으면 fallback 을 쓴다.
        /// </summary>
        static void MeasureElement(EditorWindow window, Func<Rect> panelRect, Func<Rect> fallback,
            Action<Rect> open)
        {
            bool finished = false;
            int framesLeft = 30;
            var probe = new IMGUIContainer { pickingMode = PickingMode.Ignore };
            probe.style.position = Position.Absolute;
            probe.style.left = 0;
            probe.style.top = 0;
            probe.style.width = 1;
            probe.style.height = 1;

            void Finish(Rect anchor)
            {
                if (finished)
                    return;

                // 측정값이 창 밖이면(좌표 기준이 어긋난 경우) 믿지 않는다.
                Rect bounds = window.position;
                if (!bounds.Contains(new Vector2(anchor.x + 1, anchor.center.y)))
                    anchor = fallback();

                finished = true;
                EditorApplication.update -= Timeout;
                EditorApplication.delayCall += () =>
                {
                    probe.RemoveFromHierarchy();
                    open(anchor);
                };
            }

            // 창이 다시 그려지지 않는 경우를 대비한 안전장치
            void Timeout()
            {
                if (--framesLeft <= 0)
                    Finish(fallback());
            }

            probe.onGUIHandler = () =>
            {
                if (finished || Event.current.type != EventType.Repaint)
                    return;

                Vector2 probeScreen = GUIUtility.GUIToScreenPoint(Vector2.zero);
                Vector2 probeWorld = probe.worldBound.position;
                Rect rect = panelRect();
                Finish(new Rect(probeScreen.x + rect.x - probeWorld.x, probeScreen.y + rect.y - probeWorld.y,
                    rect.width, rect.height));
            };

            EditorApplication.update += Timeout;
            window.rootVisualElement.Add(probe);
            probe.MarkDirtyRepaint();
            window.Repaint();
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
            if (focused != null && IsHierarchy(focused))
                return focused;

            return Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(IsHierarchy);
        }

        /// <summary>Hierarchy 상의 표시 순서대로 정렬하기 위한 키 (형제 인덱스 경로).</summary>
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
