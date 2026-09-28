using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>
    /// 팀 공용 용어집. ProjectSettings 에 저장되어 VCS 로 공유된다.
    /// 전체 이름 또는 단어가 일치하면 번역 결과보다 우선한다. (예: 체력 ↔ HP)
    /// 영→한 에서는 영어 쪽을 대소문자·공백·밑줄 구분 없이 역으로 찾는다.
    /// </summary>
    [FilePath("ProjectSettings/QuickTranslateGlossary.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TranslatorGlossary : ScriptableSingleton<TranslatorGlossary>
    {
        [Serializable]
        public class Entry
        {
            public string korean = string.Empty;
            public string english = string.Empty;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, string> _lookup;
        Dictionary<string, string> _reverseLookup;

        /// <summary>AI 엔진 프롬프트에 넣을 (한국어, 영어) 쌍.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Pairs =>
            entries.Select(e => new KeyValuePair<string, string>(e.korean?.Trim() ?? string.Empty, e.english?.Trim() ?? string.Empty))
                .ToList();

        public bool TryGet(string korean, out string english)
        {
            BuildLookups();
            return _lookup.TryGetValue(Normalize(korean), out english);
        }

        public bool TryGetKorean(string english, out string korean)
        {
            BuildLookups();
            return _reverseLookup.TryGetValue(Normalize(english), out korean);
        }

        void BuildLookups()
        {
            if (_lookup != null)
                return;

            _lookup = new Dictionary<string, string>();
            _reverseLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string korean = Normalize(entry.korean);
                string english = Normalize(entry.english);
                if (korean.Length == 0 || english.Length == 0)
                    continue;

                _lookup[korean] = entry.english.Trim();
                if (!_reverseLookup.ContainsKey(english)) // 같은 영어가 여러 번 있으면 먼저 적힌 항목 우선
                    _reverseLookup[english] = entry.korean.Trim();
            }
        }

        /// <summary>공백·밑줄 차이("체력 바" / "체력바", "Health Bar" / "Health_Bar")는 같은 항목으로 취급한다.</summary>
        static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsWhiteSpace(c) && c != '_')
                    sb.Append(c);
            return sb.ToString();
        }

        void SaveAndInvalidate()
        {
            _lookup = null;
            Save(true);
            NameTranslator.ClearCache();
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Quick Translate", SettingsScope.Project)
            {
                label = "Quick Translate",
                activateHandler = (_, root) => instance.BuildUI(root),
                keywords = new[] { "Glossary", "용어집", "번역" }
            };
        }

        void BuildUI(VisualElement root)
        {
            var page = TranslatorStyles.CreateSettingsPage(root, "Quick Translate");
            page.Add(TranslatorStyles.SectionTitle("용어집"));
            page.Add(TranslatorStyles.Note(
                "ProjectSettings/QuickTranslateGlossary.asset 에 저장되어 팀과 공유됩니다. " +
                "전체 이름 또는 단어가 일치하면 번역 결과보다 먼저 후보로 올라오고, 한→영 / 영→한 모두에 쓰입니다.\n" +
                "셀은 Enter 또는 다른 곳을 클릭하면 저장됩니다. 행을 끌어서 순서를 바꿀 수 있습니다(같은 영어가 여러 번이면 위쪽 우선)."));

            var list = new MultiColumnListView
            {
                itemsSource = entries,
                fixedItemHeight = 22,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                showAddRemoveFooter = true,
                showBorder = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.Multiple
            };
            list.AddToClassList("qt-glossary");
            list.columns.Add(CreateColumn("korean", "한국어", e => e.korean, (e, v) => e.korean = v));
            list.columns.Add(CreateColumn("english", "영어", e => e.english, (e, v) => e.english = v));

            list.onAdd = view =>
            {
                entries.Add(new Entry());
                SaveAndInvalidate();
                view.RefreshItems();
                view.ScrollToItem(entries.Count - 1);
            };
            list.onRemove = view =>
            {
                var indices = view.selectedIndices.Any()
                    ? view.selectedIndices.OrderByDescending(i => i).ToList()
                    : new List<int> { entries.Count - 1 };
                foreach (int index in indices.Where(i => i >= 0 && i < entries.Count))
                    entries.RemoveAt(index);
                view.ClearSelection();
                SaveAndInvalidate();
                view.RefreshItems();
            };
            list.itemIndexChanged += (_, _) => SaveAndInvalidate();

            page.Add(list);
            page.Add(TranslatorStyles.Note("API 키 등 개인 설정은 Preferences > Quick Translate 에 있습니다."));
        }

        Column CreateColumn(string name, string title, Func<Entry, string> getter, Action<Entry, string> setter)
        {
            return new Column
            {
                name = name,
                title = title,
                stretchable = true,
                minWidth = 80,
                makeCell = () =>
                {
                    // 매 글자마다 파일에 쓰지 않도록 확정(Enter/포커스 해제) 시점에만 값을 받는다.
                    var field = new TextField { isDelayed = true };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (field.userData is int index && index < entries.Count)
                        {
                            setter(entries[index], evt.newValue);
                            SaveAndInvalidate();
                        }
                    });
                    return field;
                },
                bindCell = (element, index) =>
                {
                    var field = (TextField)element;
                    field.userData = index;
                    field.SetValueWithoutNotify(getter(entries[index]));
                }
            };
        }
    }
}
