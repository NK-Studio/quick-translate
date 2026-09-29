using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace QuickTranslate
{
    /// <summary>UI Toolkit 공용: 스타일시트 로드와 설정 화면 구성 요소.</summary>
    internal static class TranslatorStyles
    {
        const string StyleSheetName = "QuickTranslate";
        const string PackageStyleSheetPath = "Packages/com.nkstudio.quick-translate/Editor/UI/QuickTranslate.uss";

        static StyleSheet _styleSheet;

        /// <summary>패키지 경로에서 찾고, 없으면(Assets 에 복사해 쓰는 경우) 이름으로 검색한다.</summary>
        public static StyleSheet StyleSheet
        {
            get
            {
                if (_styleSheet == null)
                    _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageStyleSheetPath);

                if (_styleSheet == null)
                {
                    string path = AssetDatabase.FindAssets($"{StyleSheetName} t:StyleSheet")
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .FirstOrDefault(p => p.EndsWith($"/{StyleSheetName}.uss"));
                    if (path != null)
                        _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }

                return _styleSheet;
            }
        }

        public static void Apply(VisualElement element)
        {
            if (StyleSheet != null)
                element.styleSheets.Add(StyleSheet);
        }

        public static VisualElement CreateSettingsPage(VisualElement root, string title)
        {
            Apply(root);
            var page = new ScrollView();
            page.AddToClassList("qt-settings");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("qt-settings-title");
            page.Add(titleLabel);
            root.Add(page);
            return page;
        }

        public static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("qt-section-title");
            return label;
        }

        public static Label Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList("qt-note");
            return label;
        }

        /// <summary>Inspector 처럼 라벨 폭을 맞춘다.</summary>
        public static T Aligned<T>(T field) where T : VisualElement
        {
            field.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            return field;
        }
    }
}
