using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Aetherin
{
    public enum CameraWorkType
    {
        Static,
        Orbit,
        Follow,
        Handheld,
    }

    public enum CameraWorkSwitchTiming
    {
        Beat,
        Bar,
        Every2Bars,
        Every4Bars,
    }

    [Serializable]
    public sealed class CameraWorkRecipe
    {
        public string Name = "Camera Work";
        public CameraWorkType Type;
        public Vector3Parameter Position = new(Vector3.zero);
        public Vector3Parameter LookAt = new(Vector3.zero);
        public Vector3Parameter OrbitRotation = new(Vector3.zero);
        public FloatParameter FieldOfView = new(60f);
        public FloatParameter Speed = new(1f);
        public FloatParameter Radius = new(5f);
        public FloatParameter NoiseAmount = new(0f);

        public void EnsureInitialized()
        {
            Position ??= new Vector3Parameter(Vector3.zero);
            LookAt ??= new Vector3Parameter(Vector3.zero);
            OrbitRotation ??= new Vector3Parameter(Vector3.zero);
            FieldOfView ??= new FloatParameter(60f);
            Speed ??= new FloatParameter(1f);
            Radius ??= new FloatParameter(5f);
            NoiseAmount ??= new FloatParameter(0f);
        }
    }

    [Serializable]
    public sealed class CameraWorkDeck
    {
        public string Name = "Camera Work Deck";
        public List<CameraWorkRecipe> Recipes = new();

        public void EnsureInitialized()
        {
            Recipes ??= new List<CameraWorkRecipe>();
            foreach (CameraWorkRecipe recipe in Recipes) recipe?.EnsureInitialized();
        }
    }

    [Serializable]
    internal sealed class CameraWorkDeckList
    {
        public List<CameraWorkDeck> Decks = new();
    }

    public partial class CameraStage
    {
        public IReadOnlyList<CameraWorkDeck> CameraWorkDecks => _cameraWorkDecks;
        public int CameraWorkRevision { get; private set; }
        public int SelectedCameraWorkDeck => _selectedCameraWorkDeck;
        public int CurrentCameraWork => _currentCameraWork;
        public int CinemachineChannelIndex { get; private set; } = -1;

        [SerializeField] private List<CameraWorkDeck> _cameraWorkDecks = new();

        private CinemachineBrain _cinemachineBrain;
        private CinemachineCamera _cinemachineCamera;
        private int _selectedCameraWorkDeck;
        private int _currentCameraWork;
        private Vector3 _followPosition;
        private bool _followInitialized;
        private Vector3 _baseCameraLocalPosition;
        private Quaternion _baseCameraLocalRotation;
        private float _baseCameraFieldOfView;

        public void ConfigureCinemachineChannel(int channelIndex)
        {
            CinemachineChannelIndex = Mathf.Clamp(channelIndex, 0, 31);
            ApplyCinemachineChannel();
        }

        private void InitializeCameraWork()
        {
            _cameraWorkDecks ??= new List<CameraWorkDeck>();
            foreach (CameraWorkDeck deck in _cameraWorkDecks) deck?.EnsureInitialized();

            _cinemachineBrain = _camera.GetComponent<CinemachineBrain>();
            if (_cinemachineBrain == null) _cinemachineBrain = _camera.gameObject.AddComponent<CinemachineBrain>();
            _cinemachineBrain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut,
                0f);
            _baseCameraLocalPosition = _camera.transform.localPosition;
            _baseCameraLocalRotation = _camera.transform.localRotation;
            _baseCameraFieldOfView = _camera.fieldOfView;

            var cameraObject = new GameObject("Runtime Cinemachine Camera");
            cameraObject.transform.SetParent(transform, false);
            _cinemachineCamera = cameraObject.AddComponent<CinemachineCamera>();
            _cinemachineCamera.Priority = 100;
            _cinemachineCamera.enabled = false;
            ApplyCinemachineChannel();
            ResetCameraWork();
        }

        private void ApplyCinemachineChannel()
        {
            if (CinemachineChannelIndex < 0) return;
            var channel = (OutputChannels)(1 << CinemachineChannelIndex);
            if (_cinemachineBrain != null) _cinemachineBrain.ChannelMask = channel;
            if (_cinemachineCamera != null) _cinemachineCamera.OutputChannel = channel;
        }

        private void UpdateCameraWork()
        {
            if (_cinemachineCamera == null) return;
            CameraWorkDeck deck = GetSelectedCameraWorkDeck();
            if (deck?.Recipes == null || deck.Recipes.Count == 0)
            {
                _cinemachineCamera.enabled = false;
                _camera.transform.SetLocalPositionAndRotation(_baseCameraLocalPosition, _baseCameraLocalRotation);
                _camera.fieldOfView = _baseCameraFieldOfView;
                return;
            }

            _cinemachineCamera.enabled = true;

            if (ShouldAdvanceCameraWork())
            {
                _currentCameraWork = (_currentCameraWork + 1) % deck.Recipes.Count;
                _followInitialized = false;
            }

            _currentCameraWork = Mathf.Clamp(_currentCameraWork, 0, deck.Recipes.Count - 1);
            CameraWorkRecipe recipe = deck.Recipes[_currentCameraWork];
            if (recipe == null) return;
            recipe.EnsureInitialized();

            bool allowMidi = Deck == StageDeck.Next;
            var context = new ModulationContext(Time.timeAsDouble, _audioFeatureProvider, _beatManager, allowMidi);
            Vector3 position = recipe.Position.Evaluate(context);
            Vector3 lookAt = recipe.LookAt.Evaluate(context);
            Vector3 orbitRotation = recipe.OrbitRotation.Evaluate(context);
            float speed = Mathf.Max(0f, recipe.Speed.Evaluate(context));
            float radius = Mathf.Max(0f, recipe.Radius.Evaluate(context));
            float noise = Mathf.Max(0f, recipe.NoiseAmount.Evaluate(context));

            switch (recipe.Type)
            {
                case CameraWorkType.Orbit:
                    Vector3 orbitOffset = Quaternion.Euler(orbitRotation) * Vector3.forward * radius;
                    position = lookAt + position + orbitOffset;
                    break;
                case CameraWorkType.Follow:
                    if (!_followInitialized)
                    {
                        _followPosition = position;
                        _followInitialized = true;
                    }
                    float followT = 1f - Mathf.Exp(-speed * Time.deltaTime);
                    _followPosition = Vector3.LerpUnclamped(_followPosition, position, followT);
                    position = _followPosition;
                    break;
                case CameraWorkType.Handheld:
                    float noiseTime = (float)Time.timeAsDouble * Mathf.Max(0.01f, speed);
                    position += new Vector3(
                        Mathf.PerlinNoise(noiseTime, 0.17f) - 0.5f,
                        Mathf.PerlinNoise(0.31f, noiseTime) - 0.5f,
                        Mathf.PerlinNoise(noiseTime, 0.73f) - 0.5f) * (noise * 2f);
                    break;
            }

            Vector3 worldPosition = transform.TransformPoint(position);
            Vector3 worldLookAt = transform.TransformPoint(lookAt);
            Vector3 direction = worldLookAt - worldPosition;
            Quaternion rotation = direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction, transform.up)
                : transform.rotation;
            _cinemachineCamera.transform.SetPositionAndRotation(worldPosition, rotation);

            LensSettings lens = _cinemachineCamera.Lens;
            lens.FieldOfView = Mathf.Clamp(recipe.FieldOfView.Evaluate(context), 1f, 179f);
            _cinemachineCamera.Lens = lens;
        }

        private bool ShouldAdvanceCameraWork()
        {
            if (_beatManager == null || !_beatManager.IsRunning) return false;
            CameraWorkSwitchTiming timing = StageManager.CurrentCameraWorkTiming;
            return timing switch
            {
                CameraWorkSwitchTiming.Beat => _beatManager.WasBeat,
                CameraWorkSwitchTiming.Bar => _beatManager.WasBar,
                CameraWorkSwitchTiming.Every2Bars => _beatManager.WasBar && _beatManager.BarCount % 2 == 0,
                CameraWorkSwitchTiming.Every4Bars => _beatManager.WasBar && _beatManager.BarCount % 4 == 0,
                _ => false,
            };
        }

        private CameraWorkDeck GetSelectedCameraWorkDeck()
        {
            if (_cameraWorkDecks == null || _cameraWorkDecks.Count == 0) return null;
            _selectedCameraWorkDeck = Mathf.Clamp(_selectedCameraWorkDeck, 0, _cameraWorkDecks.Count - 1);
            return _cameraWorkDecks[_selectedCameraWorkDeck];
        }

        public void SelectCameraWorkDeck(int index)
        {
            if (_cameraWorkDecks == null || _cameraWorkDecks.Count == 0) return;
            _selectedCameraWorkDeck = Mathf.Clamp(index, 0, _cameraWorkDecks.Count - 1);
            ResetCameraWork();
        }

        public void ResetCameraWork()
        {
            _currentCameraWork = 0;
            _followInitialized = false;
        }

        public CameraWorkDeck AddCameraWorkDeck()
        {
            _cameraWorkDecks ??= new List<CameraWorkDeck>();
            var deck = new CameraWorkDeck { Name = $"Deck {_cameraWorkDecks.Count + 1}" };
            _cameraWorkDecks.Add(deck);
            CameraWorkRevision++;
            return deck;
        }

        public void RemoveCameraWorkDeck(int index)
        {
            if (_cameraWorkDecks == null || index < 0 || index >= _cameraWorkDecks.Count) return;
            _cameraWorkDecks.RemoveAt(index);
            _selectedCameraWorkDeck = Mathf.Clamp(_selectedCameraWorkDeck, 0, Mathf.Max(0, _cameraWorkDecks.Count - 1));
            ResetCameraWork();
            CameraWorkRevision++;
        }

        public void MoveCameraWorkDeck(int index, int direction)
        {
            if (_cameraWorkDecks == null) return;
            int destination = index + direction;
            if (index < 0 || index >= _cameraWorkDecks.Count ||
                destination < 0 || destination >= _cameraWorkDecks.Count) return;
            (_cameraWorkDecks[index], _cameraWorkDecks[destination]) =
                (_cameraWorkDecks[destination], _cameraWorkDecks[index]);
            if (_selectedCameraWorkDeck == index) _selectedCameraWorkDeck = destination;
            else if (_selectedCameraWorkDeck == destination) _selectedCameraWorkDeck = index;
            CameraWorkRevision++;
        }

        public CameraWorkRecipe AddCameraWork(int deckIndex)
        {
            if (_cameraWorkDecks == null || deckIndex < 0 || deckIndex >= _cameraWorkDecks.Count) return null;
            CameraWorkDeck deck = _cameraWorkDecks[deckIndex];
            deck.EnsureInitialized();
            var recipe = new CameraWorkRecipe { Name = $"Camera Work {deck.Recipes.Count + 1}" };
            deck.Recipes.Add(recipe);
            CameraWorkRevision++;
            return recipe;
        }

        public void RemoveCameraWork(int deckIndex, int recipeIndex)
        {
            if (_cameraWorkDecks == null || deckIndex < 0 || deckIndex >= _cameraWorkDecks.Count) return;
            List<CameraWorkRecipe> recipes = _cameraWorkDecks[deckIndex].Recipes;
            if (recipes == null || recipeIndex < 0 || recipeIndex >= recipes.Count) return;
            recipes.RemoveAt(recipeIndex);
            ResetCameraWork();
            CameraWorkRevision++;
        }

        public void MoveCameraWork(int deckIndex, int recipeIndex, int direction)
        {
            if (_cameraWorkDecks == null || deckIndex < 0 || deckIndex >= _cameraWorkDecks.Count) return;
            List<CameraWorkRecipe> recipes = _cameraWorkDecks[deckIndex].Recipes;
            int destination = recipeIndex + direction;
            if (recipes == null || recipeIndex < 0 || recipeIndex >= recipes.Count ||
                destination < 0 || destination >= recipes.Count) return;
            (recipes[recipeIndex], recipes[destination]) = (recipes[destination], recipes[recipeIndex]);
            ResetCameraWork();
            CameraWorkRevision++;
        }

        public List<CameraWorkDeck> CaptureCameraWorkDecks() => _cameraWorkDecks;

        public void RestoreCameraWorkDecks(List<CameraWorkDeck> decks)
        {
            string json = JsonUtility.ToJson(new CameraWorkDeckList { Decks = decks ?? new List<CameraWorkDeck>() });
            _cameraWorkDecks = JsonUtility.FromJson<CameraWorkDeckList>(json)?.Decks ?? new List<CameraWorkDeck>();
            foreach (CameraWorkDeck deck in _cameraWorkDecks) deck?.EnsureInitialized();
            ResetCameraWork();
            CameraWorkRevision++;
        }
    }
}
