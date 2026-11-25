using NUnit.Framework;

namespace TournamentTeamCollector.Tests
{
    /// <summary>Thread names as Smogon has them, from the parser's output of November 2026.</summary>
    public class TournamentNamesTests
    {
        [TestCase("Official Smogon Tournament XIII - Round 1", "Official Smogon Tournament XIII", "Round 1")]
        [TestCase("NDOTSL II - Week 4", "NDOTSL II", "Week 4")]
        [TestCase("ORASPL III - Week Four", "ORASPL III", "Week Four")]
        [TestCase("Ubers UU Open Quarterfinals", "Ubers UU Open", "Quarterfinals")]
        [TestCase("BLT Group Stages: Week 3", "BLT Group Stages", "Week 3")]
        [TestCase("Ubers Ladder Tournament II - Playoffs | Round 2", "Ubers Ladder Tournament II", "Playoffs | Round 2")]
        [TestCase("RANDOM POKEMON TOURNAMENT VI Round One: The First Round", "RANDOM POKEMON TOURNAMENT VI", "Round One: The First Round")]
        // Asides in brackets are dropped, also several of them and with HTML entities.
        [TestCase("Ubers Winter Seasonal II - Round 1 [Read Post #49]", "Ubers Winter Seasonal II", "Round 1")]
        [TestCase("Smogon Exhibition - Finals [Won by Untier University]", "Smogon Exhibition", "Finals")]
        [TestCase("Ubers Summer Seasonal IX - Round 12 (POST YOUR REPLAYS) (Winners Finals)", "Ubers Summer Seasonal IX", "Round 12")]
        [TestCase("Official Neverused Seasonal I - Round 4 [Replays Required] {$50 USD Prize to Charity}", "Official Neverused Seasonal I", "Round 4")]
        [TestCase("NeverUsed Homefield Advantage I - Round 5 [$50 Grand Prize] {Replay&#039;s Required}", "NeverUsed Homefield Advantage I", "Round 5")]
        [TestCase("Smogon Masters IV - Play-Ins &amp; Top Cut Round 1", "Smogon Masters IV", "Play-Ins & Top Cut Round 1")]
        // "Won by" without brackets is an aside too.
        [TestCase("Stellar Cup - Grand Finals - Won by Ashbala", "Stellar Cup", "Grand Finals")]
        [TestCase("The Monotype Cup Playoffs - Won by Kev", "The Monotype Cup", "Playoffs")]
        // A Round keeps its brackets.
        [TestCase("Tours Plaza Spring Seasonal: Round 12 [Grand Finals] [Won by RoFnA]", "Tours Plaza Spring Seasonal", "Round 12 [Grand Finals]")]
        [TestCase("PSPL XIV - Playoffs Round of 16 [Tiebreaker @ post 66]", "PSPL XIV", "Playoffs Round of 16 [Tiebreaker @ post 66]")]
        [TestCase("1v1 Hybrid Elimination - Round 4 (READ RULES BEFORE PLAYING)", "1v1 Hybrid Elimination", "Round 4 (READ RULES BEFORE PLAYING)")]
        [TestCase("Doubles World Cup III - SEMI FINALS (ITALY v EUROPE, US CENTRAL v US WEST)", "Doubles World Cup III", "SEMI FINALS (ITALY v EUROPE, US CENTRAL v US WEST)")]
        // "Semi Finals" and "Quarter Finals" are one Round.
        [TestCase("NUPL XI - Semi Finals", "NUPL XI", "Semi Finals")]
        [TestCase("Retro Cup of Pokémon 2023 - Quarter Finals [TB @ post #37]", "Retro Cup of Pokémon 2023", "Quarter Finals")]
        // "Swiss" names the Tournament unless a separator comes before it.
        [TestCase("ADV OU Swiss I: Round 7", "ADV OU Swiss I", "Round 7")]
        [TestCase("1v1 Swiss I - Top 16", "1v1 Swiss I", "Top 16")]
        [TestCase("DOU Swiss - Top Cut Round 3", "DOU Swiss", "Top Cut Round 3")]
        [TestCase("SV Ubers UU Swiss 3 - Top Cut Play-ins", "SV Ubers UU Swiss 3", "Top Cut Play-ins")]
        // Short names split after their separator.
        [TestCase("NPA - Week 1", "NPA", "Week 1")]
        [TestCase("NPA - Playoffs - Semi-Finals", "NPA", "Playoffs - Semi-Finals")]
        [TestCase("NPA - Finals (Tie-Breaker)", "NPA", "Finals (Tie-Breaker)")]
        // Smogon Tour and the tournaments named after it keep the tier in the Round.
        [TestCase("Season 9 (Week 1 [DPP OU #2]) Won by eaglesrock", "Smogon Tour Season 9", "Week 1 [DPP OU #2]")]
        [TestCase("Season 13 (Practice Tour [BW UU #1]) Won by Eo Ut Mortus", "Smogon Tour Season 13", "Practice Tour [BW UU #1]")]
        [TestCase("Season 3 (Week 4 [ADV 2]) Won by Clefairy{m}", "Smogon Tour Season 3", "Week 4 [ADV 2]")]
        [TestCase("Monotype Cup (Week 2 [ORAS Monotype #1]) Won by SaDiSTiCNarwhal", "Monotype Cup", "Week 2 [ORAS Monotype #1]")]
        [TestCase("Smogon Tour Season 22 Playoffs [Won by BKC]", "Smogon Tour Season 22", "Playoffs")]
        // More spellings of a Round.
        [TestCase("VGC Seasonal - Rounds 8 &amp; 9", "VGC Seasonal", "Rounds 8 & 9")]
        [TestCase("HGPL V: Finale [WON BY FROS LASSES]", "HGPL V", "Finale")]
        [TestCase("NUFL II - The Finale (WON BY KARAS CUNO)", "NUFL II", "The Finale")]
        [TestCase("Portuguese Ladder Tournament V - Sétima Rodada (Loser&#039;s Finals)", "Portuguese Ladder Tournament V", "Sétima Rodada")]
        [TestCase("ORASPL III - Player Signups [Custom Avatar Prize] [Auction September 23 @ 12:30 PM GMT -4]", "ORASPL III", "Player Signups [Auction September 23 @ 12:30 PM GMT -4]")]
        // The Round before the name, and a Round in an aside when there is no other.
        [TestCase("[ROUND 6] Cool and Awesome Legends Z-A OU Tournament", "Cool and Awesome Legends Z-A OU Tournament", "ROUND 6")]
        [TestCase("[ROUND 2] [REPLAYS MANDATORY] Cool and Awesome Legends Z-A OU Tournament", "Cool and Awesome Legends Z-A OU Tournament", "ROUND 2")]
        [TestCase("[Semi-Finals] Cool and Awesome Legends Z-A OU Tournament", "Cool and Awesome Legends Z-A OU Tournament", "Semi-Finals")]
        [TestCase("National Dex PU Money Tour - $150 Dollar Prize Pool [Round 3 Losers] [REPLAYS REQUIRED]", "National Dex PU Money Tour - $150 Dollar Prize Pool", "Round 3 Losers")]
        [TestCase("National Dex Monotype Ladder Tournament! (Losers Semifinals)", "National Dex Monotype Ladder Tournament", "Losers Semifinals")]
        [TestCase("Pet Mods Premier League II - [GRAND FINALS won by Dave’s Dachsbuns]", "Pet Mods Premier League II", "GRAND FINALS")]
        [TestCase("Forre Offgon Zard (ADV Team Building Competition Round 2 Winner)", "Forre Offgon Zard", "")]
        [TestCase("HOENN STADIUM Kickoff Tournament [Winner: Fungi_man]", "HOENN STADIUM Kickoff Tournament", "")]
        // The whole Tournament in one thread.
        [TestCase("SSPL II - Finalissima [Won by the Turia Slowpokes]", "SSPL II - Finalissima", "")]
        [TestCase("Doubles Little Cup 2024 Circuit Championship (Won by PigWarrior19)", "Doubles Little Cup 2024 Circuit Championship", "")]
        public void SplitsTheThreadNameIntoTournamentAndRound(string threadName, string tournament, string round)
        {
            Assert.That(TournamentNames.Split(threadName), Is.EqualTo((tournament, round)));
        }

