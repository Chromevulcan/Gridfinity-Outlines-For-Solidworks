using System;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Gridfinity.Core;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace Gridfinity.Sw
{
    /// <summary>
    /// SolidWorks add-in shell. Adds a "Gridfinity Outlines" menu with three commands: the tray designer dialog,
    /// a one-click "build from JSON file", and "build tray from outlines" (outline-app DXF/SVG exports straight to a
    /// part). Register with regasm /codebase (see scripts\register.cmd).
    ///
    /// This add-in has its own COM identity, menu group and assembly names, so it can be installed next to the
    /// original Gridfinity for SolidWorks add-in without either one replacing the other.
    /// </summary>
    [ComVisible(true)]
    [Guid("8C0F3B52-6A1D-4E7B-A9C2-3D5E7F914B60")]
    [ProgId("GridfinityOutlines.SolidWorksAddIn")]
    public class GridfinityAddIn : ISwAddin
    {
        private const string AddInTitle = "Gridfinity Outlines";
        private const string AddInDescription = "Parametric Gridfinity trays, baseplates and clips, with pockets traced from photos (outline-app DXF/SVG).";
        private const int GroupId = 0x47464F4C; // "GFOL"; the original add-in uses "GFNY"
        private const int DesignerCommandId = 1;
        private const int JsonCommandId = 2;
        private const int OutlinesCommandId = 3;
        private const int MenuDelayMs = 4000;

        private ISldWorks _app;
        private int _cookie;
        private ICommandGroup _group;
        private bool _building;
        private bool _menuCreated;
        private long _lastDiagnosticCheckMs;
        private string _lastObservedGroupState = "not checked";
        private string _lastCommandManagerIdentity = null;
        private SldWorks _events;

        private readonly System.Diagnostics.Stopwatch _sinceConnect =
            new System.Diagnostics.Stopwatch();

        private static readonly object LogLock = new object();

        private static string LogPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.LocalApplicationData),
                    "GridfinityOutlines",
                    "Logs",
                    "Gridfinity_addin.log");
            }
        }

        private static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    string directory = Path.GetDirectoryName(LogPath);

                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.AppendAllText(
                        LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                        "  " + message + System.Environment.NewLine);
                }
            }
            catch
            {
                // Diagnostics must never prevent the add-in from loading.
            }
        }

        private static void LogEnvironment(ISldWorks app)
        {
            try
            {
                Assembly assembly = typeof(GridfinityAddIn).Assembly;
                string assemblyPath = assembly.Location;
                Version assemblyVersion = assembly.GetName().Version;

                string fileVersion = string.IsNullOrEmpty(assemblyPath)
                    ? "unavailable"
                    : FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion;

                string solidWorksRevision = "unavailable";

                try
                {
                    object revision = app.GetType().InvokeMember(
                        "RevisionNumber",
                        BindingFlags.InvokeMethod,
                        null,
                        app,
                        null);

                    if (revision != null)
                        solidWorksRevision = Convert.ToString(revision);
                }
                catch (Exception ex)
                {
                    solidWorksRevision =
                        "unavailable (" + ex.GetType().Name + ")";
                }

                Log("Environment: Add-in assembly version=" +
                    assemblyVersion +
                    "; file version=" + fileVersion +
                    "; assembly path=" + assemblyPath);

                Log("Environment: SOLIDWORKS revision=" +
                    solidWorksRevision +
                    "; Windows=" +
                    System.Environment.OSVersion.VersionString +
                    "; 64-bit OS=" +
                    System.Environment.Is64BitOperatingSystem +
                    "; 64-bit process=" +
                    System.Environment.Is64BitProcess +
                    "; .NET CLR=" +
                    System.Environment.Version);

                Log("Log file: " + LogPath);
            }
            catch (Exception ex)
            {
                Log("Could not collect environment information: " + ex);
            }
        }

        // Called by SOLIDWORKS on its UI thread.
        private int OnIdle()
        {
            CheckMenuState("idle");
            LogCommandGroupState("idle");
            return 0;
        }

        private int OnActiveDocChanged()
        {
            CheckMenuState("active document changed");
            return 0;
        }

        private int On3DExperienceStateChangeNotify(int newState)
        {
            Log("3DEXPERIENCE state changed: newState=" + newState +
                "; elapsed=" + _sinceConnect.Elapsed.TotalSeconds.ToString("F1") + " seconds after add-in connection.");

            return 0;
        }

        private void CheckMenuState(string reason)
        {
            if (_app == null ||
                _sinceConnect.ElapsedMilliseconds < MenuDelayMs)
                return;

            // Create the Gridfinity menu once after SOLIDWORKS has had
            // time to initialize.
            //
            // Do not repeatedly recreate it based on GetCommandGroup()
            // results. That check has been unreliable and has caused
            // duplicate menu entries.
            if (!_menuCreated)
            {
                CreateMenu(
                    reason + " at " +
                    (_sinceConnect.ElapsedMilliseconds / 1000.0)
                        .ToString("0.0") +
                    " s after connecting");
            }
        }

        private void LogCommandGroupState(string source)
        {
            if (_app == null)
                return;

            try
            {
                var commandManager = _app.GetCommandManager(_cookie);

                if (commandManager == null)
                {
                    Log("[" + source + "] Diagnostic: GetCommandManager returned null.");
                    return;
                }

                IntPtr identityPointer = Marshal.GetIUnknownForObject(commandManager);
                string managerIdentity;

                try
                {
                    managerIdentity = identityPointer.ToInt64().ToString();
                }
                finally
                {
                    Marshal.Release(identityPointer);
                }

                if (_lastCommandManagerIdentity != managerIdentity)
                {
                    Log("[" + source + "] Diagnostic: CommandManager COM identity changed from " +
                        (_lastCommandManagerIdentity ?? "(initial)") + " to " + managerIdentity + ".");

                    // Check the command group using the newly returned CommandManager.
                    try
                    {
                        var group = commandManager.GetCommandGroup(GroupId);

                        Log("[" + source + "] Diagnostic: GetCommandGroup after identity change returned " +
                            (group == null ? "null" : "a command group") + ".");
                    }
                    catch (Exception ex)
                    {
                        Log("[" + source + "] Diagnostic: GetCommandGroup after identity change threw: " +
                            ex.Message);
                    }

                    // Also check the existing command-group reference.
                    Log("[" + source + "] Diagnostic: Existing _group reference is " +
                        (_group == null ? "null" : "not null") + ".");

                    _lastCommandManagerIdentity = managerIdentity;
                }
            }
            catch (Exception ex)
            {
                Log("[" + source + "] Diagnostic: CommandManager check failed: " + ex.Message);
            }
        }

        private void Hook()
        {
            try
            {
                _events = (SldWorks)_app;

                _events.OnIdleNotify += OnIdle;
                _events.ActiveModelDocChangeNotify += OnActiveDocChanged;
                _events.On3DExperienceStateChangeNotify += On3DExperienceStateChangeNotify;

                Log("Subscribed to OnIdleNotify, ActiveModelDocChangeNotify, and On3DExperienceStateChangeNotify.");
            }
            catch (Exception ex)
            {
                Log("Could not subscribe to SOLIDWORKS events: " + ex);

                Unhook();

                // Best-effort fallback.
                CreateMenu("event subscription fallback");
            }
        }

        private void Unhook()
        {
            if (_events == null)
                return;

            try
            {
                _events.OnIdleNotify -= OnIdle;
            }
            catch (Exception ex)
            {
                Log("OnIdleNotify unsubscribe failed: " + ex.Message);
            }

            try
            {
                _events.ActiveModelDocChangeNotify -= OnActiveDocChanged;
            }
            catch (Exception ex)
            {
                Log("ActiveModelDocChangeNotify unsubscribe failed: " +
                    ex.Message);
            }

            try
            {
                _events.On3DExperienceStateChangeNotify -= On3DExperienceStateChangeNotify;
            }
            catch (Exception ex)
            {
                Log("On3DExperienceStateChangeNotify unsubscribe failed: " +
                    ex.Message);
            }

            _events = null;
        }

        public bool ConnectToSW(object thisSw, int cookie)
        {
            try
            {
                Log("ConnectToSW start (cookie " +
                    cookie +
                    ", assembly " +
                    typeof(GridfinityAddIn).Assembly.Location +
                    ")");

                _app = (ISldWorks)thisSw;
                _cookie = cookie;

                LogEnvironment(_app);

                Log("ConnectToSW: SOLIDWORKS add-in connection established.");

                _menuCreated = false;
                _building = false;
                _group = null;

                _lastDiagnosticCheckMs = 0;
                _lastObservedGroupState = "not checked";
                _lastCommandManagerIdentity = null;

                _app.SetAddinCallbackInfo2(0, this, _cookie);

                _sinceConnect.Restart();

                Hook();

                return true;
            }
            catch (Exception ex)
            {
                Log("ConnectToSW failed: " + ex);

                // Returning true keeps the add-in loaded so the log
                // can be inspected.
                return true;
            }
        }

        /// <summary>
        /// Reads the command IDs saved by SOLIDWORKS.
        /// IgnorePreviousVersion should only be true when the saved
        /// command definition differs from this version.
        /// </summary>
        private bool ShouldIgnorePreviousVersion(
            ICommandManager commandManager)
        {
            try
            {
                object savedIds;

                bool found =
                    commandManager.GetGroupDataFromRegistry(
                        GroupId,
                        out savedIds);

                if (!found || savedIds == null)
                {
                    Log("No saved command-group IDs found; preserving registry state.");
                    return false;
                }

                Array ids = savedIds as Array;

                if (ids == null)
                {
                    Log("Saved command-group IDs had unexpected type " +
                        savedIds.GetType().FullName +
                        "; preserving registry state.");

                    return false;
                }

                int[] expected =
                {
                    DesignerCommandId,
                    JsonCommandId,
                    OutlinesCommandId
                };

                bool match = ids.Length == expected.Length;

                if (match)
                {
                    for (int i = 0; i < expected.Length; i++)
                    {
                        try
                        {
                            if (Convert.ToInt32(ids.GetValue(i)) != expected[i])
                            {
                                match = false;
                                break;
                            }
                        }
                        catch
                        {
                            match = false;
                            break;
                        }
                    }
                }

                Log("Saved command IDs: [" +
                    JoinArray(ids) +
                    "]; current command IDs: [" +
                    DesignerCommandId +
                    ", " +
                    JsonCommandId +
                    ", " +
                    OutlinesCommandId +
                    "]; match=" +
                    match);

                return !match;
            }
            catch (Exception ex)
            {
                // Preserve saved user customization if registry lookup fails.
                Log("GetGroupDataFromRegistry failed; preserving previous settings: " +
                    ex);

                return false;
            }
        }

        private static string JoinArray(Array values)
        {
            string[] parts = new string[values.Length];

            for (int i = 0; i < values.Length; i++)
                parts[i] = Convert.ToString(values.GetValue(i));

            return string.Join(", ", parts);
        }

        private bool CreateMenu(string reason)
        {
            if (_app == null || _building || _menuCreated)
                return _menuCreated;

            _building = true;

            ICommandManager commandManager = null;
            ICommandGroup candidateGroup = null;

            try
            {
                commandManager = _app.GetCommandManager(_cookie);

                if (commandManager == null)
                {
                    Log("[" + reason +
                        "] GetCommandManager returned NULL; will retry.");

                    return false;
                }

                bool ignorePreviousVersion =
                    ShouldIgnorePreviousVersion(commandManager);

                int error = 0;

                candidateGroup = commandManager.CreateCommandGroup2(
                    GroupId,
                    "Gridfinity Outlines",
                    "Gridfinity Outlines",
                    "Gridfinity trays with pockets traced from photos",
                    -1,
                    ignorePreviousVersion,
                    ref error);

                Log("[" + reason +
                    "] CreateCommandGroup2: " +
                    (candidateGroup == null ? "NULL" : "ok") +
                    ", error code " +
                    error +
                    ", IgnorePreviousVersion=" +
                    ignorePreviousVersion);

                if (candidateGroup == null)
                    return false;

                int menu =
                    (int)swCommandItemType_e.swMenuItem;

                int designerIndex = candidateGroup.AddCommandItem2(
                    "Gridfinity designer...",
                    -1,
                    "Design a box, baseplate or part holder",
                    "Designer",
                    0,
                    "OnNewTray",
                    "CanRun",
                    DesignerCommandId,
                    menu);

                int jsonIndex = candidateGroup.AddCommandItem2(
                    "Build from JSON file...",
                    -1,
                    "Build a box or baseplate from a saved definition",
                    "Build from JSON",
                    0,
                    "OnBuildFromJson",
                    "CanRun",
                    JsonCommandId,
                    menu);

                int outlinesIndex = candidateGroup.AddCommandItem2(
                    "Build tray from outlines (DXF/SVG)...",
                    -1,
                    "Turn outline-app exports into a Gridfinity tray in one step",
                    "Build from outlines",
                    0,
                    "OnBuildFromOutlines",
                    "CanRun",
                    OutlinesCommandId,
                    menu);

                Log("[" + reason +
                    "] AddCommandItem2 returned " +
                    designerIndex +
                    ", " +
                    jsonIndex +
                    " and " +
                    outlinesIndex);

                if (designerIndex < 0 || jsonIndex < 0 || outlinesIndex < 0)
                {
                    Log("[" + reason +
                        "] One or more command items failed; removing partial group.");

                    try
                    {
                        commandManager.RemoveCommandGroup2(GroupId, false);
                    }
                    catch (Exception cleanupEx)
                    {
                        Log("Partial group cleanup failed: " +
                            cleanupEx.Message);
                    }

                    return false;
                }

                candidateGroup.HasToolbar = false;
                candidateGroup.HasMenu = true;

                bool activated = candidateGroup.Activate();

                Log("[" + reason +
                    "] Activate returned " +
                    activated);

                if (!activated)
                {
                    Log("[" + reason +
                        "] Activation failed; removing partial group.");

                    try
                    {
                        commandManager.RemoveCommandGroup2(GroupId, false);
                    }
                    catch (Exception cleanupEx)
                    {
                        Log("Activation cleanup failed: " +
                            cleanupEx.Message);
                    }

                    return false;
                }

                _group = candidateGroup;
                _menuCreated = true;
                _lastObservedGroupState = "not checked";

                Log("[" + reason +
                    "] Gridfinity command group created and activated successfully.");

                return true;
            }
            catch (Exception ex)
            {
                Log("[" + reason +
                    "] CreateMenu failed; will retry: " +
                    ex);

                if (candidateGroup != null && commandManager != null)
                {
                    try
                    {
                        commandManager.RemoveCommandGroup2(GroupId, false);
                    }
                    catch (Exception cleanupEx)
                    {
                        Log("Exception cleanup failed: " +
                            cleanupEx.Message);
                    }
                }

                return false;
            }
            finally
            {
                _building = false;
            }
        }

        public bool DisconnectFromSW()
        {
            Log("DisconnectFromSW: shutdown requested; connected for " +
                _sinceConnect.Elapsed.TotalSeconds.ToString("0.0") +
                " seconds.");

            Log("DisconnectFromSW diagnostic: _menuCreated=" +
                _menuCreated +
                "; _group is null=" +
                (_group == null) +
                "; cookie=" +
                _cookie +
                "; elapsed=" +
                _sinceConnect.ElapsedMilliseconds +
                " ms.");

            Unhook();

            try
            {
                if (_app != null)
                {
                    // Remove the live command group, but preserve saved
                    // user customization and registry data.
                    _app.GetCommandManager(_cookie)
                        .RemoveCommandGroup2(GroupId, false);

                    Log("Removed live command group; preserved saved customization.");
                }
            }
            catch (Exception ex)
            {
                Log("Command group removal during disconnect failed: " +
                    ex.Message);
            }

            _group = null;
            _app = null;
            _menuCreated = false;
            _building = false;

            _sinceConnect.Stop();

            return true;
        }

        // ---- Command callbacks (invoked by name from SOLIDWORKS) ----

        public void OnNewTray()
        {
            Log("Command: Gridfinity designer selected.");

            try
            {
                using (var form = new TrayDesignerForm(_app))
                    form.ShowDialog(Owner());

                Log("Command: Gridfinity designer closed.");
            }
            catch (Exception ex)
            {
                Log("Gridfinity designer command failed: " + ex);

                MessageBox.Show(
                    Owner(),
                    ex.Message,
                    AddInTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        public void OnBuildFromJson()
        {
            Log("Command: Build from JSON selected.");

            using (var dlg = new OpenFileDialog
            {
                Filter = "Gridfinity JSON (*.json)|*.json|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(Owner()) != DialogResult.OK)
                {
                    Log("Build from JSON cancelled by user.");
                    return;
                }

                try
                {
                    Log("Build from JSON: reading selected definition.");

                    string json = File.ReadAllText(dlg.FileName);

                    var builder = new GridfinityBuilder(_app);

                    SwBuildResult result =
                        BaseplateDefinition.LooksLikeBaseplate(json)
                            ? builder.BuildBaseplate(
                                BaseplateDefinition.FromJson(json))
                            : ClipDefinition.LooksLikeClips(json)
                                ? builder.BuildClips(
                                    ClipDefinition.FromJson(json))
                                : builder.Build(
                                    TrayDefinition.FromJson(json));

                    string message = result.Message;

                    if (result.Warnings.Count > 0)
                    {
                        message += "\n\nNotes:\n- " +
                            string.Join(
                                "\n- ",
                                result.Warnings.ToArray());
                    }

                    Log("Build from JSON completed. Warnings=" +
                        result.Warnings.Count +
                        "; result message=" +
                        result.Message);

                    MessageBox.Show(Owner(), message, AddInTitle);
                }
                catch (Exception ex)
                {
                    Log("Build from JSON failed: " + ex);

                    MessageBox.Show(
                        Owner(),
                        ex.Message,
                        AddInTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        public void OnBuildFromOutlines()
        {
            Log("Command: Build tray from outlines selected.");

            try
            {
                TrayDefinition tray;
                string jsonPath;
                if (!OutlineImportForm.CreateTray(Owner(), out tray, out jsonPath))
                {
                    Log("Build from outlines cancelled.");
                    return;
                }

                SwBuildResult result = new GridfinityBuilder(_app).Build(tray);
                string message = result.Message;
                if (jsonPath != null) message += "\n\nThe tray was saved as " + jsonPath + " (open it in the designer to change it).";
                if (result.Warnings.Count > 0)
                    message += "\n\nNotes:\n- " + string.Join("\n- ", result.Warnings.ToArray());

                Log("Build from outlines completed. Warnings=" + result.Warnings.Count + "; result message=" + result.Message);
                MessageBox.Show(Owner(), message, AddInTitle);
            }
            catch (Exception ex)
            {
                Log("Build from outlines failed: " + ex);
                MessageBox.Show(Owner(), ex.Message, AddInTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public int CanRun()
        {
            return 1;
        }

        private IWin32Window Owner()
        {
            try
            {
                IFrame frame = (IFrame)_app.Frame();

                return new WindowHandle(
                    new IntPtr(frame.GetHWndx64()));
            }
            catch
            {
                return null;
            }
        }

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; private set; }
        }

        // ---- COM registration (regasm /codebase) ----

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string id =
                "{" + t.GUID.ToString().ToUpperInvariant() + "}";

            using (RegistryKey key =
                Registry.LocalMachine.CreateSubKey(
                    "SOFTWARE\\SolidWorks\\Addins\\" + id))
            {
                key.SetValue(null, 0, RegistryValueKind.DWord);
                key.SetValue("Title", AddInTitle);
                key.SetValue("Description", AddInDescription);
            }

            using (RegistryKey key =
                Registry.CurrentUser.CreateSubKey(
                    "Software\\SolidWorks\\AddInsStartup\\" + id))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            string id =
                "{" + t.GUID.ToString().ToUpperInvariant() + "}";

            try
            {
                Registry.LocalMachine.DeleteSubKey(
                    "SOFTWARE\\SolidWorks\\Addins\\" + id,
                    false);
            }
            catch
            {
            }

            try
            {
                Registry.CurrentUser.DeleteSubKey(
                    "Software\\SolidWorks\\AddInsStartup\\" + id,
                    false);
            }
            catch
            {
            }
        }
    }
}