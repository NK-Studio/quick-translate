using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace QuickTranslate
{
    /// <summary>번역 팝업이 이름을 바꿀 대상 (Hierarchy 의 GameObject / Project 의 에셋).</summary>
    internal abstract class RenameTarget
    {
        public abstract string Name { get; }
        public abstract bool IsValid { get; }
        public abstract Object Context { get; }

        /// <summary>이름을 바꾼다. 실패하면 사유를, 성공하면 null 을 돌려준다.</summary>
        public abstract string Rename(string newName);
    }

    internal sealed class GameObjectTarget : RenameTarget
    {
        readonly GameObject _gameObject;

        public GameObjectTarget(GameObject gameObject) => _gameObject = gameObject;

        public override string Name => _gameObject.name;
        public override bool IsValid => _gameObject != null;
        public override Object Context => _gameObject;

        public override string Rename(string newName)
        {
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName) || newName == _gameObject.name)
                return null;

            Undo.RecordObject(_gameObject, "Translate GameObject Name");
            _gameObject.name = newName;
            return null;
        }
    }

    /// <summary>GUID 로 추적하므로 앞서 상위 폴더 이름이 바뀌어도 계속 유효하다. 에셋 이름 변경은 Undo 되지 않는다.</summary>
    internal sealed class AssetTarget : RenameTarget
    {
        static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .Distinct()
            .ToArray();

        readonly string _guid;

        public AssetTarget(string guid) => _guid = guid;

        string AssetPath => AssetDatabase.GUIDToAssetPath(_guid);

        public override string Name
        {
            get
            {
                string path = AssetPath;
                // 폴더는 "My.Folder" 처럼 점이 이름의 일부일 수 있다.
                return AssetDatabase.IsValidFolder(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            }
        }

        public override bool IsValid => !string.IsNullOrEmpty(AssetPath);
        public override Object Context => AssetDatabase.LoadMainAssetAtPath(AssetPath);

        public override string Rename(string newName)
        {
            newName = Sanitize(newName);
            if (string.IsNullOrEmpty(newName))
                return "파일 이름으로 쓸 수 있는 문자가 없습니다.";
            if (newName == Name)
                return null;

            string error = AssetDatabase.RenameAsset(AssetPath, newName);
            return string.IsNullOrEmpty(error) ? null : error;
        }

        /// <summary>파일 이름에 쓸 수 없는 문자를 빼고, 끝의 점/공백을 정리한다.</summary>
        static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var chars = name.Where(c => System.Array.IndexOf(InvalidChars, c) < 0).ToArray();
            return new string(chars).Trim().TrimEnd('.', ' ');
        }
    }
}
