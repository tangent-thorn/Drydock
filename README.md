# Drydock

In-game dashboard for FFXIV submarines. Inspired from the massive spreadsheets
many players with big fleets use to keep track of data. Compiles data from
several other plugins then makes it readable.


Local only: Drydock reads the files the other plugins already keep on your
PC and its own small database next to them. No network calls, no telemetry.

![Overview](docs/overview.png)

What's in it:

- **Overview**: cards for salvage today (with the last two weeks), how many
  subs are farming, how many are back, and where you stand against your next
  defined milestone. A timeline of everything coming back in the next 24
  hours, how many days of ceruleum and kits the fleet has left, the characters
  lowest on kits, and warnings for characters running low. Uses your Dalamud
  theme colours.
- **Fleet**: every sub grouped by FC, with rank, route and a live countdown
  to when it's back.
- **Levelling**: every sub that isn't on a gil route yet, with its route,
  voyage time, EXP per voyage, and a best to worst range for the days left
  to your target rank (85 unless you change it) and the dates. It works off
  the game's own EXP table and each sub's real parts and route, and uses
  how often you actually resend it. Early subs that would take forever on
  their current route get a typical range instead (marked *), from 68 real
  subs levelled from rank 1 while moving to better routes. Plus a rough
  rank spread of the fleet.
- **Salvage**: salvage today and yesterday, the 30 and 7 day average with
  the trend, per sub per day, a 90 day chart, what fuel and kits cost you
  per day, net, and how many days of ceruleum and kits you have left. Per
  gil route it shows your real gil per voyage and cycle time next to what
  the route is expected to make. With Allagan Tools installed it also shows the salvage you're
  sitting on: per item, what it's worth and how many slots it takes, and per
  character. Prices are the vendor sell prices of the eight salvage items.
- **Gil**: gil on hand across characters and retainers (straight from
  AutoRetainer), unsold salvage, and the total, with a table per character.
- **History**: Drydock keeps one row a day of its own (gil, salvage held,
  supplies, farming subs, salvage that day) and charts it, with what you
  actually made per day next to what the model says. Once it has seen 400
  sectors it measures your real ceruleum and kit use per sector and uses
  that for upkeep instead of the settings.
- **Projection**: where your gil is headed over the next year, with subs
  that are still levelling joining the farm on their worst-case date. When
  you hit your gil target (1B unless you change it), where you land at one,
  three, six and twelve months, every milestone (250M apart unless you
  change it), and what you make now against what you'll make once every
  sub is farming.
- **Tracking**: what Drydock is reading per character (subs, voyages today,
  days since the last one, ceruleum, kits). Untick a character to leave it
  out of everything. Every AutoRetainer character is listed, subs or not.
  Subs AutoRetainer still remembers but that don't exist anymore are listed
  there too, so you know why they're ignored, and so are FCs Submarine
  Tracker remembers for a character who has since moved to another FC.
- A server info bar entry: "Subs 3 back, next in 1h 12m". Click it to open
  Drydock, or turn it off in settings.

The tables on Gil, Salvage, Levelling and Tracking sort by any column, just
click the header.

A sub counts as farming when it's on one of the gil routes (OJ, MOJ, ROJ,
JOZ, MROJ, MOJZ, JORZ, MROJZ, RZVOJ, MRVOJ), whatever its rank.

Heads up: on a fresh install the fuel and kit numbers are the defaults in
settings until Drydock has a few days of its own history to measure them.
Still keeping an eye on how close those land.

## Screenshots

Names swapped out, numbers are my real fleet.

| | |
|---|---|
| ![Fleet](docs/fleet.png) | ![Salvage](docs/salvage.png) |
| ![Gil](docs/gil.png) | ![Projection](docs/projection.png) |

## Install

Settings > Experimental > Custom Plugin Repositories, add

    https://raw.githubusercontent.com/tangent-thorn/Drydock/main/repo.json

then install Drydock from /xlplugins.

## What it needs

- AutoRetainer, from the Puni.sh repo (https://love.puni.sh/ment.json). Required.
- Submarine Tracker. Required.
- Allagan Tools and CashFlow are optional. Allagan Tools adds the salvage
  you haven't sold yet; CashFlow gives the upkeep your real repair kit price
  (median of what you paid over the last 120 days) instead of the setting.

If something's missing, `/drydock setup` tells you what and links the installer.

## Commands

`/drydock` opens it. `/drydock setup`, `/drydock refresh`, `/drydock config`
(or the cog in the window's title bar).

Settings has the warning limits (days quiet, low ceruleum, low kits), when
data counts as stale, the info bar toggle, how often you resend subs
(for the levelling dates), fuel and kit prices and usage per sector for
the upkeep numbers, and the gil target and milestone step for the
projection.

AGPL-3.0.
