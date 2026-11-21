namespace TournamentTeamCollector
{
    /// <summary>
    /// Which Player each Alt is, and so which side of a Game the Match's Player a played. A replay names Showdown users, a
    /// Match names Smogon users, and players often use other names on Showdown. As in the prototype of the Tournament Teams
    /// view, every Game votes: an Alt with the Showdown id of one of the Match's users votes 5 for that user, the pairing it
    /// implies 2 for each side, and a Game without any such name 1 for each of the four pairings. Across many Games the user
    /// an Alt plays for wins; an Alt whose best users tie stays unassigned. The overrides file assigns Alts by hand.
    /// <para>The prototype counted votes per spelling ("Mada" and "mada" apart); they are counted per Showdown id here.</para>
    /// </summary>
    public static class AltPlayers
    {
        public sealed class Assignment
        {
            internal Assignment(int[] altId, int[] playerOfAltId, string[] altIdNames)
            {
                _altId = altId;
                _playerOfAltId = playerOfAltId;
                _altIdNames = altIdNames;
            }

            private readonly int[] _altId;
            private readonly int[] _playerOfAltId;
            private readonly string[] _altIdNames;

            /// <summary>The Player of an Alt (an index into <see cref="ScoutedGames.Alts"/>), or -1.</summary>
            public int PlayerOf(int alt) => alt >= 0 && _altId[alt] >= 0 ? _playerOfAltId[_altId[alt]] : -1;

            /// <summary>The Alts of each Player that has any, as spelled in their newest Game.</summary>
            public Dictionary<int, List<string>> AltsOfPlayers()
            {
                var alts = new Dictionary<int, List<string>>();
                for (var id = 0; id < _playerOfAltId.Length; id++)
                {
                    if (_playerOfAltId[id] is var player and >= 0)
                    {
                        if (!alts.TryGetValue(player, out var list))
                        {
                            alts.Add(player, list = []);
                        }
                        list.Add(_altIdNames[id]);
                    }
                }
                foreach (var list in alts.Values)
                {
                    list.Sort(StringComparer.OrdinalIgnoreCase);
                }
                return alts;
            }

            /// <summary>
            /// The side (1 or 2) that the Match's Player a played in the Game, from the Players of its two Alts; 0 if they
            /// do not tell.
            /// </summary>
            public byte SideOfA(ScoutedGames.GameEntry game, MatchTable.MatchEntry match)
            {
                var (p1, p2) = (PlayerOf(game.Alt1), PlayerOf(game.Alt2));
                if (match.A >= 0 && p1 == match.A && p2 != match.A || match.B >= 0 && p2 == match.B && p1 != match.B)
                {
                    return 1;
                }
                if (match.A >= 0 && p2 == match.A && p1 != match.A || match.B >= 0 && p1 == match.B && p2 != match.B)
                {
                    return 2;
                }
                return 0;
            }
        }

        public static Assignment Assign(ScoutedGames games, Overrides overrides)
        {
            var matches = games.Matches;
            var ids = new StringTable();
            var altId = new int[games.Alts.Count];
            for (var alt = 0; alt < altId.Length; alt++)
            {
                altId[alt] = ids.Intern(TournamentNames.ShowdownId(games.Alts[alt]));
            }
            var playerIds = matches.PlayerKeys.Select(TournamentNames.ShowdownId).ToArray();

            var votes = new Dictionary<(int AltId, int Player), int>();
            void Vote(int alt, int player, int weight)
            {
                if (alt >= 0 && player >= 0)
                {
                    votes[(alt, player)] = votes.GetValueOrDefault((alt, player)) + weight;
                }
            }
            bool Same(int alt, int player) => alt >= 0 && player >= 0 && ids[alt] == playerIds[player];

            var newest = new (string Name, int UploadTime)[ids.Count];
            foreach (var game in games.Games)
            {
                var match = matches.Matches[game.Match];
                var (a1, a2) = (game.Alt1 >= 0 ? altId[game.Alt1] : -1, game.Alt2 >= 0 ? altId[game.Alt2] : -1);
                var (u1, u2) = (match.A, match.B);
                foreach (var (alt, name) in new[] { (a1, game.Alt1), (a2, game.Alt2) })
                {
                    if (alt >= 0 && (newest[alt].Name == null || game.UploadTime >= newest[alt].UploadTime))
                    {
                        newest[alt] = (games.Alts[name], game.UploadTime);
                    }
                    foreach (var user in new[] { u1, u2 })
                    {
                        if (Same(alt, user))
                        {
                            Vote(alt, user, 5);
                        }
                    }
                }
                if (a1 < 0 || a2 < 0 || u1 < 0 || u2 < 0)
                {
                    continue;
                }
                if (Same(a1, u1) || Same(a2, u2))
                {
                    Vote(a1, u1, 2);
                    Vote(a2, u2, 2);
                }
                else if (Same(a1, u2) || Same(a2, u1))
                {
                    Vote(a2, u1, 2);
                    Vote(a1, u2, 2);
                }
                else
                {
                    Vote(a1, u1, 1);
                    Vote(a1, u2, 1);
                    Vote(a2, u1, 1);
                    Vote(a2, u2, 1);
                }
            }

            var best = new (int Player, int Votes, bool Tie)[ids.Count];
            Array.Fill(best, (-1, 0, false));
            foreach (var ((alt, player), count) in votes)
            {
                if (count > best[alt].Votes)
                {
                    best[alt] = (player, count, false);
                }
                else if (count == best[alt].Votes)
                {
                    best[alt].Tie = true;
                }
            }
            var playerOfAltId = best.Select(entry => entry.Tie ? -1 : entry.Player).ToArray();
            foreach (var (alt, playerKey) in overrides.Alts)
            {
                var id = ids.Find(TournamentNames.ShowdownId(alt));
                if (id >= 0 && matches.FindPlayer(playerKey.Trim().ToLowerInvariant()) is int player)
                {
                    playerOfAltId[id] = player;
                }
            }
            return new Assignment(altId, playerOfAltId, newest.Select((entry, id) => entry.Name ?? ids[id]).ToArray());
        }
    }
}
