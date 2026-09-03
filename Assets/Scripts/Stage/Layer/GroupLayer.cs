using System;
using System.Linq;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GroupLayer : StageLayer
    {
        [SerializeField] private GroupLayerParams _params = new();
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        public StageLayer[] Children => GetComponentsInChildren<StageLayer>(true)
            .Where(layer => layer != this && layer.transform.parent == transform)
            .OrderBy(layer => layer.Order).ToArray();

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat)
        {
            _audio = audio;
            _beat = beat;
        }

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat)
        {
            _audio = audio;
            _beat = beat;
            InitializeLayer();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            EnsureParameters();
            _stage = GetComponentInParent<StageBase>();
            EvaluateTransform();
        }

        private void Update()
        {
            EnsureParameters();
            EvaluateTransform();
        }

        private void EnsureParameters()
        {
            _params ??= new GroupLayerParams();
            _params.Opacity ??= new FloatParameter(1f);
            _params.Position ??= new Vector3Parameter();
            _params.Rotation ??= new Vector3Parameter();
            _params.Scale ??= new Vector3Parameter(Vector3.one);
            _params.Anchor ??= new Vector3Parameter();
        }

        private void EvaluateTransform()
        {
            bool runtime = Application.isPlaying;
            var context = new ModulationContext(runtime ? Time.timeAsDouble : 0d,
                runtime ? _audio : null, runtime ? _beat : null,
                runtime && (_stage == null || _stage.Deck == StageDeck.Next));
            Vector3 position = _params.Position.Evaluate(context);
            Vector3 rotation = _params.Rotation.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Quaternion orientation = Quaternion.Euler(rotation);
            transform.localPosition = position - orientation * Vector3.Scale(anchor, scale);
            transform.localRotation = orientation;
            transform.localScale = scale;
        }

        protected override void ApplyCustomLayerState(bool visible, int order)
        {
            // A layer may be nested below helper GameObjects, so do not rely on
            // transform.parent being the GroupLayer itself. Every descendant
            // evaluates the complete ancestor chain in StageLayer.ApplyLayerState().
            var descendants = GetComponentsInChildren<StageLayer>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (descendants[i] != this)
                    descendants[i].RefreshLayerState();
            }
        }
    }
}
