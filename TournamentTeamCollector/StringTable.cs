namespace TournamentTeamCollector
{
    /// <summary>
    /// Strings interned to indices, in the order they were first seen: the run keeps each species, move or Showdown name
    /// once, and Teams and Games keep indices.
    /// </summary>
    public sealed class StringTable
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];
        private readonly int _maxCount;

        /// <param name="maxCount">More strings than this are an error: <see cref="TeamCodec"/> keeps indices in 16 bits.</param>
        public StringTable(int maxCount = int.MaxValue)
        {
            _maxCount = maxCount;
        }

        public int Count => _values.Count;

        public string this[int index] => _values[index];

        /// <summary>The index of the string, added if new; -1 for null or empty.</summary>
        public int Intern(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }
            if (!_index.TryGetValue(value, out var index))
            {
                if (_values.Count >= _maxCount)
                {
                    throw new InvalidOperationException($"More than {_maxCount} distinct values; the team encoding holds no more.");
                }
                index = _values.Count;
                _values.Add(value);
                _index.Add(value, index);
            }
            return index;
        }

        /// <summary>The index of the string, or -1 if it is not in the table.</summary>
        public int Find(string? value) => value != null && _index.TryGetValue(value, out var index) ? index : -1;
    }
}
