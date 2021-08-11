﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReModCE.Loader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ReModCE.UI
{
    internal class ReQuickButton : UIElement
    {
        public ReQuickButton(Vector2 pos, string text, string tooltip, Action onClick, Transform parent = null) : base(GameObject.Find("UserInterface/QuickMenu/ShortcutMenu/ReportWorldButton"), parent, pos, $"{text}Button")
        {
            Object.DestroyImmediate(gameObject.GetComponentsInChildren<Image>(true).First(a => a.transform != gameObject.transform));

            var textComponent = gameObject.GetComponentInChildren<Text>();
            textComponent.text = text;
            textComponent.resizeTextForBestFit = true;

            var tooltipComponent = gameObject.GetComponent<UiTooltip>();
            tooltipComponent.field_Public_String_0 = tooltip;
            tooltipComponent.field_Public_String_1 = tooltip;

            var buttonComponent = gameObject.GetComponent<Button>();
            buttonComponent.onClick = new Button.ButtonClickedEvent();
            buttonComponent.onClick.AddListener(new Action(onClick));
        }

        public static void Create(Vector2 pos, string text, string tooltip, Action onClick, Transform parent = null)
        {
            var _ = new ReQuickButton(pos, text, tooltip, onClick, parent);
        }
    }
}
