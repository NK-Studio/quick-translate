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

                var imguiTarget = new ImguiTextTarget(window, container, editor);
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
        readonly IMGUIContainer _container;
        readonly TextEditor _editor;
        readonly string _original;
        readonly int _start;
        readonly int _length;

        // 팝업이 포커스를 가져가면 Unity 가 칸의 편집 상태를 끝내므로, 되살릴 때 쓸 값을 캡처 시점에 기억한다.
        readonly int _controlId;
        readonly Rect _position;
        readonly GUIStyle _style;
        readonly bool _multiline;

        public ImguiTextTarget(EditorWindow window, IMGUIContainer container, TextEditor editor)
        {
            _window = window;
            _container = container;
            _editor = editor;
            _original = editor.text ?? string.Empty;
            _controlId = editor.controlID;
            _position = editor.position;
            _style = editor.style;
            _multiline = editor.isMultiline;
            TextFieldCapture.GetRange(_original, editor.cursorIndex, editor.selectIndex, out _start, out _length);
        }

        /// <summary>
        /// 편집 중인 IMGUI 입력칸의 편집기. 일반 칸(s_RecycledEditor)과 Enter 로 확정하는 지연 칸(s_DelayedTextEditor,
        /// Animator 상태 이름 등)이 서로 다른 편집기를 쓰므로, 지금 편집 중인 쪽을 가리키는 activeEditor 를 먼저 본다.
        /// </summary>
        public static TextEditor GetActiveEditor()
        {
            var type = typeof(EditorGUI);
            object Read(string name) =>
                type.GetField(name, StaticNonPublic)?.GetValue(null) ?? type.GetProperty(name, StaticNonPublic)?.GetValue(null);

            if (Read("activeEditor") is TextEditor active)
                return active;

            // activeEditor 가 없는 버전 대비: 키보드 포커스를 가진 편집기를 고른다.
            foreach (string name in new[] { "s_DelayedTextEditor", "s_DelayedTextEditorInternal", "s_RecycledEditor", "s_RecycledEditorInternal" })
                if (Read(name) is TextEditor editor && editor.controlID != 0 && editor.controlID == GUIUtility.keyboardControl)
                    return editor;

            return Read("s_RecycledEditor") as TextEditor ?? Read("s_RecycledEditorInternal") as TextEditor;
        }

        bool IsStillEditing() =>
            EditorGUIUtility.editingTextField && GetActiveEditor() == _editor &&
            _editor.controlID == _controlId && GUIUtility.keyboardControl == _controlId;

        /// <summary>
        /// 팝업 때문에 끝난 편집 상태를 되살린다. Unity 의 편집 판정(IsEditingControl)은
        /// 창 포커스 + keyboardControl == 칸 번호 + 편집기의 controlID + 편집 중 표시(s_ActuallyEditing) 이므로,
        /// 키보드 포커스를 칸 번호로 돌려놓고 편집기의 내부 BeginEditing 으로 편집을 다시 시작한다.
        /// keyboardControl 은 OnGUI 안에서만 바꿀 수 있으므로 반드시 <see cref="RunInsideOnGui"/> 안에서 부른다.
        /// </summary>
        void RestoreEditing()
        {
            if (_controlId == 0)
                return;

            var begin = _editor.GetType().GetMethod("BeginEditing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(int), typeof(string), typeof(Rect), typeof(GUIStyle), typeof(bool), typeof(bool) }, null);
            if (begin == null)
                return;

            try
            {
                GUIUtility.keyboardControl = _controlId;
                begin.Invoke(_editor, new object[] { _controlId, _original, _position, _style, _multiline, false });
                EditorGUIUtility.editingTextField = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Quick Translate] 입력칸 편집 상태를 되살리지 못했습니다: {e.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// window 의 OnGUI 안에서 action 을 한 번 실행한다(keyboardControl 은 OnGUI 안에서만 바꿀 수 있다).
        /// 1x1 IMGUIContainer 를 잠깐 넣어 다음 그리기 때 실행하고 뺀다. 창이 다시 그려지지 않으면(30 프레임) action 없이 then 을 부른다.
        /// </summary>
        void RunInsideOnGui(Action action, Action then)
        {
            bool done = false;
            int framesLeft = 30;
            var probe = new IMGUIContainer { pickingMode = PickingMode.Ignore, focusable = false };
            probe.style.position = Position.Absolute;
            probe.style.width = 1;
            probe.style.height = 1;

            void Finish()
            {
                if (done)
                    return;
                done = true;
                EditorApplication.update -= Timeout;
                EditorApplication.delayCall += () =>
                {
                    probe.RemoveFromHierarchy();
                    then();
                };
            }

            void Timeout()
            {
                if (--framesLeft <= 0)
                    Finish();
            }

            probe.onGUIHandler = () =>
            {
                if (done)
                    return;
                action();
                Finish();
            };

            EditorApplication.update += Timeout;
            _window.rootVisualElement.Add(probe);
            probe.MarkDirtyRepaint();
            _window.Repaint();
        }

        /// <summary>Enter(또는 포커스 해제)로 값이 확정되는 지연 칸인지.</summary>
        bool IsDelayedField => _editor.GetType().Name == "DelayedTextEditor";

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

            // 팝업이 닫히고 원래 창·입력칸으로 포커스를 돌려놓은 다음, 그 창의 OnGUI 안에서 편집 상태를 되살리고 붙여넣는다.
            _window.Focus();
            if (_container != null && _container.panel != null)
                _container.Focus();

            RunInsideOnGui(() =>
            {
                if (!IsStillEditing())
                    RestoreEditing();
            }, () => Paste(newName));
            return null;
        }

        void Paste(string text)
        {
            if (_window == null)
                return;

            bool wholeText = _length == _original.Length;
            string current = _editor.text ?? string.Empty;
            if (!IsStillEditing() || (current != _original && !wholeText))
            {
                FallBackToClipboard(text);
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

                // 지연 칸은 붙여넣기만으로는 값이 확정되지 않으므로 Enter 를 보낸다.
                // (여러 줄 칸에서는 Enter 가 줄바꿈이라 보내지 않는다. 지연 칸은 한 줄 칸이다.)
                if (pasted && IsDelayedField)
                    _window.SendEvent(Event.KeyboardEvent("return"));
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = clipboard;
                _window.Repaint();
            }

            if (!pasted)
                FallBackToClipboard(text);
        }

        static void FallBackToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.LogWarning("[Quick Translate] 입력칸에 번역을 바로 넣지 못했습니다. 번역 결과를 클립보드에 복사했으니 붙여넣어 주세요.");
        }
    }
}
