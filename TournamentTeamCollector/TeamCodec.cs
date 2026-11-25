using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    /// <summary>
    /// A Team as one small array of 16-bit indices into the tables of <see cref="ScoutedGames"/>, so that a run can keep
    /// the Teams of all threads (about two per Game). Per Pokémon: species, item, ability, Tera type, a word with the lead
    /// flag and the counts, then the moves and the other names of the species. <see cref="None"/> stands for no value.
    /// </summary>
    public static class TeamCodec
    {
        public const ushort None = ushort.MaxValue;

        /// <summary>Table sizes the encoding can index.</summary>
        public const int MaxTableSize = ushort.MaxValue;

        private const int MaxMoves = 127;
        private const int MaxOtherNames = 255;

        /// <summary>One Pokémon of a decoded Team; indices are -1 for no value.</summary>
        public readonly record struct Member(int Species, int Item, int Ability, int Tera, bool Lead, int[] Moves, int[] OtherNames);

        public static ushort[] Encode(Team team, ScoutedGames tables)
        {
            var values = new List<ushort>(team.Pokemon.Count * 8);
            foreach (var pokemon in team.Pokemon)
            {
                var moves = (pokemon.Moves ?? []).Where(move => !string.IsNullOrEmpty(move)).Distinct().Take(MaxMoves)
                    .Select(move => Index(tables.Moves.Intern(move))).ToList();
                var otherNames = OtherNames(pokemon).Take(MaxOtherNames).Select(name => Index(tables.Species.Intern(name))).ToList();
                values.Add(Index(tables.Species.Intern(pokemon.Name)));
                values.Add(Index(tables.Items.Intern(pokemon.Item)));
                values.Add(Index(tables.Abilities.Intern(pokemon.Ability)));
                values.Add(Index(tables.Teras.Intern(pokemon.TeraType)));
                values.Add((ushort)((pokemon.Lead ? 1 : 0) | moves.Count << 1 | otherNames.Count << 8));
                values.AddRange(moves);
                values.AddRange(otherNames);
            }
            return [.. values];
        }

        public static IEnumerable<Member> Decode(ushort[] team)
        {
            var i = 0;
            while (i + 5 <= team.Length)
            {
                var (species, item, ability, tera, flags) = (team[i], team[i + 1], team[i + 2], team[i + 3], team[i + 4]);
                i += 5;
                var moves = new int[(flags >> 1) & MaxMoves];
                for (var m = 0; m < moves.Length; m++)
                {
                    moves[m] = team[i++];
                }
                var otherNames = new int[flags >> 8];
                for (var n = 0; n < otherNames.Length; n++)
                {
                    otherNames[n] = team[i++];
                }
                yield return new Member(Value(species), Value(item), Value(ability), Value(tera), (flags & 1) == 1, moves, otherNames);
            }
        }

        /// <summary>
        /// The other names the scouter knows for the Pokémon (the base species of a form, a form name), without the details
        /// the replay log adds to a name ("Alomomola, F" is Alomomola).
        /// </summary>
        public static IEnumerable<string> OtherNames(Pokemon pokemon) =>
            (pokemon.AltNames ?? []).Append(pokemon.FormName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Split(", ")[0].Trim())
                .Where(name => name.Length > 0 && name != pokemon.Name)
                .Distinct();

        private static ushort Index(int index) => index < 0 ? None : (ushort)index;

        private static int Value(ushort value) => value == None ? -1 : value;
    }
}
