using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class MidiInputParams : IParams
    {
        [Tooltip("未受信のCCもモニタに表示する")]
        public bool ShowAllCc = false;
    }
}
