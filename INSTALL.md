# Installation — WindowsGSM.Windrose.WorldGuard v2.4.1

1. Stop/close WindowsGSM.
2. Back up the existing plugin folder if upgrading.
3. Copy the complete `Windrose.cs` folder from this repository/release into:

```text
<WindowsGSM>\plugins\
```

Final layout:

```text
<WindowsGSM>\plugins\Windrose.cs\Windrose.cs
<WindowsGSM>\plugins\Windrose.cs\Windrose.png
<WindowsGSM>\plugins\Windrose.cs\author.png
```

4. Start WindowsGSM or use **Reload Plugins**.
5. Create/install/update the Windrose server through SteamCMD.
6. For an existing/legacy server, read the migration guide **before first start**.

## New server connection default

A new/incomplete configuration initializes to Invite Code / Connection Service mode:

```text
UseDirectConnection=false
```

No connection parameter is required.

Explicit overrides:

```text
-wr-connection invite
-wr-connection direct
```

Legacy aliases remain accepted:

```text
-wr-direct false
-wr-direct true
```

Once `UseDirectConnection` exists, omitting both connection parameters preserves the existing value.

## Important migration warning

Do not delete/regenerate `R5\ServerDescription.json` or `R5\Saved\` when migrating an existing server. Preserve the original identity and save data.

## After installation / upgrade

Recommended validation:

1. Start the server and confirm the expected world.
2. Check `WindowsGSM_Windrose.log` for `World Guard OK`.
3. Stop the server through the WindowsGSM **STOP** button.
4. Start again and confirm the same `WorldIslandId`.
5. If using Direct IP, verify the configured Server Port is reachable in both TCP and UDP.

## Admin / RCON

**Admin / RCON:** The official Windrose Dedicated Server, in its currently documented version, does not provide a documented native administrator or RCON configuration. Features of this type require third-party tools or mods.
