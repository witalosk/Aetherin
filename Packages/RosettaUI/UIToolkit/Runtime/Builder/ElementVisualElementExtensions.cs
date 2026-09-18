using System;
using RosettaUI.UIToolkit.Builder;
using UnityEngine.UIElements;

namespace RosettaUI
{
    public static class ElementVisualElementExtensions
    {
        public static T RegisterVisualElementAttachedCallback<T>(
            this T element,
            Action<VisualElement> callback)
            where T : Element
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            element.RegisterViewAttachedCallback(view =>
            {
                if (view is VisualElement visualElement)
                {
                    callback(visualElement);
                }
            });

            var attachedVisualElement = UIToolkitBuilder.Instance.GetUIObj(element);
            if (attachedVisualElement != null)
            {
                callback(attachedVisualElement);
            }

            return element;
        }

        public static T RegisterVisualElementAttachedCallback<T>(
            this T element,
            Action<VisualElement> attachedCallback,
            Action<VisualElement> detachedCallback)
            where T : Element
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (attachedCallback == null) throw new ArgumentNullException(nameof(attachedCallback));
            if (detachedCallback == null) throw new ArgumentNullException(nameof(detachedCallback));

            return element.RegisterVisualElementAttachedCallback(visualElement =>
            {
                attachedCallback(visualElement);
                element.GetViewBridge().onUnsubscribe += () => detachedCallback(visualElement);
            });
        }
    }
}
