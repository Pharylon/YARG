using System;
using System.Collections.Generic;

namespace YARG.Career
{
    /// <summary>
    /// Layout math for the band home screen: the band's stars as a row of star icons, and its fans as a
    /// crowd of person icons. The view only instantiates icons where this says, so the look can be tuned
    /// and tested without Unity. Pure (no UnityEngine), so it is covered by Tests/YARG.Career.Tests.
    ///
    /// Everything is deterministic: the "random" tilt and jitter come from an integer hash of the icon's
    /// index, never from a time-seeded generator, so the screen looks the same every time it is opened and
    /// an icon does not jump around when an unrelated one is added.
    /// </summary>
    public static class CareerBandHomeLayout
    {
        /// <summary>Stars worth one gold star. A row of hundreds of stars would be unreadable.</summary>
        public const int STARS_PER_GOLD = 100;

        /// <summary>
        /// One icon to draw. The origin and units of <see cref="X"/>/<see cref="Y"/> are described on the
        /// method that returns it.
        /// </summary>
        public struct Placed
        {
            public float X, Y;
            /// <summary>Height of the icon, in the same units as the width/height inputs.</summary>
            public float Size;
            /// <summary>Tilt in degrees.</summary>
            public float Rotation;
            /// <summary>Crowd: which fan picture (0 to variants - 1). Stars: 0.</summary>
            public int Variant;
            /// <summary>Crowd: 0 = front row to 1 = back row. Stars: 0.</summary>
            public float Depth;
            /// <summary>Stars only: a gold star (worth <see cref="STARS_PER_GOLD"/>).</summary>
            public bool Gold;
        }

        // Stars

        /// <summary>Overlap of neighbouring stars when there is room: a touch, so they read as a set.</summary>
        private const float STAR_BASE_OVERLAP = 0.10f;

        /// <summary>Most overlap before stars shrink instead; past this a star is mostly hidden.</summary>
        private const float STAR_MAX_OVERLAP = 0.65f;

        /// <summary>Gold stars are this much bigger than regular ones, so they stand out in the pile.</summary>
        private const float GOLD_SCALE = 1.25f;

        /// <summary>
        /// Vertical wobble as a share of the star's size, and tilt range in degrees. Both zero: the
        /// stars sit upright on a straight line (a wobbling line was tried and read as untidy).
        /// </summary>
        private const float STAR_WOBBLE = 0f;
        private const float STAR_TILT = 0f;

        // Crowd

        /// <summary>Most person icons ever drawn: past this the crowd is a texture, and costs draw calls.</summary>
        public const int MAX_CROWD = 240;

        /// <summary>Distance between neighbours in a row, as a share of icon width (they overlap).</summary>
        private const float COLUMN_SPACING = 0.55f;

        /// <summary>Distance between rows, as a share of icon height (back rows peek over front ones).</summary>
        private const float ROW_SPACING = 0.30f;

        /// <summary>Back row size relative to the front row, for a little perspective.</summary>
        private const float BACK_ROW_SCALE = 0.80f;

        /// <summary>Horizontal jitter as a share of icon width, so the rows do not look drilled.</summary>
        private const float CROWD_X_JITTER = 0.12f;

        /// <summary>Vertical jitter as a share of icon height.</summary>
        private const float CROWD_Y_JITTER = 0.04f;

        /// <summary>Tilt range in degrees (either way).</summary>
        private const float CROWD_TILT = 4f;

        /// <summary>
        /// Least spacing (as a share of the normal spacing) tried when the crowd does not fit even at the
        /// minimum size, before the icons shrink below it.
        /// </summary>
        private const float MIN_TIGHTNESS = 0.02f;

        /// <summary>Aspect used when the caller passes none: a portrait icon, like a person.</summary>
        private const float DEFAULT_ASPECT = 2f / 3f;

        // Salts so each jittered property uses its own hash stream.
        private const uint SALT_X = 0x68E31DA4;
        private const uint SALT_Y = 0xB5297A4D;
        private const uint SALT_ROTATION = 0x1B56C4E9;
        private const uint SALT_VARIANT = 0x9E3779B9;
        private const uint SALT_VARIANT_BUMP = 0x7FEB352D;