        [TestCase("SPL XVI - Replays", true)]
        [TestCase("RBY OU Tournament Archives", true)]
        [TestCase("SSPL IV - Replays and Useage", true)]
        [TestCase("Official Neverused Seasonal I - Round 4 [Replays Required]", false)]
        [TestCase("SPL XVI - Week 1", false)]
        public void KnowsTheThreadsThatCollectReplays(string threadName, bool collection)
        {
            Assert.That(TournamentNames.IsCollection(threadName), Is.EqualTo(collection));
        }

        [TestCase("ADPL IX", "adpl-ix")]
        [TestCase("Retro Cup of Pokémon 2023", "retro-cup-of-pokemon-2023")]
        [TestCase("The (un)Official DP Ladder Tournament", "the-un-official-dp-ladder-tournament")]
        [TestCase("SS OU Summer Seasonal #4", "ss-ou-summer-seasonal-4")]
        [TestCase("宝可梦", "")]
        public void SlugsAreLowerCaseLettersDigitsAndHyphens(string name, string slug)
        {
            Assert.That(TournamentNames.Slug(name), Is.EqualTo(slug));
        }

        [Test]
        public void SlugsAreCutAtAHyphen()
        {
            Assert.That(TournamentNames.Slug("Ubers UU Open Spring Seasonal", 20), Is.EqualTo("ubers-uu-open-spring"));
        }
    }
}
