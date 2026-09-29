using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>번역 후보 선택 팝업. 스타일은 QuickTranslate.uss.</summary>
    internal sealed class TranslationPopup : EditorWindow
    {
        const float MinWidth = 220; // 실제 폭은 .qt-panel 의 min/max-width 로 정해진다
        const float RowHeight = 24;
        const float InitialHeight = 104;
        const float HoverMoveThreshold = 2;

        Rect _anchor;
        bool _placeBelow;
        List<RenameTarget> _targets;
        int _targetIndex;

        IReadOnlyList<string> _candidates;
        int _selected;
        string _error;
        bool _loading = true; // Load 는 한 틱 뒤에 시작하므로 처음부터 "번역 중" 으로 그린다
        string _progress;
        string _renameError;
        bool _editing;

        CancellationTokenSource _cts;

        // UI
        VisualElement _panel;
        Label _directionChip;
        Label _kindChip;
        Label _counter;
        Image _sourceIcon;
        Label _source;
        Label _status;
        VisualElement _errorBox;
        HelpBox _errorHelp;
        Button _skipButton;
        VisualElement _list;
        readonly List<VisualElement> _rows = new List<VisualElement>();
        IReadOnlyList<string> _rowsBuiltFor;
        bool _hoverArmed;
        Vector2? _hoverOrigin;
        TextField _editField;
        Label _renameErrorLabel;
        VisualElement _footer;
        string _footerKeys;

        // IMGUI 입력칸이 대상이면 그 창이 포커스를 잃지 않도록 팝업을 포커스 없이 띄우고 키는 그 창에서 가로챈다.
        EditorWindow _keepFocusOn;
        VisualElement _hookedRoot;

        RenameTarget Current => _targets != null && _targetIndex < _targets.Count && _targets[_targetIndex].IsValid
            ? _targets[_targetIndex]
            : null;

        bool HasRemaining => _targets.Count - _targetIndex > 1;

        public static void Open(List<RenameTarget> targets, Rect anchorScreenRect)
        {
            var window = CreateInstance<TranslationPopup>();
            window._targets = targets;
            window._keepFocusOn = targets.Count > 0 ? targets[0].SourceWindowToKeepFocused : null;
            Show(window, anchorScreenRect, 100 + RowHeight * TranslatorSettings.MaxCandidates);
            // 요청은 첫 await 전까지 메인 스레드에서 돌므로 팝업을 먼저 그린 다음 틱에 시작한다.
            EditorApplication.delayCall += () =>
            {
                if (window != null)
                    window.Load();
            };
        }

        /// <param name="height">위/아래 배치를 정할 최대 높이. 창은 작게 열고 내용에 맞춰 키운다.</param>
        static void Show(TranslationPopup window, Rect anchorScreenRect, float height)
        {
            window._anchor = anchorScreenRect;
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            bool anchorInMain = main.Contains(anchorScreenRect.center);
            window._placeBelow = !anchorInMain || anchorScreenRect.yMax + height <= main.yMax ||
                                 anchorScreenRect.y - height < main.y;

            if (window._keepFocusOn != null && window.ShowWithoutFocus(anchorScreenRect))
                return;

            window._keepFocusOn = null;
            window.ShowAsDropDown(anchorScreenRect, new Vector2(MinWidth, InitialHeight));
        }

        bool ShowWithoutFocus(Rect anchor)
        {
            // EditorWindow.ShowPopupWithMode(ShowMode.PopupMenu, giveFocus: false) — 공개 API 가 없다.
            var showWithMode = typeof(EditorWindow).GetMethod("ShowPopupWithMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var modeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ShowMode");
            var sourcePanel = _keepFocusOn.rootVisualElement?.panel;
            if (showWithMode == null || modeType == null || sourcePanel == null)
                return false;

            float y = _placeBelow ? anchor.yMax : anchor.y - InitialHeight;
            position = new Rect(anchor.x, y, MinWidth, InitialHeight);
            try
            {
                showWithMode.Invoke(this, new[] { Enum.ToObject(modeType, 1), (object)false });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Quick Translate] 포커스 없이 팝업을 띄우지 못해 일반 팝업으로 엽니다: {e.GetBaseException().Message}");
                return false;
            }

            // 키 이벤트는 패널 루트에서 입력칸 쪽으로 내려가므로 TrickleDown 으로 입력칸보다 먼저 받는다.
            _hookedRoot = sourcePanel.visualTree;
            _hookedRoot.RegisterCallback<KeyDownEvent>(OnSourceKeyDown, TrickleDown.TrickleDown);
            EditorApplication.update += CloseWhenSourceLosesFocus;
            return true;
        }

        void OnDisable()
        {
            _cts?.Cancel();
            _hookedRoot?.UnregisterCallback<KeyDownEvent>(OnSourceKeyDown, TrickleDown.TrickleDown);
            EditorApplication.update -= CloseWhenSourceLosesFocus;
        }

        void CloseWhenSourceLosesFocus()
        {
            var focused = EditorWindow.focusedWindow;
            if (focused == this)
                return; // 후보를 마우스로 누른 경우, Apply 가 닫는다
            if (focused != _keepFocusOn || !EditorGUIUtility.editingTextField)
                Close();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            TranslatorStyles.Apply(root);
            root.AddToClassList("qt-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "qt-dark" : "qt-light");
            root.focusable = true;

            // 패널은 내용 크기에 맞춰 줄어들고, 그 크기를 창 크기로 쓴다(FitToContent).
            _panel = new VisualElement();
            _panel.AddToClassList("qt-panel");
            root.Add(_panel);

            var header = AddTo(_panel, new VisualElement(), "qt-header");
            var chips = AddTo(header, new VisualElement(), "qt-header__chips");
            _directionChip = AddTo(chips, new Label(), "qt-chip", "qt-chip--direction");
            _kindChip = AddTo(chips, new Label(), "qt-chip");
            AddTo(chips, new VisualElement(), "qt-spacer");
            _counter = AddTo(chips, new Label(), "qt-counter");

            var sourceRow = AddTo(header, new VisualElement(), "qt-source-row");
            _sourceIcon = AddTo(sourceRow, new Image { scaleMode = ScaleMode.ScaleToFit }, "qt-source-icon");
            _source = AddTo(sourceRow, new Label(), "qt-source");

            var body = AddTo(_panel, new VisualElement(), "qt-body");
            _status = AddTo(body, new Label(), "qt-status");

            _errorBox = AddTo(body, new VisualElement(), "qt-error");
            _errorHelp = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            _errorBox.Add(_errorHelp);
            var errorButtons = AddTo(_errorBox, new VisualElement(), "qt-error-buttons");
            errorButtons.Add(new Button(() =>
            {
                SettingsService.OpenUserPreferences(TranslatorSettings.PreferencesPath);
                Close();
            }) { text = "설정 열기" });
            errorButtons.Add(new Button(Load) { text = "다시 시도" });
            _skipButton = new Button(Advance) { text = "건너뛰기" };
            errorButtons.Add(_skipButton);

            _list = AddTo(body, new VisualElement(), "qt-list");
            _editField = AddTo(body, new TextField(), "qt-edit");
            _renameErrorLabel = AddTo(body, new Label(), "qt-rename-error");

            _footer = AddTo(_panel, new VisualElement(), "qt-footer");

            // 편집용 TextField 보다 먼저 Enter/Esc/Tab/방향키를 받는다.
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                evt.StopPropagation();
                root.focusController?.IgnoreEvent(evt);
            }, TrickleDown.TrickleDown);
            _panel.RegisterCallback<GeometryChangedEvent>(_ => FitToContent());

            Refresh();
            if (_keepFocusOn == null)
                root.schedule.Execute(() => root.Focus());
        }

        static T AddTo<T>(VisualElement parent, T element, params string[] classNames) where T : VisualElement
        {
            foreach (string className in classNames)
                element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        void RebuildFooter()
        {
            var keys = _keepFocusOn != null ? new[] { ("↑↓", "이동"), ("Enter", "적용"), ("Esc", "닫기") }
                : _editing ? new[] { ("Enter", "적용"), ("Esc", "취소") }
                : HasRemaining ? new[] { ("↑↓", "이동"), ("Enter", "적용"), ("Tab", "수정"), ("⇧Enter", "전체"), ("Esc", "닫기") }
                : new[] { ("↑↓", "이동"), ("Enter", "적용"), ("Tab", "수정"), ("Esc", "닫기") };

            string signature = string.Join("|", keys);
            if (signature == _footerKeys)
                return;
            _footerKeys = signature;

            _footer.Clear();
            foreach (var (key, description) in keys)
            {
                var item = AddTo(_footer, new VisualElement(), "qt-footer__item");
                AddTo(item, new Label(key), "qt-key");
                AddTo(item, new Label(description), "qt-key-desc");
            }
        }

        async void Load()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _candidates = null;
            _error = null;
            _renameError = null;
            _selected = 0;
            _editing = false;
            _loading = true;
            Refresh();

            if (_targetIndex + 1 < _targets.Count && _targets[_targetIndex + 1].IsValid)
                NameTranslator.Prefetch(_targets[_targetIndex + 1].Name, _targets[_targetIndex + 1].IsName);

            try
            {
                var candidates = await NameTranslator.GetCandidatesAsync(Current.Name, token, Current.IsName);
                if (this == null || token.IsCancellationRequested)
                    return;
                _candidates = candidates;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                if (this == null)
                    return;
                _error = e is TranslationException ? e.Message : e.GetType().Name + ": " + e.Message;
                if (!(e is TranslationException))
                    Debug.LogException(e);
            }

            _loading = false;
            Refresh();
        }

        void Apply(string newName)
        {
            // 실패하면(예: 같은 이름의 에셋이 있음) 넘어가지 않고 다른 후보를 고르게 한다.
            _renameError = Current.Rename(newName);
            if (_renameError != null)
            {
                Refresh();
                return;
            }

            Advance();
        }

        void Advance()
        {
            do _targetIndex++;
            while (_targetIndex < _targets.Count && !_targets[_targetIndex].IsValid);

            if (_targetIndex >= _targets.Count)
                Close();
            else
                Load();
        }

        /// <summary>현재 선택을 적용하고 나머지는 1순위 후보로 적용한다.</summary>
        async void ApplyRemainingWithBest(string currentName)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            string firstError = Current.Rename(currentName);
            if (firstError != null)
                Debug.LogWarning($"[Quick Translate] '{Current.Name}' 이름 변경 실패: {firstError}", Current.Context);

            _loading = true;
            _editing = false;
            _candidates = null;
            for (int i = _targetIndex + 1; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (!target.IsValid)
                    continue;

                _progress = $"일괄 적용 중… {i + 1}/{_targets.Count}";
                Refresh();
                try
                {
                    var candidates = await NameTranslator.GetCandidatesAsync(target.Name, token, target.IsName);
                    if (this == null || token.IsCancellationRequested)
                        return;
                    string error = target.Rename(candidates[0]);
                    if (error != null)
                        Debug.LogWarning($"[Quick Translate] '{target.Name}' 이름 변경 실패: {error}", target.Context);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (this == null)
                        return;
                    Debug.LogWarning($"[Quick Translate] '{target.Name}' 번역 실패: {e.Message}", target.Context);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Close();
        }

        void Refresh()
        {
            if (_panel == null || Current == null)
                return;

            bool toKorean = NameTranslator.DetectDirection(Current.Name) == TranslationDirection.EnglishToKorean;
            _directionChip.text = toKorean ? "EN → KO" : "KO → EN";
            SetVisible(_kindChip, Current.Kind != null);
            _kindChip.text = Current.Kind ?? string.Empty;
            SetVisible(_counter, _targets.Count > 1);
            _counter.text = $"{_targetIndex + 1} / {_targets.Count}";
            _source.text = SingleLine(Current.Name);
            _sourceIcon.image = Current.Context != null ? AssetPreview.GetMiniThumbnail(Current.Context) : null;
            SetVisible(_sourceIcon, _sourceIcon.image != null);

            bool showError = !_loading && _error != null;
            bool showList = !_loading && _error == null && _candidates != null;

            SetVisible(_status, _loading);
            _status.text = _progress ?? $"{TranslationEngines.Current.DisplayName} 로 번역 중…";

            SetVisible(_errorBox, showError);
            _errorHelp.text = _error ?? string.Empty;
            SetVisible(_skipButton, HasRemaining);

            SetVisible(_list, showList);
            if (showList && !ReferenceEquals(_rowsBuiltFor, _candidates))
                RebuildRows();
            UpdateSelection();

            SetVisible(_editField, showList && _editing);
            SetVisible(_renameErrorLabel, _renameError != null);
            _renameErrorLabel.text = _renameError ?? string.Empty;
            RebuildFooter();

            FitToContent();
        }

        void RebuildRows()
        {
            _list.Clear();
            _rows.Clear();
            _rowsBuiltFor = _candidates;

            // 직전에 클릭한 자리에 마우스가 그대로 있으면 새 행 위에 놓이므로, 실제로 움직였을 때만 hover 를 받는다.
            _hoverArmed = false;
            _hoverOrigin = null;

            for (int i = 0; i < _candidates.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("qt-row");

                var text = new Label(SingleLine(_candidates[i]));
                text.AddToClassList("qt-row__text");
                row.Add(text);

                if (i == 0)
                {
                    var badge = new Label("BEST");
                    badge.AddToClassList("qt-row__badge");
                    row.Add(badge);
                }

                row.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (_editing || !IsHoverArmed(evt.position) || _selected == index)
                        return;
                    _selected = index;
                    UpdateSelection();
                });
                row.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                        return;
                    evt.StopPropagation();
                    Apply(_candidates[index]);
                });

                _rows.Add(row);
                _list.Add(row);
            }
        }

        bool IsHoverArmed(Vector2 pointer)
        {
            if (_hoverArmed)
                return true;
            if (_hoverOrigin == null)
            {
                _hoverOrigin = pointer;
                return false;
            }

            _hoverArmed = (pointer - _hoverOrigin.Value).sqrMagnitude > HoverMoveThreshold * HoverMoveThreshold;
            return _hoverArmed;
        }

        void UpdateSelection()
        {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].EnableInClassList("qt-row--selected", i == _selected);
        }

        void OnKeyDown(KeyDownEvent evt) => HandleKey(evt, rootVisualElement);

        void OnSourceKeyDown(KeyDownEvent evt) => HandleKey(evt, _hookedRoot);

        void HandleKey(KeyDownEvent evt, VisualElement owner)
        {
            void Consume()
            {
                evt.StopImmediatePropagation();
                owner?.panel?.focusController?.IgnoreEvent(evt);
            }

            // Enter/Tab 은 keyCode 이벤트와 문자 이벤트가 따로 오므로 문자 쪽도 삼킨다.
            if (evt.keyCode == KeyCode.None && evt.character is '\n' or '\r' or '\t')
            {
                Consume();
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Consume();
                    if (_editing) ExitEditing();
                    else Close();
                    return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Consume();
                    string chosen = _editing ? _editField.value : SelectedCandidate();
                    if (chosen == null)
                        return;
                    if (evt.shiftKey && HasRemaining) ApplyRemainingWithBest(chosen);
                    else Apply(chosen);
                    return;

                case KeyCode.Tab:
                    Consume();
                    if (_keepFocusOn == null && !_editing && SelectedCandidate() != null)
                        EnterEditing();
                    return;

                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    if (_editing)
                        return; // 편집 칸의 커서 이동
                    Consume();
                    if (_candidates == null || _loading || _candidates.Count == 0)
                        return;
                    int step = evt.keyCode == KeyCode.UpArrow ? -1 : 1;
                    _selected = (_selected + step + _candidates.Count) % _candidates.Count;
                    UpdateSelection();
                    return;
            }
        }

        void EnterEditing()
        {
            _editing = true;
            _editField.SetValueWithoutNotify(SelectedCandidate());
            Refresh();
            _editField.schedule.Execute(() =>
            {
                _editField.Focus();
                _editField.SelectAll();
            });
        }

        void ExitEditing()
        {
            _editing = false;
            Refresh();
            rootVisualElement.Focus();
        }

        string SelectedCandidate() =>
            _candidates != null && _selected < _candidates.Count ? _candidates[_selected] : null;

        /// <summary>여러 줄 텍스트를 한 줄로 보여준다(적용은 원문 그대로).</summary>
        static string SingleLine(string text) =>
            text == null ? string.Empty : text.Trim().Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ");

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// 패널의 실제 크기를 창 크기로 쓴다. 위치는 _anchor 에서 계산한다
        /// (드롭다운이 자리 잡기 전에는 position 이 (0,0) 근처로 읽힐 수 있다).
        /// </summary>
        void FitToContent()
        {
            if (_panel == null)
                return;

            Vector2 size = _panel.layout.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || size.x <= 0 || size.y <= 0)
                return;

            float width = Mathf.Ceil(size.x) + 2;  // 테두리
            float height = Mathf.Ceil(size.y) + 2;
            float x = _anchor.x;
            float y = _placeBelow ? _anchor.yMax : _anchor.y - height;
            Rect rect = position;
            if (Mathf.Abs(rect.x - x) < 0.5f && Mathf.Abs(rect.y - y) < 0.5f &&
                Mathf.Abs(rect.height - height) < 0.5f && Mathf.Abs(rect.width - width) < 0.5f)
                return;

            maxSize = new Vector2(4000, 4000);
            minSize = new Vector2(width, height);
            maxSize = new Vector2(width, height);
            position = new Rect(x, y, width, height);
        }
    }
}
