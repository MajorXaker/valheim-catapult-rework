# Catapult Rework

Artillery-style aiming for the Valheim catapult, in the spirit of World of Tanks artillery.

- **Legs up:** the catapult works exactly like vanilla (load = fire on a fixed arc).
- **Legs down:** loading no longer fires automatically. Use the load point with empty hands to
  switch to an overhead view, put the marker on a spot inside the firing sector and click to fire.

## How it plays

1. Lower the catapult legs.
2. Load ammo from the hotbar as usual. With the legs down it stays in the bucket.
3. Press **Use (E)** on the load point with empty hands. The camera rises above the target area.
4. Aim and fire:

| Input | Action |
| --- | --- |
| Mouse / WASD / left stick | Move the marker |
| Mouse wheel | Zoom (camera height) |
| Left click / attack | Fire (only if loaded and the target is reachable) |
| Right click (block), Jump, Dodge or Esc | Leave the aiming view |

The ground overlay shows:

- **White outline:** the allowed sector, ±`AimConeDegrees` around the firing direction, from
  `MinRangePercent` to `MaxRangePercent` of the maximum range.
- **Yellow circle:** where the shot can land. Each shot hits a random point inside it (`SpreadConeDegrees`).
  It turns **red** when the target is out of reach, for example too far uphill.
- **White line:** the predicted flight path.

Reloading is not possible from the aiming view. Leave the view and load, or have a teammate load
while you aim. Teammates' loads do not fire on their own while the legs are down.

A short controls hint (fire, aim, exit) is shown in the lower right in the game's font. Its position
and size are set by `HintOffsetX`, `HintOffsetY` and `HintFontSize`.

With the legs down the catapult is anchored: shots and bumps no longer move it. If the ground under it
changes, it settles onto the new ground and is anchored again (`AnchorWhenLegsDown`).

Only one player can aim a catapult at a time. If ammo is still loaded when the legs are raised, it
is fired the vanilla way once the legs are up (`FireOnLegsUp`).

## Installation

The mod is required on **the server and every client**. Jötunn refuses connections with a missing or
different version.

Dependencies: [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).

- **r2modman / Thunderstore Mod Manager:** import the release zip (`Settings → Import local mod`).
- **Manual:** put `CatapultRework.dll` into `BepInEx/plugins/CatapultRework/` on the client and on the
  dedicated server.

## Configuration

File: `BepInEx/config/com.majorxaker.catapultrework.cfg`

### 1 - Gameplay (server)

These values are read from the **server's** config file and pushed to every client on connect.
Players cannot override them locally. Only server admins can change them in-game, through the
BepInEx configuration manager.

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Aimed mode on/off. Off = fully vanilla catapult. |
| `AimConeDegrees` | `10` | Half-angle of the aiming sector (±°). |
| `MinRangePercent` | `50` | Closest target, % of max range. |
| `MaxRangePercent` | `100` | Farthest target, % of max range. |
| `SpreadConeDegrees` | `1.5` | Dispersion cone half-angle. Landing circle radius = distance × tan(angle). 0 = pin-point. |
| `MaxRangeMultiplier` | `1` | Multiplies the vanilla maximum launch speed. |
| `ExitOnDamage` | `true` | Taking damage kicks you out of the aiming view. |
| `FireOnLegsUp` | `true` | Fire leftover ammo the vanilla way when the legs are raised. |
| `OnlyCatapultAmmo` | `true` | Only real payloads (Grausten, Explosive, Bloodgold) can be loaded, so tools and weapons can't be launched by accident. |
| `ExtraAllowedAmmo` | `""` | Comma-separated prefab names that may also be loaded, e.g. `Stone`. |
| `AnchorWhenLegsDown` | `true` | Hold the catapult in place while its legs are down. It still settles if the ground under it changes. |

### 2 - Client

These are personal settings for camera and control feel and are not synced: `CameraHeight`, `CameraMinHeight`,
`CameraMaxHeight`, `CameraPitch`, `CameraSmoothing`, `MouseSensitivity`, `KeyboardAimSpeed`,
`ShowTrajectory`, `ShowControlsHint`, `HintOffsetX` / `HintOffsetY` (distance of the hint from the
lower-right corner in HUD pixels, defaults 40 / 120), `HintFontSize` (20), `DebugLogging`.