        /// <summary>
        /// One gold star per <see cref="STARS_PER_GOLD"/> stars, the rest regular. 326 is (3, 26).
        /// </summary>
        public static (int gold, int regular) SplitStars(int totalStars)
        {
            if (totalStars <= 0)
            {
                return (0, 0);
            }

            return (totalStars / STARS_PER_GOLD, totalStars % STARS_PER_GOLD);
        }

        /// <summary>
        /// The star icons on one horizontal line, gold first (left), then regular, in draw order (left to
        /// right, later ones on top). The origin is the center of the row: <see cref="Placed.X"/> is the
        /// icon's center in [-width / 2, width / 2] and <see cref="Placed.Y"/> is 0 (a straight line).
        /// Icons are square.
        ///
        /// Few stars are drawn at <paramref name="maxSize"/> with a slight overlap. As the row gets crowded
        /// the stars overlap more first (a pile of medals still reads as "many"), and only then shrink
        /// towards <paramref name="minSize"/>; past that they overlap further. The row always fits the
        /// width: if even fully stacked minimum-size stars do not, they shrink below the minimum.
        /// </summary>
        public static List<Placed> Stars(int totalStars, float width, float maxSize, float minSize)
        {
            var result = new List<Placed>();
            var (gold, regular) = SplitStars(totalStars);
            int count = gold + regular;
            if (count == 0 || !(width > 0f) || !(maxSize > 0f))
            {
                return result;
            }

            minSize = minSize > 0f ? Math.Min(minSize, maxSize) : 0f;

            var row = new StarRow(gold, count);
            float size = maxSize;
            float overlap = STAR_BASE_OVERLAP;
            if (size * row.Extent(overlap) > width)
            {
                // 1. More overlap at full size.
                overlap = row.OverlapToFit(width / size);
                if (overlap > STAR_MAX_OVERLAP)
                {
                    // 2. Shrink at the most overlap.
                    overlap = STAR_MAX_OVERLAP;
                    size = width / row.Extent(overlap);
                    if (size < minSize)
                    {
                        // 3. Overlap past the limit at the minimum size, and as a last resort, shrink
                        // below the minimum with the stars fully stacked.
                        size = minSize;
                        overlap = row.OverlapToFit(width / size);
                        if (overlap > 1f)
                        {
                            overlap = 1f;
                            size = width / row.Extent(overlap);
                        }
                    }
                }
            }

            float span = size * row.Extent(overlap);
            float x = -span / 2f + size * StarScale(0, gold) / 2f;
            for (int i = 0; i < count; i++)
            {
                float iconSize = size * StarScale(i, gold);
                result.Add(new Placed
                {
                    X = x,
                    Y = Signed(i, SALT_Y) * STAR_WOBBLE * iconSize,
                    Size = iconSize,
                    Rotation = Signed(i, SALT_ROTATION) * STAR_TILT,
                    Variant = 0,
                    Depth = 0f,
                    Gold = i < gold,
                });

                if (i < count - 1)
                {
                    x += (1f - overlap) * size * (StarScale(i, gold) + StarScale(i + 1, gold)) / 2f;
                }
            }

            return result;
        }

        private static float StarScale(int index, int gold)
        {
            return index < gold ? GOLD_SCALE : 1f;
        }

