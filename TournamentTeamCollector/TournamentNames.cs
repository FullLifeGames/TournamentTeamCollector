using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TournamentTeamCollector
{
    /// <summary>
    /// Splits a thread name into its Tournament and Round ("SPL XVI - Week 1" is Week 1 of SPL XVI). Smogon's threads are
    /// named by hand in many spellings, so this is a heuristic: the rules of <c>split_name</c> of the prototype of the
    /// Tournament Teams view, with its plain errors fixed (see <see cref="Split"/>). The overrides file corrects single
    /// threads.
    /// </summary>
    public static class TournamentNames
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private const string NoteWords = @"(?:won|winner|replay|loser|sub|read post|announce|accepting|custom|prize|update|edit|#\d)";

        /// <summary>
        /// An aside in brackets, "[Won by X]", "(Replays Required)", "{$50 Prize}". Only an innermost pair counts: the
        /// prototype let "(" pair with "]" and cut "Season 13 (Practice Tour [BW OU #1]) Won by X" down to "Season 13) Won by X".
        /// </summary>
        private static readonly Regex Note = new(
            @"\s*(?:\[[^\[\]]*" + NoteWords + @"[^\[\]]*\]|\([^()]*" + NoteWords + @"[^()]*\)|\{[^{}]*" + NoteWords + @"[^{}]*\})",
            Options
        );

        /// <summary>"- Won by X" outside brackets is an aside too; it runs to the end of the name.</summary>
        private static readonly Regex WonBy = new(@"\s*[-–:|,]*\s*\(?\bwon by\b.*$", Options);

        private const string RoundWords =
            @"rounds?|rd\.?|week|wk|finals?|semi[- ]?finals?|semis|quarter[- ]?finals?|quarters|grand finals?|(?:the )?(?:grand |semi[- ]?)?finale"
            + @"|top \d+|playoffs?|losers?'?|winners?'?|day \d+|group stage|final stage|swiss|tiebreak(?:er)?s?|bracket|r\d+|primeira|segunda"
            + @"|terceira|quarta|quinta|sexta|s[eé]tima|oitava|nona|d[eé]cima|ronda|rodada|(?:manager |player |team )?sign[- ]?ups|replays"
            + @"|usage stats|pools|top cut|play-?ins?";

        /// <summary>"Second Round" is one Round, not the Tournament "Murkrow Cup Second".</summary>
        private const string Ordinal = @"(?:(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|final)\s+(?=round\b))?";

        /// <summary>The word that starts the Round, with the separator before it.</summary>
        private static readonly Regex Round = new(@"\s*[-:–|,.]?\s*[\[(]?\s*\b(" + Ordinal + "(?:" + RoundWords + @"))\b", Options);

        /// <summary>
        /// A bracket whose text starts with a Round: "[Round 2 Losers]", "(Losers Semifinals)", not "[Replays Required]" or
        /// "[Winner: X]".
        /// </summary>
        private static readonly Regex StartsWithRound = new(@"^[\[({]\s*(?!replays\b|winners?\s*[:=-])" + Ordinal + "(?:" + RoundWords + @")\b", Options);

        /// <summary>"[ROUND 4] Cool and Awesome Legends Z-A OU Tournament": the Round first, in brackets.</summary>
        private static readonly Regex LeadingRound = new(@"^\[(?<round>\s*" + Ordinal + "(?:" + RoundWords + @")\b[^\[\]]*)\]\s*(?<tournament>.*[a-z0-9].*)$", Options);

        private static readonly Regex Separated = new(@"[-:–|,.\[(]\s*$", Options);
        private static readonly Regex Trail = new(@"[\s\-:–|,.!(\[]+$", Options);
        private static readonly Regex Lead = new(@"^[\s\-:–|,.\[(]+", Options);
        private static readonly Regex Space = new(@"\s+", Options);

        /// <summary>
        /// Smogon Tour, and tournaments named after it: "Season 23 (Week 4 [SM OU #2]) Won by X", "Monotype Cup (Week 3 [BW
        /// Monotype #2]) Won by X". The tier in brackets stays in the Round: a week has one thread per tier.
        /// </summary>
        private static readonly Regex SmogonTour = new(@"^(?<tournament>.+?)\s*\((?<round>(?:Week \d+|Practice Tour|Test Week)\b[^()]*)", Options);
        private static readonly Regex Season = new(@"^Season \d+$", Options);

        /// <summary>Threads that collect the replays of other threads: "SPL XVI - Replays", "RBY OU Tournament Archives".</summary>
        private static readonly Regex Collection = new(@"replay|usage stat|archive|dump", Options);

        /// <summary>
        /// The Tournament and the Round of a thread, the Round "" when the thread is the whole Tournament. The fixes of the
        /// prototype's rules: asides in brackets pair only matching brackets, "Won by X" without brackets is an aside, a Round
        /// keeps its closing bracket ("Round 6 [3-2]", not "Round 6 [3-2"), "Swiss" names the Tournament unless a separator
        /// comes before it ("PU Swiss III - Round 3", "RBC VI - Gen 9 Swiss - Round 1"), a Round found right after a short name
        /// splits after a separator ("TLT: Round 1"), "Semi Finals" and "Quarter Finals" are Rounds, and "Top Cut",
        /// "Play-ins", "Rounds 8 &amp; 9", "Grand Finale" and "Player Signups" are Rounds like "Playoffs". A Round in brackets
        /// before the name names the Round ("[ROUND 4] Cool and Awesome ..."), and an aside that starts with a Round ("[Round 2
        /// Losers]") names it when the name has no other Round.
        /// </summary>
        public static (string Tournament, string Round) Split(string threadName)
        {
            var name = Space.Replace(WebUtility.HtmlDecode(threadName), " ").Trim();
            if (SmogonTour.Match(name) is { Success: true } smogonTour)
            {
                var tournament = smogonTour.Groups["tournament"].Value.Trim();
                if (Season.IsMatch(tournament))
                {
                    tournament = "Smogon Tour " + tournament;
                }
                return (tournament, Balance(WonBy.Replace(smogonTour.Groups["round"].Value, "")));
            }

            var withoutNotes = WonBy.Replace(Note.Replace(name, ""), "");
            if (LeadingRound.Match(withoutNotes) is { Success: true } leading)
            {
                return (Balance(Trail.Replace(leading.Groups["tournament"].Value, "").Trim()), Balance(leading.Groups["round"].Value.Trim()));
            }
            var cut = Cut(withoutNotes);
            if (cut < 0)
            {
                var withRoundNotes = WonBy.Replace(Note.Replace(name, note => StartsWithRound.IsMatch(note.Value.TrimStart()) ? note.Value : ""), "");
                if (Cut(withRoundNotes) is var roundCut and >= 0)
                {
                    (withoutNotes, cut) = (withRoundNotes, roundCut);
                }
            }

            var tournamentPart = withoutNotes;
            var roundPart = "";
            if (cut >= 0)
            {
                // A Round inside a bracket that opens before it: "Corsola Cup: NU Tournament [Third Round Pairings]".
                cut = UnclosedBracket(withoutNotes, cut) ?? cut;
                tournamentPart = withoutNotes[..cut];
                roundPart = withoutNotes[cut..];
            }
            tournamentPart = Balance(Trail.Replace(tournamentPart, "").Trim());
            roundPart = Balance(Lead.Replace(roundPart, ""));
            if (roundPart.Length > 0 && "-:–|,".Contains(roundPart[^1]))
            {
                roundPart = Trail.Replace(roundPart, "").Trim();
            }
            return (tournamentPart.Length > 0 ? tournamentPart : name, roundPart);
        }

        /// <summary>Where the Round starts in a name without asides, -1 if it has none.</summary>
        private static int Cut(string name)
        {
            foreach (Match round in Round.Matches(name))
            {
                var separated = Separated.IsMatch(name[..round.Groups[1].Index]);
                var word = round.Groups[1].Value.Split(' ')[^1];
                if (!separated && word.Equals("swiss", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // The name starts with the Round ("Round 9 Trios") or with a short word ("The Bracket Tournament").
                return LettersAndDigitsOf(name[..round.Index]).Length >= 2 && (round.Index > 3 || separated) ? round.Index : -1;
            }
            return -1;
        }

        /// <summary>
        /// Whether a thread collects the replays of other threads. A replay posted in a round thread and in a replay collection
        /// belongs to the match of the round thread.
        /// </summary>
        public static bool IsCollection(string threadName) =>
            Collection.IsMatch(Note.Replace(WebUtility.HtmlDecode(threadName), ""));

        /// <summary>
        /// The name reduced to lower-case letters and digits, accents dropped: threads whose Tournament names have the same
        /// key belong to the same Tournament ("UU Open" and "UU open!", "Pokémon Cup" and "Pokemon Cup"), as far as their
        /// dates allow.
        /// </summary>
        public static string Key(string name) => LettersAndDigitsOf(name.Normalize(NormalizationForm.FormD));

        /// <summary>
        /// Showdown's id of a name: lower-case letters and digits, everything else dropped ("Mada" and "mada" are one
        /// Showdown user; Showdown drops "é" rather than reading it as "e").
        /// </summary>
        public static string ShowdownId(string name) => LettersAndDigitsOf(name);

        private static string LettersAndDigitsOf(string text)
        {
            var key = new StringBuilder(text.Length);
            foreach (var c in text.ToLowerInvariant())
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    key.Append(c);
                }
            }
            return key.ToString();
        }

        /// <summary>
        /// A file name and URL segment for a name: lower-case letters, digits and hyphens, accents dropped ("Pokémon" is
        /// "pokemon"), at most <paramref name="maxLength"/> characters. Empty if the name has no letter or digit.
        /// </summary>
        public static string Slug(string name, int maxLength = 80)
        {
            var slug = new StringBuilder(name.Length);
            foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    slug.Append(c);
                }
                else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && slug.Length > 0 && slug[^1] != '-')
                {
                    slug.Append('-');
                }
            }
            var text = slug.ToString().Trim('-');
            if (text.Length > maxLength)
            {
                var hyphen = text.LastIndexOf('-', maxLength);
                text = text[..(hyphen > maxLength / 2 ? hyphen : maxLength)].Trim('-');
            }
            return text;
        }

        private static int? UnclosedBracket(string text, int end)
        {
            var open = new Stack<int>();
            for (var i = 0; i < end; i++)
            {
                if (text[i] is '[' or '(' or '{')
                {
                    open.Push(i);
                }
                else if (text[i] is ']' or ')' or '}' && open.Count > 0)
                {
                    open.Pop();
                }
            }
            return open.Count > 0 ? open.Peek() : null;
        }

        /// <summary>Drops closing brackets without an opening one and opening brackets that are never closed.</summary>
        private static string Balance(string text)
        {
            var chars = new StringBuilder(text.Length);
            var open = new Stack<int>();
            foreach (var c in text)
            {
                switch (c)
                {
                    case '[' or '(' or '{':
                        open.Push(chars.Length);
                        chars.Append(c);
                        break;
                    case ']' or ')' or '}':
                        if (open.Count > 0 && chars[open.Peek()] == (c == ']' ? '[' : c == ')' ? '(' : '{'))
                        {
                            open.Pop();
                            chars.Append(c);
                        }
                        else
                        {
                            chars.Append(' ');
                        }
                        break;
                    default:
                        chars.Append(c);
                        break;
                }
            }
            foreach (var position in open)
            {
                chars[position] = ' ';
            }
            return Space.Replace(chars.ToString(), " ").Trim();
        }
    }
}
