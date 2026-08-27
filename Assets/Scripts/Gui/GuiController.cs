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
        
        [Inject]
        public void Construct(IEnumerable<IUiTarget> uiTargets)
        {
            _uiTargets = uiTargets.OrderBy(ut => ut.gameObject.name).ToList();
        }

        private void Awake()
        {
            _root = GetComponentInParent<RosettaUIRoot>();
            List<Element> launchers = new List<Element>();
            foreach (var uiTarget in _uiTargets)
            {
                launchers.Add(UI.WindowLauncher(uiTarget.gameObject.name,
                    UI.Window(uiTarget.gameObject.name, UI.Column(
                        UI.Field(null, Binder.Create(uiTarget.Params, uiTarget.Params.GetType())).Open(),
                        uiTarget.AdditiveUi()
                    ))
                ));
            }
            
            _root.Build(UI.Window("Aetherin",
                UI.Column(launchers)
            ));
            
        }
    }
}