        /// <summary>
        /// Width of a star row in units of the regular star size, for an overlap. Neighbouring centers are
        /// (1 - overlap) times their average width apart. The right edge is the last star's, unless the
        /// overlap is so high that the (bigger) last gold star pokes out past the smaller regular ones.
        /// The left edge is always the first star's, since gold comes first and is the biggest.
        /// </summary>
        private readonly struct StarRow
        {
            /// <summary>Sum of neighbour average widths across the whole row.</summary>
            private readonly float _pairs;
            /// <summary>Half of the first plus half of the last star.</summary>
            private readonly float _ends;
            /// <summary>Sum of neighbour average widths across the gold stars only.</summary>
            private readonly float _goldPairs;
            private readonly bool _hasGold;

            public StarRow(int gold, int count)
            {
                _pairs = 0f;
                for (int i = 0; i < count - 1; i++)
                {
                    _pairs += (StarScale(i, gold) + StarScale(i + 1, gold)) / 2f;
                }

                _ends = (StarScale(0, gold) + StarScale(count - 1, gold)) / 2f;
                _goldPairs = Math.Max(0, gold - 1) * GOLD_SCALE;
                _hasGold = gold > 0;
            }

            public float Extent(float overlap)
            {
                float extent = (1f - overlap) * _pairs + _ends;
                if (_hasGold)
                {
                    extent = Math.Max(extent, (1f - overlap) * _goldPairs + GOLD_SCALE);
                }

                return extent;
            }

            /// <summary>
            /// The least overlap that fits a row <paramref name="widthInSizes"/> regular stars wide; above
            /// 1 when even fully stacked stars do not fit.
            /// </summary>
            public float OverlapToFit(float widthInSizes)
            {
                float spacing = Spacing(widthInSizes, _pairs, _ends);
                if (_hasGold)
                {
                    spacing = Math.Min(spacing, Spacing(widthInSizes, _goldPairs, GOLD_SCALE));
                }

                return 1f - spacing;
            }

            /// <summary>Largest (1 - overlap) with pairs * spacing + ends within the width.</summary>
            private static float Spacing(float widthInSizes, float pairs, float ends)
            {
                if (pairs > 0f)
                {
                    return (widthInSizes - ends) / pairs;
                }

                // A single star: no overlap helps, it fits or it does not.
                return widthInSizes >= ends ? float.PositiveInfinity : -1f;
            }
        }

        /// <summary>
        /// How many person icons to draw for <paramref name="fans"/>: the square root, so the first fans
        /// each show up and a huge following still fits, capped at <see cref="MAX_CROWD"/>.
        /// 188 fans is 14 icons, 50,000 is 224.
        /// </summary>
        public static int CrowdSize(int fans)
        {
            if (fans <= 0)
            {
                return 0;
            }

            return (int) Math.Min(MAX_CROWD, Math.Ceiling(Math.Sqrt(fans)));
        }

