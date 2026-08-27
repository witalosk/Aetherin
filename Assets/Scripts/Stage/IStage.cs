using UnityEngine;

namespace Aetherin
{
    public interface IStage
    {
        string StageName { get; }
        RenderTexture OutputTexture { get; }
    }
}
