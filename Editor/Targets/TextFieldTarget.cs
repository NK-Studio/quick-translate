using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace QuickTranslate
{
    /// <summary>
    /// 지금 커서가 있는 텍스트 입력칸을 찾는다. 모든 에디터 창은 UI Toolkit 패널 위에 있고,
    /// IMGUI 화면도 IMGUIContainer 안에서 그려지므로 포커스된 요소 하나로 두 종류를 구분할 수 있다.
    /// </summary>
    internal static class TextFieldCapture
    {
        /// <param name="panelRect">팝업 기준 영역(창 패널 좌표). 알 수 없으면 null.</param>
        public static bool TryCapture(EditorWindow window, out RenameTarget target, out Func<Rect> panelRect)
        {
            target = null;
            panelRect = null;
            if (window == null)
                return false;

            var focused = window.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null)
                return false;

            // UI Toolkit 입력칸 (Unity 6 기본 Inspector, 새 Hierarchy 이름 바꾸기, 설정 화면 등)
            for (var element = focused; element != null; element = element.parent)
            {
                if (!(element is TextInputBaseField<string> field))
                    continue;
                if (field.isReadOnly || field.isPasswordField)
                    return false;

                var uitkTarget = new UIToolkitTextTarget(field);
                if (NameTranslator.DetectDirection(uitkTarget.Name) == TranslationDirection.None)
                    return false;

                target = uitkTarget;
                panelRect = () => field.worldBound;
                return true;
            }

            // IMGUI 입력칸 (TextMeshPro 등 커스텀 에디터, Project 이름 바꾸기, 6.0 Hierarchy 등)
            if (focused is IMGUIContainer container && EditorGUIUtility.editingTextField)
            {
                var editor = ImguiTextTarget.GetActiveEditor();
                if (editor == null || string.IsNullOrEmpty(editor.text))
                    return false;

                var imguiTarget = new ImguiTextTarget(window, editor);
                if (NameTranslator.DetectDirection(imguiTarget.Name) == TranslationDirection.None)
                    return false;

                target = imguiTarget;
                // TextEditor.position 은 입력칸을 그린 IMGUIContainer 기준 좌표다.
                Rect local = editor.position;
                panelRect = () =>
                {
                    Rect bound = container.worldBound;
                    return new Rect(bound.x + local.x, bound.y + local.y, Mathf.Max(1, local.width), Mathf.Max(1, local.height));
                };
                return true;
            }

            return false;
        }

        /// <summary>선택 영역이 있으면 그 범위, 없으면 전체.</summary>
        internal static void GetRange(string text, int cursor, int select, out int start, out int length)
        {
            cursor = Mathf.Clamp(cursor, 0, text.Length);
            select = Mathf.Clamp(select, 0, text.Length);
            if (cursor == select)
            {
                start = 0;
                length = text.Length;
            }
            else
            {
                start = Mathf.Min(cursor, select);
                length = Mathf.Abs(cursor - select);
            }
        }
    }

    /// <summary>UI Toolkit 입력칸. 값을 바꾸면 바인딩된 SerializedProperty 저장·Undo 는 Unity 가 처리한다.</summary>
    internal sealed class UIToolkitTextTarget : RenameTarget
    {
        readonly TextInputBaseField<string> _field;
        readonly string _original;
        readonly int _start;
        readonly int _length;

        public UIToolkitTextTarget(TextInputBaseField<string> field)
        {
            _field = field;
            // isDelayed 칸은 입력 중인 글이 아직 value 에 없으므로 화면의 text 를 읽는다.
            _original = field.text ?? string.Empty;
            var selection = field.textSelection;
            TextFieldCapture.GetRange(_original, selection.cursorIndex, selection.selectIndex, out _start, out _length);
        }

        public override string Name => _original.Substring(_start, _length);
        public override bool IsValid => _field.panel != null;
        public override Object Context => null;
        public override bool IsName => false;
        public override string Kind => "텍스트";

        public override string Rename(string newName)
        {
            if (newName == null)
                return null;

            string current = _field.text ?? string.Empty;
            string replaced = current == _original
                ? _original.Substring(0, _start) + newName + _original.Substring(_start + _length)
                : _length == _original.Length ? newName : null;
            if (replaced == null)
                return "입력칸 내용이 바뀌어서 선택한 부분을 찾을 수 없습니다.";

            _field.value = replaced;
            int caret = _start + newName.Length;
            _field.Focus();
            _field.SelectRange(caret, caret);
            return null;
        }
    }

    /// <summary>
    /// IMGUI 입력칸. 내부 편집기(EditorGUI.s_RecycledEditor)로 글과 선택 범위를 읽고,
    /// 값은 Cmd+V 와 같은 "Paste" 명령으로 넣는다(저장·Undo 는 입력칸 쪽 코드가 처리).
    /// 클립보드는 잠깐 빌려 쓰고 곧바로 원래 내용으로 되돌린다.
    /// </summary>
    internal sealed class ImguiTextTarget : RenameTarget
    {
        static readonly BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

        readonly EditorWindow _window;
        readonly TextEditor _editor;
        readonly string _original;
        readonly int _start;
        readonly int _length;

        public ImguiTextTarget(EditorWindow window, TextEditor editor)
        {
            _window = window;
            _editor = editor;
            _original = editor.text ?? string.Empty;
            TextFieldCapture.GetRange(_original, editor.cursorIndex, editor.selectIndex, out _start, out _length);
        }

        /// <summary>편집 중인 IMGUI 입력칸의 편집기. Unity 버전에 따라 프로퍼티/필드 이름이 달라 둘 다 본다.</summary>
        public static TextEditor GetActiveEditor()
        {
            var type = typeof(EditorGUI);
            object value = type.GetProperty("s_RecycledEditor", StaticNonPublic)?.GetValue(null)
                           ?? type.GetField("s_RecycledEditorInternal", StaticNonPublic)?.GetValue(null);
            return value as TextEditor;
        }

        public override string Name => _original.Substring(_start, _length);
        public override bool IsValid => _window != null;
        public override Object Context => null;
        public override bool IsName => false;
        public override string Kind => "텍스트";

        public override string Rename(string newName)
        {
            if (newName == null)
                return null;
            if (_window == null)
                return "입력칸이 있던 창이 닫혔습니다.";

            // 팝업이 닫히고 원래 창으로 포커스가 돌아온 다음에 붙여넣는다.
            _window.Focus();
            EditorApplication.delayCall += () => Paste(newName);
            return null;
        }

        void Paste(string text)
        {
            if (_window == null)
                return;

            bool wholeText = _length == _original.Length;
            bool changed = _editor.text != _original;
            if (!EditorGUIUtility.editingTextField || GetActiveEditor() != _editor || (changed && !wholeText))
            {
                EditorGUIUtility.systemCopyBuffer = text;
                Debug.LogWarning("[Quick Translate] 입력칸 편집이 끝나서 번역을 바로 넣지 못했습니다. 번역 결과를 클립보드에 복사했으니 붙여넣어 주세요.");
                return;
            }

            string clipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                EditorGUIUtility.systemCopyBuffer = text;
                if (wholeText)
                {
                    _window.SendEvent(EditorGUIUtility.CommandEvent("SelectAll"));
                }
                else
                {
                    _editor.cursorIndex = _start;
                    _editor.selectIndex = _start + _length;
                }

                _window.SendEvent(EditorGUIUtility.CommandEvent("Paste"));
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = clipboard;
                _window.Repaint();
            }
        }
    }
}