        /// <summary>
        /// The person icons of the crowd, in draw order: back row first, front row last, each row left to
        /// right. The origin is the bottom-left of the <paramref name="width"/> x <paramref name="height"/>
        /// area: <see cref="Placed.X"/> is the icon's horizontal center, <see cref="Placed.Y"/> its bottom
        /// edge, and its width is <see cref="Placed.Size"/> * <paramref name="aspect"/>.
        ///
        /// The crowd fills the front row first, each row from the center outwards, so a small following is
        /// a knot of people at the bottom center and a big one fills the width, several rows deep.
        /// Neighbours overlap and odd rows are staggered by half a column. Icons start at
        /// <paramref name="maxSize"/> and shrink (to <paramref name="minSize"/>) until the crowd fits the
        /// area; past that the spacing tightens, and only then do they shrink further. Nothing is ever
        /// placed outside the area (rotation aside).
        /// </summary>
        public static List<Placed> Crowd(int fans, float width, float height, int variants, float maxSize,
            float minSize, float aspect)
        {
            var result = new List<Placed>();
            int count = CrowdSize(fans);
            if (count == 0 || !(width > 0f) || !(height > 0f))
            {
                return result;
            }

            variants = Math.Max(1, variants);
            aspect = aspect > 0f && !float.IsInfinity(aspect) ? aspect : DEFAULT_ASPECT;
            if (!(maxSize > 0f))
            {
                maxSize = height;
            }

            // Without a usable minimum, a quarter of the maximum keeps people recognisable.
            minSize = minSize > 0f ? Math.Min(minSize, maxSize) : maxSize / 4f;

            // The largest size that can fit at all: one icon (with jitter) within the height and width.
            float largest = Math.Min(maxSize, Math.Min(height / (1f + CROWD_Y_JITTER),
                width / (aspect * (1f + 2f * CROWD_X_JITTER))));
            float smallest = Math.Min(minSize, largest);

            var rows = new List<Row>();
            float size = largest;
            float tightness = 1f;
            if (!PlanRows(count, width, height, size, aspect, tightness, rows))
            {
                if (PlanRows(count, width, height, smallest, aspect, tightness, rows))
                {
                    // Binary search the largest size that fits; lo always fits, hi never does.
                    float lo = smallest;
                    float hi = largest;
                    for (int i = 0; i < 24; i++)
                    {
                        float mid = (lo + hi) / 2f;
                        if (PlanRows(count, width, height, mid, aspect, tightness, rows))
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }

                    size = lo;
                }
                else
                {
                    // Too many even at the minimum size: tighten the spacing, then shrink further.
                    size = smallest;
                    bool fits = false;
                    while (!fits && tightness > MIN_TIGHTNESS)
                    {
                        tightness = Math.Max(MIN_TIGHTNESS, tightness * 0.85f);
                        fits = PlanRows(count, width, height, size, aspect, tightness, rows);
                    }

                    for (int i = 0; !fits && i < 60 && size > 0f; i++)
                    {
                        size *= 0.85f;
                        fits = PlanRows(count, width, height, size, aspect, tightness, rows);
                    }

                    if (!fits)
                    {
                        return Fallback(count, width, variants, smallest, aspect);
                    }
                }
            }

            // PlanRows leaves the last plan it tried in rows, so plan the chosen one again.
            PlanRows(count, width, height, size, aspect, tightness, rows);

            // Fill index (front row center first, outwards, then the next row) drives the jitter, so an
            // icon keeps its look as fans join while the rows keep their shape.
            var offsets = new List<(float offset, int id)>();
            var placedRows = new List<Placed>[rows.Count];
            int firstId = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                offsets.Clear();
                for (int k = 0; k < row.Count; k++)
                {
                    offsets.Add((FillOffset(k, row.Staggered), firstId + k));
                }

                offsets.Sort((a, b) => a.offset.CompareTo(b.offset));

                float iconWidth = row.Size * aspect;
                var placed = new List<Placed>(row.Count);
                int previousVariant = -1;
                foreach (var (offset, id) in offsets)
                {
                    float x = width / 2f + offset * row.Spacing + Signed(id, SALT_X) * CROWD_X_JITTER * iconWidth;
                    x = Math.Clamp(x, iconWidth / 2f, Math.Max(iconWidth / 2f, width - iconWidth / 2f));

                    float yJitter = Signed(id, SALT_Y);
                    if (r == 0)
                    {
                        // The front row stands on the bottom edge: it may only lift.
                        yJitter = Math.Abs(yJitter);
                    }

                    float y = Math.Max(0f, row.Y + yJitter * CROWD_Y_JITTER * row.Size);

                    // Hashing alone repeats a neighbour's picture one time in `variants`; a repeat reads
                    // as a pattern, so bump it to another picture.
                    int variant = (int) (Hash(id, SALT_VARIANT) % (uint) variants);
                    if (variant == previousVariant && variants > 1)
                    {
                        variant = (int) ((variant + 1 + Hash(id, SALT_VARIANT_BUMP) % (uint) (variants - 1))
                            % (uint) variants);
                    }

                    previousVariant = variant;

                    placed.Add(new Placed
                    {
                        X = x,
                        Y = y,
                        Size = row.Size,
                        Rotation = Signed(id, SALT_ROTATION) * CROWD_TILT,
                        Variant = variant,
                        Depth = row.Depth,
                        Gold = false,
                    });
                }

                placedRows[r] = placed;
                firstId += row.Count;
            }

            for (int r = placedRows.Length - 1; r >= 0; r--)
            {
                result.AddRange(placedRows[r]);
            }

            return result;
        }

        private struct Row
        {
            public int Count;
            public float Size;
            public float Y;
            /// <summary>Distance between neighbouring centers.</summary>
            public float Spacing;
            /// <summary>Columns sit half a column off the center line.</summary>
            public bool Staggered;
            public float Depth;
        }

