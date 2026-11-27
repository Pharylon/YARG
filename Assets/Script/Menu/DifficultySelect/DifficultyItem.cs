using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YARG.Helpers.Extensions;
using YARG.Menu.Navigation;

namespace YARG.Menu.DifficultySelect
{
    public class DifficultyItem : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _header;
        [SerializeField]
        private TextMeshProUGUI _body;

        [SerializeField]
        private Image _selectionFill;
        [SerializeField]
        private CanvasGroup _canvasGroup;

        [field: SerializeField]
        public NavigatableButton Button { get; private set; }

        public void Initialize(string header, string body, UnityAction action)
        {
            _header.gameObject.SetActive(true);
            _header.text = header;

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        public void Initialize(string body, UnityAction action)
        {
            _header.gameObject.SetActive(false);

            _body.text = body;
            Button.SetOnClickEvent(action);
        }

        /// <summary>
        /// Tints the header/body text, using the selected accent while selected.
        /// Each text's own alpha is preserved.
        /// </summary>
        public void SetAccentColors(Color textColor, Color selectedTextColor)
        {
            ApplyAccent(Button.Selected);

            Button.SelectionStateChanged += (_, selected, _) => ApplyAccent(selected);

            void ApplyAccent(bool selected)
            {
                var color = selected ? selectedTextColor : textColor;
                _header.color = color.WithAlpha(_header.color.a);
                _body.color = color.WithAlpha(_body.color.a);
            }
        }

        /// <summary>
        /// Shows the item as a non-interactive menu title: header text only, no
        /// body, no action. Used so a sub-menu identifies itself.
        /// </summary>
        public void InitializeAsTitle(string header)
        {
            // A title never joins the navigation group, so nothing ever
            // deselects it once the pointer selects it. Keep the pointer away
            // from it entirely; the row is a label, not a button.
            Button.SelectOnHover = false;

            _header.gameObject.SetActive(true);
            _header.text = header;
            _body.gameObject.SetActive(false);
        }

        /// <summary>
        /// The width of the body text as drawn.
        /// </summary>
        public float BodyTextWidth
        {
            get
            {
                _body.ForceMeshUpdate();
                float width = _body.textBounds.size.x;
                return width > 0f && !float.IsInfinity(width) ? width : _body.preferredWidth;
            }
        }

        /// <summary>
        /// Puts a copy of the given tier ring to the left of the body text, with the text starting at
        /// a fixed distance from it. Rows given the same <paramref name="textWidth"/> (that of the
        /// widest of them) get their rings in one column and their texts starting on one line, the
        /// whole block centered in the row.
        /// </summary>
        public YARG.Menu.MusicLibrary.DifficultyRing AttachLeadingRing(
            YARG.Menu.MusicLibrary.DifficultyRing template, float textWidth, float size = 40f, float gap = 14f)
        {
            float rowWidth = _body.rectTransform.rect.width;
            if (rowWidth <= 0f)
            {
                rowWidth = ((RectTransform) transform).sizeDelta.x;
            }

            // Where the block of ring, gap and text starts, from the row's left edge
            float blockLeft = Mathf.Max(0f, (rowWidth - (size + gap + textWidth)) / 2f);

            _body.horizontalAlignment = HorizontalAlignmentOptions.Left;
            var margin = _body.margin;
            margin.x = blockLeft + size + gap;
            _body.margin = margin;

            var ring = Instantiate(template, _body.transform);
            var rect = (RectTransform) ring.transform;

            // The ring prefab's native rect is 65x65; scale it uniformly
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(65f, 65f);
            rect.localScale = Vector3.one * (size / 65f);
            rect.anchoredPosition = new Vector2(blockLeft + size / 2f, 0f);

            ring.gameObject.SetActive(true);
            return ring;
        }

        /// <summary>
        /// Shrinks the body text to the header's font size. Used by rows whose
        /// body lists several settings and would otherwise dominate the menu.
        /// </summary>
        public void UseSmallBodyText()
        {
            _body.fontSize = _header.fontSize;
        }

        /// <summary>
        /// Dims the item and disables interaction (used to show a fixed, non-editable choice).
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            _canvasGroup.alpha = interactable ? 1f : 0.3f;
            _canvasGroup.interactable = interactable;
            _canvasGroup.blocksRaycasts = interactable;
        }

        /// <summary>
        /// Fills the entire button background with the given color while the item
        /// is selected, behind the selected text color. The colored variant's
        /// selection background carries no sprite — a sprite-less Image draws a
        /// plain full-rect quad — so the color alone paints an edge-to-edge
        /// ready/sit-out style fill, without a per-color sprite.
        /// </summary>
        public void SetSelectionFill(Color fillColor)
        {
            _selectionFill.color = fillColor;
        }
    }
}