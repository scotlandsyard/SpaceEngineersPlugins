# Sacrificial Stockpile Manager and SHEPHERD: integration contract

[SHEPHERD](https://github.com/chrumck/SHEPHERD), built by chrumck, is an inventory script for a programmable block. Sacrificial Stockpile Manager (SSM) is a client plugin. Where SHEPHERD runs on a grid, SSM leaves SHEPHERD's jobs to it, shows its state, and can hand SSM's setup over to it. This file lists everything SSM relies on from SHEPHERD and everything SSM writes for it. SHEPHERD's side is its own [INTEGRATION.md](https://github.com/chrumck/SHEPHERD/blob/main/INTEGRATION.md): SSM relies only on its Part A (promised), and reads nothing from its Part B.

SSM never uses IGC, never works out which SHEPHERD block leads, and never runs SHEPHERD except when the player presses Export (section 3).

## 1. What SSM reads

All of it is data the game already has on the player's computer: block names, Custom Data and LCD text. Reading it sends nothing to the server. The parsing is in `Source/SacrificialStockpileManager/ShepherdFormat.cs`.

| SHEPHERD's Part A | What SSM does with it |
|---|---|
| A1 Detection: a programmable block whose Custom Data has `[Settings]` with `refineryFillPct`, and `SHEPHERD` in the script text when the client has it; or a `[SHEPHERD]` status panel | SSM looks only at the player's own blocks and panels. Another player's SHEPHERD manages only its owner's blocks |
| A2 Job switches: `reactorFillPct`, `gasGenFillPct`, `refineryFillPct`, `bottleSweepMinutes`, `dockedFuel`, `dockedProduction`, read as A2 says (key names case-sensitive, value cut at the first `;`, a number counts only when it parses with `.` and is in range, otherwise the default; booleans `true`/`false` in any case, anything else `false`) | A fill % or bottle sweep of 0 keeps that job with SSM. Docked machines count as SHEPHERD's only with `dockedFuel` / `dockedProduction`. With `refineryFillPct=0`, ingot quotas stay with SSM's refine by ingot need |
| A3 Status panel: line 1 `SHEPHERD <version> · <health>`, line 2 the drawing block's name, line 3 a clock that changes on every redraw; `\r\n` and `\n` alike | See "Running" below. The version and health word are shown in SSM's Overview. The block named on line 2 picks whose settings apply when the player has several SHEPHERD blocks (the first working one when names repeat) |
| A3 Health words | `working`, `idle` and `learning` mean SHEPHERD is doing its jobs. `standing by` means another SHEPHERD block is, never that SHEPHERD isn't running. `HALTED` means it isn't, for now |
| A4 Tags (`[SHEPHERD:...(:#N)]`, `-RESERVE(:Name)`, `-Main`, `-Coop`, `-Off`, `-Split`, `[SHEPHERD]`; keyword case-insensitive) and reach | `[SHEPHERD:...]` and `-RESERVE` containers are SHEPHERD's: SSM never moves items into or out of them. `-Main` / `-Coop` assemblers only get SHEPHERD's orders. `-Off` blocks are SSM's. SHEPHERD's reach (its construct and those joined over connected connectors, except past `-Split`) is the set of units SSM treats as SHEPHERD's |
| A4 Never managed: weapons, parachute hatches; gas tanks only for bottles | Limits the player sets on weapons and tanks stay SSM's |
| A5.1 `[Reserve]` / `[Reserve:Name]` lines: key with an optional `MyObjectBuilder_` prefix, value `<amount>[M\|L][A]` or `All` (letters in any case); anything else ignored; `docked` is a pool setting | Items with an A line are left to SHEPHERD's production (SSM's Items view shows the line). Lines SHEPHERD would ignore are ignored here too |
| A5.2 `[Categories]` | Not used, except that export sets its own lines there (section 3) |
| A6 `configReload` | Run once after an export |

**Running.** SSM counts SHEPHERD as doing its jobs while any of the player's `[SHEPHERD]` panels on the grid has a moving clock and a `working`, `idle`, `learning` or `standing by` health word. Any one moving panel is enough, since SHEPHERD redraws only the first. A panel counts as frozen when its text hasn't changed for max(90 s, 3 s × the number of SHEPHERD screens), never within 3 minutes of SSM first seeing it. SSM takes every job back when every panel is frozen or `HALTED`, or when all the player's SHEPHERD blocks are off, damaged or unpowered. It hands the jobs back by itself on its next refresh (1 to 3 s) once a panel moves again with a running word. With no panel at all, SSM assumes SHEPHERD runs while one of the player's SHEPHERD blocks works, and suggests adding a panel.

**Not read.** The Echo / detail pane (a multiplayer client only receives it while the block is open in the terminal), anything below line 3 of the panel except to show it, the election, `priority`, `factionShare`, `sortPassSeconds`, and the other screens. SSM shows the panel's lines below the last rule in its Overview. New ones get a chat line from SSM's "Shepherd (SSM)" voice, except actions (`crafting`, `disassembling`, `refining`), a standing-by panel's leader line, and anything while the health word is `learning`. This is informative only, and nothing depends on it.

