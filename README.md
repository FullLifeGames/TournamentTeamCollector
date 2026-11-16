# TournamentTeamCollector

This program collects all teams used in Smogon tournaments: it takes the tournament matches that [TournamentParser](https://github.com/FullLifeGames/TournamentParser) finds, scouts the Showdown replays of each tournament with [ShowdownReplayScouter](https://github.com/FullLifeGames/Showdown-Replay-Scouter) and writes the teams per tournament.

## Usage

```
TournamentTeamCollector --matches <output.json> [--parser-db <SmogonTournamentParser.db>]
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

Teams of replays that are gone are left out. The tournaments are scouted in the order of `output.json` and written one at a time, so a run holds the teams of one tournament, not of all (reading the 440 MB `output.json` of 2026-11-16 takes about 250 MB). The files are written into a staging folder and then moved into place, so `Tournaments/` holds only the tournaments of the current run. Matches without any tournament, a bot check, an unexpected page, a refusing replay server or a reached request limit leave all outputs as they were.

Exit codes: 0 done, 1 error, 2 bot check, unexpected page or replay server refusing requests, 3 request limit reached, 64 usage.

## Tests

`dotnet test` runs offline: the replay server is a fake in memory that serves a saved replay (`TournamentTeamCollector.Tests/Fixtures`).
