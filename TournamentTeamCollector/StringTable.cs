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
