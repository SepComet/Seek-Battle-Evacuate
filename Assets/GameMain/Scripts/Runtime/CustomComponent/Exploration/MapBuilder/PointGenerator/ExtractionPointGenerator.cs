using System;
using System.Collections.Generic;
using SepCore.CustomComponent;
using UnityEngine;

namespace SepCore.Exploration
{
    internal sealed class ExtractionPointGenerator
    {
        private readonly IRoundRandomSource _random;

        public ExtractionPointGenerator(IRoundRandomSource random)
        {
            _random = random;
        }

        public Vector2 Generate(IReadOnlyList<Vector2> definitions)
        {
            if (definitions.Count == 0)
            {
                throw new InvalidOperationException("Map definition does not contain an extraction point.");
            }

            return definitions[_random.NextInt(0, definitions.Count)];
        }
    }
}
