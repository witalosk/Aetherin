using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// レイヤー種別ごとの保存ID、表示名、および生成時に必要なコンポーネントを定義する。
    /// 新しいレイヤーはここへ登録することで、生成・保存・復元・追加UIで同じ定義を共有できる。
    /// </summary>
    public sealed class LayerDescriptor
    {
        private readonly Action<GameObject> _configureGameObject;
        private readonly Action<StageLayer, IAudioFeatureProvider, IBeatManager, IDeckStateProvider> _initializeLayer;

        internal LayerDescriptor(
            string typeId,
            string displayName,
            string defaultObjectName,
            Type layerType,
            bool canAddToGroup,
            Action<StageLayer, IAudioFeatureProvider, IBeatManager, IDeckStateProvider> initializeLayer,
            Action<GameObject> configureGameObject = null)
        {
            TypeId = typeId;
            DisplayName = displayName;
            DefaultObjectName = defaultObjectName;
            LayerType = layerType;
            CanAddToGroup = canAddToGroup;
            _initializeLayer = initializeLayer;
            _configureGameObject = configureGameObject;
        }

        public string TypeId { get; }
        public string DisplayName { get; }
        public string DefaultObjectName { get; }
        public Type LayerType { get; }
        public bool CanAddToGroup { get; }

        internal StageLayer Create(GameObject layerObject)
        {
            _configureGameObject?.Invoke(layerObject);
            return layerObject.AddComponent(LayerType) as StageLayer;
        }

        internal void Initialize(
            StageLayer layer,
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            IDeckStateProvider deckStateProvider) =>
            _initializeLayer?.Invoke(layer, audioFeatureProvider, beatManager, deckStateProvider);
    }

    /// <summary>CameraStage が扱うレイヤー種別の単一の登録場所。</summary>
    public static class LayerRegistry
    {
        private static readonly Action<GameObject> MeshLayerComponents = layerObject =>
        {
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
        };

        private static LayerDescriptor Create<TLayer>(
            string typeId,
            string displayName,
            string defaultObjectName,
            bool canAddToGroup,
            Action<TLayer, IAudioFeatureProvider, IBeatManager, IDeckStateProvider> initializeLayer,
            Action<GameObject> configureGameObject = null)
            where TLayer : StageLayer =>
            new(typeId, displayName, defaultObjectName, typeof(TLayer), canAddToGroup,
                (layer, audio, beat, deckState) => initializeLayer((TLayer)layer, audio, beat, deckState),
                configureGameObject);

        public static LayerDescriptor Shape { get; } = Create<ShapeLayer>(
            "shape", "Shape", "Shape Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), MeshLayerComponents);
        public static LayerDescriptor Primitive3D { get; } = Create<Primitive3DLayer>(
            "primitive3d", "3D", "Primitive 3D Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), MeshLayerComponents);
        public static LayerDescriptor Model { get; } = Create<ModelLayer>(
            "model", "Model", "Model Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState));
        public static LayerDescriptor SpriteSheet { get; } = Create<SpriteSheetLayer>(
            "sprite-sheet", "Sprite", "Sprite Sheet Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), MeshLayerComponents);
        public static LayerDescriptor Movie { get; } = Create<MovieLayer>(
            "movie", "Movie", "Movie Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), MeshLayerComponents);
        public static LayerDescriptor Light { get; } = Create<LightLayer>(
            "light", "Light", "Light Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), layerObject =>
            {
                layerObject.AddComponent<Light>();
                MeshLayerComponents(layerObject);
            });
        public static LayerDescriptor GpuParticle { get; } = Create<GpuParticleLayer>(
            "gpu-particle", "Particles", "GPU Particle Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState));
        public static LayerDescriptor Text { get; } = Create<TextLayer>(
            "text", "Text", "Text Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), layerObject =>
            {
                layerObject.AddComponent<MeshRenderer>();
                layerObject.AddComponent<TextMeshPro>();
            });
        public static LayerDescriptor RuntimeShader { get; } = Create<RuntimeShaderLayer>(
            "runtime-shader", "Shader", "Runtime Shader Layer", false, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState), MeshLayerComponents);
        public static LayerDescriptor Group { get; } = Create<GroupLayer>(
            "group", "Group", "Group Layer", true, (layer, audio, beat, deckState) => layer.Initialize(audio, beat, deckState));

        private static readonly LayerDescriptor[] AllDescriptors =
        {
            Shape, Primitive3D, Model, SpriteSheet, Movie, Light, Group, GpuParticle, Text, RuntimeShader,
        };

        public static IReadOnlyList<LayerDescriptor> Descriptors => AllDescriptors;

        public static LayerDescriptor GetDescriptor(string typeId) =>
            string.IsNullOrEmpty(typeId)
                ? null
                : AllDescriptors.FirstOrDefault(descriptor => descriptor.TypeId == typeId);

        public static LayerDescriptor GetDescriptor(StageLayer layer) =>
            layer == null
                ? null
                : AllDescriptors.FirstOrDefault(descriptor => descriptor.LayerType.IsInstanceOfType(layer));
    }
}
