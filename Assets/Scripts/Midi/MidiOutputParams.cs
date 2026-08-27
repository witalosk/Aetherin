using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class MidiOutputParams : IParams
    {
        [Tooltip("接続するMIDI出力ポート名 (部分一致、大文字小文字は無視)")]
        public string PortNameFilter = "APC mini mk2";

        [Tooltip("未接続時に再接続を試みる間隔 (秒)")]
        public float ReconnectInterval = 2f;

        [Tooltip("停止時に全てのLEDを消灯する")]
        public bool ClearLedsOnDisable = true;
    }
}
