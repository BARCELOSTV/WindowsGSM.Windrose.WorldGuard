using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    /// <summary>
    /// WindowsGSM plugin for Windrose Dedicated Server.
    ///
    /// Main design goal:
    ///   Prevent accidental generation of a new world on every normal restart.
    ///
    /// The plugin therefore treats the server/world identity as protected state:
    ///   - PersistentServerId
    ///   - InviteCode
    ///   - WorldIslandId
    ///
    /// After an existing world is successfully discovered, the identity is locked in
    /// WindowsGSM_Windrose_State.json. A mismatch blocks startup instead of letting the
    /// game silently generate a replacement world.
    ///
    /// v2.4.0 adds safe friendly-world-name synchronization:
    ///   - WindowsGSM Server Start Map -> WorldDescription.WorldName
    ///   - preserves WorldIslandId, islandId and the physical save-folder ID
    ///   - applies RocksDB_v2 edits through R5WorldDescriptionUpdater.exe
    ///   - keeps all v2.3.0 identity fixes and World Guard protections
    ///
    /// WindowsGSM "Server Start Map" is intentionally NOT part of the Windrose identity.
    /// It is used as the friendly Windrose WorldName and may be changed without altering
    /// WorldIslandId, islandId, or the save-folder name.
    ///
    /// Steam Dedicated Server AppID: 4129620
    /// </summary>
    public class Windrose : SteamCMDAgent
    {
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Windrose.WorldGuard",
            author = "Luiz Augusto Barcelos",
            description = "Windrose Dedicated Server for WindowsGSM with World Identity Guard",
            version = "2.4.0",
            url = "https://github.com/BARCELOSTV/WindowsGSM.Windrose.WorldGuard",
            color = "#8B1A1A"
        };

        public override bool loginAnonymous => true;
        public override string AppId => "4129620";

        // WindowsGSM validates this path when installing/importing the server.
        // This is the actual UE server executable used by the official foreground launcher.
        public override string StartPath => @"R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe";

        public string FullName = "Windrose Dedicated Server";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 0;
        public dynamic QueryMethod = null;

        // Windrose normally uses NAT punch-through and dynamic ports.
        // ServerPort is only applied when UseDirectConnection = true.
        public string Port = "7777";
        public string QueryPort = "7778";
        // FRIENDLY WORLD NAME:
        // WindowsGSM's "Server Start Map" field is mapped to
        // WorldDescription.json -> WorldDescription -> WorldName.
        //
        // IMPORTANT: this does NOT select or rename the physical world/save. The real
        // world identity still comes from WorldIslandId / islandId / the world-folder ID.
        // Changing this field only changes the user-facing WorldName.
        public string Defaultmap = "Windrose";
        public string Maxplayers = "4";

        // WindowsGSM "Additional Parameters" are consumed by this plugin and are NOT
        // passed through to the Windrose executable.
        //
        // Supported plugin options:
        //   -password "your password"
        //   -wr-region AUTO|EU|SEA|CIS
        //   -wr-direct true|false
        //   -wr-bind 0.0.0.0
        //   -wr-autorestore true|false
        //   -wr-adopt-world <WorldIslandId>
        public string Additional = "-password \"\"";

        public string Error;
        public string Notice;

        private readonly ServerConfig _serverData;

        private const string StateFileName = "WindowsGSM_Windrose_State.json";
        private const string LogFileName = "WindowsGSM_Windrose.log";
        private const string BackupFolderName = "WindowsGSM_Windrose_Backups";

        public Windrose(ServerConfig serverData) : base(serverData)
        {
            base.serverData = _serverData = serverData;
        }

        /// <summary>
        /// Windrose generates ServerDescription.json and the first world itself.
        /// Pre-generating that file can interfere with the game's bootstrap/migration logic,
        /// so this method intentionally does nothing.
        /// </summary>
        public async void CreateServerCFG()
        {
            await Task.CompletedTask;
        }

        public async Task<Process> Start()
        {
            Error = null;
            Notice = null;

            string root = ServerRoot;
            string exePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);

            if (!File.Exists(exePath))
            {
                Error = "Windrose server executable not found: " + exePath;
                Log("START BLOCKED: " + Error);
                return null;
            }

            try
            {
                WorldGuardResult guard = PrepareWorldGuardAndPatchConfiguration();
                if (!guard.Ok)
                {
                    Error = guard.Message;
                    Log("START BLOCKED: " + guard.Message);
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(guard.Message))
                {
                    Notice = guard.Message;
                    Log(guard.Message);
                }
            }
            catch (Exception ex)
            {
                Error = "World Guard failed safely. Server was NOT started. " + ex.Message;
                Log("START BLOCKED (exception): " + ex);
                return null;
            }

            // The official foreground launcher ultimately runs the Shipping executable
            // with logging enabled. Starting it directly gives WindowsGSM a stable process
            // handle to monitor.
            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = root,
                    FileName = exePath,
                    Arguments = "-log",
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false,
                    CreateNoWindow = false
                },
                EnableRaisingEvents = true
            };

            try
            {
                p.Start();
                Log("Windrose process started. PID=" + p.Id);
                return p;
            }
            catch (Exception ex)
            {
                Error = "Failed to start Windrose: " + ex.Message;
                Log("PROCESS START ERROR: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Tries to stop the server gracefully first. A forced Kill is only used after
        /// grace periods have expired, because killing the process while it writes RocksDB
        /// can damage the save database.
        /// </summary>
        public async Task Stop(Process p)
        {
            if (p == null)
                return;

            await Task.Run(() =>
            {
                try
                {
                    if (p.HasExited)
                        return;

                    Log("Stop requested for PID=" + p.Id);

                    bool signalSent = false;
                    try
                    {
                        p.Refresh();
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            Functions.ServerConsole.SetMainWindow(p.MainWindowHandle);
                            Functions.ServerConsole.SendWaitToMainWindow("^c");
                            signalSent = true;
                            Log("CTRL+C sent to Windrose console.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("CTRL+C attempt failed: " + ex.Message);
                    }

                    if (signalSent && p.WaitForExit(15000))
                    {
                        Log("Windrose stopped gracefully after CTRL+C.");
                        return;
                    }

                    try
                    {
                        if (!p.HasExited && p.CloseMainWindow())
                        {
                            Log("CloseMainWindow sent.");
                            if (p.WaitForExit(10000))
                            {
                                Log("Windrose stopped after CloseMainWindow.");
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("CloseMainWindow attempt failed: " + ex.Message);
                    }

                    if (!p.HasExited)
                    {
                        Log("WARNING: Graceful shutdown timed out. Forcing process termination.");
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    Error = "Failed to stop Windrose: " + ex.Message;
                    Log("STOP ERROR: " + ex);
                }
            });
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            Error = null;
            Notice = null;

            try
            {
                BackupIdentityFiles("pre-update");
            }
            catch (Exception ex)
            {
                Notice = "Pre-update identity backup warning: " + ex.Message;
                Log(Notice);
            }

            var result = await Installer.SteamCMD.UpdateEx(
                serverData.ServerID,
                AppId,
                validate,
                custom: custom,
                loginAnonymous: loginAnonymous);

            Process p = result.Item1;
            Error = result.Item2;

            if (p != null)
                await Task.Run(() => p.WaitForExit());

            if (!string.IsNullOrWhiteSpace(Error))
                Log("SteamCMD update error: " + Error);
            else
                Log("SteamCMD update completed.");

            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, StartPath);
            bool ok = File.Exists(exePath);
            Error = ok ? null : "Invalid Windrose server path. Missing: " + exePath;
            return ok;
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }

        // ---------------------------------------------------------------------
        // World Guard
        // ---------------------------------------------------------------------

        private WorldGuardResult PrepareWorldGuardAndPatchConfiguration()
        {
            string configPath = ServerDescriptionPath;
            string statePath = StatePath;
            List<WorldLocation> worlds = FindExistingWorlds();
            WorldGuardState state = LoadState();

            // Case A: ServerDescription.json does not exist.
            // A completely fresh install is allowed to bootstrap only when no old world/state exists.
            if (!File.Exists(configPath))
            {
                if (state != null && state.Locked)
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: ServerDescription.json is missing but a locked WindowsGSM world state exists. " +
                        "Startup was blocked to prevent Windrose from generating a replacement world. Restore R5\\ServerDescription.json " +
                        "from backup or follow the migration guide.");
                }

                if (worlds.Count > 0)
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: Existing Windrose world data was found, but R5\\ServerDescription.json is missing. " +
                        "Startup was blocked because Windrose could create a new world. Restore/copy the original ServerDescription.json " +
                        "or configure the existing WorldIslandId before starting.");
                }

                Log("WORLD GUARD: Clean installation detected. First bootstrap is allowed.");
                return WorldGuardResult.Success(
                    "First clean launch: Windrose may create its initial ServerDescription.json and world. " +
                    "Restart once after the first world is fully created so World Guard can lock its identity.");
            }

            JObject root;
            JObject persistent;
            try
            {
                root = JObject.Parse(File.ReadAllText(configPath));
                persistent = root["ServerDescription_Persistent"] as JObject;
            }
            catch (Exception ex)
            {
                return WorldGuardResult.Fail(
                    "WORLD GUARD: R5\\ServerDescription.json is invalid JSON. Startup blocked. " + ex.Message);
            }

            if (persistent == null)
            {
                return WorldGuardResult.Fail(
                    "WORLD GUARD: ServerDescription_Persistent is missing from R5\\ServerDescription.json. Startup blocked.");
            }

            string persistentServerId = ReadString(persistent, "PersistentServerId");
            string inviteCode = ReadString(persistent, "InviteCode");
            string configuredWorldId = ReadString(persistent, "WorldIslandId");

            // Optional controlled world switch requested explicitly through WindowsGSM Additional Parameters.
            string requestedWorldId = GetOptionValue(_serverData.ServerParam, "-wr-adopt-world");
            if (!string.IsNullOrWhiteSpace(requestedWorldId))
            {
                WorldLocation target = worlds.FirstOrDefault(
                    w => string.Equals(w.WorldId, requestedWorldId, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: -wr-adopt-world requested world '" + requestedWorldId +
                        "', but that world was not found in any known Windrose save location. Startup blocked.");
                }

                BackupIdentityFiles("before-world-adoption");
                persistent["WorldIslandId"] = target.WorldId;
                configuredWorldId = target.WorldId;

                if (state == null)
                    state = new WorldGuardState();

                state.Version = 1;
                state.Locked = true;
                state.PersistentServerId = persistentServerId;
                state.InviteCode = inviteCode;
                state.WorldIslandId = configuredWorldId;
                state.LastValidatedUtc = DateTime.UtcNow.ToString("o");
                SaveState(state);

                WriteJsonAtomic(configPath, root);
                Log("WORLD GUARD: Controlled world adoption completed: " + configuredWorldId);
            }

            // If a locked state already exists, identity may not drift silently.
            if (state != null && state.Locked)
            {
                if (!IdsEqual(state.PersistentServerId, persistentServerId))
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: PersistentServerId changed unexpectedly. " +
                        "Locked='" + Safe(state.PersistentServerId) + "', current='" + Safe(persistentServerId) +
                        "'. Startup blocked.");
                }

                // An EMPTY WorldIslandId is handled by the recovery block below.
                // Only block here when the file contains a non-empty, conflicting world ID.
                if (!string.IsNullOrWhiteSpace(configuredWorldId) &&
                    !IdsEqual(state.WorldIslandId, configuredWorldId))
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: WorldIslandId changed unexpectedly. " +
                        "Locked='" + Safe(state.WorldIslandId) + "', current='" + Safe(configuredWorldId) +
                        "'. Startup blocked. To intentionally switch to an existing world, use -wr-adopt-world <WorldID>.");
                }
            }

            if (string.IsNullOrWhiteSpace(configuredWorldId))
            {
                // Recovery path 1:
                // A locked state already exists and the game's ServerDescription.json lost/failed
                // to persist WorldIslandId. If the locked world is physically present, restore the
                // known-good ID automatically instead of generating a new world or blocking forever.
                if (state != null && state.Locked && !string.IsNullOrWhiteSpace(state.WorldIslandId))
                {
                    WorldLocation lockedWorld = worlds.FirstOrDefault(
                        w => string.Equals(w.WorldId, state.WorldIslandId, StringComparison.OrdinalIgnoreCase));

                    if (lockedWorld != null)
                    {
                        string validationProblem = ValidateWorldDescription(lockedWorld, true);
                        if (!string.IsNullOrWhiteSpace(validationProblem))
                            return WorldGuardResult.Fail("WORLD GUARD: " + validationProblem);

                        BackupIdentityFiles("restore-locked-world-id");
                        persistent["WorldIslandId"] = state.WorldIslandId;
                        configuredWorldId = state.WorldIslandId;
                        WriteJsonAtomic(configPath, root);

                        Log("WORLD GUARD: Restored missing WorldIslandId from locked state: " + configuredWorldId);
                    }
                    else
                    {
                        return WorldGuardResult.Fail(
                            "WORLD GUARD: WorldIslandId is empty and the previously locked world '" +
                            state.WorldIslandId + "' cannot be found. Startup blocked.");
                    }
                }
                // Recovery path 2:
                // This is the common first-start behavior observed on current Windrose builds:
                // the server creates ServerDescription.json and exactly one world directory, but
                // WorldIslandId can remain empty after the first shutdown. If there is exactly ONE
                // valid world and no prior lock, adopting it is unambiguous and safe.
                else if ((state == null || !state.Locked) && worlds.Count == 1)
                {
                    WorldLocation onlyWorld = worlds[0];
                    string validationProblem = ValidateWorldDescription(onlyWorld, true);
                    if (!string.IsNullOrWhiteSpace(validationProblem))
                        return WorldGuardResult.Fail("WORLD GUARD: " + validationProblem);

                    BackupIdentityFiles("auto-adopt-first-world");
                    persistent["WorldIslandId"] = onlyWorld.WorldId;
                    configuredWorldId = onlyWorld.WorldId;
                    WriteJsonAtomic(configPath, root);

                    state = new WorldGuardState
                    {
                        Version = 1,
                        Locked = true,
                        PersistentServerId = persistentServerId,
                        InviteCode = inviteCode,
                        WorldIslandId = configuredWorldId,
                        FirstLockedUtc = DateTime.UtcNow.ToString("o"),
                        LastValidatedUtc = DateTime.UtcNow.ToString("o")
                    };
                    SaveState(state);

                    Log("WORLD GUARD: Automatically adopted the only existing world after first bootstrap: " +
                        configuredWorldId);
                }
                // More than one world exists and none is locked: never guess.
                else if ((state == null || !state.Locked) && worlds.Count > 1)
                {
                    string discovered = string.Join(", ",
                        worlds.Select(w => w.WorldId)
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToArray());

                    return WorldGuardResult.Fail(
                        "WORLD GUARD: WorldIslandId is empty and multiple worlds exist: " +
                        discovered +
                        ". Startup blocked because the plugin cannot safely choose one. " +
                        "Use -wr-adopt-world <WorldID> to select the intended existing world.");
                }
                // Still no world: allow the clean bootstrap to continue.
                else if (worlds.Count == 0 && (state == null || !state.Locked))
                {
                    BackupIdentityFiles("bootstrap-pre-start");
                    PatchServerDescription(root, persistent);
                    WriteJsonAtomic(configPath, root);

                    Log("WORLD GUARD: Incomplete clean bootstrap detected. WorldIslandId is empty and no existing worlds are present; startup allowed.");
                    return WorldGuardResult.Success(
                        "World Guard bootstrap: ServerDescription.json exists but no world has been created yet. " +
                        "Startup is allowed because no existing saves or locked identity were found.");
                }
                else
                {
                    return WorldGuardResult.Fail(
                        "WORLD GUARD: WorldIslandId is empty and the server state could not be recovered safely. " +
                        "Startup blocked.");
                }
            }

            WorldLocation selectedWorld = worlds.FirstOrDefault(
                w => string.Equals(w.WorldId, configuredWorldId, StringComparison.OrdinalIgnoreCase));

            if (selectedWorld == null)
            {
                string discovered = worlds.Count == 0
                    ? "(none)"
                    : string.Join(", ", worlds.Select(w => w.WorldId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

                return WorldGuardResult.Fail(
                    "WORLD GUARD: Configured WorldIslandId '" + configuredWorldId +
                    "' was not found in any active RocksDB_v2/RocksDB save location. Existing IDs: " +
                    discovered + ". Startup blocked so Windrose cannot silently generate another world.");
            }

            // Validate the selected world. If its identity field is genuinely empty,
            // repairing it is safe here because ServerDescription.json already points at
            // this exact folder ID.
            string worldDescriptionProblem = ValidateWorldDescription(selectedWorld, true);
            if (!string.IsNullOrWhiteSpace(worldDescriptionProblem))
            {
                return WorldGuardResult.Fail("WORLD GUARD: " + worldDescriptionProblem);
            }

            // First safe adoption: existing server/migrated server becomes locked here.
            if (state == null)
            {
                state = new WorldGuardState
                {
                    Version = 1,
                    Locked = true,
                    PersistentServerId = persistentServerId,
                    InviteCode = inviteCode,
                    WorldIslandId = configuredWorldId,
                    FirstLockedUtc = DateTime.UtcNow.ToString("o"),
                    LastValidatedUtc = DateTime.UtcNow.ToString("o")
                };
                SaveState(state);
                Log("WORLD GUARD: Existing identity adopted and locked: " + configuredWorldId);
            }
            else
            {
                state.LastValidatedUtc = DateTime.UtcNow.ToString("o");
                SaveState(state);
            }

            // Synchronize WindowsGSM's "Server Start Map" field to the friendly
            // Windrose world name. This NEVER changes WorldIslandId, islandId, or the
            // physical save-folder name.
            string worldNameProblem = SyncWorldNameFromWindowsGSM(selectedWorld);
            if (!string.IsNullOrWhiteSpace(worldNameProblem))
            {
                return WorldGuardResult.Fail("WORLD GUARD: " + worldNameProblem);
            }

            // Only after identity validation do we patch ServerDescription user-facing settings.
            BackupIdentityFiles("pre-start");
            PatchServerDescription(root, persistent);
            WriteJsonAtomic(configPath, root);

            Log(
                "WORLD GUARD: Identity validated. World=" + configuredWorldId +
                ", location=" + selectedWorld.RootKind +
                ", path=" + selectedWorld.WorldDirectory);

            string friendlyWorldName = (_serverData.ServerMap ?? string.Empty).Trim();
            string friendlySuffix = string.IsNullOrWhiteSpace(friendlyWorldName)
                ? string.Empty
                : " (WorldName: " + friendlyWorldName + ")";

            return WorldGuardResult.Success(
                "World Guard OK: loading locked world " + configuredWorldId +
                friendlySuffix + " from " + selectedWorld.RootKind + ".");
        }

        private void PatchServerDescription(JObject root, JObject persistent)
        {
            if (!string.IsNullOrWhiteSpace(_serverData.ServerName))
                persistent["ServerName"] = _serverData.ServerName;

            int maxPlayers;
            if (!int.TryParse(_serverData.ServerMaxPlayer, out maxPlayers) || maxPlayers < 1)
                maxPlayers = 4;
            persistent["MaxPlayerCount"] = maxPlayers;

            string password = GetOptionValue(_serverData.ServerParam, "-password") ?? string.Empty;
            persistent["Password"] = password;
            persistent["IsPasswordProtected"] = !string.IsNullOrEmpty(password);

            string region = GetOptionValue(_serverData.ServerParam, "-wr-region");
            if (!string.IsNullOrWhiteSpace(region) && persistent["UserSelectedRegion"] != null)
            {
                if (region.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
                {
                    persistent["UserSelectedRegion"] = "";
                }
                else if (region.Equals("EU", StringComparison.OrdinalIgnoreCase) ||
                         region.Equals("SEA", StringComparison.OrdinalIgnoreCase) ||
                         region.Equals("CIS", StringComparison.OrdinalIgnoreCase))
                {
                    persistent["UserSelectedRegion"] = region.ToUpperInvariant();
                }
                else
                {
                    throw new InvalidOperationException(
                        "Invalid -wr-region value '" + region + "'. Use AUTO, EU, SEA or CIS.");
                }
            }

            bool boolValue;
            string direct = GetOptionValue(_serverData.ServerParam, "-wr-direct");
            if (!string.IsNullOrWhiteSpace(direct) && persistent["UseDirectConnection"] != null)
            {
                if (!TryParseBool(direct, out boolValue))
                    throw new InvalidOperationException("Invalid -wr-direct value. Use true or false.");

                persistent["UseDirectConnection"] = boolValue;
            }

            if (persistent["UseDirectConnection"] != null &&
                persistent["UseDirectConnection"].Value<bool>())
            {
                int directPort;
                if (!int.TryParse(_serverData.ServerPort, out directPort) ||
                    directPort < 1 || directPort > 65535)
                {
                    throw new InvalidOperationException(
                        "Direct connection is enabled but WindowsGSM Server Port is invalid.");
                }

                persistent["DirectConnectionServerPort"] = directPort;

                string bind = GetOptionValue(_serverData.ServerParam, "-wr-bind");
                if (!string.IsNullOrWhiteSpace(bind) && persistent["DirectConnectionProxyAddress"] != null)
                    persistent["DirectConnectionProxyAddress"] = bind;
            }

            string autoRestore = GetOptionValue(_serverData.ServerParam, "-wr-autorestore");
            if (!string.IsNullOrWhiteSpace(autoRestore) &&
                persistent["AutoLoadLatestBackupIfHasBroken"] != null)
            {
                if (!TryParseBool(autoRestore, out boolValue))
                    throw new InvalidOperationException("Invalid -wr-autorestore value. Use true or false.");

                persistent["AutoLoadLatestBackupIfHasBroken"] = boolValue;
            }

            // IMPORTANT:
            // PersistentServerId, InviteCode and WorldIslandId are intentionally NOT modified here.
            // Unknown/new fields are preserved because we mutate the existing JObject rather than
            // replacing ServerDescription_Persistent with a hard-coded schema.
        }

        // ---------------------------------------------------------------------
        // World discovery / validation
        // ---------------------------------------------------------------------

        private List<WorldLocation> FindExistingWorlds()
        {
            var found = new List<WorldLocation>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string profileRoot = Path.Combine(
                ServerRoot, "R5", "Saved", "SaveProfiles", "Default");

            if (!Directory.Exists(profileRoot))
                return found;

            // Only LIVE databases are eligible for automatic startup/adoption.
            // Backup folders are intentionally excluded: a backup should first be restored
            // into a live RocksDB_v2/RocksDB location before it can become the active world.
            //
            // Priority:
            //   1. RocksDB_v2 (current runtime)
            //   2. RocksDB    (legacy runtime)
            var roots = new List<Tuple<string, string>>
            {
                Tuple.Create("RocksDB_v2 (runtime)", Path.Combine(profileRoot, "RocksDB_v2")),
                Tuple.Create("RocksDB (legacy)", Path.Combine(profileRoot, "RocksDB"))
            };

            foreach (Tuple<string, string> root in roots)
            {
                if (!Directory.Exists(root.Item2))
                    continue;

                // Prefer the most recently modified version directory when the same WorldId
                // is present in more than one version folder.
                string[] versionDirs = Directory.GetDirectories(root.Item2)
                    .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                    .ToArray();

                foreach (string versionDir in versionDirs)
                {
                    string worldsDir = Path.Combine(versionDir, "Worlds");
                    if (!Directory.Exists(worldsDir))
                        continue;

                    foreach (string worldDir in Directory.GetDirectories(worldsDir))
                    {
                        string description = Path.Combine(worldDir, "WorldDescription.json");
                        if (!File.Exists(description))
                            continue;

                        string id = Path.GetFileName(worldDir);
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        // Count a WorldId only once even if old version directories contain
                        // another copy of the same ID.
                        if (!seenIds.Add(id))
                            continue;

                        found.Add(new WorldLocation
                        {
                            WorldId = id,
                            RootKind = root.Item1,
                            VersionDirectory = versionDir,
                            WorldDirectory = worldDir,
                            WorldDescriptionPath = description
                        });
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Validates the identity stored in WorldDescription.json.
        ///
        /// IMPORTANT: current Windrose WorldDescription uses "islandId" (lowercase i).
        /// Older versions of this plugin incorrectly looked only for "IslandId", which
        /// caused a false "IslandId is empty" error on otherwise valid worlds.
        ///
        /// When allowRepairEmptyId=true, a genuinely empty/missing islandId can be repaired
        /// from the world directory name only when the caller has already established an
        /// unambiguous identity (single discovered world or locked/configured exact world).
        /// </summary>
        private string ValidateWorldDescription(WorldLocation world, bool allowRepairEmptyId)
        {
            try
            {
                JObject obj = JObject.Parse(File.ReadAllText(world.WorldDescriptionPath));
                JObject desc = obj["WorldDescription"] as JObject;

                if (desc == null)
                    return "WorldDescription.json has no WorldDescription object: " + world.WorldDescriptionPath;

                // Current schema: lowercase "islandId".
                // Fallback to legacy/alternate capitalization for compatibility.
                string islandId = ReadString(desc, "islandId");
                if (string.IsNullOrWhiteSpace(islandId))
                    islandId = ReadString(desc, "IslandId");

                if (string.IsNullOrWhiteSpace(islandId))
                {
                    if (!allowRepairEmptyId)
                    {
                        return "WorldDescription islandId is empty in: " + world.WorldDescriptionPath;
                    }

                    // The folder name is the only authoritative ID available at this point,
                    // and the caller has already proven that this world is unambiguous.
                    BackupWorldDescription(world.WorldDescriptionPath, "before-islandId-repair");

                    // Write the CURRENT Windrose key name.
                    desc["islandId"] = world.WorldId;
                    WriteJsonAtomic(world.WorldDescriptionPath, obj);

                    Log("WORLD GUARD: Repaired empty WorldDescription islandId from folder name: " +
                        world.WorldId);

                    // Modern RocksDB_v2 builds require the updater to push JSON changes back
                    // into the database. Legacy RocksDB builds may consume JSON directly.
                    if (world.RootKind.StartsWith("RocksDB_v2", StringComparison.OrdinalIgnoreCase))
                    {
                        string updaterMessage;
                        if (!RunWorldDescriptionUpdater(world.WorldDescriptionPath, out updaterMessage))
                        {
                            return "WorldDescription islandId was repaired in JSON, but " +
                                   "R5WorldDescriptionUpdater.exe could not apply the change. " +
                                   updaterMessage;
                        }

                        Log("WORLD GUARD: " + updaterMessage);
                    }

                    islandId = world.WorldId;
                }

                if (!string.Equals(islandId, world.WorldId, StringComparison.OrdinalIgnoreCase))
                {
                    return "World folder ID and WorldDescription islandId do not match. Folder='" +
                           world.WorldId + "', islandId='" + islandId + "'. File: " +
                           world.WorldDescriptionPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                return "Could not validate WorldDescription.json: " + ex.Message +
                       " File: " + world.WorldDescriptionPath;
            }
        }

        /// <summary>
        /// Creates a small safety copy of WorldDescription.json before plugin-owned repairs.
        /// </summary>
        private void BackupWorldDescription(string sourcePath, string reason)
        {
            string folder = Path.Combine(ServerRoot, BackupFolderName);
            Directory.CreateDirectory(folder);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            string safeReason = Regex.Replace(reason ?? "world-backup", @"[^A-Za-z0-9_-]", "_");
            string worldId = Path.GetFileName(Path.GetDirectoryName(sourcePath));

            string destination = Path.Combine(
                folder,
                stamp + "_" + safeReason + "_" + worldId + "_WorldDescription.json");

            File.Copy(sourcePath, destination, false);
            PruneIdentityBackups(folder, 60);
        }

        /// <summary>
        /// Runs the official R5WorldDescriptionUpdater.exe after a plugin-owned edit to
        /// WorldDescription.json on modern RocksDB_v2 servers.
        /// </summary>
        private bool RunWorldDescriptionUpdater(string worldDescriptionPath, out string message)
        {
            string[] candidates =
            {
                Path.Combine(ServerRoot, "R5WorldDescriptionUpdater.exe"),
                Path.Combine(ServerRoot, "R5", "R5WorldDescriptionUpdater.exe"),
                Path.Combine(ServerRoot, "R5", "Binaries", "Win64", "R5WorldDescriptionUpdater.exe")
            };

            string updater = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(updater))
            {
                message =
                    "R5WorldDescriptionUpdater.exe was not found. Run a SteamCMD Update/Validate " +
                    "and try again. JSON path: " + worldDescriptionPath;
                Log("WORLD GUARD: " + message);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = updater,
                    Arguments = "\"" + worldDescriptionPath + "\"",
                    WorkingDirectory = ServerRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        message = "Failed to start R5WorldDescriptionUpdater.exe.";
                        return false;
                    }

                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch { }
                        message = "R5WorldDescriptionUpdater.exe timed out after 60 seconds.";
                        return false;
                    }

                    if (proc.ExitCode != 0)
                    {
                        message = "R5WorldDescriptionUpdater.exe failed with exit code " +
                                  proc.ExitCode + ".";
                        return false;
                    }
                }

                message = "World database updated successfully with R5WorldDescriptionUpdater.exe.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Failed to run R5WorldDescriptionUpdater.exe: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Synchronizes WindowsGSM's "Server Start Map" field with the friendly
        /// Windrose WorldDescription.WorldName.
        ///
        /// This method intentionally does NOT modify:
        ///   - ServerDescription.WorldIslandId
        ///   - WorldDescription.islandId
        ///   - the physical Worlds\<WORLD_ID> directory name
        ///
        /// On RocksDB_v2 servers, the official R5WorldDescriptionUpdater.exe is run
        /// after a WorldName change so the database receives the updated JSON value.
        /// </summary>
        private string SyncWorldNameFromWindowsGSM(WorldLocation world)
        {
            string desiredName = (_serverData.ServerMap ?? string.Empty).Trim();

            // Blank WindowsGSM map field means "leave the existing Windrose world name alone".
            if (string.IsNullOrWhiteSpace(desiredName))
                return null;

            try
            {
                string originalJson = File.ReadAllText(world.WorldDescriptionPath);
                JObject obj = JObject.Parse(originalJson);
                JObject desc = obj["WorldDescription"] as JObject;

                if (desc == null)
                {
                    return "WorldDescription.json has no WorldDescription object while synchronizing WorldName: " +
                           world.WorldDescriptionPath;
                }

                string currentName = ReadString(desc, "WorldName");
                if (string.Equals(currentName, desiredName, StringComparison.Ordinal))
                {
                    return null;
                }

                // Protect the current file before changing its friendly name.
                BackupWorldDescription(world.WorldDescriptionPath, "before-world-name-sync");

                desc["WorldName"] = desiredName;
                WriteJsonAtomic(world.WorldDescriptionPath, obj);

                Log(
                    "WORLD GUARD: WorldName changed from '" + Safe(currentName) +
                    "' to '" + desiredName + "' for WorldId=" + world.WorldId + ".");

                // Current RocksDB_v2 builds require the official updater after JSON edits.
                if (world.RootKind.StartsWith("RocksDB_v2", StringComparison.OrdinalIgnoreCase))
                {
                    string updaterMessage;
                    if (!RunWorldDescriptionUpdater(world.WorldDescriptionPath, out updaterMessage))
                    {
                        // Restore the JSON file. We deliberately block startup instead of
                        // continuing with a potentially half-applied name change.
                        try
                        {
                            WriteTextAtomic(world.WorldDescriptionPath, originalJson);

                            string rollbackMessage;
                            if (RunWorldDescriptionUpdater(world.WorldDescriptionPath, out rollbackMessage))
                            {
                                Log("WORLD GUARD: WorldName rollback applied successfully after updater failure.");
                            }
                            else
                            {
                                Log("WORLD GUARD WARNING: WorldName JSON was restored, but updater rollback also failed: " +
                                    rollbackMessage);
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            Log("WORLD GUARD WARNING: Failed to restore WorldDescription.json after WorldName sync error: " +
                                rollbackEx.Message);
                        }

                        return "Failed to apply WorldName '" + desiredName +
                               "' with R5WorldDescriptionUpdater.exe. " + updaterMessage +
                               " The server was not started.";
                    }

                    Log("WORLD GUARD: " + updaterMessage);
                }

                return null;
            }
            catch (Exception ex)
            {
                return "Could not synchronize WindowsGSM Server Start Map to Windrose WorldName: " +
                       ex.Message + " File: " + world.WorldDescriptionPath;
            }
        }

        // ---------------------------------------------------------------------
        // State / backup / logging
        // ---------------------------------------------------------------------

        private WorldGuardState LoadState()
        {
            if (!File.Exists(StatePath))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<WorldGuardState>(File.ReadAllText(StatePath));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "World Guard state file is unreadable: " + StatePath + ". " + ex.Message);
            }
        }

        private void SaveState(WorldGuardState state)
        {
            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            WriteTextAtomic(StatePath, json);
        }

        private void BackupIdentityFiles(string reason)
        {
            string folder = Path.Combine(ServerRoot, BackupFolderName);
            Directory.CreateDirectory(folder);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            string safeReason = Regex.Replace(reason ?? "backup", @"[^A-Za-z0-9_-]", "_");

            if (File.Exists(ServerDescriptionPath))
            {
                File.Copy(
                    ServerDescriptionPath,
                    Path.Combine(folder, stamp + "_" + safeReason + "_ServerDescription.json"),
                    false);
            }

            if (File.Exists(StatePath))
            {
                File.Copy(
                    StatePath,
                    Path.Combine(folder, stamp + "_" + safeReason + "_" + StateFileName),
                    false);
            }

            PruneIdentityBackups(folder, 40);
        }

        private static void PruneIdentityBackups(string folder, int keep)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(folder)
                    .GetFiles("*.json")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToArray();

                for (int i = keep; i < files.Length; i++)
                {
                    try { files[i].Delete(); }
                    catch { }
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
                File.AppendAllText(Path.Combine(ServerRoot, LogFileName), line + Environment.NewLine);
            }
            catch
            {
                // Logging must never prevent server management.
            }
        }

        private static void WriteJsonAtomic(string path, JObject obj)
        {
            WriteTextAtomic(path, obj.ToString(Formatting.Indented));
        }

        private static void WriteTextAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temp = path + ".wgsm.tmp";
            File.WriteAllText(temp, text);

            if (File.Exists(path))
            {
                string replaceBackup = path + ".wgsm.replace.bak";
                try
                {
                    File.Replace(temp, path, replaceBackup, true);
                    if (File.Exists(replaceBackup))
                        File.Delete(replaceBackup);
                    return;
                }
                catch
                {
                    if (File.Exists(replaceBackup))
                    {
                        try { File.Delete(replaceBackup); }
                        catch { }
                    }
                }
            }

            File.Copy(temp, path, true);
            File.Delete(temp);
        }

        // ---------------------------------------------------------------------
        // Parameter helpers
        // ---------------------------------------------------------------------

        private static string GetOptionValue(string input, string option)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(option))
                return null;

            string pattern =
                @"(?:^|\s)" + Regex.Escape(option) +
                @"(?:\s+|=)(?:""([^""]*)""|'([^']*)'|([^\s]+))";

            Match match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            for (int i = 1; i <= 3; i++)
            {
                if (match.Groups[i].Success)
                    return match.Groups[i].Value;
            }

            return null;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
                return true;

            if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static string ReadString(JObject obj, string key)
        {
            JToken token = obj[key];
            return token == null || token.Type == JTokenType.Null
                ? string.Empty
                : token.Value<string>() ?? string.Empty;
        }

        private static bool IdsEqual(string a, string b)
        {
            return string.Equals(
                (a ?? string.Empty).Trim(),
                (b ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------------
        // Paths
        // ---------------------------------------------------------------------

        private string ServerRoot
        {
            get { return Functions.ServerPath.GetServersServerFiles(_serverData.ServerID); }
        }

        private string ServerDescriptionPath
        {
            get { return Path.Combine(ServerRoot, "R5", "ServerDescription.json"); }
        }

        private string StatePath
        {
            get { return Path.Combine(ServerRoot, StateFileName); }
        }

        // ---------------------------------------------------------------------
        // Models
        // ---------------------------------------------------------------------

        private sealed class WorldGuardState
        {
            public int Version { get; set; }
            public bool Locked { get; set; }
            public string PersistentServerId { get; set; }
            public string InviteCode { get; set; }
            public string WorldIslandId { get; set; }
            public string FirstLockedUtc { get; set; }
            public string LastValidatedUtc { get; set; }
        }

        private sealed class WorldLocation
        {
            public string WorldId { get; set; }
            public string RootKind { get; set; }
            public string VersionDirectory { get; set; }
            public string WorldDirectory { get; set; }
            public string WorldDescriptionPath { get; set; }
        }

        private sealed class WorldGuardResult
        {
            public bool Ok { get; private set; }
            public string Message { get; private set; }

            public static WorldGuardResult Success(string message)
            {
                return new WorldGuardResult { Ok = true, Message = message };
            }

            public static WorldGuardResult Fail(string message)
            {
                return new WorldGuardResult { Ok = false, Message = message };
            }
        }
    }
}
