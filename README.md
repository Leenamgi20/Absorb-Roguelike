# Absorb Roguelike

**Absorb your enemies. Wear their power.**
A top-down action roguelike for 1–2 players, made in one month (2026.09.15 – 10.16) with AI as the main developer and a human designing how the AI works.

▶ **Play / download:** https://somojeok.itch.io/absorbroguelike
(browser version: single player · Windows version: online co-op)

![Title](Art_Source/itch/shot_1_title.png)

| Combat | Boss |
|---|---|
| ![Combat](Art_Source/itch/shot_2_combat.png) | ![Boss](Art_Source/itch/shot_3_boss.png) |

## The game
- **Absorb** a weakened enemy (hold `E`) and wear its animal mask — each of the 12 zodiac animals gives its own attack (tiger 3-hit claw, rabbit ear whip, ox charge, …).
- **Bosses** drop debris you can hold in your mouth and spit back. Defeat a boss and absorb it to put an element on your greatsword (fire · ice · electric · poison · earth).
- Get hit and your mask pops off — absorb it again before it's gone (or your teammate grabs it first).
- 5 floors, random animal pools, situations and boss patterns.
- 2-player online co-op with room codes (Epic Online Services). Browser build is single player only.

### Controls
| Key | Action |
|---|---|
| `WASD` | Move |
| Left click | Attack / spit debris |
| Right click | Guard |
| `Space` | Dash |
| `E` (hold) | Absorb · hold debris |
| `F` (hold) | Revive teammate |
| `Tab` (hold) | Map — click a cleared room to teleport |
| `Esc` | Menu |

## Tech
- **Unity 6000.3.18f1**, URP 2D renderer, Pixel Perfect camera (640×360)
- **Mirror** networking (host-authoritative) + **EOSTransport** (Epic Online Services lobbies, room codes and public rooms)
- **Edgar** procedural level generator for floors
- Korean / English localization, first-play story intro and contextual hints
- Builds: `Tools/Absorb/빌드` menu — Windows (download) and WebGL (browser, single player, Gzip + decompression fallback)

### Open the project
1. Open the folder with Unity Hub (Unity **6000.3.18f1**). Packages restore automatically (Edgar is pulled from GitHub).
2. Open `Assets/Scenes/SampleScene.unity` and press Play → **Play Solo** works without any setup.
3. Online co-op needs your own Epic Online Services credentials: copy `Assets/Data/EOSConfig.template.asset` to `Assets/_Local/Resources/EOSConfig.asset` and fill in your product / sandbox / deployment / client values. `Assets/_Local/` is git-ignored — never commit real credentials.

Large binary assets use **Git LFS** (`git lfs install` before cloning).

## How it was made — AI as the developer
This project was an experiment: *when AI writes most of the code, what should the human do?*
- **Rules for the AI** live in [`AGENTS.md`](AGENTS.md) (why each rule exists: [`RULE_RATIONALE.md`](RULE_RATIONALE.md)) — choosing how to work by task size, batching what the human must check, writing how each item was verified, keeping code and structure docs in the same commit.
- **Feature pipeline** ([`.claude/workflows/feature-pipeline.js`](.claude/workflows/feature-pipeline.js)): plan → implement → code QA (loops back on failure) → docs, run in the background.
- **Human role:** deciding what only a human can verify (2-PC online tests, game feel), setting scope, and asking "does this really serve the goal?".
- Design docs, daily records and planning notes were kept in a private `docs/` folder and are not included here, so some links in `AGENTS.md` point to files that are not in this repository.

## Project layout
| Path | Contents |
|---|---|
| `Assets/Scripts` | Game code (Player, Monster, Dungeon, Network, UI, Audio, …) |
| `Assets/Editor` | Editor tools (level design sheet, build menu, sound import) |
| `Art_Source` | Source art / sound files and itch.io page images |
| `Tools` | Small Python helpers (balance report, refactor value check) |

## Credits & licenses
- Game by **Somojeok** (이남기).
- Art images generated with VARCO 3D (Nano Banana Pro · GPT Image) and edited by hand; BGM generated with VARCO Sound.
- Third-party code keeps its own license: [Mirror](Assets/Mirror/LICENSE) (MIT), [EOSTransport](Assets/Mirror/Transports/EOSTransport/LICENSE.md), Edgar for Unity (MIT, via Unity Package Manager), [Galmuri font](Assets/Resources/Fonts/Galmuri_OFL_LICENSE.txt) (SIL OFL 1.1), plus the licenses bundled under `Assets/Mirror/`.
- **Source code** (C# scripts, editor tools, Python/JS helpers): [MIT License](LICENSE) — feel free to learn from or reuse it.
- **Assets and content data** (art, sprites, illustrations, music, sound effects, and Unity scenes/prefabs/data — including dungeon room templates, level graphs and procedural map setups): © 2026 Somojeok. All rights reserved — please don't reuse them without permission.
