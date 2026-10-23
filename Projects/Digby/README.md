# Digby

A client-side [Pulsar](https://github.com/StarCpt/Pulsar) plugin for Space Engineers that plans a tunnel to a deposit: from where you stand (or any GPS) to a spot a given depth under the surface somewhere else on the same planet, or straight to a point in space. It reads the planet and the real terrain height from the game, gives the angles and the route, marks the start, the turns and the end, and writes the plan onto a datapad you can hand round.

Open it with `/digby`, or from the plugin list at the top left of any of our plugin windows.

## The plan

Pick **From** (Here, a GPS, a library point, or a pasted GPS), **Target** (roughly the spot on the surface above the deposit) and **Depth** (metres under the surface at the target), then **Calculate**:

- **Straight tunnel**: length and angle below the horizon. Aim at the *Dig target* marker.
- **Down then across**: dig straight down to the target's altitude (as a cockpit shows it, sea level), then across. *Dig turn* marks where you turn.
- Notes when the straight line sags deeper in the middle (planets are round), ends below the lowest terrain, or is very long.
- The window shows which planet you're on (or that you're in space) and how deep you are.

**In space** (both points in space, e.g. an asteroid) the tunnel goes straight to the target, so put the target GPS on the deposit; the depth is ignored.

### Rig line

For extending a grid toward the deposit without hinges or rotors: P1 and P2 are two GPS on the grid along its forward axis, the optional Up point a GPS straight above P1 (needed in space). The plan adds the turn and pitch against the true horizon and against the grid itself, the tunnel in whole blocks forward / sideways / down with the step pattern per 10 blocks, and the route as straight legs along the grid with a *Dig rig turn* marker at each corner. Small grid switches to 0.5 m blocks.

### Markers

*Dig start*, *Dig target* and the turn markers are **local** by default: on your HUD and in your GPS list, never sent to the server, gone when you leave the world. Tick **As server GPS** for normal GPS entries. A new plan replaces the old markers.

## Datapad

**Write datapad** writes the plan onto a datapad in your own inventory: the plan's name, which planet it's on (or that it's in space), the start of the dig, a GPS line for every marker, then the whole plan. It's named `Dig: <plan>` (`#2` if you already carry one by that name). Whoever opens it can press the game's **Create GPS** button to add the start of the dig as a GPS; the other markers are plain GPS lines to copy.

- Digby can't make datapads; carry one. *Auto* uses an empty datapad or the ones already holding this plan; pick one in the list to use that one (it asks before overwriting other text).
- A datapad holds 1000 characters, so a long plan uses several, named `(1/2)`, `(2/2)`. If you don't carry enough, Digby asks before writing a shortened plan.
- A few seconds later Digby checks the datapads show the new name.

## Library

**Save to library** moves the plan's markers into Digby's library for this world (off your GPS list). The Library page can use a point as From or Target, show it on the HUD as a local marker, or remove it. Library points also appear in the point lists.

If another plugin's marker library has points for this world (for example OreScout's), it shows in the Library list, read only, with **Copy to Digby** and **Copy all**.

## Planet names

The game only knows a planet's type (EarthLike, Mars...). Stand on the planet and type `/digby planet name <name>` to use the server's name for it in this world. If you named the planet in Fat Albert, Digby uses that name too.

## Commands

| Command | What it does |
|---|---|
| `/digby` | Opens the window |
| `/digby dig [from] <target> [depth]` | Plans from chat. Points: a GPS, `"a name"` from your GPS list or the library, or `here`. Add `server`, `nomarker` or `nostart` to change the markers |
| `/digby dig` | Plans your last plan again |
| `/digby rig <P1> <P2> [up] [small\|large]` | Sets the rig line; `/digby rig off` removes it |
| `/digby pad` | Writes the last plan to a datapad |
| `/digby clear` | Removes the dig markers |
| `/digby planet` | Says where you are; `/digby planet name <name>` names the planet |
| `/digby library`, `/digby help` | Opens those pages |
| `/digby chat off\|quiet\|normal\|chatty` | How much Digby comments |
| `/digby name <name>` | The name Digby's chat lines show under; on its own it puts it back |

## What the server sees

| Feature | Sent to the server |
|---|---|
| Planning, planet line, rig line, library, local markers | Nothing: uses what your game already knows |
| As server GPS | The normal GPS add, change and remove requests, as when making GPS by hand |
| Write datapad | One datapad edit request per datapad, the same one the game's datapad screen sends when you press OK, only for datapads in your own inventory. The server saves the name and text with your inventory |

## Files

In `%AppData%\SpaceEngineers\Storage\Digby`: `Digby_Settings.txt` (chat setting) and per world `Digby_<world>_plan.txt` (last plan; named points are kept as GPS), `Digby_<world>_library.xml` and `Digby_<world>_planets.txt`. Other plugins' files are only read, never changed.

## Building

`dotnet build -c Release` in `Source`, or `Build.ps1 "Digby"` from the repo root. The game's `Bin64` path can be set with `-p:Bin64=...`.
