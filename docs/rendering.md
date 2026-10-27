# Multiplayer rendering

The multiplayer scene uses the existing sprites in `Assets/Art/Temp` for ship
blocks and projectiles. The builder assigns those sprites when it creates the
scene. The placement preview uses a transparent sprite square.

The project previously referenced three render pipeline assets that were missing
from the repository. `Hullbreach/Online/Repair multiplayer rendering` creates a
small URP pipeline and universal renderer when needed, fills missing graphics and
quality references, and assigns missing multiplayer sprites. It keeps existing
valid references and does not rebuild the lobby.

The same repair can run in the pinned editor with
`-executeMethod Hullbreach.Editor.MultiplayerRenderingSetup.Repair`.
Commit the generated `Assets/Settings` assets and their meta files along with the
saved scene and project settings. No package or editor upgrade is required.

After rebuilding, host a LAN game and join from another client. Both ships should
be visible, the build preview should be green or red, and projectiles should be
visible when firing. Check the player log for missing renderer or shader errors.
