using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YARG.Menu.Navigation
{
    public sealed class NavigatableButton : NavigatableBehaviour
    {
        [SerializeField]
        private Button.ButtonClickedEvent _onClick = new();

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            Confirm();
        }

        public override void Confirm()
        {
            _onClick.Invoke();
        }

        public void RemoveOnClickListeners()
        {
            _onClick.RemoveAllListeners();
        }

        public void SetOnClickEvent(UnityAction a)
        {
            _onClick.RemoveAllListeners();
            _onClick.AddListener(a);
        }

        /// <summary>
        /// Whether the prefab wired the button to this method of this object.
        /// </summary>
        public bool Calls(Object target, string method)
        {
            for (var i = 0; i < _onClick.GetPersistentEventCount(); i++)
            {
                if (_onClick.GetPersistentMethodName(i) == method &&
                    ReferenceEquals(_onClick.GetPersistentTarget(i), target))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Makes the button do only this, dropping what the prefab wired it to as well (for a button
        /// cloned from another at run time).
        /// </summary>
        public void ReplaceOnClickEvent(UnityAction a)
        {
            _onClick = new Button.ButtonClickedEvent();
            _onClick.AddListener(a);
        }
    }
}