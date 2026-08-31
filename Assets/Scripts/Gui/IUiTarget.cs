using RosettaUI;

namespace Aetherin
{
    public interface IUiTarget : IParamsTarget, IMonoBehaviour
    {
        /// <summary>
        /// GUIのタブ分けに使うカテゴリ名
        /// </summary>
        string Category => UiCategory.Main;
        bool FoldParams => false;
        Element AdditiveUi() { return null; }
    }

    /// <summary>
    /// <see cref="IUiTarget.Category"/>で使う標準のカテゴリ名
    /// </summary>
    public static class UiCategory
    {
        public const string Main = "Main";
        public const string Settings = "Settings";
        public const string Misc = "Misc";
    }
}
