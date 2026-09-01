namespace Aetherin
{
    /// <summary>
    /// パラメータ1個では表現できない、実行時に変化する構成を保存する対象。
    /// </summary>
    public interface ICustomSaveTarget : IMonoBehaviour
    {
        string SaveId { get; }
        string CaptureSaveData();
        void RestoreSaveData(string json);
    }
}
 
