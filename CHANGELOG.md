# Changelog

## 0.1.2
- New server option `OnlyCatapultAmmo` (default on): only real catapult payloads can be loaded. Vanilla
  accepts almost any item, so tools and weapons could be launched by accident. `ExtraAllowedAmmo` can
  whitelist more prefabs.
- Debug logging now traces each shot after release: what it hit, where, how far from the aimed point, and for
  each explosion target its tool-tier requirement and the damage left after resistances.

## 0.1.1
- Fixed: the aiming view closed right after opening. The damage check polled health, which also drops when
  max health shrinks as food wears off. It now uses the game's real damage event.
- Fixed: the game's own 5 m feet-to-load-point check could cancel aiming at once. It is replaced by a more
  lenient check to the catapult body, since the load point swings with the arm when firing.
- Compatibility with Azumatt's FirstPersonMode (and other mods that redraw the camera in the same hook):
  the aiming camera is now applied last.
- New client option `DebugLogging`: detailed `[CR]` lines in the BepInEx log (why aiming ended,
  ballistics, shots, network messages, estimated vs actual release point).

## 0.1.0
- First implementation: aimed firing view with the legs down, fixed vanilla arc, solved launch speed.
- Firing sector (±cone, min–max % of range) and a random landing point inside a dispersion cone.
- No auto-fire when loading with the legs down. Leftover ammo fires the vanilla way when the legs are raised (optional).
- One aimer per catapult. The aiming player is not launched.
- Gameplay settings are synced from the server and locked for players (Jötunn). Camera and control settings stay local.
