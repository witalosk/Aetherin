using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// StageManager のステージ構成保存・復元を担う部分実装。
    /// デッキの構築前に復元要求を受けた場合は、Start後に同じ内容を適用する。
    /// </summary>
    public partial class StageManager
    {
        private CameraStageSaveData _pendingCameraStageData;

        public string CaptureSaveData()
        {
            var data = new CameraStageSaveData();
            List<StageBase> stages = EditingStages;
            if (stages == null) return JsonUtility.ToJson(data);

            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] is not CameraStage stage) continue;
                data.Stages.Add(new CameraStageLayersSaveData
                {
                    StageId = stage.StageId,
                    StageName = GetStageDisplayName(_stages[i], i),
                    RuntimeCreated = _runtimeStageIds.Contains(stage.StageId),
                    StageIndex = i,
                    BackgroundMode = stage.BackgroundMode,
                    BackgroundColor = stage.BackgroundColor,
                    DefaultLut = stage.CaptureDefaultLut(),
                    Layers = stage.CaptureLayers(),
                    CameraWorkDecks = stage.CaptureCameraWorkDecks(),
                });
            }

            return JsonUtility.ToJson(data);
        }

        public void RestoreSaveData(string json)
        {
            _pendingCameraStageData = JsonUtility.FromJson<CameraStageSaveData>(json);
            if (_nextStages != null) ApplyPendingCameraStageData();
        }

        private void ApplyPendingCameraStageData()
        {
            if (_pendingCameraStageData?.Stages == null) return;
            int currentStageIndex = _params.CurrentStageIndex;
            int nextStageIndex = _params.NextStageIndex;

            foreach (CameraStageLayersSaveData savedStage in _pendingCameraStageData.Stages)
            {
                if (savedStage == null) continue;
                int stageIndex = FindStageIndex(savedStage.StageId);
                if (stageIndex < 0 && savedStage.RuntimeCreated && !string.IsNullOrEmpty(savedStage.StageId))
                {
                    AddCameraStage(savedStage.StageName, savedStage.StageId);
                    stageIndex = FindStageIndex(savedStage.StageId);
                }
                if (stageIndex < 0) stageIndex = savedStage.StageIndex;
                if (stageIndex < 0 || stageIndex >= _nextStages.Count) continue;

                if (!string.IsNullOrEmpty(savedStage.StageId))
                {
                    string stageName = string.IsNullOrEmpty(savedStage.StageName)
                        ? GetStageDisplayName(_stages[stageIndex], stageIndex)
                        : savedStage.StageName;
                    _stages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                    _currentStages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                    _nextStages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                }

                _stages[stageIndex]?.RestoreDefaultLut(savedStage.DefaultLut);
                _currentStages[stageIndex]?.RestoreDefaultLut(savedStage.DefaultLut);
                _nextStages[stageIndex]?.RestoreDefaultLut(savedStage.DefaultLut);

                if (_nextStages[stageIndex] is CameraStage nextStage)
                {
                    nextStage.RestoreLayers(savedStage.Layers);
                    nextStage.RestoreCameraWorkDecks(savedStage.CameraWorkDecks);
                    nextStage.BackgroundMode = savedStage.BackgroundMode;
                    nextStage.BackgroundColor = savedStage.BackgroundColor;
                }
                if (_currentStages[stageIndex] is CameraStage currentStage)
                {
                    currentStage.RestoreLayers(savedStage.Layers);
                    currentStage.RestoreCameraWorkDecks(savedStage.CameraWorkDecks);
                    currentStage.BackgroundMode = savedStage.BackgroundMode;
                    currentStage.BackgroundColor = savedStage.BackgroundColor;
                }
            }

            int maxIndex = Mathf.Max(0, _stages.Count - 1);
            _params.CurrentStageIndex = Mathf.Clamp(currentStageIndex, 0, maxIndex);
            _params.NextStageIndex = Mathf.Clamp(nextStageIndex, 0, maxIndex);
            _deckRevision++;
            _pendingCameraStageData = null;
        }
    }

    [Serializable]
    public sealed class CameraStageSaveData
    {
        public int Version = 3;
        public List<CameraStageLayersSaveData> Stages = new();
    }

    [Serializable]
    public sealed class CameraStageLayersSaveData
    {
        public string StageId;
        public string StageName;
        public bool RuntimeCreated;
        public int StageIndex;
        public CameraStageBackgroundMode BackgroundMode;
        public PaletteColorSource BackgroundColor = PaletteColorSource.BackgroundColor1;
        public StageDefaultLutSettings DefaultLut = new();
        public List<CameraStageLayerSaveData> Layers = new();
        public List<CameraWorkDeck> CameraWorkDecks = new();
    }
}
