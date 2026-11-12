# Drydock

An in-game dashboard for your FFXIV submarines. It started from the giant
spreadsheets a lot of people with big fleets end up keeping, so Drydock
pulls the data the other plugins already have and puts it in one window
you can actually read.

Everything stays on your PC, it reads the files AutoRetainer and Submarine
Tracker already keep plus a small database of its own next to them, there
are no network calls and no telemetry.

![Overview](docs/overview.png)

## What's in it

- **Overview**: salvage today, how many subs are farming and back, your
  next milestone, everything coming back in the next 24 hours, and who's
  running low on ceruleum or kits.

- **Fleet**: every sub grouped by FC, with rank, route and a countdown.

- **Levelling**: every sub not on a gil route yet, with EXP per voyage and
  a best to worst range of days until it hits your target rank (85 by
  default), based off its real parts, route and how often you resend it.

- **Salvage**: today, the 7 and 30 day averages, a 90 day chart, net after
  fuel and kits, and real gil per voyage against what each route should
  make. With Allagan Tools it also shows the salvage you haven't sold yet.

- **Gil**: gil across all your characters and retainers plus unsold
  salvage, with a table per character.

- **History**: one row a day of gil, salvage and supplies, charted against
  what the model expected. After 400 sectors it uses your real fuel and kit
  use instead of the settings.

- **Projection**: where your gil is headed over the next year, when you'll
  hit your target (1B by default), and every milestone along the way. A
  faint second line shows the best case.

- **Tracking**: what Drydock is reading per character. Untick one to leave
  it out of everything.

- The tables on Gil, Salvage, Levelling and Tracking have a Copy button
  if you'd rather work the numbers in a spreadsheet.

- A server info bar entry like "Subs 3 back, next in 1h 12m". Click it to
  open Drydock, or turn it off in settings.

A sub counts as farming when it's on one of the gil routes (OJ, MOJ, ROJ,
JOZ, MROJ, MOJZ, JORZ, MROJZ, RZVOJ, MRVOJ), doesn't matter what rank it is.

On a fresh install the fuel and kit numbers come from the defaults in
settings until Drydock has a few days of its own history to go off. I'm
still keeping an eye on how close those defaults land.

## Screenshots

Example data

| | |
|---|---|
| ![Fleet](docs/fleet.png) | ![Salvage](docs/salvage.png) |
| ![Gil](docs/gil.png) | ![Projection](docs/projection.png) |

## Install

In Dalamud go to Settings > Experimental > Custom Plugin Repositories and
add

    https://raw.githubusercontent.com/tangent-thorn/Drydock/main/repo.json

then install Drydock from /xlplugins.

## What you need

- AutoRetainer, from the Puni.sh repo (https://love.puni.sh/ment.json). Required.
- Submarine Tracker. Required.
- Allagan Tools and CashFlow are optional. Allagan Tools adds the salvage
  you haven't sold yet, and CashFlow lets upkeep use what you actually
  paid for repair kits (the median over the last 120 days) instead of the
  price in settings.

If you're missing something, `/drydock setup` will tell you what and link
the installer.

## Commands

`/drydock` opens it. There's also `/drydock setup`, `/drydock refresh` and
`/drydock config` (or just hit the cog in the window's title bar).

In settings you can change the warning limits (days quiet, low ceruleum,
low kits), when data counts as stale, the info bar toggle, how often you
resend subs (used for the levelling dates), fuel and kit prices and usage
per sector for upkeep, and the gil target and milestone step for the
projection.

## License

AGPL-3.0.
