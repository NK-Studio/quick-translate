using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>
    /// 번역 후보 선택 팝업 (UI Toolkit). 스타일은 QuickTranslate.uss.
    /// ↑↓ 이동 · Enter 적용 · Tab 직접 수정 · Shift+Enter 남은 항목 모두 1순위 적용 · Esc 닫기
    /// </summary>
    internal sealed class TranslationPopup : EditorWindow
    {
        const float MinWidth = 220; // 실제 최소/최대 폭은 .qt-panel 의 min-width / max-width
        const float RowHeight = 24;
        const float InitialHeight = 104; // 머리 + "번역 중" + 바닥 안내 정도
        const float HoverMoveThreshold = 2; // 이만큼 움직여야 hover 로 선택이 바뀐다

        Rect _anchor;
        bool _placeBelow;
        List<RenameTarget> _targets;
        int _targetIndex;

        IReadOnlyList<string> _candidates;
        int _selected;
        string _error;
        bool _loading = true; // 첫 프레임부터 "번역 중" 상태로 그린다(Load 는 한 틱 뒤에 시작).
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
        bool? _footerBuiltForEditing;
        bool _footerBuiltWithBatch;

        RenameTarget Current => _targets != null && _targetIndex < _targets.Count && _targets[_targetIndex].IsValid
            ? _targets[_targetIndex]
            : null;

        bool HasRemaining => _targets.Count - _targetIndex > 1;

        public static void Open(List<RenameTarget> targets, Rect anchorScreenRect)
        {
            var window = CreateInstance<TranslationPopup>();
            window._targets = targets;
            Show(window, anchorScreenRect, 100 + RowHeight * TranslatorSettings.MaxCandidates);
            // 번역 요청은 첫 await 전까지(설정 읽기·요청 생성·전송) 메인 스레드에서 동기로 돈다.
            // 여기서 바로 시작하면 그만큼 팝업이 늦게 그려지므로, 팝업을 먼저 그린 다음 틱에 시작한다.
            EditorApplication.delayCall += () =>
            {
                if (window != null)
                    window.Load();
            };
        }

        /// <param name="height">위/아래 배치를 정할 때 쓰는 최대 높이. 창은 작게 열고 내용에 맞춰 키운다.</param>
        static void Show(TranslationPopup window, Rect anchorScreenRect, float height)
        {
            window._anchor = anchorScreenRect;
            // 위/아래 배치는 최대 크기 기준으로 한 번만 정하고, 이후 내용에 맞게 높이만 줄인다.
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            bool anchorInMain = main.Contains(anchorScreenRect.center);
            window._placeBelow = !anchorInMain || anchorScreenRect.yMax + height <= main.yMax ||
                                 anchorScreenRect.y - height < main.y;
            // 처음부터 "번역 중" 크기로 열어, 큰 빈 창이 떴다가 줄어드는 깜빡임을 없앤다.
            window.ShowAsDropDown(anchorScreenRect, new Vector2(MinWidth, InitialHeight));
        }

        void OnDisable() => _cts?.Cancel();

        void CreateGUI()
        {
            var root = rootVisualElement;
            TranslatorStyles.Apply(root);
            root.AddToClassList("qt-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "qt-dark" : "qt-light");
            root.focusable = true;

            // 패널은 내용 크기에 맞춰 줄어들고(절대 배치), 그 크기를 창 크기로 쓴다.
            _panel = new VisualElement();
            _panel.AddToClassList("qt-panel");
            root.Add(_panel);

            // ── 머리: 방향/종류 칩, 진행 수, 원문 ──
            var header = AddTo(_panel, new VisualElement(), "qt-header");
            var chips = AddTo(header, new VisualElement(), "qt-header__chips");
            _directionChip = AddTo(chips, new Label(), "qt-chip", "qt-chip--direction");
            _kindChip = AddTo(chips, new Label(), "qt-chip");
            AddTo(chips, new VisualElement(), "qt-spacer");
            _counter = AddTo(chips, new Label(), "qt-counter");

            var sourceRow = AddTo(header, new VisualElement(), "qt-source-row");
            _sourceIcon = AddTo(sourceRow, new Image { scaleMode = ScaleMode.ScaleToFit }, "qt-source-icon");
            _source = AddTo(sourceRow, new Label(), "qt-source");

            // ── 본문: 상태/오류/후보/편집 ──
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

            // ── 바닥: 단축키 안내 ──
            _footer = AddTo(_panel, new VisualElement(), "qt-footer");

            // 자식(TextField 포함)보다 먼저 받아서 Enter/Esc/Tab/방향키를 가로챈다.
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                evt.StopPropagation();
                root.focusController?.IgnoreEvent(evt);
            }, TrickleDown.TrickleDown);
            _panel.RegisterCallback<GeometryChangedEvent>(_ => FitToContent());

            Refresh();
            root.schedule.Execute(() => root.Focus());
        }

        static T AddTo<T>(VisualElement parent, T element, params string[] classNames) where T : VisualElement
        {
            foreach (string className in classNames)
                element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        /// <summary>단축키 안내를 키캡 모양으로 다시 만든다(모드가 바뀔 때만).</summary>
        void RebuildFooter()
        {
            bool batch = HasRemaining;
            if (_footerBuiltForEditing == _editing && _footerBuiltWithBatch == batch)
                return;
            _footerBuiltForEditing = _editing;
            _footerBuiltWithBatch = batch;

            _footer.Clear();
            var keys = _editing
                ? new[] { ("Enter", "적용"), ("Esc", "취소") }
                : batch
                    ? new[] { ("↑↓", "이동"), ("Enter", "적용"), ("Tab", "수정"), ("⇧Enter", "전체"), ("Esc", "닫기") }
                    : new[] { ("↑↓", "이동"), ("Enter", "적용"), ("Tab", "수정"), ("Esc", "닫기") };

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

            // 다음 항목도 미리 번역해 둔다.
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
            // 에셋은 같은 이름이 이미 있으면 실패한다. 이때는 다음으로 넘어가지 않고 다른 후보를 고르게 한다.
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

        /// <summary>현재 선택을 적용하고 나머지는 1순위 후보로 일괄 적용한다.</summary>
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

        /// <summary>현재 상태를 요소 표시/텍스트/클래스에 반영한다.</summary>
        void Refresh()
        {
            if (_panel == null)
                return;

            if (Current == null)
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

            // 이전 팝업/항목에서 클릭한 자리에 마우스가 그대로 있으면 새 행 위에 놓여 hover 로 선택이 바뀐다.
            // 항목이 새로 그려진 뒤 마우스를 실제로 움직였을 때만 hover 선택을 받는다.
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

        void OnKeyDown(KeyDownEvent evt)
        {
            // 한 번의 키 입력에 keyCode 이벤트와 문자 이벤트가 따로 올 수 있어 문자 쪽도 함께 삼킨다.
            if (evt.keyCode == KeyCode.None && (evt.character == '\n' || evt.character == '\r' || evt.character == '\t'))
            {
                Consume(evt);
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    if (_editing) ExitEditing();
                    else Close();
                    Consume(evt);
                    return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    string chosen = _editing ? _editField.value : SelectedCandidate();
                    Consume(evt);
                    if (chosen == null)
                        return;
                    if (evt.shiftKey && HasRemaining) ApplyRemainingWithBest(chosen);
                    else Apply(chosen);
                    return;

                case KeyCode.Tab:
                    if (!_editing && SelectedCandidate() != null)
                        EnterEditing();
                    Consume(evt);
                    return;
            }

            // 편집 중에는 방향키를 TextField 로 넘긴다.
            if (_editing || _candidates == null || _loading || _candidates.Count == 0)
                return;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    _selected = (_selected - 1 + _candidates.Count) % _candidates.Count;
                    UpdateSelection();
                    Consume(evt);
                    return;
                case KeyCode.DownArrow:
                    _selected = (_selected + 1) % _candidates.Count;
                    UpdateSelection();
                    Consume(evt);
                    return;
            }
        }

        void Consume(EventBase evt)
        {
            evt.StopImmediatePropagation();
            rootVisualElement.focusController?.IgnoreEvent(evt);
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

        /// <summary>여러 줄 텍스트를 한 줄로 보여준다(적용할 때는 원문 그대로).</summary>
        static string SingleLine(string text) =>
            text == null ? string.Empty : text.Trim().Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ");

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// 패널(내용 크기에 맞춰 줄어드는 절대 배치)의 실제 레이아웃 크기를 창 크기로 쓴다.
        /// 위치는 창의 현재 position 이 아니라 기준 행(_anchor)에서 계산한다:
        /// 드롭다운이 자리 잡기 전에는 position 이 (0,0) 근처로 읽힐 수 있기 때문.
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
