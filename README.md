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

- **Overview**: salvage today with the last two weeks behind it, how many
  subs are farming, how many are back, and how far you are from your next
  milestone. Under that there's a timeline of everything coming back in
  the next 24 hours, how many days of ceruleum and kits the fleet has
  left, who's lowest on kits, and a warning when a character is running
  low. It picks up your Dalamud theme colours too.
- **Fleet**: every sub grouped by FC, with rank, route and a countdown.
- **Levelling**: every sub that isn't on a gil route yet, with its route,
  voyage time, EXP per voyage, and how many days until it hits your target
  rank (85 unless you change it) as a best to worst range with the dates.
  It works off the game's own EXP table, each sub's actual parts and
  route, and how often you really resend it, not a perfect schedule. Early
  subs that would take forever on their current route get a typical range
  instead (marked with a *), based off 68 real subs levelled from rank 1
  while moving to better routes. Plus a rough rank spread of the fleet.
- **Salvage**: today, yesterday, the 30 and 7 day averages and which way
  it's trending, per sub per day, and a 90 day chart. It works out what
  fuel and kits cost you per day so you see net, and how long your
  ceruleum and kits will last. For each gil route you get your real gil
  per voyage and cycle time next to what the route should make. If you
  have Allagan Tools it also shows the salvage you're sitting on, per item
  and per character, what it's worth and how many slots it's taking up.
  Prices are the vendor sell prices of the eight salvage items.
- **Gil**: gil on hand across all your characters and retainers (straight
  from AutoRetainer), unsold salvage, and the total, with a table per
  character.
- **History**: Drydock saves one row a day on its own (gil, salvage held,
  supplies, farming subs, salvage that day) and charts it, with what you
  actually made each day next to what the model said you would. Once it's
  seen 400 sectors it measures your real ceruleum and kit use per sector
  and uses that for upkeep instead of the numbers in settings.
- **Projection**: where your gil is headed over the next year. Subs that
  are still levelling join the farm on their worst case date, so it leans
  conservative. It shows when you'll hit your gil target (1B unless you
  change it), where you'll be at 1, 3, 6 and 12 months, every milestone
  along the way (250M apart by default), and what you make now compared to
  once every sub is farming.
- **Tracking**: what Drydock is reading for each character, subs, voyages
  today, days since the last one, ceruleum and kits. Untick a character
  and it's left out of everything. Every AutoRetainer character shows up
  here whether it has subs or not. Subs AutoRetainer still remembers that
  don't exist anymore are listed too so you know why they're being
  ignored, same with FCs Submarine Tracker remembers for a character that
  has since moved to a different FC.
- A server info bar entry like "Subs 3 back, next in 1h 12m". Click it to
  open Drydock, or turn it off in settings if you don't want it.

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
