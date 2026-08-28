using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RosettaUI;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public class SaveManager : MonoBehaviour, ISaveManager, IElementCreator
    {
        [SerializeField] private string _saveDataName = "SaveData.json";

        private List<ISaveTarget> _saveTargets = new();

        [Inject]
        public void Construct(IEnumerable<ISaveTarget> saveTargets)
        {
            _saveTargets = saveTargets.ToList();
        }

        private void Awake()
        {
            Load();
        }

        public void Save(string path = null)
        {
            path ??= GetDefaultPath();
            SaveData saveData = new();

            foreach (ISaveTarget target in _saveTargets)
            {
                if (target?.Params == null)
                {
                    Debug.LogWarning("Save target or its Params was null and has been skipped.", this);
                    continue;
                }

                saveData.Entries.Add(new SaveEntry
                {
                    TargetType = GetTypeId(target.GetType()),
                    GameObjectName = GetGameObjectName(target),
                    ParamsType = GetTypeId(target.Params.GetType()),
                    Json = JsonUtility.ToJson(target.Params)
                });
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonUtility.ToJson(saveData, true));
        }

        public void Load(string path = null)
        {
            path ??= GetDefaultPath();
            if (!File.Exists(path)) return;

            SaveData saveData;
            try
            {
                saveData = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to read save data from '{path}'.\n{exception}", this);
                return;
            }

            if (saveData?.Entries == null) return;

            // 型と GameObject 名の組み合わせで対象を識別する。同名の場合のみ保存時の列挙順を使う。
            Dictionary<string, Queue<SaveEntry>> entriesByTarget = saveData.Entries
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.TargetType))
                .GroupBy(entry => GetTargetId(entry.TargetType, entry.GameObjectName))
                .ToDictionary(group => group.Key, group => new Queue<SaveEntry>(group));

            foreach (ISaveTarget target in _saveTargets)
            {
                if (target?.Params == null) continue;

                string targetType = GetTypeId(target.GetType());
                string gameObjectName = GetGameObjectName(target);
                string targetId = GetTargetId(targetType, gameObjectName);
                if (!TryGetEntry(entriesByTarget, targetId, out SaveEntry entry) &&
                    !TryGetEntry(entriesByTarget, GetTargetId(targetType, string.Empty), out entry)) continue;

                string paramsType = GetTypeId(target.Params.GetType());
                if (entry.ParamsType != paramsType)
                {
                    Debug.LogWarning(
                        $"Saved parameter type '{entry.ParamsType}' does not match '{paramsType}' for '{targetType}'.",
                        this);
                    continue;
                }

                try
                {
                    JsonUtility.FromJsonOverwrite(entry.Json, target.Params);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to load parameters for '{targetType}'.\n{exception}", this);
                }
            }
        }

        private string GetDefaultPath() => Path.Combine(Application.persistentDataPath, _saveDataName);

        private static string GetTypeId(Type type) => type.FullName ?? type.Name;

        private static string GetGameObjectName(ISaveTarget target) =>
            target is Component component ? component.gameObject.name : string.Empty;

        private static string GetTargetId(string targetType, string gameObjectName) =>
            $"{targetType}\n{gameObjectName}";

        private static bool TryGetEntry(
            IReadOnlyDictionary<string, Queue<SaveEntry>> entriesByTarget,
            string targetId,
            out SaveEntry entry)
        {
            if (entriesByTarget.TryGetValue(targetId, out Queue<SaveEntry> entries) && entries.Count > 0)
            {
                entry = entries.Dequeue();
                return true;
            }

            entry = null;
            return false;
        }

        [Serializable]
        private sealed class SaveData
        {
            public int Version = 2;
            public List<SaveEntry> Entries = new();
        }

        [Serializable]
        private sealed class SaveEntry
        {
            public string TargetType;
            public string GameObjectName;
            public string ParamsType;
            public string Json;
        }

        public Element CreateElement(LabelElement label)
        {
            return UI.Row(
                UI.Button("Save", () => Save()).SetFlexBasis(100f).SetBackgroundColor(Color.darkCyan * 0.5f),
                UI.Button("Load", () => Load()).SetFlexBasis(100f).SetBackgroundColor(Color.mediumSlateBlue * 0.5f)
            );
        }
    }
}
