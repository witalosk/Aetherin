using RosettaUI;

namespace Aetherin
{
    public interface IUiTarget : IParamsTarget, IMonoBehaviour
    {
        Element AdditiveUi();
    }
}
