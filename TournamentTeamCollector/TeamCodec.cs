using ShowdownReplayScouter.Core.Data;

namespace TournamentTeamCollector
{
    /// <summary>
    /// A Team as one small array of indices into the tables of <see cref="ScoutedGames"/>, so that a run can keep the Teams
    /// of all threads (about two per Game). Per Pokémon: species, item, ability, Tera type, a word with the lead flag and
    /// the counts, then the moves and the other names of the species. <see cref="None"/> stands for no value.
    /// </summary>
    public static class TeamCodec
    {
        public const int None = -1;

        private const int MaxMoves = 127;
        private const int MaxOtherNames = 255;

        /// <summary>One Pokémon of a decoded Team; indices are -1 for no value.</summary>
        public readonly record struct Member(int Species, int Item, int Ability, int Tera, bool Lead, int[] Moves, int[] OtherNames);

        public static int[] Encode(Team team, ScoutedGames tables)
        {
            var values = new List<int>(team.Pokemon.Count * 10);
            foreach (var pokemon in team.Pokemon)
            {
                var moves = (pokemon.Moves ?? []).Where(move => !string.IsNullOrEmpty(move)).Distinct().Take(MaxMoves)
                    .Select(tables.Moves.Intern).ToList();
                var otherNames = OtherNames(pokemon).Take(MaxOtherNames).Select(tables.Species.Intern).ToList();
                values.Add(tables.Species.Intern(pokemon.Name));
                values.Add(tables.Items.Intern(pokemon.Item));
                values.Add(tables.Abilities.Intern(pokemon.Ability));
                values.Add(tables.Teras.Intern(pokemon.TeraType));
                values.Add((pokemon.Lead ? 1 : 0) | moves.Count << 1 | otherNames.Count << 8);
                values.AddRange(moves);
                values.AddRange(otherNames);
            }
            return [.. values];
        }

        public static IEnumerable<Member> Decode(int[] team)
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
                yield return new Member(species, item, ability, tera, (flags & 1) == 1, moves, otherNames);
            }
        }

        /// <summary>
        /// The other names of the Pokémon's species among those the scouter gives: the base species of a form and the forms
        /// it took ("Landorus" for Landorus-Therian, "Necrozma-Ultra" for Necrozma-Dusk-Mane), without the details the replay
        /// log adds to a name ("Alomomola, F" is Alomomola). The scouter also lists the nickname a player gave the Pokémon;
        /// nicknames are left out: they are free text, tens of thousands of them, and no name of a species.
        /// </summary>
        public static IEnumerable<string> OtherNames(Pokemon pokemon) =>
            (pokemon.AltNames ?? []).Append(pokemon.FormName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Split(", ")[0].Trim())
                .Where(name => name.Length > 0 && name != pokemon.Name && IsSameSpecies(name, pokemon.Name))
                .Distinct();

        /// <summary>
        /// Whether two names are forms of one species: Showdown names a form by the species, a hyphen and the form, so both
        /// start with the same word. A nickname passes only if it is written like a form ("Rotom-W"), which is rare.
        /// </summary>
        private static bool IsSameSpecies(string name, string? other) =>
            other != null && name.AsSpan(0, WordLength(name)).SequenceEqual(other.AsSpan(0, WordLength(other)));

        private static int WordLength(string name) => name.IndexOf('-') is var hyphen and >= 0 ? hyphen : name.Length;
    }
}
