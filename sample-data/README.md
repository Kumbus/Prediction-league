# Sample import data

Test fixtures for the admin surface (`admin-seed-tournament`).

## sample-matches.csv
Manual-match CSV import. Admin → a tournament's detail page → **Import CSV**.

- Headers: `HomeTeam,AwayTeam,KickoffUtc,Status,HomeScore,AwayScore,Round`
- Teams are matched by name and **auto-created** when missing (no need to pre-add them).
- `KickoffUtc` is an ISO instant (`Z` = UTC). `Status` ∈ `Scheduled | Live | Finished`.
- A `Finished` row must have both scores; `Scheduled` rows leave them blank.
- Preview first (dry-run), then Commit.

## sample-players.csv
Player CSV import. Admin → **Players** → **Import**.

- Headers: `Name,NationalityCode,Position,DateOfBirth,HeightCm,ExternalPlayerId`
- `NationalityCode` is ISO 3166-1 **alpha-3** and must exist in the seeded Nationalities
  (Germany = `DEU`).
- Bind to a tournament (optional) to also fill its squad.

The two files share the same four nations (Poland/Spain/France/Germany) so the players line up
with the teams created by the match import.