## 2. What SSM does while SHEPHERD runs

These jobs are left to SHEPHERD by default. The player can keep any of them for SSM in SSM's Overview:

| Job | SSM stops |
|---|---|
| Sorting | Sorting storage, draining connectors and collectors. Weapons and gas tanks were never part of it |
| Docked ships | Restocking docked ships for their quotas |
| Reactor and generator fuel | Their minimums and maximums |
| Refineries | Refinery minimums and maximums, refinery priority, refine by ingot need |
| Production output | Emptying refinery and assembler output |
| Crafting its A quotas | Crafting and disassembling items that have a SHEPHERD A line (ingots only while `refineryFillPct` > 0) |
| Bottles | Refilling bottles |

SSM also warns about setups where the two would undo each other's work:
- an SSM-stocked block that SHEPHERD would empty;
- an assembler with both SSM's mode and a SHEPHERD tag;
- a quota both keep;
- fuel or refineries kept for SSM while SHEPHERD's fill % is above 0.

## 3. What SSM writes (Export setup to SHEPHERD)

Only when the player presses it and confirms. It changes only the grid itself (not docked ships), only blocks the player owns, and skips blocks that already have a SHEPHERD tag. Each change is the same request as editing it in the terminal.

| SSM setting | Written |
|---|---|
| Storage block accepting categories | `[SHEPHERD:<categories>]` appended to the name, plus `:#N` for SSM priority N > 0. In both, a higher number fills first among equally good containers. Categories map to Ores, Ingots, Components, Ammo, Tools, Consumables, Others |
| Storage block accepting a single item | A custom category named with the item's type, e.g. `Ingot/Iron=IronIngot`, in `[Categories]`. SHEPHERD then sends that item only to its own containers (then the catch-all), not to its built-in category's. In SSM, an item-specific container is only the first choice, so the confirm box says what changes |
| Storage block accepting bottles | A `Bottles` category, with every bottle item in `[Categories]` |
| General storage container | `[SHEPHERD:*]` |
| Stock block | `[SHEPHERD-RESERVE:<name>]` and a `[Reserve:<name>]` section. A minimum alone becomes `<min>M`. A maximum alone becomes `<max>L`: SSM only caps such an item, but `L` also pulls it in up to the amount, which the confirm box says. Both become `<max>` |
| Manual block | `[SHEPHERD-Off]` |
| Grid quotas (with Autocraft on) | Section `[Reserve:Quotas]`, with no container (from 1.0.5 SHEPHERD doesn't warn about an all-A pool). A quota becomes `<min>MA`. With Disassemble surplus on, a maximum alone becomes `<max>LA`, and a quota equal to its maximum becomes `<min>A`. Ingots never get `LA`. A quota with a higher maximum keeps only the `MA`. SHEPHERD adds up all its pools, so each amount is lowered by what other pools keep of the item: plain and `M` lines for the amount made up to, all lines for the amount held back from disassembly. Those are the stock blocks exported now and pools already in the Custom Data that the export doesn't replace. A line that comes to 0 is left out. With `refineryFillPct=0`, ingot quotas aren't written. Ores are skipped. The confirm box lists each of these |
| SSM Main / Co-op assemblers (when quotas are written and no `[SHEPHERD-Main]` exists) | The first Main gets `[SHEPHERD-Main]`; other Mains and the Co-ops get `[SHEPHERD-Coop]` |
| Then | `configReload` (the terminal's Run, needs the player to own the block). With SHEPHERD older than 1.0.21 (or of unknown version), SSM re-reads the Custom Data 5 s later, puts back any `[Categories]` line it wrote that is missing, and runs `configReload` once more |

Category and pool names are letters and digits only, so none contains `[`, `]` or `,`, or ends in `:#N`.

The sections it writes (`[Reserve:Quotas]`, `[Reserve:<name>]`) are replaced whole. The `[Categories]` lines are set one by one. Everything else in the Custom Data is kept (written with MyIni). SSM never writes `[Settings]`, `[Production]`, `[Recipes]` or `[Yields]`.

## 4. Contract test

`Tests/` parses SHEPHERD's sample files with SSM's own parsing code. The samples are the status panel for each health word and `settings-defaults.ini`, copied unchanged into `Tests/Fixtures/<tag>`, currently pinned to SHEPHERD **v1.0.21**. The test also checks the A2 reading rules and A5.1 quota lines. Run `dotnet run -c Release` in `Tests`. The pin moves when SHEPHERD releases new samples (`Tests/Fixtures/README.md`).

## 5. Change rule

- A change to anything in sections 1 or 3 starts as an issue labelled `integration` on SHEPHERD's repository, before the release that makes it.
- It ships with a version bump and an "Integration" heading in SSM's release notes.
- This file changes in the same commit as the code.
- People approve every post. No agent pushes to the other project's repository.
