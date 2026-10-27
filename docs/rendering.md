# multiplayer rendering

the multiplayer scene uses the existing sprites in `Assets/Art/Temp` for ship
blocks and projectiles. the builder assigns those sprites when it creates the
scene. the placement preview uses a transparent sprite square.

the project previously referenced three render pipeline assets that were missing
from the repository. `Hullbreach/Online/Repair multiplayer rendering` creates a
small urp pipeline and universal renderer when needed, fills missing graphics and
quality references, and assigns missing multiplayer sprites. it keeps existing
valid references and does not rebuild the lobby.

the same repair can run in the pinned editor with
`-executeMethod Hullbreach.Editor.MultiplayerRenderingSetup.Repair`.
commit the generated `Assets/Settings` assets and their meta files along with the
saved scene and project settings. no package or editor upgrade is required.

after rebuilding, host a lan game and join from another client. both ships should
be visible, the build preview should be green or red, and projectiles should be
visible when firing. check the player log for missing renderer or shader errors.
