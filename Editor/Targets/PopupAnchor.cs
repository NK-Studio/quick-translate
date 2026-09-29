using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>팝업을 띄울 기준 영역(화면 좌표)을 구한다. 팝업은 이 영역 바로 아래에 열린다.</summary>
    internal static class PopupAnchor
    {
        // 탭 + 툴바 + 열 머리글 정도
        const float WindowTopOffset = 72;

        // 창별 "패널 좌표 → 화면 좌표" 차이. 도킹 상태(탭 높이 등)에 따라 다르지만 창이 움직여도 일정하다.
        static readonly Dictionary<EditorWindow, Vector2> Offsets = new Dictionary<EditorWindow, Vector2>();

        public static Rect BelowWindowTop(EditorWindow window)
        {
            Rect area = window != null ? window.position : new Rect(200, 200, 400, 300);
            return new Rect(area.x + 16, area.y + WindowTopOffset, Mathf.Max(1, area.width - 32), 1);
        }

        /// <summary>
        /// 창 패널 좌표의 영역을 화면 좌표로 바꿔 open 에 넘긴다. 처음 한 번은 1x1 IMGUIContainer 를 넣어
        /// GUIToScreenPoint 로 실제 차이를 잰다. 결과가 창 밖이거나 창이 그려지지 않으면 fallback 을 쓴다.
        /// </summary>
        public static void Measure(EditorWindow window, Func<Rect> panelRect, Func<Rect> fallback, Action<Rect> open)
        {
            if (Offsets.TryGetValue(window, out Vector2 offset))
            {
                Rect rect = panelRect();
                var anchor = new Rect(window.position.position + offset + rect.position, rect.size);
                if (IsInside(window, anchor))
                {
                    open(anchor);
                    return;
                }

                Offsets.Remove(window);
            }

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
                finished = true;
                EditorApplication.update -= Timeout;
                EditorApplication.delayCall += () =>
                {
                    probe.RemoveFromHierarchy();
                    open(IsInside(window, anchor) ? anchor : fallback());
                };
            }

            void Timeout()
            {
                if (--framesLeft <= 0)
                    Finish(fallback());
            }

            probe.onGUIHandler = () =>
            {
                if (finished || Event.current.type != EventType.Repaint)
                    return;

                Vector2 screen = GUIUtility.GUIToScreenPoint(Vector2.zero);
                Vector2 world = probe.worldBound.position;
                Offsets[window] = screen - world - window.position.position;

                Rect rect = panelRect();
                Finish(new Rect(screen.x + rect.x - world.x, screen.y + rect.y - world.y, rect.width, rect.height));
            };

            EditorApplication.update += Timeout;
            window.rootVisualElement.Add(probe);
            probe.MarkDirtyRepaint();
            window.Repaint();
        }

        static bool IsInside(EditorWindow window, Rect anchor) =>
            window.position.Contains(new Vector2(anchor.x + 1, anchor.center.y));
    }
}