        /// <summary>
        /// Plans <paramref name="count"/> icons into as few rows as fit the width at front-row size
        /// <paramref name="size"/>, and whether those rows fit the height. Spacing is scaled by
        /// <paramref name="tightness"/>. Fills <paramref name="rows"/> front to back.
        /// </summary>
        private static bool PlanRows(int count, float width, float height, float size, float aspect,
            float tightness, List<Row> rows)
        {
            rows.Clear();
            if (!(size > 0f))
            {
                return false;
            }

            // The row count decides the perspective, which decides how many fit per row, so try each.
            for (int rowCount = 1; rowCount <= count; rowCount++)
            {
                int capacity = 0;
                for (int r = 0; r < rowCount && capacity < count; r++)
                {
                    capacity += RowCapacity(r, rowCount, count, width, size, aspect, tightness, out _, out _);
                }

                if (capacity < count)
                {
                    continue;
                }

                float y = 0f;
                int remaining = count;
                for (int r = 0; r < rowCount && remaining > 0; r++)
                {
                    float depth = Depth(r, rowCount);
                    float rowSize = RowSize(size, depth);
                    int rowCapacity = RowCapacity(r, rowCount, count, width, size, aspect, tightness,
                        out bool staggered, out float spacing);
                    if (y + rowSize * (1f + CROWD_Y_JITTER) > height)
                    {
                        return false;
                    }

                    int rowIcons = Math.Min(rowCapacity, remaining);
                    rows.Add(new Row
                    {
                        Count = rowIcons,
                        Size = rowSize,
                        Y = y,
                        Spacing = spacing,
                        Staggered = staggered,
                        Depth = depth,
                    });

                    remaining -= rowIcons;
                    y += ROW_SPACING * tightness * rowSize;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// How many icons fit across row <paramref name="row"/> of <paramref name="rowCount"/>, keeping
        /// each icon (with its jitter) inside the width. Odd rows are staggered half a column, unless the
        /// row is so narrow that only a centered icon fits.
        /// </summary>
        private static int RowCapacity(int row, int rowCount, int count, float width, float size, float aspect,
            float tightness, out bool staggered, out float spacing)
        {
            float iconWidth = RowSize(size, Depth(row, rowCount)) * aspect;
            spacing = COLUMN_SPACING * tightness * iconWidth;
            staggered = false;

            // Farthest a column center may sit from the middle.
            float reach = width / 2f - iconWidth * (0.5f + CROWD_X_JITTER);
            if (reach < 0f || !(spacing > 0f))
            {
                return 0;
            }

            if (row % 2 == 1 && reach >= spacing / 2f)
            {
                staggered = true;
                return (int) Math.Min(count, 2.0 * (Math.Floor(reach / spacing - 0.5) + 1.0));
            }

            return (int) Math.Min(count, 1.0 + 2.0 * Math.Floor(reach / spacing));
        }

        private static float Depth(int row, int rowCount)
        {
            return rowCount > 1 ? (float) row / (rowCount - 1) : 0f;
        }

        private static float RowSize(float size, float depth)
        {
            return size * (1f + (BACK_ROW_SCALE - 1f) * depth);
        }

        /// <summary>
        /// Column of the <paramref name="k"/>th icon to join a row, in columns from the middle: 0, +1, -1,
        /// +2, ... or, staggered, -0.5, +0.5, -1.5, ...
        /// </summary>
        private static float FillOffset(int k, bool staggered)
        {
            if (staggered)
            {
                return (k / 2 + 0.5f) * (k % 2 == 0 ? -1f : 1f);
            }

            return k == 0 ? 0f : (k + 1) / 2 * (k % 2 == 1 ? 1f : -1f);
        }

        /// <summary>
        /// Last resort for degenerate areas: one row spread evenly across the width, without jitter.
        /// </summary>
        private static List<Placed> Fallback(int count, float width, int variants, float size, float aspect)
        {
            var result = new List<Placed>(count);
            size = Math.Min(size, width / aspect);
            float iconWidth = size * aspect;
            float reach = Math.Max(0f, width / 2f - iconWidth / 2f);
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float) i / (count - 1) : 0.5f;
                result.Add(new Placed
                {
                    X = width / 2f - reach + 2f * reach * t,
                    Y = 0f,
                    Size = size,
                    Variant = (int) (Hash(i, SALT_VARIANT) % (uint) variants),
                });
            }

            return result;
        }

        /// <summary>
        /// A well-mixed 32-bit hash of <paramref name="index"/> (the "lowbias32" finalizer), so neighbouring
        /// indices get unrelated values.
        /// </summary>
        private static uint Hash(int index, uint salt)
        {
            unchecked
            {
                uint x = (uint) index * 0x9E3779B1u + salt;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>A deterministic value in [-1, 1] for <paramref name="index"/>.</summary>
        private static float Signed(int index, uint salt)
        {
            return (Hash(index, salt) & 0xFFFFFF) / (float) 0xFFFFFF * 2f - 1f;
        }
    }
}