`DebugLogging = true` writes detailed lines prefixed with `[CR]` to `BepInEx/LogOutput.log`:

- why the aiming view ended,
- catapult geometry and projectile physics,
- every shot, with the estimated vs actual release point,
- where each shot landed and what it hit,
- every object an explosion reached, with its tool-tier requirement and the damage left after resistances,
- network messages.

Please include these lines in bug reports.

## Damage

The mod changes only the launch velocity. Damage comes from the payload, as in vanilla:

| Payload | Explosion damage | Radius |
|---|---|---|
| Grausten | 5 blunt, 5 chop, 5 pickaxe (plus 20 blunt on direct hit) | 2 m |
| Explosive | 100 pure, 100 chop, 400 pickaxe, tool tier 5 | 3 m |
| Bloodgold | 200 pure, 5 blunt, 200 chop, 600 pickaxe, tool tier 5 (plus 20 blunt on direct hit) | 2 m |

Values were read from the game data of the dedicated server build.

### Far targets

In Valheim only the owner of an object applies damage to it, and objects get an owner only within about
1.5 zones (~96 m) of a player. Aimed shots land farther than that, where buildings and rocks are loaded but
unowned, so vanilla silently drops the hit. The same thing has been reported in vanilla when firing at
fortresses from the edge of range. The mod lets the shooter take ownership of an unowned object right
before a catapult projectile or explosion hits it; the game hands it back on its own afterwards.

## Compatibility

- **FirstPersonMode (Azumatt):** supported. The aiming camera is applied after that mod's camera
  code, and the normal first-person view returns when you leave the aiming view.
- Other camera mods that patch `GameCamera.UpdateCamera` should also work, because the aiming camera is applied last.

## How it works

The rework keeps the vanilla shot and changes only the launch velocity:

- The **launch elevation is the vanilla one** (fixed arc). Only the yaw and the speed change.
- **Maximum range** = the vanilla launch speed without its random +0–10% bonus, multiplied by
  `MaxRangeMultiplier`, simulated over flat ground at the catapult's base height.
- The required speed is found by **simulating the projectile exactly like `Projectile.FixedUpdate`**:
  gravity plus quadratic drag at the physics timestep, then bisecting on speed. Range grows
  monotonically with speed at a fixed angle, so the search is reliable.
- Projectiles despawn after their lifetime (`m_ttl`), so shots that would take longer are rejected.
- **Network:** the aiming player picks the random landing point and sends it to everybody. Every
  client starts the vanilla shot animation. The catapult's owner spawns the projectile with the
  solved velocity, recomputed from the real release point. Damage, ammo types, effects and the
  "launch yourself" trick are untouched. The aiming player is never launched.

Vanilla quirks found while reading the code:

- Vanilla adds a random +0–10% to launch speed, which is why shots wander.
- Vanilla computes an accuracy spread but never applies it to the velocity.

## Building

Requirements: .NET SDK 8+. It builds `net462` through reference assemblies, so Windows and Linux both work.

You need the game's managed DLLs. Either:

- copy `Environment.props.example` to `Environment.props` and point `VALHEIM_INSTALL` at your game, or
- download the free dedicated server DLLs without owning the game: `./scripts/fetch-server-dlls.sh`, then build with
  `-p:VALHEIM_INSTALL=$PWD/game`.

```bash
dotnet test  tests/CatapultRework.Tests        # ballistics unit tests (no game needed)
dotnet build src/CatapultRework -c Release      # -> src/CatapultRework/bin/Release/CatapultRework.dll
./scripts/package.sh                            # -> dist/CatapultRework-<version>.zip
```

GitHub Actions does the same on every push and attaches the zip to releases for `v*` tags.

## Status

Version 0.1.1 is an early build. It is built against the game update of 27 October 2026 and has not been
play-tested much yet. The ground overlay renders correctly. Still to check in-game:

- the estimated release point before the first shot,
- the camera feel at long range.
