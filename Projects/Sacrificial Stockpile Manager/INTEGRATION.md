# Sacrificial Stockpile Manager and SHEPHERD: integration contract

[SHEPHERD](https://github.com/chrumck/SHEPHERD), built by chrumck, is an inventory script for a programmable block. Sacrificial Stockpile Manager (SSM) is a client plugin. Where SHEPHERD runs on a grid, SSM leaves SHEPHERD's jobs to it, shows its state, and can hand SSM's setup over to it. This file lists everything SSM relies on from SHEPHERD and everything SSM writes for it. SHEPHERD's side of the contract is its own INTEGRATION.md.

SSM never uses IGC and never runs SHEPHERD except when the player presses Export (below).

## 1. What SSM reads

All of it is data the game already has on the player's computer: block names, Custom Data and LCD text. Reading it sends nothing to the server. SSM relies on these items of SHEPHERD's stable contract:

| Contract item | What SSM does with it |
|---|---|
| `[Settings]` keys `refineryFillPct` and `sortPassSeconds` in a programmable block's Custom Data | Marks the block as a SHEPHERD block |
| "SHEPHERD" in the script text | When the client has the script text, a block whose text lacks it is skipped (a block that switched scripts but kept the Custom Data) |
| `[Settings]` values: `reactorFillPct`, `gasGenFillPct`, `refineryFillPct`, `bottleSweepMinutes`, `priority`, `factionShare`, `dockedProduction`, `dockedFuel` | A fill % or bottle sweep of 0 keeps that job with SSM; docked machines count as SHEPHERD's only with `dockedFuel` / `dockedProduction`; `priority` for the election; `factionShare` for ownership |
| `[Reserve]` / `[Reserve:Name]` quota lines (`<amount>[M\|L][A]` or `All`) and `[Categories]` | Items with an A line are left to SHEPHERD's crafting; SSM shows the line in its Items view |
| Ownership and `factionShare` | A SHEPHERD block counts when the player owns it, or when it's a faction-mate's with `factionShare=true` |
| Election ranking: priority x 4 + static x 2 + large, lower entity id wins a tie | Among several SHEPHERD blocks on one terminal system, SSM follows the leader. Working blocks come first |
| Status panel `[SHEPHERD]`: line 1 `SHEPHERD <version> · <health>`, line 2 the drawing block's name, line 3 a clock that changes on every redraw, problems below the last separator line | SSM reads the panel whose line 2 is the leader's name. The version and health are shown in SSM's Overview. Lines below the separator are shown, and new ones that aren't `crafting`, `disassembling` or `refining` get a chat line |
| Health words `working`, `idle`, `learning`, `HALTED`, `standing by` | `working`, `idle` and `learning` count as running (SSM steps back). `HALTED` and `standing by` count as not running (SSM does every job) |
| Liveness: the panel text changes on every redraw | No change for max(90 s, 3 s x the number of SHEPHERD screens) counts as stopped. There's no verdict in the first 3 minutes after SSM first sees a panel. SHEPHERD screens are blocks without an inventory whose names carry a `[SHEPHERD...]` tag |
| Tag grammar: `[SHEPHERD]`, `[SHEPHERD:cats(:#N)]`, `[SHEPHERD-RESERVE(:Name)]`, `-Main`, `-Coop`, `-Off`, `-Split` (keyword case-insensitive) | `[SHEPHERD:...]` and `-RESERVE` containers are SHEPHERD's: SSM never moves items into or out of them. `-Main` / `-Coop` assemblers only get SHEPHERD's orders. `-Off` blocks are SSM's. `-Split` connectors limit the units SSM considers SHEPHERD's |
| `configReload` | Run once after an export |

SSM does **not** read the programmable block's Echo / detail pane. A multiplayer client only receives it while the block is open in the terminal.

With no status panel, SSM assumes SHEPHERD runs while its block is on, built and powered, and suggests adding a panel.

## 2. What SSM does while SHEPHERD runs

These jobs are left to SHEPHERD by default. The player can keep any of them for SSM in SSM's Overview:

| Job | SSM stops |
|---|---|
| Sorting | Sorting storage, draining connectors and collectors |
| Docked ships | Restocking docked ships for their quotas |
| Reactor and generator fuel | Their minimums and maximums |
| Refineries | Refinery minimums and maximums, refinery priority, refine by ingot need |
| Production output | Emptying refinery and assembler output |
| Crafting its A quotas | Crafting and disassembling items that have a SHEPHERD A line |
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
| Storage block accepting categories | `[SHEPHERD:<categories>]` appended to the name, plus `:#N` for SSM priority N > 0. Categories map to Ores, Ingots, Components, Ammo, Tools, Consumables, Others |
| Storage block accepting a single item | A custom category named after the item, in `[Categories]` |
| Storage block accepting bottles | A `Bottles` category, with every bottle item in `[Categories]` |
| General storage container | `[SHEPHERD:*]` |
| Stock block | `[SHEPHERD-RESERVE:<name>]` and a `[Reserve:<name>]` section. A minimum alone becomes `<min>M`, a maximum alone `<max>L`, both become `<max>` |
| Manual block | `[SHEPHERD-Off]` |
| Grid quotas (with Autocraft on) | Section `[Reserve:Quotas]` with no container. A quota becomes `<min>MA`. With Disassemble surplus on, a maximum alone becomes `<max>LA`, and a quota equal to its maximum becomes `<min>A`. A quota with a higher maximum keeps only the `MA` (the confirm box lists those). Ores are skipped |
| SSM Main / Co-op assemblers (when quotas are written and no `[SHEPHERD-Main]` exists) | The first Main gets `[SHEPHERD-Main]`; other Mains and the Co-ops get `[SHEPHERD-Coop]` |
| Then | `configReload` (the terminal's Run, needs the player to own the block) |

The sections it writes (`[Reserve:Quotas]`, `[Reserve:<name>]`) are replaced whole. The `[Categories]` lines are set one by one. Everything else in the Custom Data is kept (parsed and written with MyIni).

## 4. Change rule

- A change to anything in sections 1 or 3 starts as an issue labelled `integration` on SHEPHERD's repository, before the release that makes it.
- It ships with a version bump and an "Integration" heading in SSM's release notes.
- This file changes in the same commit as the code.
- SHEPHERD's sample files (status panels per health word, a stand-by panel, a sample `[Settings]`) are pinned to a SHEPHERD release tag. SSM's parser test reads them, and the pin moves when SHEPHERD releases.
- People approve every post. No agent pushes to the other project's repository.
