using UnityEngine;

namespace Aetherin
{
    public interface IPostEffectManager
    {
        Texture ProcessCurrent(Texture source);
        Texture ProcessNext(Texture source);
        void PromoteNextToCurrent();
    }
}
