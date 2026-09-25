using System;
using Klak.Spout;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public interface IOutputTextureProvider
    {
        RenderTexture OutputTexture { get; }
    }
    
    public class SpoutSender : MonoBehaviour
    {
        public string SpoutName = "Aetherin";
        [SerializeField] private SpoutResources _resources;

        private Klak.Spout.SpoutSender _spoutSender;
        private IOutputTextureProvider _outputTextureProvider;
        
        [Inject]
        public void Construct(IOutputTextureProvider outputTextureProvider)
        {
            _outputTextureProvider = outputTextureProvider;
        }

        private void Start()
        {
            _spoutSender = GetComponent<Klak.Spout.SpoutSender>();
            if (_spoutSender == null)
            {
                _spoutSender = gameObject.AddComponent<Klak.Spout.SpoutSender>();
                _spoutSender.SetResources(_resources);
            }
            _spoutSender.captureMethod = CaptureMethod.Texture;
            _spoutSender.spoutName = SpoutName;
            _spoutSender.sourceTexture = _outputTextureProvider.OutputTexture;
        }

        private void Update()
        {
            if (_spoutSender.sourceTexture != _outputTextureProvider.OutputTexture)
            {
                _spoutSender.sourceTexture = _outputTextureProvider.OutputTexture;
            }
        }
    }
}
