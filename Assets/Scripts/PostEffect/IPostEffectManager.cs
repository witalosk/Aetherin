using UnityEngine;

namespace Aetherin
{
    public interface IPostEffectManager
    {
        Texture ProcessCurrent(Texture source, StageDefaultLutSettings stageLut);
        Texture ProcessNext(Texture source, StageDefaultLutSettings stageLut);
        Texture ProcessOutput(Texture source);
        void ApplyDeckVolumes(Camera currentCamera, Camera nextCamera);
    }
}
