using System.Collections.Generic;

namespace YARG.Career
{
    /// <summary>
    /// Which of the band's songs plays behind its home screen: a random one of the songs it has
    /// stars on, each as likely as it has stars (4 stars is picked 4 times in 11 next to a 4 and a 3).
    /// </summary>
    public static class CareerBandAnthem
    {
        /// <summary>
        /// The index picked by <paramref name="roll"/>, a number from 0 up to (not including) the sum
        /// of the weights. Weights of zero or less are never picked; -1 when nothing can be.
        /// </summary>
        public static int Pick(IReadOnlyList<int> weights, int roll)
        {
            if (weights is null || roll < 0)
            {
                return -1;
            }

            for (var i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0)
                {
                    continue;
                }

                if (roll < weights[i])
                {
                    return i;
                }

                roll -= weights[i];
            }

            return -1;
        }

        public static int Total(IReadOnlyList<int> weights)
        {
            var total = 0;
            for (var i = 0; i < (weights?.Count ?? 0); i++)
            {
                if (weights[i] > 0)
                {
                    total += weights[i];
                }
            }

            return total;
        }
    }
}
