using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// 拍に合わせてオブジェクトを回転させる
    /// 拍の頭で勢いよく回り、拍の終わりにかけて止まる
    /// </summary>
    public class BeatRotator : MonoBehaviour
    {
        [SerializeField] private Vector3 _axis = Vector3.up;
        [SerializeField] private float _anglePerBeat = 90f;

        [Tooltip("大きいほど拍の頭で一気に回る")]
        [Range(1f, 8f)]
        [SerializeField] private float _sharpness = 3f;

        private IBeatManager _beat;
        private Quaternion _initialRotation;
        private int _beatCount;
        private float _previousPhase;

        [Inject]
        public void Construct(IBeatManager beat)
        {
            _beat = beat;
        }

        private void Start()
        {
            _initialRotation = transform.localRotation;
        }

        private void Update()
        {
            if (_beat == null || !_beat.IsRunning) return;

            float phase = Mathf.Clamp01(_beat.BeatPhase);

            // BeatCountはタップで巻き戻ることがあるため、位相の折り返しで自前で数える
            if (phase < _previousPhase) _beatCount++;
            _previousPhase = phase;

            float eased = 1f - Mathf.Pow(1f - phase, _sharpness);
            float angle = (_beatCount + eased) * _anglePerBeat;

            var axis = _axis.sqrMagnitude < 0.0001f ? Vector3.up : _axis.normalized;
            transform.localRotation = _initialRotation * Quaternion.AngleAxis(angle, axis);
        }
    }
}
