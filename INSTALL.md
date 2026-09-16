# Installation

1. Stop or close WindowsGSM.
2. Copy the complete `Windrose.cs` folder from this repository/release into:

```text
<WindowsGSM>\plugins\
```

The final layout must be:

```text
<WindowsGSM>\plugins\Windrose.cs\Windrose.cs
<WindowsGSM>\plugins\Windrose.cs\Windrose.png
<WindowsGSM>\plugins\Windrose.cs\author.png
```

3. Start WindowsGSM or use **Reload Plugins**.
4. Create a Windrose server and install/update it through SteamCMD.
5. For an existing/legacy server, read the migration guide before the first start.

## Important

Do not delete or regenerate `R5\ServerDescription.json` or `R5\Saved\` when migrating an existing server. WorldGuard is specifically designed to preserve the existing server/world identity.
