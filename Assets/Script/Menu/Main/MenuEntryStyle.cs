using TMPro;
using UnityEngine;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The sizes of a main-menu style entry (MenuEntry prefab): its rects, text sizes and layout heights,
    /// read from one entry and given to another built the same way. The main menu draws its first entry
    /// larger than the rest; this carries that look to entries that move (see MainMenu, GameModesMenu).
    /// </summary>
    public class MenuEntryStyle
    {
        public RectTransform[] Rects;
        public Vector2[] Sizes;
        public Vector2[] Positions;
        public TextMeshProUGUI[] Texts;
        public float[] FontSizes;
        public UnityEngine.UI.LayoutElement[] Layouts;
        public Vector2[] LayoutHeights;

        public static MenuEntryStyle Read(GameObject entry)
        {
            var style = new MenuEntryStyle
            {
                Rects = entry.GetComponentsInChildren<RectTransform>(true),
                Texts = entry.GetComponentsInChildren<TextMeshProUGUI>(true),
                Layouts = entry.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true),
            };

            style.Sizes = System.Array.ConvertAll(style.Rects, rect => rect.sizeDelta);
            style.Positions = System.Array.ConvertAll(style.Rects, rect => rect.anchoredPosition);
            style.FontSizes = System.Array.ConvertAll(style.Texts, text => text.fontSize);
            style.LayoutHeights = System.Array.ConvertAll(style.Layouts,
                layout => new Vector2(layout.minHeight, layout.preferredHeight));
            return style;
        }

        /// <summary>
        /// Gives this entry the sizes of <paramref name="from"/> (an entry built the same way).
        /// </summary>
        public void Apply(MenuEntryStyle from)
        {
            if (from.Rects.Length != Rects.Length || from.Texts.Length != Texts.Length ||
                from.Layouts.Length != Layouts.Length)
            {
                return;
            }

            for (var i = 0; i < Rects.Length; i++)
            {
                Rects[i].sizeDelta = from.Sizes[i];

                // The entry itself is placed by the menu's layout.
                if (i > 0)
                {
                    Rects[i].anchoredPosition = from.Positions[i];
                }
            }

            for (var i = 0; i < Texts.Length; i++)
            {
                Texts[i].fontSize = from.FontSizes[i];
            }

            for (var i = 0; i < Layouts.Length; i++)
            {
                Layouts[i].minHeight = from.LayoutHeights[i].x;
                Layouts[i].preferredHeight = from.LayoutHeights[i].y;
            }
        }
    }
}
