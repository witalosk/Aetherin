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
            
            List<Element> launchers = new List<Element>();
            foreach (var uiTarget in _uiTargets)
            {
                launchers.Add(UI.WindowLauncher(uiTarget.gameObject.name,
                    UI.Window(uiTarget.gameObject.name, UI.Column(
                        UI.Field("Params", Binder.Create(uiTarget.Params, uiTarget.Params.GetType())).SetOpenFlag(!uiTarget.FoldParams),
                        uiTarget.AdditiveUi()
                    ))
                ));
            }
            launchers.Add(_saveManager.CreateElement(null));
            
            _root.Build(UI.Window("Aetherin",
                UI.Column(launchers)
            ).SetWidth(300f));
            
        }

        private void RegisterUiCustomFuncs()
        {
            
        }
    }
}
