using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
public class Windrose : SteamCMDAgent
{
public Plugin Plugin = new Plugin
{
name = "WindowsGSM.Windrose.WorldGuard",
author = "Luiz Augusto Barcelos",
description = "Windrose Dedicated Server for WindowsGSM with WorldGuard protection and safe connection-mode management",
version = "2.4.1",
url = "https://github.com/BARCELOSTV/WindowsGSM.Windrose.WorldGuard",
color = "#8B1A1A"
};
public override bool loginAnonymous => true;
public override string AppId => "4129620";
public override string StartPath => @"R5\Binaries\Win64\WindroseServer-Win64-Shipping.exe";
public string FullName = "Windrose Dedicated Server";
public bool AllowsEmbedConsole = false;
public int PortIncrements = 0;
public dynamic QueryMethod = null;
public string Port = "7777";
public string QueryPort = "7778";
public string Defaultmap = "Windrose";
public string Maxplayers = "4";
public string Additional = "";
public string Error;
public string Notice;
private const uint CTRL_C_EVENT = 0;
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool AttachConsole(uint dwProcessId);
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool FreeConsole();
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
[DllImport("kernel32.dll")]
private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handlerRoutine, bool add);
private delegate bool ConsoleCtrlDelegate(uint ctrlType);
private readonly ServerConfig _serverData;
private const string StateFileName = "WindowsGSM_Windrose_State.json";
private const string LogFileName = "WindowsGSM_Windrose.log";
private const string BackupFolderName = "WindowsGSM_Windrose_Backups";
public Windrose(ServerConfig serverData) : base(serverData)
{
base.serverData = _serverData = serverData;
}
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
public async Task Stop(Process p)
{
Error = null;
if (p == null)
return;
await Task.Run(() =>
{
string stopRoot = ResolveServerRootFromProcess(p);
try
{
StopLog(stopRoot, "STOP: Request received for PID=" + SafeProcessId(p) + ".");
try
{
if (p.HasExited)
{
StopLog(stopRoot, "STOP: Process was already exited.");
return;
}
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: Could not read initial process state: " + ex.Message);
}
bool consoleSignalSent = TrySendConsoleCtrlCToProcess(p, stopRoot);
if (consoleSignalSent)
{
StopLog(stopRoot, "STOP: Native console CTRL+C signal sent. Waiting up to 20 seconds for graceful shutdown.");
try
{
if (p.WaitForExit(20000))
{
LogObservedProcessExit(stopRoot, p, "native console signal");
return;
}
StopLog(stopRoot, "STOP WARNING: Process did not exit within 20 seconds after native console signal.");
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: Wait after native console signal failed: " + ex.Message);
}
}
bool closeRequested = false;
try
{
p.Refresh();
if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
{
closeRequested = p.CloseMainWindow();
StopLog(
stopRoot,
closeRequested
? "STOP: CloseMainWindow fallback requested. Waiting up to 10 seconds."
: "STOP: CloseMainWindow fallback returned false.");
}
else if (!p.HasExited)
{
StopLog(stopRoot, "STOP: CloseMainWindow fallback unavailable because no MainWindowHandle exists.");
}
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: CloseMainWindow fallback failed: " + ex.Message);
}
if (closeRequested)
{
try
{
if (p.WaitForExit(10000))
{
LogObservedProcessExit(stopRoot, p, "CloseMainWindow fallback");
return;
}
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: Wait after CloseMainWindow fallback failed: " + ex.Message);
}
}
try
{
if (!p.HasExited)
{
StopLog(
stopRoot,
"STOP WARNING: Graceful shutdown methods failed or timed out. " +
"Forcing process termination as FINAL fallback.");
p.Kill();
if (p.WaitForExit(5000))
{
StopLog(stopRoot, "STOP: Process terminated by FINAL fallback. ExitCode=" +
SafeExitCode(p) + ".");
}
else
{
StopLog(stopRoot, "STOP ERROR: Process still appears alive after Kill().");
}
}
}
catch (Exception ex)
{
Error = "Failed to stop Windrose: " + ex.Message;
StopLog(stopRoot, "STOP ERROR: " + ex);
}
}
catch (Exception ex)
{
Error = "Failed to stop Windrose: " + ex.Message;
try
{
StopLog(stopRoot, "STOP ERROR (outer): " + ex);
}
catch { }
}
});
}
private static bool TrySendConsoleCtrlCToProcess(Process p, string stopRoot)
{
if (p == null)
return false;
uint pid;
try
{
pid = unchecked((uint)p.Id);
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: Could not resolve Windrose PID for console signalling: " + ex.Message);
return false;
}
bool attached = false;
bool ignoreCtrlCInstalled = false;
try
{
try { FreeConsole(); }
catch { }
if (!AttachConsole(pid))
{
int error = Marshal.GetLastWin32Error();
StopLog(stopRoot, "STOP WARNING: AttachConsole(" + pid + ") failed. Win32Error=" + error + ".");
return false;
}
attached = true;
StopLog(stopRoot, "STOP: Attached to Windrose console by PID=" + pid + ".");
if (SetConsoleCtrlHandler(null, true))
{
ignoreCtrlCInstalled = true;
}
else
{
StopLog(stopRoot, "STOP WARNING: SetConsoleCtrlHandler(ignore CTRL+C) failed. Win32Error=" +
Marshal.GetLastWin32Error() + ".");
return false;
}
if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0))
{
int error = Marshal.GetLastWin32Error();
StopLog(stopRoot, "STOP WARNING: GenerateConsoleCtrlEvent(CTRL_C_EVENT) failed. Win32Error=" +
error + ".");
return false;
}
StopLog(stopRoot, "STOP: CTRL_C_EVENT generated successfully for the attached Windrose console.");
System.Threading.Thread.Sleep(250);
return true;
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP WARNING: Native console CTRL+C signalling failed: " + ex.Message);
return false;
}
finally
{
if (ignoreCtrlCInstalled)
{
try { SetConsoleCtrlHandler(null, false); }
catch { }
}
if (attached)
{
try
{
if (FreeConsole())
StopLog(stopRoot, "STOP: Detached from Windrose console.");
else
StopLog(stopRoot, "STOP WARNING: FreeConsole failed. Win32Error=" +
Marshal.GetLastWin32Error() + ".");
}
catch { }
}
}
}
private static string ResolveServerRootFromProcess(Process p)
{
if (p == null)
return null;
try
{
string workingDirectory = p.StartInfo != null
? p.StartInfo.WorkingDirectory
: null;
if (!string.IsNullOrWhiteSpace(workingDirectory) &&
Directory.Exists(workingDirectory))
{
return workingDirectory;
}
}
catch { }
try
{
string exePath = p.MainModule != null ? p.MainModule.FileName : null;
if (!string.IsNullOrWhiteSpace(exePath))
{
DirectoryInfo dir = new FileInfo(exePath).Directory;
if (dir != null) dir = dir.Parent;
if (dir != null) dir = dir.Parent;
if (dir != null) dir = dir.Parent;
if (dir != null && Directory.Exists(dir.FullName))
return dir.FullName;
}
}
catch { }
return null;
}
private static void StopLog(string serverRoot, string message)
{
if (string.IsNullOrWhiteSpace(serverRoot))
return;
try
{
string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
File.AppendAllText(
Path.Combine(serverRoot, LogFileName),
line + Environment.NewLine);
}
catch
{
}
}
private static void LogObservedProcessExit(string stopRoot, Process p, string mechanism)
{
try
{
int exitCode = p.ExitCode;
string hex = "0x" + unchecked((uint)exitCode).ToString("X8");
StopLog(stopRoot, "STOP: Process exited after " + mechanism +
". ExitCode=" + exitCode + " (" + hex + ").");
if (exitCode != 0)
{
StopLog(stopRoot,
"STOP WARNING: Windrose returned a non-zero Windows process exit code after " +
"responding to the graceful shutdown signal. Review R5.log if shutdown integrity is uncertain. " +
"On tested Windrose build 0.10.0.9.32-22d39a16, exit code 0xC0000005 was observed only after " +
"successful synchronous backups, RocksDB closure, engine shutdown, and log closure.");
}
}
catch (Exception ex)
{
StopLog(stopRoot, "STOP: Process exited after " + mechanism +
", but ExitCode could not be read: " + ex.Message);
}
}
private static string SafeProcessId(Process p)
{
try { return p.Id.ToString(); }
catch { return "(unknown)"; }
}
private static string SafeExitCode(Process p)
{
try { return p.ExitCode.ToString(); }
catch { return "(unavailable)"; }
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
private WorldGuardResult PrepareWorldGuardAndPatchConfiguration()
{
string configPath = ServerDescriptionPath;
string statePath = StatePath;
List<WorldLocation> worlds = FindExistingWorlds();
WorldGuardState state = LoadState();
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
if (state != null && state.Locked)
{
if (!IdsEqual(state.PersistentServerId, persistentServerId))
{
return WorldGuardResult.Fail(
"WORLD GUARD: PersistentServerId changed unexpectedly. " +
"Locked='" + Safe(state.PersistentServerId) + "', current='" + Safe(persistentServerId) +
"'. Startup blocked.");
}
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
string worldDescriptionProblem = ValidateWorldDescription(selectedWorld, true);
if (!string.IsNullOrWhiteSpace(worldDescriptionProblem))
{
return WorldGuardResult.Fail("WORLD GUARD: " + worldDescriptionProblem);
}
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
string worldNameProblem = SyncWorldNameFromWindowsGSM(selectedWorld);
if (!string.IsNullOrWhiteSpace(worldNameProblem))
{
return WorldGuardResult.Fail("WORLD GUARD: " + worldNameProblem);
}
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
{
persistent["ServerName"] = _serverData.ServerName.Trim();
Log("CONFIG: ServerName synchronized from WindowsGSM.");
}
else
{
Log("CONFIG: WindowsGSM Server Name is empty; existing ServerName preserved.");
}
if (!string.IsNullOrWhiteSpace(_serverData.ServerMaxPlayer))
{
int maxPlayers;
if (!int.TryParse(_serverData.ServerMaxPlayer, out maxPlayers) ||
maxPlayers < 1 || maxPlayers > 200)
{
throw new InvalidOperationException(
"WindowsGSM Server Maxplayer is invalid. Use a value from 1 to 200.");
}
persistent["MaxPlayerCount"] = maxPlayers;
Log("CONFIG: WindowsGSM Server Maxplayer synchronized to MaxPlayerCount=" +
maxPlayers + ".");
}
else
{
Log("CONFIG: WindowsGSM Server Maxplayer is empty; existing MaxPlayerCount preserved.");
}
if (!string.IsNullOrWhiteSpace(_serverData.ServerIP))
{
string serverIp = _serverData.ServerIP.Trim();
System.Net.IPAddress parsedIp;
if (!System.Net.IPAddress.TryParse(serverIp, out parsedIp))
{
throw new InvalidOperationException(
"WindowsGSM Server IP Address is invalid: '" + serverIp + "'.");
}
if (persistent["P2pProxyAddress"] != null)
{
persistent["P2pProxyAddress"] = serverIp;
Log("CONFIG: WindowsGSM Server IP synchronized to P2pProxyAddress=" +
serverIp + ".");
}
}
else
{
Log("CONFIG: WindowsGSM Server IP Address is empty; P2pProxyAddress preserved.");
}
string password;
int passwordOptionCount;
if (TryGetLastOptionValue(_serverData.ServerParam, "-password", out password, out passwordOptionCount))
{
persistent["Password"] = password ?? string.Empty;
persistent["IsPasswordProtected"] = !string.IsNullOrEmpty(password);
Log("CONFIG: Password parameter detected (" +
passwordOptionCount + " occurrence(s)); password protection=" +
(!string.IsNullOrEmpty(password) ? "enabled" : "disabled") + ".");
if (passwordOptionCount > 1)
{
Log("CONFIG WARNING: Multiple password parameters were found. The last value was used.");
}
}
else
{
string existingPassword = ReadString(persistent, "Password");
bool shouldProtect = !string.IsNullOrEmpty(existingPassword);
persistent["IsPasswordProtected"] = shouldProtect;
Log("CONFIG: No password parameter detected; existing password preserved and " +
"IsPasswordProtected normalized to " + (shouldProtect ? "true" : "false") + ".");
}
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
string effectiveRegion = ReadString(persistent, "UserSelectedRegion");
Log("CONNECTION: Connection Service region=" +
(string.IsNullOrWhiteSpace(effectiveRegion) ? "AUTO" : effectiveRegion) + ".");
bool boolValue;
bool useDirectConnection;
bool connectionModeWasExplicit = false;
bool initializedConnectionDefault = false;
string connectionMode = GetOptionValue(_serverData.ServerParam, "-wr-connection");
if (!string.IsNullOrWhiteSpace(connectionMode))
{
connectionModeWasExplicit = true;
string normalizedMode = connectionMode.Trim().ToLowerInvariant();
if (normalizedMode == "direct" ||
normalizedMode == "direct-ip" ||
normalizedMode == "ip")
{
useDirectConnection = true;
}
else if (normalizedMode == "invite" ||
normalizedMode == "code" ||
normalizedMode == "p2p" ||
normalizedMode == "ice")
{
useDirectConnection = false;
}
else
{
throw new InvalidOperationException(
"Invalid -wr-connection value '" + connectionMode +
"'. Use invite or direct.");
}
}
else
{
string direct = GetOptionValue(_serverData.ServerParam, "-wr-direct");
if (!string.IsNullOrWhiteSpace(direct))
{
connectionModeWasExplicit = true;
if (!TryParseBool(direct, out boolValue))
throw new InvalidOperationException("Invalid -wr-direct value. Use true or false.");
useDirectConnection = boolValue;
}
else
{
JToken currentDirectToken = persistent["UseDirectConnection"];
if (currentDirectToken != null &&
currentDirectToken.Type == JTokenType.Boolean)
{
useDirectConnection = currentDirectToken.Value<bool>();
Log("CONNECTION: No connection-mode parameter supplied; existing " +
"UseDirectConnection=" +
(useDirectConnection ? "true" : "false") +
" preserved.");
}
else
{
useDirectConnection = false;
initializedConnectionDefault = true;
Log("CONNECTION: UseDirectConnection is missing or invalid; initializing " +
"to false (Invite Code / Connection Service default).");
}
}
}
if (connectionModeWasExplicit || initializedConnectionDefault)
{
persistent["UseDirectConnection"] = useDirectConnection;
}
if (connectionModeWasExplicit)
{
if (useDirectConnection)
{
Log("CONNECTION: Explicit override -> Direct IP mode enabled " +
"(UseDirectConnection=true).");
}
else
{
Log("CONNECTION: Explicit override -> Invite Code / Connection Service mode " +
"enabled (UseDirectConnection=false).");
}
}
else if (!initializedConnectionDefault)
{
Log("CONNECTION: Effective preserved mode=" +
(useDirectConnection
? "Direct IP (UseDirectConnection=true)."
: "Invite Code / Connection Service (UseDirectConnection=false)."));
}
if (persistent["DirectConnectionServerPort"] != null)
{
int directPort;
if (!int.TryParse(_serverData.ServerPort, out directPort) ||
directPort < 1 || directPort > 65535)
{
throw new InvalidOperationException(
"WindowsGSM Server Port is invalid. Use a value from 1 to 65535.");
}
persistent["DirectConnectionServerPort"] = directPort;
Log("CONFIG: WindowsGSM Server Port synchronized to DirectConnectionServerPort=" +
directPort + ".");
}
string bind = GetOptionValue(_serverData.ServerParam, "-wr-bind");
if (!string.IsNullOrWhiteSpace(bind) && persistent["DirectConnectionProxyAddress"] != null)
{
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
if (!string.IsNullOrWhiteSpace(_serverData.ServerQueryPort))
{
Log("CONFIG: WindowsGSM Query Port=" + _serverData.ServerQueryPort +
" is not mapped because Windrose has no Query/A2S port field.");
}
if (!string.IsNullOrWhiteSpace(_serverData.ServerGSLT))
{
Log("CONFIG: WindowsGSM GSLT is set but ignored; Windrose does not use GSLT.");
}
}
private List<WorldLocation> FindExistingWorlds()
{
var found = new List<WorldLocation>();
var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
string profileRoot = Path.Combine(
ServerRoot, "R5", "Saved", "SaveProfiles", "Default");
if (!Directory.Exists(profileRoot))
return found;
var roots = new List<Tuple<string, string>>
{
Tuple.Create("RocksDB_v2 (runtime)", Path.Combine(profileRoot, "RocksDB_v2")),
Tuple.Create("RocksDB (legacy)", Path.Combine(profileRoot, "RocksDB"))
};
foreach (Tuple<string, string> root in roots)
{
if (!Directory.Exists(root.Item2))
continue;
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
private string ValidateWorldDescription(WorldLocation world, bool allowRepairEmptyId)
{
try
{
JObject obj = JObject.Parse(File.ReadAllText(world.WorldDescriptionPath));
JObject desc = obj["WorldDescription"] as JObject;
if (desc == null)
return "WorldDescription.json has no WorldDescription object: " + world.WorldDescriptionPath;
string islandId = ReadString(desc, "islandId");
if (string.IsNullOrWhiteSpace(islandId))
islandId = ReadString(desc, "IslandId");
if (string.IsNullOrWhiteSpace(islandId))
{
if (!allowRepairEmptyId)
{
return "WorldDescription islandId is empty in: " + world.WorldDescriptionPath;
}
BackupWorldDescription(world.WorldDescriptionPath, "before-islandId-repair");
desc["islandId"] = world.WorldId;
WriteJsonAtomic(world.WorldDescriptionPath, obj);
Log("WORLD GUARD: Repaired empty WorldDescription islandId from folder name: " +
world.WorldId);
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
private string SyncWorldNameFromWindowsGSM(WorldLocation world)
{
string desiredName = (_serverData.ServerMap ?? string.Empty).Trim();
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
BackupWorldDescription(world.WorldDescriptionPath, "before-world-name-sync");
desc["WorldName"] = desiredName;
WriteJsonAtomic(world.WorldDescriptionPath, obj);
Log(
"WORLD GUARD: WorldName changed from '" + Safe(currentName) +
"' to '" + desiredName + "' for WorldId=" + world.WorldId + ".");
if (world.RootKind.StartsWith("RocksDB_v2", StringComparison.OrdinalIgnoreCase))
{
string updaterMessage;
if (!RunWorldDescriptionUpdater(world.WorldDescriptionPath, out updaterMessage))
{
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
private static bool TryGetLastOptionValue(
string input,
string option,
out string value,
out int occurrenceCount)
{
value = null;
occurrenceCount = 0;
if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(option))
return false;
string normalized = input
.Replace('\u201C', '"')
.Replace('\u201D', '"')
.Replace('\u2018', '\'')
.Replace('\u2019', '\'')
.Replace('\u2013', '-')
.Replace('\u2014', '-');
string bareOption = option.TrimStart('-');
string optionPattern =
@"(?<!\S)(?:--?" + Regex.Escape(bareOption) +
@"|" + Regex.Escape(bareOption) + @")";
string pattern =
optionPattern +
@"(?:\s+|=)(?:""([^""]*)""|'([^']*)'|([^\s]+))";
MatchCollection matches = Regex.Matches(
normalized,
pattern,
RegexOptions.IgnoreCase);
occurrenceCount = matches.Count;
if (matches.Count == 0)
return false;
Match match = matches[matches.Count - 1];
for (int i = 1; i <= 3; i++)
{
if (match.Groups[i].Success)
{
value = match.Groups[i].Value;
return true;
}
}
value = string.Empty;
return true;
}
private static string GetOptionValue(string input, string option)
{
string value;
int count;
return TryGetLastOptionValue(input, option, out value, out count)
? value
: null;
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
