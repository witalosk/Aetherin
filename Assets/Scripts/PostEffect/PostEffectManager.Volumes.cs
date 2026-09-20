using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aetherin
{
    /// <summary>
    /// デッキごとに分離したURP Volume Profileを管理する。
    /// </summary>
    public sealed partial class PostEffectManager
    {
        private VolumeProfile _currentVolumeProfile;
        private VolumeProfile _nextVolumeProfile;

        /// <summary>
        /// Current / Nextの各カメラが別々のRuntime Volume Profileだけを読むようにする。
        /// プロファイルはシーンのGlobal Volumeから複製するため、SSRなど未公開の設定も保たれる。
        /// </summary>
        public void ApplyDeckVolumes(Camera currentCamera, Camera nextCamera)
        {
            RefreshEditingDeckState();
            _params ??= new PostEffectManagerParams();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();
            UpdateEditingToggleButtons();

            EnsureVolumeProfiles();
            if (_currentVolumeProfile == null || _nextVolumeProfile == null) return;

            // Swap直後はNextを段階的に再生成するため、数フレームはnextCameraがnullになる。
            // それでも昇格したCurrentのCameraには、再分離処理で変更されたVolume用Layerを
            // 即座に戻す必要がある。両Cameraの存在を必須にすると、この間だけURP Volumeが
            // 見つからず、Bloom/DoFなどが一瞬消える。
            if (currentCamera != null)
            {
                bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Current) ?? false;
                var currentContext = new ModulationContext(
                    Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter,
                    cameraWorkChangeCount: _deckStateProvider?.GetCameraWorkChangeCount(StageDeck.Current) ?? 0);
                ApplyVolumeSettings(_currentVolumeProfile, _params.CurrentVolume,
                    currentCamera.GetComponentInParent<CameraStage>()?.ActiveCameraWorkRecipe, currentContext);
                ConfigureCameraVolume(currentCamera, _currentVolumeProfile, 30, "Current Deck Volume");
            }

            if (nextCamera != null)
            {
                bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Next) ?? true;
                var nextContext = new ModulationContext(
                    Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter,
                    cameraWorkChangeCount: _deckStateProvider?.GetCameraWorkChangeCount(StageDeck.Next) ?? 0);
                ApplyVolumeSettings(_nextVolumeProfile, _params.NextVolume,
                    nextCamera.GetComponentInParent<CameraStage>()?.ActiveCameraWorkRecipe, nextContext);
                ConfigureCameraVolume(nextCamera, _nextVolumeProfile, 31, "Next Deck Volume");
            }
        }

        private void UpdateEditingToggleButtons()
        {
            if (_deckStateProvider.IsPreparingNext) return;

            DeckVolumeEffects effects = EditingVolume;
            effects.EnsureInitialized();

            if (effects.BloomToggleButton.WasNoteOn)
                effects.BloomEnabled = !effects.BloomEnabled;

            effects.BloomToggleButton.SetLed(effects.BloomEnabled ? Color.yellow : Color.yellow * 0.15f);

            if (EditingStack?.Decks == null) return;
            foreach (PostEffectDeck deck in EditingStack.Decks)
            {
                if (deck == null) continue;
                deck.EnsureInitialized();
                if (deck.ControlMode == PostEffectControlMode.OutputPad)
                {
                    deck.OutputPad.SetLed(deck.OutputPad.IsNoteOn ? Color.white : Color.white * 0.15f);
                    continue;
                }
                if (deck.ToggleButton.WasNoteOn) deck.Enabled = !deck.Enabled;
                deck.ToggleButton.SetLed(deck.Enabled ? Color.white : Color.white * 0.15f);
            }
        }

        private void EnsureVolumeProfiles()
        {
            if (_currentVolumeProfile != null && _nextVolumeProfile != null) return;

            VolumeProfile source = FindGlobalVolumeProfile();
            if (source == null) return;
            _currentVolumeProfile ??= CreateRuntimeProfile(source, "Current Deck Volume Profile");
            _nextVolumeProfile ??= CreateRuntimeProfile(source, "Next Deck Volume Profile");
        }

        private static VolumeProfile FindGlobalVolumeProfile()
        {
            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.isGlobal && volume.sharedProfile != null)
                    return volume.sharedProfile;
            }
            return null;
        }

        private static VolumeProfile CreateRuntimeProfile(VolumeProfile source, string profileName)
        {
            // VolumeProfileを単にInstantiateすると、componentsリスト内のSubAsset参照が
            // Current / Next間で共有され得る。各Componentも複製して完全に分離する。
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = profileName;
            profile.hideFlags = HideFlags.HideAndDontSave;
            foreach (VolumeComponent component in source.components)
            {
                if (component == null) continue;
                var copy = Instantiate(component);
                copy.hideFlags = HideFlags.HideAndDontSave;
                profile.components.Add(copy);
            }
            return profile;
        }

        private static void ConfigureCameraVolume(Camera camera, VolumeProfile profile, int layer, string volumeName)
        {
            Transform child = camera.transform.Find(volumeName);
            if (child == null)
            {
                var volumeObject = new GameObject(volumeName) { hideFlags = HideFlags.DontSave };
                child = volumeObject.transform;
                child.SetParent(camera.transform, false);
                volumeObject.layer = layer;
                Volume volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;
            }

            child.gameObject.layer = layer;
            Volume deckVolume = child.GetComponent<Volume>();
            deckVolume.sharedProfile = profile;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.volumeLayerMask = 1 << layer;
        }

        private void ApplyVolumeSettings(
            VolumeProfile profile, DeckVolumeEffects settings, CameraWorkRecipe cameraWork,
            in ModulationContext context)
        {
            settings.EnsureInitialized();
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            // Keep the override active and control the effect with its intensity, as DoF does
            // with DepthOfFieldMode. Toggling VolumeComponent.active causes the inherited
            // Bloom state to win on some URP volume-stack updates.
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = settings.BloomEnabled
                ? Mathf.Max(0f, settings.BloomIntensity.Evaluate(context))
                : 0f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = Mathf.Max(0f, settings.BloomThreshold.Evaluate(context));
            bloom.scatter.overrideState = true;
            bloom.scatter.value = Mathf.Clamp01(settings.BloomScatter.Evaluate(context));

            if (!profile.TryGet(out DepthOfField depthOfField)) depthOfField = profile.Add<DepthOfField>(true);
            depthOfField.active = true;
            depthOfField.mode.overrideState = true;
            depthOfField.mode.value = cameraWork?.DepthOfFieldEnabled == true
                ? settings.DepthOfFieldMode == VolumeDepthOfFieldMode.Bokeh
                    ? DepthOfFieldMode.Bokeh
                    : DepthOfFieldMode.Gaussian
                : DepthOfFieldMode.Off;
            depthOfField.focusDistance.overrideState = true;
            depthOfField.focusDistance.value = Mathf.Max(0.1f,
                cameraWork?.FocusDistance?.Evaluate(context) ?? 10f);
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = Mathf.Clamp(cameraWork?.Aperture?.Evaluate(context) ?? 5.6f, 1f, 32f);
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = Mathf.Clamp(cameraWork?.FocalLength?.Evaluate(context) ?? 50f, 1f, 300f);
        }

        private void DisposeDeckVolumes()
        {
            DestroyRuntimeProfile(_currentVolumeProfile);
            DestroyRuntimeProfile(_nextVolumeProfile);
            _currentVolumeProfile = null;
            _nextVolumeProfile = null;
        }

        private static void DestroyRuntimeProfile(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (VolumeComponent component in profile.components)
            {
                if (component == null) continue;
                if (Application.isPlaying) Destroy(component);
                else DestroyImmediate(component);
            }
            if (Application.isPlaying) Destroy(profile);
            else DestroyImmediate(profile);
        }
    }
}
