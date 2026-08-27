using RosettaUI;

namespace Aetherin
{
    public interface IUiTarget : IParamsTarget, IMonoBehaviour
    {
        bool FoldParams => false;
        Element AdditiveUi(){ return null; }
    }
}
