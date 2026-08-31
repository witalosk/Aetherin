using System;
using System.Collections.Generic;
using System.Linq;
using RosettaUI;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public class GuiController : MonoBehaviour
    {
        /// <summary>
        /// タブの表示順。ここに無いカテゴリは名前順で後ろに並ぶ
        /// </summary>
        private static readonly string[] CategoryOrder =
        {
            UiCategory.Main,
            UiCategory.Settings,
            UiCategory.Misc
        };

        private RosettaUIRoot _root;
        private List<IUiTarget> _uiTargets;
        private ISaveManager _saveManager;
        
        [Inject]
        public void Construct(IEnumerable<IUiTarget> uiTargets, ISaveManager saveManager)
        {
            _uiTargets = uiTargets.OrderBy(ut => ut.gameObject.name).ToList();
            _saveManager = saveManager;
        }

        private void Awake()
        {
            _root = GetComponentInChildren<RosettaUIRoot>();
            RegisterUiCustomFuncs();

            var tabs = _uiTargets
                .GroupBy(ut => string.IsNullOrEmpty(ut.Category) ? UiCategory.Misc : ut.Category)
                .OrderBy(GetCategorySortKey)
                .ThenBy(g => g.Key)
                .Select(g => Tab.Create(g.Key, () => UI.Column(g.Select(CreateLauncher))))
                .ToList();

            _root.Build(UI.Window("Aetherin",
                UI.Column(
                    _saveManager.CreateElement(null),
                    UI.Tabs(tabs)
                )
            ).SetWidth(300f));
        }

        private static Element CreateLauncher(IUiTarget uiTarget)
        {
            return UI.WindowLauncher(uiTarget.gameObject.name,
                UI.Window(uiTarget.gameObject.name, UI.Column(
                    UI.Field("Params", Binder.Create(uiTarget.Params, uiTarget.Params.GetType())).SetOpenFlag(!uiTarget.FoldParams),
                    uiTarget.AdditiveUi()
                ))
            );
        }

        private static int GetCategorySortKey(IGrouping<string, IUiTarget> group)
        {
            var index = Array.IndexOf(CategoryOrder, group.Key);
            return index < 0 ? CategoryOrder.Length : index;
        }

        private void RegisterUiCustomFuncs()
        {
            
        }
    }
}
