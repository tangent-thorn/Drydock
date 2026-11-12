# Changelog

What changed in each release. The section for a version is what shows in
the plugin installer when you update.

## 1.0.10

- The Projection chart has a faint second line for the best case, where
  every levelling sub starts farming on its best date instead of its worst.
  Hover the chart to see both. Everything else on the tab still goes off
  the worst case.

## 1.0.9

- The tables on Gil, Salvage, Levelling and Tracking have a Copy button
  now, it copies the table the way it's sorted so you can paste it straight
  into a spreadsheet. Numbers come out as plain numbers so you can add them
  up.
- Reworded the plugin description in the installer.

## 1.0.8

- If an Allagan Tools update changes what inventories.csv's columns mean,
  salvage now shows as unavailable with the reason instead of as wrong
  numbers.
- Only the recent part of Submarine Tracker's loot is read in full; the
  lifetime counters come from the database itself. Refreshes stay fast
  however old the database grows, and nothing visible changes.
- Days silent no longer shows -1 when a voyage is filed under tomorrow's
  day.
- An FC header no longer shows another same-named character's "sent today".
- Column sorting reuses its order between frames instead of resorting
  every frame.

## 1.0.7

- Click a column header to sort the tables on Gil, Salvage (per character),
  Levelling and Tracking. Click again to flip it, a third time to go back.

## 1.0.6

- What changed in an update now shows in the plugin installer.
- Settings are saved before Drydock stops reading when the plugin unloads.

## 1.0.5

- Early subs that would take forever on their current route get a typical
  levelling range instead (marked *), from 68 real subs levelled from rank 1.
- FCs Submarine Tracker remembers for a character who has since moved FC are
  left out.

## 1.0.4

- The milestone step on the Overview and Projection is a setting (250M by
  default).

## 1.0.3

- Settings grouped under headings, and a cog in the window's title bar.

## 1.0.2

- Setup and the installer show AutoRetainer's repo URL.

## 1.0.1

- Sharper big numbers on the Overview.

## 1.0.0

- First public release.
