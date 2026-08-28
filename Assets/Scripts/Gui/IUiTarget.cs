using RosettaUI;

namespace Aetherin
{
    public interface IUiTarget : IParamsTarget, IMonoBehaviour
    {
        /// <summary>
        /// GUIのタブ分けに使うカテゴリ名
        /// </summary>
        string Category => UiCategory.Misc;
        bool FoldParams => false;
        Element AdditiveUi(){ return null; }
    }

    /// <summary>
    /// <see cref="IUiTarget.Category"/>で使う標準のカテゴリ名
    /// </summary>
    public static class UiCategory
    {
        public const string Audio = "Audio";
        public const string Beat = "Beat";
        public const string Midi = "Midi";
        public const string System = "System";
        public const string Misc = "Misc";
    }
}
