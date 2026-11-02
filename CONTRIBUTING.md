# Contributing

Bug reports and ideas are welcome, open an issue. PRs too, but read this
first so we're not both wasting time.

## Issues

- Say which Drydock version you're on (it's in `/xlplugins`) and which of
  AutoRetainer, Submarine Tracker, Allagan Tools and CashFlow you have
  installed and enabled.
- Say which tab and what you expected to see.
- `/xllog` lines from around the problem help a lot. Filter on `Drydock`.
- **Blur or cut your character and retainer names** out of screenshots and
  logs before posting. Drydock never needs them to figure out a bug.

## Pull requests

- Open an issue first for anything bigger than a small fix, so we can agree
  on it before you build it.
- Keep a PR to one thing. Small ones get looked at fast, big ones sit.
- Drydock only reads files the other plugins already write on your own PC.
  It never sends anything anywhere and never touches the game or other
  players. PRs that add network calls, hooks or game automation won't be
  merged.
- No new plugin dependencies. The plan is fewer, not more.
- Build with the Dalamud dev files like any plugin (`dotnet build -c
  Release`). The test suite isn't in this repo, so I run it before merging
  and will tell you if something breaks.
- Match the code around what you change. No big reformat or rename passes.

## Releases

I cut releases from `main` when there's enough in it. Your change ships in
the next one, and the repo URL in the README picks it up on its own.
