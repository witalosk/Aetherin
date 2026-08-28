using RosettaUI;

namespace Aetherin
{
    public interface ISaveManager
    {
        void Save(string path = null);
        void Load(string path = null);
        Element CreateElement(LabelElement label);
    }
}
