# Changelog

## 0.1.3
- Fixed: shots did no damage to far targets (Ashlands fortresses, grausten debris). Damage is applied only
  by an object's owner, and objects more than ~96 m from every player have none, so the hit was dropped.
  The shooter now claims unowned objects right before a catapult projectile or explosion hits them.
- Fixed: right click did not leave the aiming view (RMB is bound to Block, which vanilla ignores here).
  It no longer raises the shield either.
- The catapult is anchored while its legs are down and no longer creeps between shots. It still settles
  if the ground under it is changed. Server option `AnchorWhenLegsDown`.
- The on-screen text is replaced by a three-line controls hint (fire, aim, exit) in the game's font,
  in the lower right. Vanilla key hints are hidden while aiming. Client options `ShowControlsHint`
  (replaces `ShowInfoText`), `HintOffsetX`, `HintOffsetY`, `HintFontSize`.

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
