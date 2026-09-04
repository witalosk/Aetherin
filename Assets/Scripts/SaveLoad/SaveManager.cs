using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        [ContextMenu("Save")]
        public void Save(string path = null)
        {
            path ??= GetDefaultPath();
            JObject saveData = new()
            {
                ["Version"] = 4
            };
            JArray entries = new();
            saveData["Entries"] = entries;

            foreach (ISaveTarget target in _saveTargets)
            {
                if (target?.Params == null)
                {
                    Debug.LogWarning("Save target or its Params was null and has been skipped.", this);
                    continue;
                }

                entries.Add(new JObject
                {
                    ["TargetType"] = GetTypeId(target.GetType()),
                    ["GameObjectName"] = GetGameObjectName(target),
                    ["ParamsType"] = GetTypeId(target.Params.GetType()),
                    ["Params"] = JObject.Parse(JsonUtility.ToJson(target.Params))
                });

                if (target is not ICustomSaveTarget customTarget) continue;

                try
                {
                    string customJson = customTarget.CaptureSaveData();
                    if (string.IsNullOrEmpty(customJson)) continue;

                    entries.Add(new JObject
                    {
                        ["TargetType"] = $"{GetTypeId(target.GetType())}#Custom",
                        ["GameObjectName"] = GetGameObjectName(target),
                        ["ParamsType"] = $"Custom:{customTarget.SaveId}",
                        ["Params"] = JObject.Parse(customJson)
                    });
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to save custom data for '{target.GetType()}'.\n{exception}", this);
                }
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, saveData.ToString(Formatting.Indented));
        }

        [ContextMenu("Load")]
        public void Load(string path = null)
        {
            path ??= GetDefaultPath();
            if (!File.Exists(path)) return;

            List<SaveEntry> entries;
            try
            {
                entries = ParseEntries(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to read save data from '{path}'.\n{exception}", this);
                return;
            }

            if (entries == null) return;

            // 型と GameObject 名の組み合わせで対象を識別する。同名の場合のみ保存時の列挙順を使う。
            Dictionary<string, Queue<SaveEntry>> entriesByTarget = entries
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.TargetType))
                .GroupBy(entry => GetTargetId(entry.TargetType, entry.GameObjectName))
                .ToDictionary(group => group.Key, group => new Queue<SaveEntry>(group));

            foreach (ISaveTarget target in _saveTargets)
            {
                if (target?.Params == null) continue;

                string targetType = GetTypeId(target.GetType());
                string gameObjectName = GetGameObjectName(target);
                string targetId = GetTargetId(targetType, gameObjectName);
                if (!TryGetEntry(entriesByTarget, targetId, out SaveEntry entry)) continue;

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
                    JsonUtility.FromJsonOverwrite(entry.ParamsJson, target.Params);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to load parameters for '{targetType}'.\n{exception}", this);
                }
            }

            foreach (ISaveTarget target in _saveTargets)
            {
                if (target is not ICustomSaveTarget customTarget) continue;

                string targetType = $"{GetTypeId(target.GetType())}#Custom";
                string targetId = GetTargetId(targetType, GetGameObjectName(target));
                if (!TryGetEntry(entriesByTarget, targetId, out SaveEntry entry)) continue;

                if (entry.ParamsType != $"Custom:{customTarget.SaveId}")
                {
                    Debug.LogWarning($"Saved custom data type '{entry.ParamsType}' does not match '{customTarget.SaveId}'.", this);
                    continue;
                }

                try
                {
                    customTarget.RestoreSaveData(entry.ParamsJson);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to load custom data for '{targetType}'.\n{exception}", this);
                }
            }
        }

        private string GetDefaultPath() => Path.Combine(Application.streamingAssetsPath, _saveDataName);

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

        private static List<SaveEntry> ParseEntries(string json)
        {
            JObject root = JObject.Parse(json);
            int? version = root.Value<int?>("Version");
            if (version is not (3 or 4))
                throw new InvalidDataException("Unsupported save data version.");

            if (root["Entries"] is not JArray jsonEntries) return new List<SaveEntry>();

            List<SaveEntry> entries = new();
            foreach (JToken token in jsonEntries)
            {
                if (token is not JObject jsonEntry) continue;

                if (jsonEntry["Params"] is not JObject paramsObject) continue;

                entries.Add(new SaveEntry
                {
                    TargetType = jsonEntry.Value<string>("TargetType"),
                    GameObjectName = jsonEntry.Value<string>("GameObjectName"),
                    ParamsType = jsonEntry.Value<string>("ParamsType"),
                    ParamsJson = paramsObject.ToString(Formatting.None)
                });
            }

            return entries;
        }

        private sealed class SaveEntry
        {
            public string TargetType;
            public string GameObjectName;
            public string ParamsType;
            public string ParamsJson;
        }

        public Element CreateElement(LabelElement label)
        {
            return UI.Column(
                UI.TextAreaReadOnly(null, GetDefaultPath).SetMaxWidth(300f),
                UI.Row(
                    UI.Button("Save", () => Save()).SetFlexGrow(1f).SetFlexBasis(100f).SetBackgroundColor(Color.darkCyan * 0.5f),
                    UI.Button("Load", () => Load()).SetFlexGrow(1f).SetFlexBasis(100f).SetBackgroundColor(Color.mediumSlateBlue * 0.5f)
                )
            );
        }
    }
}
