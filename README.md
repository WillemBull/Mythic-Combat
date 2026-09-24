# Mythic Combat — the Unity build

The Unity version of Godsbound: a real-time 1v1 mobile strategy game where each player picks a
pantheon, builds a deck and an arena, and fights over a hex board.

The game's name is unsettled. The repository is Mythic-Combat; everything inside still says
Godsbound, and renaming is a deliberate later change, not a drift.

## What is here

```
GodsboundUnity/Godsbound/     the Unity 6000.6.0f1 project
  Assets/Godsbound/Runtime/Core          pure simulation — no UnityEngine, ever
  Assets/Godsbound/Runtime/Presentation  views, HUD, cameras; may reference Core
  Assets/Godsbound/Runtime/Data          loaders for the exported data tables
  Assets/Godsbound/Tests/EditMode        879 tests, including the exported fixtures
godsbound_beta.html           the browser build: the port's SPECIFICATION, not a second product
tools/export_unity_*.js       exporters that read that HTML and write the fixtures and data tables
tools/check_unity_port.js     72 static checks over the C# source
tests/stub_dom.js             the DOM stub the exporters evaluate the HTML under
```

## Why the browser build lives here

The port is verified against the original, not against someone's memory of it. Every exporter runs
`godsbound_beta.html` under `tests/stub_dom.js`, plays out the scenario, and writes what actually
happened into `Tests/EditMode/Fixtures/`. C# expectations are never hand-written, and balance numbers
live only in the exported data tables — C# reads them, it does not retype them.

That is why a copy of the browser build sits at this root. It is a reference: the browser game itself
is developed in its own repository, and this copy is refreshed from there when it changes.

## Running the checks

```
node tools/check_unity_port.js        # 72 static checks, no Unity needed
node tools/export_unity_reference.js  # re-export a fixture; the others follow the same pattern
```

The real gate is the EditMode suite, which only runs inside the editor: open the project and use
**Godsbound > Run EditMode Tests**. It writes `Logs/TestResults_latest.txt`.

## State

Complete and playable in the editor: open `Assets/Godsbound/Scenes/Menu.unity`, set the Game view to
393x852, press Play. Pick a pantheon, build a deck and board, fight, see the result, play again.

Not done yet: tutorial hints, touch input and a real device build, audio. There is no standalone
build of any kind yet — the editor is the only way to play this version.

## The other repositories

- `Godsbound-design-drafts` — the browser game, the raw art, and the project documentation:
  ROADMAP.md (the plan and every step's log), HANDOFF.md (architecture and the traps), HISTORY.md
  (the completed-work archive), AGENTS.md and CLAUDE.md (how the work is run). Read those before
  changing anything here; the Unity roadmap lives in that ROADMAP.
- `Godsbound-demo` — the shipped browser package, served over GitHub Pages.

## Lineage

Split out of `Godsbound-design-drafts` on 2026-09-24, after step U35, with a fresh history. Every
commit before that lives in that repository. The stock Unity template content (`Assets/Tutorials`,
`Assets/SourceFiles`, `Assets/Skyboxes`, `Assets/Scenes/GetStarted_Scene.unity` and the Learn IET
Framework package — 250 MB of it) was left behind in the move rather than carried across.
