using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace QuickTranslate
{
    /// <summary>커서가 있는 텍스트 입력칸을 찾는다. IMGUI 화면도 IMGUIContainer 안에 있으므로 포커스된 요소로 구분한다.</summary>
    internal static class TextFieldCapture
    {
        public static bool TryCapture(EditorWindow window, out RenameTarget target, out Func<Rect> panelRect)
        {
            target = null;
            panelRect = null;

            var focused = window != null
                ? window.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement
                : null;
            if (focused == null)
                return false;

            for (var element = focused; element != null; element = element.parent)
            {
                if (!(element is TextInputBaseField<string> field))
                    continue;
                if (field.isReadOnly || field.isPasswordField)
                    return false;

                target = new UIToolkitTextTarget(field);
                panelRect = () => field.worldBound;
                return NameTranslator.DetectDirection(target.Name) != TranslationDirection.None;
            }

            if (focused is IMGUIContainer container && EditorGUIUtility.editingTextField)
            {
                var editor = ImguiTextTarget.GetActiveEditor();
                if (editor == null || string.IsNullOrEmpty(editor.text))
                    return false;

                target = new ImguiTextTarget(window, editor);
                Rect local = editor.position; // IMGUIContainer 기준 좌표
                panelRect = () =>
                {
                    Rect bound = container.worldBound;
                    return new Rect(bound.x + local.x, bound.y + local.y, Mathf.Max(1, local.width), Mathf.Max(1, local.height));
                };
                return NameTranslator.DetectDirection(target.Name) != TranslationDirection.None;
            }

            return false;
        }

        /// <summary>선택 영역이 있으면 그 범위, 없으면 전체.</summary>
        public static void GetRange(string text, int cursor, int select, out int start, out int length)
        {
            cursor = Mathf.Clamp(cursor, 0, text.Length);
            select = Mathf.Clamp(select, 0, text.Length);
            start = cursor == select ? 0 : Mathf.Min(cursor, select);
            length = cursor == select ? text.Length : Mathf.Abs(cursor - select);
        }
    }

    /// <summary>UI Toolkit 입력칸. 값을 바꾸면 바인딩 저장과 Undo 는 Unity 가 처리한다.</summary>
    internal sealed class UIToolkitTextTarget : RenameTarget
    {
        readonly TextInputBaseField<string> _field;
        readonly string _original;
        readonly int _start;
        readonly int _length;

        public UIToolkitTextTarget(TextInputBaseField<string> field)
        {
            _field = field;
            _original = field.text ?? string.Empty; // isDelayed 칸은 입력 중인 글이 아직 value 에 없다
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

            bool unchanged = (_field.text ?? string.Empty) == _original;
            if (!unchanged && _length != _original.Length)
                return "입력칸 내용이 바뀌어서 선택한 부분을 찾을 수 없습니다.";

            _field.value = unchanged
                ? _original.Substring(0, _start) + newName + _original.Substring(_start + _length)
                : newName;

            int caret = _start + newName.Length;
            _field.Focus();
            _field.SelectRange(caret, caret);
            return null;
        }
    }

    /// <summary>
    /// IMGUI 입력칸. 창이 포커스를 잃으면 Unity 가 편집을 끝내므로 팝업은 포커스 없이 뜨고(<see cref="SourceWindowToKeepFocused"/>),
    /// 값은 입력칸의 Paste 명령으로 넣는다. 클립보드는 잠깐 빌려 쓰고 되돌린다.
    /// </summary>
    internal sealed class ImguiTextTarget : RenameTarget
    {
        const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

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

        /// <summary>
        /// 편집 중인 편집기. 일반 칸과 Enter 로 확정하는 지연 칸(Animator 상태 이름 등)이 서로 다른 편집기를 써서
        /// activeEditor 를 먼저 본다.
        /// </summary>
        public static TextEditor GetActiveEditor()
        {
            object Read(string name) =>
                typeof(EditorGUI).GetField(name, StaticNonPublic)?.GetValue(null)
                ?? typeof(EditorGUI).GetProperty(name, StaticNonPublic)?.GetValue(null);

            return Read("activeEditor") as TextEditor ?? Read("s_RecycledEditor") as TextEditor;
        }

        bool IsDelayedField => _editor.GetType().Name == "DelayedTextEditor";

        public override string Name => _original.Substring(_start, _length);
        public override bool IsValid => _window != null;
        public override Object Context => null;
        public override bool IsName => false;
        public override string Kind => "텍스트";
        public override EditorWindow SourceWindowToKeepFocused => _window;

        public override string Rename(string newName)
        {
            if (newName == null)
                return null;
            if (_window == null)
                return "입력칸이 있던 창이 닫혔습니다.";

            // 후보를 마우스로 눌러 팝업이 포커스를 가져갔다면 칸은 이미 편집이 끝났다.
            if (EditorWindow.focusedWindow != _window)
                CopyToClipboard(newName);
            else
                EditorApplication.delayCall += () => Paste(newName);
            return null;
        }

        void Paste(string text)
        {
            if (_window == null)
                return;

            bool wholeText = _length == _original.Length;
            if (_editor.text != _original && !wholeText)
            {
                CopyToClipboard(text);
                return;
            }

            string expected = wholeText ? text : _original.Substring(0, _start) + text + _original.Substring(_start + _length);
            string clipboard = EditorGUIUtility.systemCopyBuffer;
            bool pasted;
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
                pasted = _editor.text == expected;

                // 지연 칸은 Enter 를 눌러야 값이 확정된다(한 줄 칸이라 줄바꿈 걱정은 없다).
                if (pasted && IsDelayedField)
                    _window.SendEvent(Event.KeyboardEvent("return"));
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = clipboard;
                _window.Repaint();
            }

            if (!pasted)
                CopyToClipboard(text);
        }

        static void CopyToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.LogWarning("[Quick Translate] 입력칸에 번역을 바로 넣지 못해 클립보드에 복사했습니다. 붙여넣어 주세요.");
        }
    }
}
