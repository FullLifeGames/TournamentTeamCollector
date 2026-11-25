# TournamentTeamCollector

This program collects all teams used in Smogon tournaments: it takes the tournament matches that [TournamentParser](https://github.com/FullLifeGames/TournamentParser) finds, scouts the Showdown replays of each tournament with [ShowdownReplayScouter](https://github.com/FullLifeGames/Showdown-Replay-Scouter) and writes the teams per tournament.

## Usage

```
TournamentTeamCollector --matches <output.json> [--parser-db <SmogonTournamentParser.db>] [--forums <forums.json>]
                        [--replay-db <ShowdownReplayScouter.db>] [--delay <seconds>] [--max-requests <count>]
TournamentTeamCollector <SmogonTournamentParser.db> [<ShowdownReplayScouter.db>]
```

### Where the matches come from

- `--matches` reads the `output.json` that `TournamentParser.Cmd` writes after each complete run (user name to user with matches, the result of `SmogonParser.GetMatchesForUsers()`). This sends no request to Smogon and needs no browser. Run the collector after the parser, in a folder of its own: it writes an `output.json` of its own.
- `--parser-db` is the fallback when `--matches` is not given or its file does not exist: the collector crawls Smogon itself with TournamentParser 3.0 (plain HTTP, a headless Chromium for Smogon's bot check, polite requests) and keeps the crawl state in that database, so after the first run only changes are read. The second form of the command, the one of version 1, does the same. Never point it at a database that a TournamentParser job writes at the same time; where a TournamentParser job runs, use `--matches`.

### Replays

The replays are downloaded from replay.pokemonshowdown.com and analysed; `--replay-db` (default `ShowdownReplayScouter.db` in the working folder) keeps the analysed teams, so a replay is downloaded once. Keep that file between runs.

- Requests go out one at a time with a pause of `--delay` seconds (default 1), wait as long as `Retry-After` asks, are retried only after timeouts, 408, 429 and 5xx answers, and carry the User-Agent `TournamentTeamCollector (+https://github.com/FullLifeGames/TournamentTeamCollector)`. This is TournamentParser's `PoliteHttpMessageHandler`. At the end the run prints how many requests it sent.
- About 40 % of the replays linked in Smogon's tournaments are gone (404). The collector remembers them in the same database and asks for them again after 30 days, instead of in every run.
- If the server refuses three requests in a row (403, or 429/5xx after the retries), the run stops with exit code 2.
- `--max-requests` spreads a long first run over several runs: the run stops after that many requests with exit code 3, and the next one continues from the analysed replays. The replays of a tournament are scouted in chunks of 20, each stored when it is done.
- ShowdownReplayScouter 1.15 stores its analysis under new keys (`<replay>.json+v2+<player>`; it now counts a form change such as Zacian-Crowned as one Pokémon), so a replay database of an older version is of no use to it. TournamentParser's output of 2026-11-16 names 27,494 tournaments with 467,449 distinct replays: a first run reads them all once, about 5½ days at the default delay. Spread it over several runs with `--max-requests`.

### Output

After a complete run it writes into the working folder:

- `output.json`: thread id to thread with its replays and teams, ordered by thread name
- `outputJustThreads.json`: thread id to thread (id, name, locked)
- `newOutput.json`: thread id to thread with replays and the scouting result including the formatted outputs
- `Tournaments/<thread id>.json` and `Tournaments/<thread id>.txt` for each tournament, and `Tournaments/outputJustThreads.json`
- `Matches/`: the Matches, Games and Teams grouped by Tournament and by Format, for the Tournament Teams view (see below)

Teams of replays that are gone are left out. The tournaments are scouted in the order of `output.json` and written one at a time, so a run holds the teams of one tournament, not of all (reading the 440 MB `output.json` of 2026-11-16 takes about 250 MB). The files are written into a staging folder and then moved into place, so `Tournaments/` holds only the tournaments of the current run. Matches without any tournament, a bot check, an unexpected page, a refusing replay server or a reached request limit leave all outputs as they were, `Matches/` included.

### Matches/

The other outputs list teams per thread. `Matches/` keeps what they leave out (who played whom, who won, when, which replay of which Match) and groups the threads of one Tournament ("SPL XVI - Week 1" to "SPL XVI - Finals") into one. The web app only displays these files, so everything it shows is derived here. All files are JSON without indentation, carry `"v": 1`, count days since 1970-01-01 (UTC) and use `-1` for "none"; a table entry is referred to by its position in the same file.

- `index.json`: `{"v", "generated", "formats": [{"id", "name", "games", "matches", "lastDay"}]}`, every Format with a Game, most Games first.
- `tournaments.json`: `{"v", "categories", "formats", "tournaments": [{"id", "name", "category", "year", "forum", "games", "formats": [[format, games]], "threads": [[thread id, Round, day of the first match post, matches, matches with a Game]]}]}`, every Tournament (also those without Games), newest first, threads oldest first. The Round is `""` when the thread is the whole Tournament.
- `players.json`: `{"v", "formats", "players": [[key, name, [Alts], [[format, games]]]]}`, every Player with a Game; the key is the parser's user key, the name as written on Smogon, the Alts the Showdown names the Player used.
- `Formats/<format id>.json` and `Tournaments/<tournament id>.json`: `{"v", "formats", "players": [[key, name]], "species", "items", "abilities", "teras", "moves", "teams", "games", "matches"}`.
  - A Team is the merged team of one thread as in `output.json`: up to six `[species, item, ability, tera, [moves], lead]`, followed by `[other names]` (species indices) when the scouter knows other names or a form name. `lead` is 1 if the scouter saw the Pokémon lead a Game of that thread. It knows the lead only when the replay shows no team preview (Gen 1 to 4); with team preview it is 0.
  - A Game is `[replay id, format, upload time, Alt of side 1, Alt of side 2, winner, Team of side 1, Team of side 2, side of Player a, views, rating]`: the winner by the replay (1, 2, 0 unknown), the side the Match's Player a played (1, 2, 0 unknown), views and rating 0 when unknown.
  - A Match is `[thread id, Player a, Player b, winner, day of the match post, [Games]]`: the winner by the thread (1 = a, 2 = b, 0 none), its Games in the order they were played.
  - Only replays that still exist are Games, and only Matches with a Game are listed. A Format file holds every Match with a Game in that Format, with all its Games (a best-of-three across generations is one Match); a Tournament file every Match of the Tournament.

How it is derived:

- **Tournament and Round** from the thread names (`TournamentNames`): the rules of the prototype of the Tournament Teams view, with its plain errors fixed. Threads whose Tournament names agree in their letters and digits are one Tournament, unless more than 150 days lie between them (a post date the parser could not read, which it writes as the time of its crawl, counts as unknown); a name that several Tournaments share gets the year the Tournament started ("PU Open (2016)"). A Tournament's id is its name in lower-case letters, digits and hyphens; if an older Tournament has it already, the id of its first thread follows. Ids stay the same from run to run while name and grouping do.
- **Category** from `Data/categories.json`: an override wins over the first name rule that matches, which wins over the rule of the nearest forum up the tree that has one, which wins over the fallback. The forums come from `--forums`, the `forums.json` TournamentParser writes next to `output.json` (`{"forums": {key: {"u", "p", "n"}}, "threads": {thread id: forum key}}`). Without it, or if the file does not exist, the run says so once and the Categories come from the name rules and the fallback.
- **Which Match a Game belongs to**: a replay is often linked twice (in its round thread and in a thread that collects replays, or by both players' posts). It belongs to the Match whose users have the Showdown names of its players, else to the Match of a round thread, else to the one posted first, and takes the Teams of that Match's thread. If that thread has no Teams for it, it stays with the thread it was first seen in.
- **Which Alt is which Player**: every Game votes, an Alt with the Showdown id of one of the Match's users 5 for that user, the pairing that implies 2 for each side, a Game without such a name 1 for each pairing; an Alt whose best users tie stays unassigned.
- **Overrides** in `Data/overrides.json` (shipped empty) correct what the rules get wrong:

```json
{
  "threads": { "3779094": { "tournament": "ADPL IX", "round": "Week 1" } },
  "tournaments": { "adpl-ix": { "name": "ADPL IX", "category": "Team Tournament" } },
  "players": { "mada alt": "mada" },
  "alts": { "Mada (smog)": "mada" }
}
```

  `threads` sets a thread's Tournament and Round; `tournaments` (by the id derived without the entry) a name or a Category; `players` makes a Smogon user key another Player's; `alts` gives a Showdown name its Player.

`Data/categories.json` lists the Categories in the order the site shows them, the fallback, the name rules (.NET regular expressions, case-insensitive, with an optional `except`) and the forum rules; no code depends on a Category's name:

```json
{
  "categories": ["Official", "Team Tournament", "Other"],
  "fallback": "Other",
  "names": [{ "category": "Team Tournament", "pattern": "premier league|world cup", "except": "tours plaza premier" }],
  "forums": [{ "category": "Official", "forums": ["tournaments.34", "smogon-tour.49"] }]
}
```

Both files are copied next to the program when it is built; a rule or override with an error stops the run before its first replay request. The run keeps the matches and the Games and Teams of all threads until the end, as small arrays of indices: on TournamentParser's output of 2026-11-17 the match table adds about 50 MB to the 105 MB the run holds anyway, and 270,000 Games with their Teams (simulated from the real replay links) about 70 MB. Writing the folder (some 6,000 files, 200 MB) takes about half a minute. It is written into the staging folder and moved into place with the other outputs.

Exit codes: 0 done, 1 error, 2 bot check, unexpected page or replay server refusing requests, 3 request limit reached, 64 usage.

## Tests

`dotnet test` runs offline: the replay server is a fake in memory that serves a saved replay (`TournamentTeamCollector.Tests/Fixtures`).

`DerivationTests` derives the Tournaments and Categories of a real parser output without downloading a replay, and measures memory with simulated Games; it runs only when asked for:

```
TTC_DERIVATION_MATCHES=output.json TTC_DERIVATION_FORUMS=forums.json TTC_DERIVATION_OUTPUT=derivation \
  dotnet test --filter FullyQualifiedName~DerivationTests
```

It writes `collector-derivation.txt` (counts per Category, the 40 Tournaments with the most threads and 40 at random, with their Rounds) and `derived-tournaments.json` into `TTC_DERIVATION_OUTPUT`.
