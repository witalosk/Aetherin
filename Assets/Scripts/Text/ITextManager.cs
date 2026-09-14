using System.Collections.Generic;

namespace Aetherin
{
    public interface ITextManager
    {
        IReadOnlyList<string> Keys { get; }
        IReadOnlyList<string> GetTexts(string key);
    }
}
