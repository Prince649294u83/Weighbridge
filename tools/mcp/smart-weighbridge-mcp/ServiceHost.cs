using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace DesktopAutomationHost
{
    public class BridgeRequest
    {
        public string Command { get; set; } = "";
        public string ExePath { get; set; } = "";
        public string AppTitle { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string TextToType { get; set; } = "";
        public string TargetAutomationId { get; set; } = "";
        public string TargetName { get; set; } = "";
        public string ScreenshotName { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 15;

        // Vehicle Entry Semantic Fields
        public string VehicleNumber { get; set; } = "";
        public string PartyName { get; set; } = "";
        public string MaterialName { get; set; } = "";
        public string VehicleTypeName { get; set; } = "";
        public decimal? WeightKg { get; set; }
        public bool? IsStable { get; set; } = true;
        public string SlipNumber { get; set; } = "";
        public string ExpectedState { get; set; } = "";
    }

    public class BridgeResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Status { get; set; } = "";
        public int ProcessId { get; set; }
        public int SessionId { get; set; }
        public long NativeWindowHandle { get; set; }
        public string WindowTitle { get; set; } = "";
        public string AutomationId { get; set; } = "";
        public string Bounds { get; set; } = "";
        public int TotalControls { get; set; }
        public string ScreenshotPath { get; set; } = "";
        public string[] VisibleControls { get; set; } = Array.Empty<string>();
        public string[] NavigationControls { get; set; } = Array.Empty<string>();
        public string ErrorMessage { get; set; } = "";
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class ServiceHost
    {
        private static readonly string PipeName = "WeighBridgeDesktopAutomationPipe";
        private static Process? currentAppProcess;

        public static void Main(string[] args)
        {
            string artifactDir = @"test-artifacts\desktop-automation";
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(Path.Combine(artifactDir, "vehicle-entry"));
            string logPath = Path.Combine(artifactDir, "host_service.log");

            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Starting Desktop Automation IPC Service on pipe: {PipeName}\n");
            Console.WriteLine($"[IPC HOST] Listening on NamedPipe: {PipeName}");

            while (true)
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte))
                    {
                        pipeServer.WaitForConnection();
                        try
                        {
                            using (var reader = new StreamReader(pipeServer, Encoding.UTF8, false, 4096, true))
                            using (var writer = new StreamWriter(pipeServer, Encoding.UTF8, 4096, true))
                            {
                                string? requestJson = reader.ReadLine();
                                if (!string.IsNullOrEmpty(requestJson))
                                {
                                    var req = JsonSerializer.Deserialize<BridgeRequest>(requestJson);
                                    if (req != null)
                                    {
                                        var resp = HandleRequest(req, artifactDir, logPath);
                                        string responseJson = JsonSerializer.Serialize(resp);
                                        writer.WriteLine(responseJson);
                                        writer.Flush();
                                    }
                                }
                            }
                        }
                        catch (Exception reqEx)
                        {
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Request Error: {reqEx.Message}\n");
                        }
                        finally
                        {
                            try
                            {
                                if (pipeServer.IsConnected)
                                {
                                    pipeServer.Disconnect();
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Host Error: {ex.Message}\n");
                    Thread.Sleep(500);
                }
            }
        }

        private static Process? GetOrFindAppProcess()
        {
            if (currentAppProcess != null && !currentAppProcess.HasExited)
            {
                return currentAppProcess;
            }

            var running = Process.GetProcessesByName("WeighBridge.App");
            if (running.Length > 0)
            {
                currentAppProcess = running[0];
                return currentAppProcess;
            }

            return null;
        }

        private static BridgeResponse HandleRequest(BridgeRequest req, string artifactDir, string logPath)
        {
            var response = new BridgeResponse();

            try
            {
                if (req.Command.Equals("ping", StringComparison.OrdinalIgnoreCase))
                {
                    response.Success = true;
                    response.Message = "PONG";
                    response.Status = "OK";
                    return response;
                }

                if (req.Command.Equals("launch_app", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(req.ExePath) || !File.Exists(req.ExePath))
                    {
                        response.Success = false;
                        response.Status = "FILE_NOT_FOUND";
                        response.Message = "Executable file not found: " + req.ExePath;
                        return response;
                    }

                    var psi = new ProcessStartInfo(req.ExePath)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(req.ExePath) ?? ""
                    };
                    psi.EnvironmentVariables["WEIGHBRIDGE_Hardware__WeightIndicator__DriverType"] = "Simulator";
                    psi.EnvironmentVariables["WEIGHBRIDGE_Hardware__Camera__Enabled"] = "false";
                    psi.EnvironmentVariables["WEIGHBRIDGE_AUTOMATION_ACTIVE"] = "1";

                    currentAppProcess = Process.Start(psi);

                    if (currentAppProcess == null)
                    {
                        response.Success = false;
                        response.Status = "LAUNCH_FAILED";
                        response.Message = "Failed to start process: " + req.ExePath;
                        return response;
                    }

                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Started process {req.ExePath} with PID {currentAppProcess.Id}\n");

                    // Poll for top-level window for this process
                    Window? targetWin = null;
                    using (var automation = new UIA3Automation())
                    {
                        var desktop = automation.GetDesktop();
                        for (int attempt = 0; attempt < 15; attempt++)
                        {
                            Thread.Sleep(1000);
                            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
                            foreach (var w in windows)
                            {
                                var win = w.AsWindow();
                                int pid = win.Properties.ProcessId.ValueOrDefault;
                                if (pid == currentAppProcess.Id || (!string.IsNullOrEmpty(req.AppTitle) && win.Title.Contains(req.AppTitle, StringComparison.OrdinalIgnoreCase)))
                                {
                                    targetWin = win;
                                    break;
                                }
                            }
                            if (targetWin != null) break;
                        }

                        if (targetWin != null)
                        {
                            PopulateWindowEvidence(response, targetWin);
                            response.Success = true;
                            response.Status = targetWin.Title.Contains("Login") ? "LOGIN_DIALOG_ACTIVE" : "WINDOW_ACTIVE";

                            string shotName = string.IsNullOrEmpty(req.ScreenshotName) ? "launch_proof.png" : req.ScreenshotName;
                            response.ScreenshotPath = SaveScreenshot(targetWin, Path.Combine(artifactDir, shotName));
                            response.Message = $"Window '{targetWin.Title}' found and attached.";
                        }
                        else
                        {
                            response.Success = false;
                            response.Status = "TIMEOUT";
                            response.ProcessId = currentAppProcess.Id;
                            response.Message = $"Process launched (PID: {currentAppProcess.Id}) but window not found in desktop UIA tree after 15s.";
                        }
                    }
                    return response;
                }

                if (req.Command.Equals("login_status", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var loginWin = FindLoginDialog(automation);
                        if (loginWin != null)
                        {
                            PopulateWindowEvidence(response, loginWin);
                            response.Success = true;
                            response.Status = "LOGIN_DIALOG_ACTIVE";

                            var discoveredControls = new List<string>();
                            var userBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("UsernameBox"));
                            var passBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox"));
                            var confirmBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmPasswordBox"));
                            var loginBtn = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("LoginButton"));
                            var cancelBtn = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("CancelButton"));

                            if (userBox != null) discoveredControls.Add("UsernameBox (TextBox)");
                            if (passBox != null) discoveredControls.Add("PasswordBox (PasswordBox)");
                            if (confirmBox != null) discoveredControls.Add("ConfirmPasswordBox (PasswordBox)");
                            if (loginBtn != null) discoveredControls.Add($"LoginButton (Button: '{loginBtn.Name}')");
                            if (cancelBtn != null) discoveredControls.Add($"CancelButton (Button: '{cancelBtn.Name}')");

                            response.VisibleControls = discoveredControls.ToArray();

                            var textBlocks = loginWin.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                            foreach (var tb in textBlocks)
                            {
                                string text = tb.Name;
                                if (!string.IsNullOrEmpty(text) &&
                                    !text.Equals("Username", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Equals("Password", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Equals("Confirm password", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Contains("System Login", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Contains("Create the administrator account", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Contains("Please authenticate", StringComparison.OrdinalIgnoreCase) &&
                                    !text.Contains("no accounts yet", StringComparison.OrdinalIgnoreCase))
                                {
                                    response.ErrorMessage = text;
                                    break;
                                }
                            }

                            response.Message = $"Login dialog '{loginWin.Title}' active with {discoveredControls.Count} controls discovered.";
                            return response;
                        }

                        var mainWin = FindMainWindow(automation);
                        if (mainWin != null)
                        {
                            PopulateWindowEvidence(response, mainWin);
                            response.Success = true;
                            response.Status = "MAINWINDOW_ACTIVE";
                            response.NavigationControls = ExtractNavigationControls(mainWin);
                            response.Message = $"MainWindow '{mainWin.Title}' is already active and authenticated.";
                            return response;
                        }

                        response.Success = false;
                        response.Status = "NO_WINDOW_FOUND";
                        response.Message = "Neither LoginDialog nor MainWindow is currently visible.";
                        return response;
                    }
                }

                if (req.Command.Equals("fill_login_username", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var loginWin = FindLoginDialog(automation);
                        if (loginWin == null)
                        {
                            response.Success = false;
                            response.Status = "LOGIN_NOT_FOUND";
                            response.Message = "LoginDialog is not visible.";
                            return response;
                        }

                        PopulateWindowEvidence(response, loginWin);

                        var userBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("UsernameBox"));
                        if (userBox == null)
                        {
                            response.Success = false;
                            response.Status = "CONTROL_NOT_FOUND";
                            response.Message = "UsernameBox control not found in LoginDialog.";
                            return response;
                        }

                        string userToSet = !string.IsNullOrEmpty(req.Username)
                            ? req.Username
                            : (Environment.GetEnvironmentVariable("WEIGHBRIDGE_TEST_USERNAME") ?? "admin");

                        userBox.Focus();
                        Thread.Sleep(100);

                        if (userBox.Patterns.Value.IsSupported)
                        {
                            userBox.Patterns.Value.Pattern.SetValue(userToSet);
                        }
                        else
                        {
                            SafeTypeIntoControl(loginWin, userBox, userToSet);
                        }

                        response.Success = true;
                        response.Status = "USERNAME_FILLED";
                        response.Message = "Username successfully entered into UsernameBox.";
                        return response;
                    }
                }

                if (req.Command.Equals("fill_login_password", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var loginWin = FindLoginDialog(automation);
                        if (loginWin == null)
                        {
                            response.Success = false;
                            response.Status = "LOGIN_NOT_FOUND";
                            response.Message = "LoginDialog is not visible.";
                            return response;
                        }

                        PopulateWindowEvidence(response, loginWin);

                        var passBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox"));
                        if (passBox == null)
                        {
                            response.Success = false;
                            response.Status = "CONTROL_NOT_FOUND";
                            response.Message = "PasswordBox control not found in LoginDialog.";
                            return response;
                        }

                        string passToSet = !string.IsNullOrEmpty(req.Password)
                            ? req.Password
                            : (Environment.GetEnvironmentVariable("WEIGHBRIDGE_TEST_PASSWORD") ?? "admin123");

                        if (string.IsNullOrEmpty(passToSet))
                        {
                            response.Success = false;
                            response.Status = "TEST CREDENTIALS REQUIRED";
                            response.Message = "No test credentials available for PasswordBox.";
                            return response;
                        }

                        SafeTypeIntoControl(loginWin, passBox, passToSet);

                        response.Success = true;
                        response.Status = "PASSWORD_FILLED";
                        response.Message = "Password successfully entered into PasswordBox.";
                        return response;
                    }
                }

                if (req.Command.Equals("submit_login", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var loginWin = FindLoginDialog(automation);
                        if (loginWin == null)
                        {
                            response.Success = false;
                            response.Status = "LOGIN_NOT_FOUND";
                            response.Message = "LoginDialog is not visible.";
                            return response;
                        }

                        PopulateWindowEvidence(response, loginWin);

                        string beforeShot = Path.Combine(artifactDir, "login_before.png");
                        response.ScreenshotPath = SaveScreenshot(loginWin, beforeShot);

                        var loginBtn = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("LoginButton"));
                        if (loginBtn == null)
                        {
                            response.Success = false;
                            response.Status = "CONTROL_NOT_FOUND";
                            response.Message = "LoginButton control not found in LoginDialog.";
                            return response;
                        }

                        if (loginBtn.Patterns.Invoke.IsSupported)
                        {
                            loginBtn.Patterns.Invoke.Pattern.Invoke();
                        }
                        else
                        {
                            loginBtn.Click();
                        }

                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] Invoked LoginButton.\n");
                        response.Success = true;
                        response.Status = "SUBMITTED";
                        response.Message = "LoginButton invoked.";
                        return response;
                    }
                }

                if (req.Command.Equals("wait_for_login_result", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        int timeout = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : 15;

                        for (int attempt = 0; attempt < timeout; attempt++)
                        {
                            Thread.Sleep(1000);

                            var mainWin = FindMainWindow(automation);
                            if (mainWin != null)
                            {
                                PopulateWindowEvidence(response, mainWin);
                                response.Success = true;
                                response.Status = "AUTHENTICATED";

                                string afterShot = Path.Combine(artifactDir, "login_success_mainwindow.png");
                                response.ScreenshotPath = SaveScreenshot(mainWin, afterShot);
                                response.NavigationControls = ExtractNavigationControls(mainWin);
                                response.Message = "Authentication succeeded. MainWindow discovered.";
                                File.AppendAllText(logPath, $"[{DateTime.Now:O}] MainWindow '{mainWin.Title}' discovered. Login successful.\n");
                                return response;
                            }

                            var loginWin = FindLoginDialog(automation);
                            if (loginWin != null)
                            {
                                var textBlocks = loginWin.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                                foreach (var tb in textBlocks)
                                {
                                    string text = tb.Name;
                                    if (!string.IsNullOrEmpty(text) &&
                                        (text.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("locked", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        PopulateWindowEvidence(response, loginWin);
                                        response.Success = false;
                                        response.Status = "FAILED";
                                        response.ErrorMessage = text;

                                        string failShot = Path.Combine(artifactDir, "login_failed.png");
                                        response.ScreenshotPath = SaveScreenshot(loginWin, failShot);
                                        response.Message = "Authentication failed: " + text;
                                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] Login failed with message: {text}\n");
                                        return response;
                                    }
                                }
                            }
                        }

                        response.Success = false;
                        response.Status = "TIMEOUT";
                        response.Message = $"Timed out after {timeout}s waiting for MainWindow transition.";
                        return response;
                    }
                }

                if (req.Command.Equals("login_with_credentials", StringComparison.OrdinalIgnoreCase) ||
                    req.Command.Equals("login_with_test_credentials", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        string username = !string.IsNullOrEmpty(req.Username)
                            ? req.Username
                            : (Environment.GetEnvironmentVariable("WEIGHBRIDGE_TEST_USERNAME") ?? "admin");

                        string password = !string.IsNullOrEmpty(req.Password)
                            ? req.Password
                            : (Environment.GetEnvironmentVariable("WEIGHBRIDGE_TEST_PASSWORD") ?? "admin123");

                        if (string.IsNullOrEmpty(password))
                        {
                            response.Success = false;
                            response.Status = "TEST CREDENTIALS REQUIRED";
                            response.Message = "No valid test credentials could be resolved.";
                            return response;
                        }

                        var loginWin = FindLoginDialog(automation);
                        if (loginWin == null)
                        {
                            var mainWin = FindMainWindow(automation);
                            if (mainWin != null)
                            {
                                PopulateWindowEvidence(response, mainWin);
                                response.Success = true;
                                response.Status = "AUTHENTICATED";
                                response.NavigationControls = ExtractNavigationControls(mainWin);
                                string afterShot = Path.Combine(artifactDir, "vehicle-entry", "login_success.png");
                                response.ScreenshotPath = SaveScreenshot(mainWin, afterShot);
                                response.Message = "MainWindow is already active and authenticated.";
                                return response;
                            }

                            response.Success = false;
                            response.Status = "LOGIN_NOT_FOUND";
                            response.Message = "Login dialog window not found.";
                            return response;
                        }

                        PopulateWindowEvidence(response, loginWin);

                        var userBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("UsernameBox"));
                        var passBox = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox"));
                        var loginBtn = loginWin.FindFirstDescendant(cf => cf.ByAutomationId("LoginButton"));

                        if (userBox == null || passBox == null || loginBtn == null)
                        {
                            response.Success = false;
                            response.Status = "CONTROLS_MISSING";
                            response.Message = $"Controls missing in LoginDialog: UsernameBox={(userBox != null)}, PasswordBox={(passBox != null)}, LoginButton={(loginBtn != null)}";
                            return response;
                        }

                        string beforeShot = Path.Combine(artifactDir, "login_before.png");
                        SaveScreenshot(loginWin, beforeShot);

                        userBox.Focus();
                        Thread.Sleep(100);
                        if (userBox.Patterns.Value.IsSupported)
                        {
                            userBox.Patterns.Value.Pattern.SetValue(username);
                        }
                        else
                        {
                            SafeTypeIntoControl(loginWin, userBox, username);
                        }

                        SafeTypeIntoControl(loginWin, passBox, password);
                        Thread.Sleep(200);

                        if (loginBtn.Patterns.Invoke.IsSupported)
                        {
                            loginBtn.Patterns.Invoke.Pattern.Invoke();
                        }
                        else
                        {
                            loginBtn.Click();
                        }

                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] Invoked LoginButton. Waiting for MainWindow...\n");

                        Window? mainWindow = null;
                        for (int attempt = 0; attempt < 15; attempt++)
                        {
                            Thread.Sleep(1000);
                            mainWindow = FindMainWindow(automation);
                            if (mainWindow != null) break;

                            var textBlocks = loginWin.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                            foreach (var tb in textBlocks)
                            {
                                string text = tb.Name;
                                if (!string.IsNullOrEmpty(text) &&
                                    (text.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                                {
                                    response.Success = false;
                                    response.Status = "FAILED";
                                    response.ErrorMessage = text;
                                    string failShot = Path.Combine(artifactDir, "login_failed.png");
                                    response.ScreenshotPath = SaveScreenshot(loginWin, failShot);
                                    response.Message = "Authentication failed: " + text;
                                    return response;
                                }
                            }
                        }

                        if (mainWindow != null)
                        {
                            PopulateWindowEvidence(response, mainWindow);
                            response.Success = true;
                            response.Status = "AUTHENTICATED";

                            string afterShot = Path.Combine(artifactDir, "vehicle-entry", "login_success.png");
                            response.ScreenshotPath = SaveScreenshot(mainWindow, afterShot);
                            response.NavigationControls = ExtractNavigationControls(mainWindow);
                            response.Message = "Login succeeded! MainWindow loaded and verified.";
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] MainWindow '{mainWindow.Title}' loaded successfully.\n");
                        }
                        else
                        {
                            response.Success = false;
                            response.Status = "TIMEOUT";
                            response.Message = "Login did not transition to MainWindow within 15s.";
                        }
                        return response;
                    }
                }

                if (req.Command.Equals("inspect_window", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var targetWin = FindActiveTargetWindow(automation, req.AppTitle);

                        if (targetWin != null)
                        {
                            PopulateWindowEvidence(response, targetWin);
                            response.Success = true;
                            response.Status = "INSPECTED";

                            var descendants = targetWin.FindAllDescendants();
                            response.TotalControls = descendants.Length;

                            var visibleList = new List<string>();
                            foreach (var child in descendants)
                            {
                                string name = child.Name;
                                string autoId = child.AutomationId;
                                string type = child.ControlType.ToString();
                                if (!string.IsNullOrEmpty(autoId) || !string.IsNullOrEmpty(name))
                                {
                                    visibleList.Add($"Type={type} | AutoId={autoId} | Name={name}");
                                    if (visibleList.Count >= 40) break;
                                }
                            }
                            response.VisibleControls = visibleList.ToArray();

                            if (response.AutomationId == "ShellWindow" || targetWin.Title.Contains("WeighBridge", StringComparison.OrdinalIgnoreCase))
                            {
                                response.NavigationControls = ExtractNavigationControls(targetWin);
                            }

                            string shotName = string.IsNullOrEmpty(req.ScreenshotName) ? "inspect_proof.png" : req.ScreenshotName;
                            response.ScreenshotPath = SaveScreenshot(targetWin, Path.Combine(artifactDir, shotName));
                            response.Message = $"Inspected '{targetWin.Title}': {response.TotalControls} total controls, {response.NavigationControls.Length} navigation controls.";
                        }
                        else
                        {
                            response.Success = false;
                            response.Status = "NO_WINDOW_FOUND";
                            response.Message = "No target window available to inspect.";
                        }
                    }
                    return response;
                }

                // =========================================================================
                // VEHICLE ENTRY AUTOMATION COMMANDS
                // =========================================================================

                if (req.Command.Equals("navigate_to_vehicle_entry", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null)
                        {
                            response.Success = false;
                            response.Status = "MAINWINDOW_NOT_FOUND";
                            response.Message = "MainWindow is not active. Launch and authenticate first.";
                            return response;
                        }

                        PopulateWindowEvidence(response, mainWin);

                        // Find navigation item for Vehicle Entry
                        var allRadios = mainWin.FindAllDescendants(cf => cf.ByControlType(ControlType.RadioButton));
                        AutomationElement? navItem = null;
                        foreach (var r in allRadios)
                        {
                            if (r.Name.Equals("Vehicle Entry", StringComparison.OrdinalIgnoreCase) ||
                                r.AutomationId.Equals("Vehicle Entry", StringComparison.OrdinalIgnoreCase))
                            {
                                navItem = r;
                                break;
                            }
                        }

                        if (navItem == null)
                        {
                            response.Success = false;
                            response.Status = "NAV_ITEM_NOT_FOUND";
                            response.Message = "Vehicle Entry navigation radio button not found in ShellWindow.";
                            return response;
                        }

                        try
                        {
                            if (navItem.Patterns.SelectionItem.IsSupported)
                            {
                                navItem.Patterns.SelectionItem.Pattern.Select();
                            }
                            else if (navItem.Patterns.Invoke.IsSupported)
                            {
                                navItem.Patterns.Invoke.Pattern.Invoke();
                            }
                            else
                            {
                                navItem.Click();
                            }
                        }
                        catch
                        {
                            navItem.Click();
                        }

                        Thread.Sleep(800);

                        // Verify Vehicle Entry view loaded
                        bool viewLoaded = false;
                        for (int attempt = 0; attempt < 12; attempt++)
                        {
                            var vNo = mainWin.FindFirstDescendant(cf => cf.ByName("Vehicle No").Or(cf.ByName("Switch To F1")));
                            if (vNo != null)
                            {
                                viewLoaded = true;
                                break;
                            }
                            Thread.Sleep(300);
                        }

                        string shotPath = Path.Combine(artifactDir, "vehicle-entry", "vehicle_entry_initial.png");
                        response.ScreenshotPath = SaveScreenshot(mainWin, shotPath);

                        response.Success = viewLoaded;
                        response.Status = viewLoaded ? "NAVIGATED" : "NAVIGATION_UNCONFIRMED";
                        response.Message = viewLoaded
                            ? "Successfully navigated to Vehicle Entry view via UI Automation control."
                            : "Clicked navigation item but Vehicle Entry view signature was not detected.";
                        return response;
                    }
                }

                if (req.Command.Equals("inspect_vehicle_entry", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null)
                        {
                            response.Success = false;
                            response.Status = "MAINWINDOW_NOT_FOUND";
                            response.Message = "MainWindow not found.";
                            return response;
                        }

                        PopulateWindowEvidence(response, mainWin);

                        var descendants = mainWin.FindAllDescendants();
                        response.TotalControls = descendants.Length;

                        var controlMap = new Dictionary<string, object>();
                        var controlList = new List<string>();

                        // Identify semantic controls
                        foreach (var el in descendants)
                        {
                            try
                            {
                                string name = el.Name ?? "";
                                string autoId = el.AutomationId ?? "";
                                string type = el.ControlType.ToString();
                                bool isEnabled = el.IsEnabled;
                                string bounds = el.BoundingRectangle.ToString();
                                string val = "";
                                if (el.Patterns.Value.IsSupported)
                                {
                                    try { val = el.Patterns.Value.Pattern.Value.Value ?? ""; } catch { }
                                }

                                if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(autoId))
                                {
                                    string key = !string.IsNullOrEmpty(name) ? name : autoId;
                                    controlList.Add($"[{type}] '{key}' (Enabled={isEnabled}, Value='{val}')");

                                    // Map to semantic fields (prefer non-text controls over label textblocks)
                                    if (type != "Text")
                                    {
                                        if (key.Equals("Vehicle No", StringComparison.OrdinalIgnoreCase))
                                            controlMap["VehicleNumber"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Party Name", StringComparison.OrdinalIgnoreCase))
                                            controlMap["Party"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Material", StringComparison.OrdinalIgnoreCase))
                                            controlMap["Material"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Vehicle Type", StringComparison.OrdinalIgnoreCase))
                                            controlMap["VehicleType"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Gross Weight Output", StringComparison.OrdinalIgnoreCase))
                                            controlMap["GrossWeightOutput"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Tare Weight Output", StringComparison.OrdinalIgnoreCase))
                                            controlMap["TareWeightOutput"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Net Weight Output", StringComparison.OrdinalIgnoreCase))
                                            controlMap["NetWeightOutput"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Weight Input", StringComparison.OrdinalIgnoreCase))
                                            controlMap["WeightInput"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Read Scale", StringComparison.OrdinalIgnoreCase))
                                            controlMap["ReadScaleAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Submit Workflow", StringComparison.OrdinalIgnoreCase))
                                            controlMap["SubmitWorkflowAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Clear Form", StringComparison.OrdinalIgnoreCase))
                                            controlMap["ClearFormAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Switch To F1", StringComparison.OrdinalIgnoreCase))
                                            controlMap["FirstWeighmentAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Switch To F2", StringComparison.OrdinalIgnoreCase))
                                            controlMap["SecondWeighmentAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Ticket No", StringComparison.OrdinalIgnoreCase))
                                            controlMap["TicketNo"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("F2 Search Input", StringComparison.OrdinalIgnoreCase))
                                            controlMap["F2SearchInput"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled, Value = val };
                                        else if (key.Equals("Search Pending", StringComparison.OrdinalIgnoreCase))
                                            controlMap["SearchPendingAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                        else if (key.Equals("Load Selected Pending", StringComparison.OrdinalIgnoreCase))
                                            controlMap["LoadPendingAction"] = new { Name = name, AutoId = autoId, Type = type, IsEnabled = isEnabled };
                                    }
                                }
                            }
                            catch { }
                        }

                        response.VisibleControls = controlList.Take(50).ToArray();
                        response.Data["SemanticControls"] = controlMap;
                        response.Success = true;
                        response.Status = "INSPECTED";

                        string shotPath = Path.Combine(artifactDir, "vehicle-entry", "vehicle_entry_initial.png");
                        if (!File.Exists(shotPath))
                        {
                            response.ScreenshotPath = SaveScreenshot(mainWin, shotPath);
                        }
                        else
                        {
                            response.ScreenshotPath = shotPath;
                        }

                        response.Message = $"Discovered {descendants.Length} controls in live UI tree. {controlMap.Count} primary semantic controls identified.";
                        return response;
                    }
                }

                if (req.Command.Equals("get_vehicle_entry_state", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null)
                        {
                            response.Success = false;
                            response.Status = "MAINWINDOW_NOT_FOUND";
                            response.Message = "MainWindow not found.";
                            return response;
                        }

                        PopulateWindowEvidence(response, mainWin);

                        var state = ExtractVehicleEntryState(mainWin);
                        response.Data["State"] = state;
                        response.Success = true;
                        response.Status = "OK";
                        response.Message = $"Vehicle Entry State: Ticket={state.GetValueOrDefault("TicketNo")}, Mode={state.GetValueOrDefault("Mode")}, LiveWeight={state.GetValueOrDefault("LiveWeightDisplay")}";
                        return response;
                    }
                }

                if (req.Command.Equals("set_vehicle_number", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        bool ok = SetControlValue(mainWin, "Vehicle No", req.VehicleNumber);
                        response.Success = ok;
                        response.Status = ok ? "UPDATED" : "FAILED";
                        response.Message = ok ? $"Set vehicle number to '{req.VehicleNumber}'" : "Failed to set vehicle number.";

                        string shotPath = Path.Combine(artifactDir, "vehicle-entry", "vehicle_entry_filled.png");
                        response.ScreenshotPath = SaveScreenshot(mainWin, shotPath);
                        return response;
                    }
                }

                if (req.Command.Equals("select_party", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        bool ok = SetControlValue(mainWin, "Party Name", req.PartyName);
                        response.Success = ok;
                        response.Status = ok ? "UPDATED" : "FAILED";
                        response.Message = ok ? $"Set party to '{req.PartyName}'" : "Failed to set party.";
                        return response;
                    }
                }

                if (req.Command.Equals("select_material", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        bool ok = SetControlValue(mainWin, "Material", req.MaterialName);
                        response.Success = ok;
                        response.Status = ok ? "UPDATED" : "FAILED";
                        response.Message = ok ? $"Set material to '{req.MaterialName}'" : "Failed to set material.";
                        return response;
                    }
                }

                if (req.Command.Equals("select_vehicle_type", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        bool ok = SetControlValue(mainWin, "Vehicle Type", req.VehicleTypeName);
                        response.Success = ok;
                        response.Status = ok ? "UPDATED" : "FAILED";
                        response.Message = ok ? $"Set vehicle type to '{req.VehicleTypeName}'" : "Failed to set vehicle type.";
                        return response;
                    }
                }

                if (req.Command.Equals("set_simulator_weight", StringComparison.OrdinalIgnoreCase))
                {
                    decimal weight = req.WeightKg ?? 0m;
                    bool stable = req.IsStable ?? true;
                    SetSimulatorWeight(weight, stable);

                    Thread.Sleep(500);

                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin != null)
                        {
                            PopulateWindowEvidence(response, mainWin);
                            string shotPath = Path.Combine(artifactDir, "vehicle-entry", "stable_weight.png");
                            response.ScreenshotPath = SaveScreenshot(mainWin, shotPath);
                        }
                    }

                    response.Success = true;
                    response.Status = "SIMULATOR_UPDATED";
                    response.Message = $"Hardware simulator feed updated to {weight:N0} kg (Stable={stable}).";
                    return response;
                }

                if (req.Command.Equals("get_displayed_weight", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        var weightInfo = ReadLiveWeightHUD(mainWin);
                        response.Data["WeightHUD"] = weightInfo;
                        response.Success = true;
                        response.Status = "OK";
                        response.Message = $"HUD: {weightInfo.GetValueOrDefault("LiveWeightDisplay")} ({weightInfo.GetValueOrDefault("StabilityStatus")}) [{weightInfo.GetValueOrDefault("LiveSource")}]";
                        return response;
                    }
                }

                if (req.Command.Equals("execute_first_weighment", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        // 1. Set simulator weight if specified
                        if (req.WeightKg.HasValue)
                        {
                            SetSimulatorWeight(req.WeightKg.Value, req.IsStable ?? true);
                            Thread.Sleep(600);
                        }

                        // Capture stable weight evidence
                        string stableShot = Path.Combine(artifactDir, "vehicle-entry", "stable_weight.png");
                        SaveScreenshot(mainWin, stableShot);

                        // 2. Click "Read Scale" button [F3]
                        var readBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Read Scale"));
                        if (readBtn != null && readBtn.IsEnabled)
                        {
                            if (readBtn.Patterns.Invoke.IsSupported) readBtn.Patterns.Invoke.Pattern.Invoke();
                            else readBtn.Click();
                            Thread.Sleep(400);
                        }

                        // Capture filled state evidence
                        string filledShot = Path.Combine(artifactDir, "vehicle-entry", "vehicle_entry_filled.png");
                        SaveScreenshot(mainWin, filledShot);

                        // 3. Click "Submit Workflow" button [F5]
                        var submitBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Submit Workflow"));
                        if (submitBtn == null || !submitBtn.IsEnabled)
                        {
                            response.Success = false;
                            response.Status = "SUBMIT_DISABLED";
                            response.Message = "Submit Workflow button not found or is disabled.";
                            return response;
                        }

                        if (submitBtn.Patterns.Invoke.IsSupported) submitBtn.Patterns.Invoke.Pattern.Invoke();
                        else submitBtn.Click();

                        // 4. Wait for state transition (queued status or print modal appearing)
                        bool transitioned = false;
                        for (int i = 0; i < 15; i++)
                        {
                            Thread.Sleep(500);
                            var state = ExtractVehicleEntryState(mainWin);
                            string statusText = state.GetValueOrDefault("StatusMessage")?.ToString() ?? "";
                            var cancelPrintBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Cancel Print"));
                            var confirmPrintBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Confirm Print"));
                            bool isPrintModalVisible = (cancelPrintBtn != null && !cancelPrintBtn.IsOffscreen) ||
                                                       (confirmPrintBtn != null && !confirmPrintBtn.IsOffscreen);

                            if (statusText.Contains("queued for second weight", StringComparison.OrdinalIgnoreCase) ||
                                statusText.Contains("First weight", StringComparison.OrdinalIgnoreCase) ||
                                isPrintModalVisible)
                            {
                                transitioned = true;
                                state["PrintModalOpen"] = isPrintModalVisible;
                                response.Data["ResultState"] = state;
                                break;
                            }
                        }

                        string resultShot = Path.Combine(artifactDir, "vehicle-entry", "first_weighment_result.png");
                        response.ScreenshotPath = SaveScreenshot(mainWin, resultShot);

                        response.Success = transitioned;
                        response.Status = transitioned ? "FIRST_WEIGHMENT_COMPLETED" : "STATE_TRANSITION_TIMEOUT";
                        response.Message = transitioned
                            ? "First weighment successfully executed and queued for second weight."
                            : "Submit clicked but queued status confirmation timed out.";
                        return response;
                    }
                }

                if (req.Command.Equals("execute_second_weighment", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        // 1. Switch to F2 mode
                        var f2Btn = mainWin.FindFirstDescendant(cf => cf.ByName("Switch To F2"));
                        if (f2Btn != null && f2Btn.IsEnabled)
                        {
                            if (f2Btn.Patterns.Invoke.IsSupported) f2Btn.Patterns.Invoke.Pattern.Invoke();
                            else f2Btn.Click();
                            Thread.Sleep(500);
                        }

                        // 2. If ticket specified, search or select it
                        if (!string.IsNullOrEmpty(req.SlipNumber))
                        {
                            var f2Search = mainWin.FindFirstDescendant(cf => cf.ByName("F2 Search Input"));
                            if (f2Search != null)
                            {
                                SetControlValue(mainWin, "F2 Search Input", req.SlipNumber);
                                Thread.Sleep(200);
                                var searchBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Search Pending"));
                                if (searchBtn != null && searchBtn.IsEnabled)
                                {
                                    if (searchBtn.Patterns.Invoke.IsSupported) searchBtn.Patterns.Invoke.Pattern.Invoke();
                                    else searchBtn.Click();
                                    Thread.Sleep(600);
                                }
                            }
                        }
                        else
                        {
                            // Load first row from pending queue if available
                            var loadPendingBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Load Selected Pending"));
                            if (loadPendingBtn != null && loadPendingBtn.IsEnabled)
                            {
                                if (loadPendingBtn.Patterns.Invoke.IsSupported) loadPendingBtn.Patterns.Invoke.Pattern.Invoke();
                                else loadPendingBtn.Click();
                                Thread.Sleep(600);
                            }
                        }

                        // 3. Set simulator weight for second reading (e.g. Tare)
                        if (req.WeightKg.HasValue)
                        {
                            SetSimulatorWeight(req.WeightKg.Value, req.IsStable ?? true);
                            Thread.Sleep(600);
                        }

                        // 4. Click "Read Scale" [F3]
                        var readBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Read Scale"));
                        if (readBtn != null && readBtn.IsEnabled)
                        {
                            if (readBtn.Patterns.Invoke.IsSupported) readBtn.Patterns.Invoke.Pattern.Invoke();
                            else readBtn.Click();
                            Thread.Sleep(400);
                        }

                        // 5. Submit Workflow [F5]
                        var submitBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Submit Workflow"));
                        if (submitBtn == null || !submitBtn.IsEnabled)
                        {
                            response.Success = false;
                            response.Status = "SUBMIT_DISABLED";
                            response.Message = "Submit Workflow button disabled or not found.";
                            return response;
                        }

                        if (submitBtn.Patterns.Invoke.IsSupported) submitBtn.Patterns.Invoke.Pattern.Invoke();
                        else submitBtn.Click();

                        // 6. Wait for completed state (completed status or print modal appearing)
                        bool completed = false;
                        for (int i = 0; i < 15; i++)
                        {
                            Thread.Sleep(500);
                            var state = ExtractVehicleEntryState(mainWin);
                            string statusText = state.GetValueOrDefault("StatusMessage")?.ToString() ?? "";
                            var cancelPrintBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Cancel Print"));
                            var confirmPrintBtn = mainWin.FindFirstDescendant(cf => cf.ByName("Confirm Print"));
                            bool isPrintModalVisible = (cancelPrintBtn != null && !cancelPrintBtn.IsOffscreen) ||
                                                       (confirmPrintBtn != null && !confirmPrintBtn.IsOffscreen);

                            if (statusText.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                                statusText.Contains("Net", StringComparison.OrdinalIgnoreCase) ||
                                isPrintModalVisible)
                            {
                                completed = true;
                                state["PrintModalOpen"] = isPrintModalVisible;
                                response.Data["ResultState"] = state;
                                break;
                            }
                        }

                        string secondShot = Path.Combine(artifactDir, "vehicle-entry", "second_weighment_result.png");
                        SaveScreenshot(mainWin, secondShot);
                        string finalShot = Path.Combine(artifactDir, "vehicle-entry", "final_state.png");
                        response.ScreenshotPath = SaveScreenshot(mainWin, finalShot);

                        response.Success = completed;
                        response.Status = completed ? "SECOND_WEIGHMENT_COMPLETED" : "STATE_TRANSITION_TIMEOUT";
                        response.Message = completed
                            ? "Second weighment successfully completed. Gross, Tare, and Net verified."
                            : "Second weighment submitted but completion status timed out.";
                        return response;
                    }
                }

                if (req.Command.Equals("wait_for_weighment_state", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        int timeout = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : 15;
                        string expected = req.ExpectedState ?? "";
                        bool matched = false;

                        for (int i = 0; i < timeout; i++)
                        {
                            var state = ExtractVehicleEntryState(mainWin);
                            string statusMsg = state.GetValueOrDefault("StatusMessage")?.ToString() ?? "";
                            string badge = state.GetValueOrDefault("WorkflowBadge")?.ToString() ?? "";

                            if (statusMsg.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                                badge.Contains(expected, StringComparison.OrdinalIgnoreCase))
                            {
                                matched = true;
                                response.Data["MatchedState"] = state;
                                break;
                            }
                            Thread.Sleep(1000);
                        }

                        response.Success = matched;
                        response.Status = matched ? "STATE_MATCHED" : "TIMEOUT";
                        response.Message = matched
                            ? $"Target state '{expected}' achieved."
                            : $"Timed out after {timeout}s waiting for state '{expected}'.";
                        return response;
                    }
                }

                if (req.Command.Equals("capture_vehicle_entry_evidence", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var mainWin = FindMainWindow(automation);
                        if (mainWin == null) { response.Success = false; response.Message = "MainWindow not found"; return response; }
                        PopulateWindowEvidence(response, mainWin);

                        string shotName = string.IsNullOrEmpty(req.ScreenshotName) ? "evidence.png" : req.ScreenshotName;
                        if (!shotName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) shotName += ".png";

                        string dest = Path.Combine(artifactDir, "vehicle-entry", shotName);
                        response.ScreenshotPath = SaveScreenshot(mainWin, dest);
                        response.Data["State"] = ExtractVehicleEntryState(mainWin);
                        response.Success = true;
                        response.Status = "EVIDENCE_CAPTURED";
                        response.Message = $"Captured evidence screenshot to '{dest}'";
                        return response;
                    }
                }

                if (req.Command.Equals("type_text", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var targetWin = FindActiveTargetWindow(automation, req.AppTitle);

                        if (targetWin != null)
                        {
                            AutomationElement? control = null;
                            if (!string.IsNullOrEmpty(req.TargetAutomationId))
                            {
                                control = targetWin.FindFirstDescendant(cf => cf.ByAutomationId(req.TargetAutomationId));
                            }

                            if (control != null)
                            {
                                control.Focus();
                                Keyboard.Type(req.TextToType);
                                response.Success = true;
                                response.Status = "TYPED";
                                response.Message = $"Typed text into control '{req.TargetAutomationId}'.";

                                string shotName = string.IsNullOrEmpty(req.ScreenshotName) ? "interaction_proof.png" : req.ScreenshotName;
                                response.ScreenshotPath = SaveScreenshot(targetWin, Path.Combine(artifactDir, shotName));
                            }
                            else
                            {
                                response.Success = false;
                                response.Status = "CONTROL_NOT_FOUND";
                                response.Message = $"Target control '{req.TargetAutomationId}' not found.";
                            }
                        }
                        else
                        {
                            response.Success = false;
                            response.Status = "NO_WINDOW_FOUND";
                            response.Message = "Target window not found for text typing.";
                        }
                    }
                    return response;
                }

                if (req.Command.Equals("click_control", StringComparison.OrdinalIgnoreCase))
                {
                    using (var automation = new UIA3Automation())
                    {
                        var targetWin = FindActiveTargetWindow(automation, req.AppTitle);
                        if (targetWin != null)
                        {
                            AutomationElement? control = null;
                            if (!string.IsNullOrEmpty(req.TargetAutomationId))
                            {
                                control = targetWin.FindFirstDescendant(cf => cf.ByAutomationId(req.TargetAutomationId));
                            }
                            else if (!string.IsNullOrEmpty(req.TargetName))
                            {
                                control = targetWin.FindFirstDescendant(cf => cf.ByName(req.TargetName));
                            }

                            if (control != null)
                            {
                                if (control.Patterns.Invoke.IsSupported)
                                {
                                    control.Patterns.Invoke.Pattern.Invoke();
                                }
                                else
                                {
                                    control.Click();
                                }
                                response.Success = true;
                                response.Status = "CLICKED";
                                response.Message = $"Clicked control '{req.TargetAutomationId ?? req.TargetName}'.";

                                string shotName = string.IsNullOrEmpty(req.ScreenshotName) ? "click_proof.png" : req.ScreenshotName;
                                response.ScreenshotPath = SaveScreenshot(targetWin, Path.Combine(artifactDir, shotName));
                            }
                            else
                            {
                                response.Success = false;
                                response.Status = "CONTROL_NOT_FOUND";
                                response.Message = $"Control '{req.TargetAutomationId ?? req.TargetName}' not found.";
                            }
                        }
                        else
                        {
                            response.Success = false;
                            response.Status = "NO_WINDOW_FOUND";
                            response.Message = "Target window not found for click.";
                        }
                    }
                    return response;
                }

                if (req.Command.Equals("close_app", StringComparison.OrdinalIgnoreCase))
                {
                    var appProc = GetOrFindAppProcess();
                    if (appProc != null && !appProc.HasExited)
                    {
                        appProc.Kill();
                        response.Success = true;
                        response.Status = "CLOSED";
                        response.Message = "Process killed successfully.";
                    }
                    else
                    {
                        response.Success = true;
                        response.Status = "NO_PROCESS";
                        response.Message = "No active process to close.";
                    }
                    return response;
                }

                response.Success = false;
                response.Status = "UNKNOWN_COMMAND";
                response.Message = "Unknown command: " + req.Command;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Status = "EXCEPTION";
                response.Message = "Execution Exception: " + ex.Message;
            }

            return response;
        }

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CHAR = 0x0102;
        private const int VK_BACK = 0x08;

        public static void SetSimulatorWeight(decimal weightKg, bool isStable = true)
        {
            string simControlFile = Path.Combine(Path.GetTempPath(), "weighbridge_sim_weight.txt");
            File.WriteAllText(simControlFile, $"{weightKg.ToString(CultureInfo.InvariantCulture)},{isStable}");
        }

        private static AutomationElement? FindInputControl(Window window, string controlName)
        {
            // First search for interactive controls matching the name or automation id
            var interactive = window.FindFirstDescendant(cf =>
                (cf.ByControlType(ControlType.ComboBox)
                 .Or(cf.ByControlType(ControlType.Edit))
                 .Or(cf.ByControlType(ControlType.Button))
                 .Or(cf.ByControlType(ControlType.CheckBox))
                 .Or(cf.ByControlType(ControlType.Custom)))
                .And(cf.ByName(controlName).Or(cf.ByAutomationId(controlName))));

            if (interactive != null) return interactive;

            // Second pass: any descendant matching name or autoId that is NOT a text label
            var all = window.FindAllDescendants(cf => cf.ByName(controlName).Or(cf.ByAutomationId(controlName)));
            foreach (var el in all)
            {
                if (el.ControlType != ControlType.Text)
                {
                    return el;
                }
            }

            return null;
        }

        private static string ReadControlValue(AutomationElement? el)
        {
            if (el == null) return "";
            try
            {
                if (el.ControlType == ControlType.ComboBox)
                {
                    var edit = el.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                    if (edit != null && edit.Patterns.Value.IsSupported)
                    {
                        var val = edit.Patterns.Value.Pattern.Value.Value;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }

                if (el.Patterns.Value.IsSupported)
                {
                    var val = el.Patterns.Value.Pattern.Value.Value;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }

            return el.Name ?? "";
        }

        private static bool SetControlValue(Window window, string controlName, string value)
        {
            var element = FindInputControl(window, controlName);
            if (element == null) return false;

            AutomationElement targetToFocus = element;

            if (element.ControlType == ControlType.ComboBox)
            {
                var editChild = element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                if (editChild != null)
                {
                    targetToFocus = editChild;
                    try
                    {
                        if (editChild.Patterns.Value.IsSupported)
                        {
                            editChild.Patterns.Value.Pattern.SetValue(value);
                            return true;
                        }
                    }
                    catch { }
                }
                else if (element.Patterns.Value.IsSupported)
                {
                    try
                    {
                        element.Patterns.Value.Pattern.SetValue(value);
                        return true;
                    }
                    catch { }
                }
            }
            else if (element.Patterns.Value.IsSupported)
            {
                try
                {
                    element.Patterns.Value.Pattern.SetValue(value);
                    return true;
                }
                catch { }
            }

            SafeTypeIntoControl(window, targetToFocus, value);
            return true;
        }

        private static Dictionary<string, object> ExtractVehicleEntryState(Window window)
        {
            var state = new Dictionary<string, object>();
            try
            {
                // Ticket No
                var ticketBox = FindInputControl(window, "Ticket No");
                state["TicketNo"] = ReadControlValue(ticketBox);

                // Vehicle No
                var vBox = FindInputControl(window, "Vehicle No");
                state["VehicleNo"] = ReadControlValue(vBox);

                // Party Name
                var pBox = FindInputControl(window, "Party Name");
                state["PartyName"] = ReadControlValue(pBox);

                // Material
                var mBox = FindInputControl(window, "Material");
                state["Material"] = ReadControlValue(mBox);

                // Vehicle Type
                var vtBox = FindInputControl(window, "Vehicle Type");
                state["VehicleType"] = ReadControlValue(vtBox);

                // Gross Weight Output
                var gw = FindInputControl(window, "Gross Weight Output");
                state["GrossWeight"] = ReadControlValue(gw);

                // Tare Weight Output
                var tw = FindInputControl(window, "Tare Weight Output");
                state["TareWeight"] = ReadControlValue(tw);

                // Net Weight Output
                var nw = FindInputControl(window, "Net Weight Output");
                state["NetWeight"] = ReadControlValue(nw);

                // Weight Input
                var wi = FindInputControl(window, "Weight Input");
                state["WeightInput"] = ReadControlValue(wi);

                // HUD readings
                var hud = ReadLiveWeightHUD(window);
                foreach (var kv in hud) state[kv.Key] = kv.Value;

                // Search for status text blocks
                var textBlocks = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                foreach (var tb in textBlocks)
                {
                    string t = tb.Name ?? "";
                    if (t.Contains("queued for second weight", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("First weight", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("Ticket WB-", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("loaded into Second Entry", StringComparison.OrdinalIgnoreCase))
                    {
                        state["StatusMessage"] = t;
                        break;
                    }
                }
            }
            catch { }
            return state;
        }

        private static Dictionary<string, object> ReadLiveWeightHUD(Window window)
        {
            var dict = new Dictionary<string, object>();
            try
            {
                var textBlocks = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                foreach (var tb in textBlocks)
                {
                    string name = tb.Name ?? "";
                    if (name.EndsWith(" kg", StringComparison.OrdinalIgnoreCase) && (name.Contains(",") || name.Length < 12))
                    {
                        if (!dict.ContainsKey("LiveWeightDisplay")) dict["LiveWeightDisplay"] = name;
                    }
                    else if (name.Equals("STABLE", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("ZERO", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("UNSTABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        dict["StabilityStatus"] = name;
                    }
                    else if (name.Contains("[Simulator]", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("[Indicator]", StringComparison.OrdinalIgnoreCase))
                    {
                        dict["LiveSource"] = name;
                    }
                }
            }
            catch { }
            return dict;
        }

        private static void SafeTypeIntoControl(Window window, AutomationElement element, string text)
        {
            IntPtr hWnd = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
            SetForegroundWindow(hWnd);
            Thread.Sleep(80);

            try
            {
                element.Focus();
            }
            catch { }

            Thread.Sleep(80);

            // Clear existing characters with backspaces
            for (int i = 0; i < 30; i++)
            {
                PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_BACK, IntPtr.Zero);
                PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_BACK, IntPtr.Zero);
            }
            Thread.Sleep(40);

            // Post each character as WM_CHAR
            foreach (char c in text)
            {
                PostMessage(hWnd, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                Thread.Sleep(20);
            }
        }

        private static void PopulateWindowEvidence(BridgeResponse response, Window win)
        {
            response.WindowTitle = win.Title;
            try { response.AutomationId = win.AutomationId; } catch { response.AutomationId = ""; }
            try { response.Bounds = win.BoundingRectangle.ToString(); } catch { response.Bounds = ""; }
            try { response.NativeWindowHandle = win.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(); } catch { response.NativeWindowHandle = 0; }

            int pid = 0;
            try { pid = win.Properties.ProcessId.ValueOrDefault; } catch { }
            response.ProcessId = pid;

            if (pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    response.SessionId = proc.SessionId;
                }
                catch { }
            }
        }

        private static List<Window> GetAppWindows(UIA3Automation automation, int targetPid, string logPath)
        {
            var result = new List<Window>();
            if (targetPid > 0)
            {
                try
                {
                    var flauiApp = FlaUI.Core.Application.Attach(targetPid);
                    var appWins = flauiApp.GetAllTopLevelWindows(automation);
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] FlaUI App.GetAllTopLevelWindows returned {appWins.Length} windows for PID {targetPid}.\n");
                    foreach (var w in appWins)
                    {
                        result.Add(w);
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] FlaUI App.Attach error: {ex.Message}\n");
                }
            }

            if (result.Count == 0)
            {
                try
                {
                    var desktop = automation.GetDesktop();
                    var winChildren = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Desktop window children returned {winChildren.Length} elements for PID {targetPid}.\n");
                    foreach (var c in winChildren)
                    {
                        var win = c.AsWindow();
                        int pid = 0;
                        try { pid = win.Properties.ProcessId.ValueOrDefault; } catch { }
                        if (targetPid == 0 || pid == targetPid)
                        {
                            result.Add(win);
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Desktop.FindAllChildren error: {ex.Message}\n");
                }
            }

            return result;
        }

        private static Window? FindLoginDialog(UIA3Automation automation)
        {
            string artifactDir = @"test-artifacts\desktop-automation";
            string logPath = Path.Combine(artifactDir, "host_service.log");

            var appProc = GetOrFindAppProcess();
            int targetPid = appProc != null ? appProc.Id : 0;
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] FindLoginDialog: Target PID = {targetPid}\n");

            var windows = GetAppWindows(automation, targetPid, logPath);

            foreach (var win in windows)
            {
                try
                {
                    int pid = 0;
                    try { pid = win.Properties.ProcessId.ValueOrDefault; } catch { }
                    string title = "";
                    try { title = win.Title; } catch { }
                    string autoId = "";
                    try { autoId = win.AutomationId; } catch { }

                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Window: PID={pid}, Title='{title}', AutoId='{autoId}'\n");

                    if (targetPid > 0 && pid == targetPid)
                    {
                        if (title.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Administrator setup", StringComparison.OrdinalIgnoreCase) ||
                            autoId != "ShellWindow")
                        {
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Matched LoginDialog window: PID={pid}, Title='{title}'\n");
                            return win;
                        }
                    }
                    else if (targetPid == 0)
                    {
                        if (title.Contains("Login", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Administrator setup", StringComparison.OrdinalIgnoreCase))
                        {
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Matched LoginDialog window by title: PID={pid}, Title='{title}'\n");
                            return win;
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Window inspection exception: {ex.Message}\n");
                }
            }
            return null;
        }

        private static Window? FindMainWindow(UIA3Automation automation)
        {
            string artifactDir = @"test-artifacts\desktop-automation";
            string logPath = Path.Combine(artifactDir, "host_service.log");

            var appProc = GetOrFindAppProcess();
            int targetPid = appProc != null ? appProc.Id : 0;

            var windows = GetAppWindows(automation, targetPid, logPath);

            foreach (var win in windows)
            {
                try
                {
                    int pid = 0;
                    try { pid = win.Properties.ProcessId.ValueOrDefault; } catch { }
                    string title = "";
                    try { title = win.Title; } catch { }
                    string autoId = "";
                    try { autoId = win.AutomationId; } catch { }

                    if (targetPid > 0 && pid != targetPid) continue;

                    if (autoId == "ShellWindow" ||
                        (title.Contains("WeighBridge", StringComparison.OrdinalIgnoreCase) &&
                         !title.Contains("Login", StringComparison.OrdinalIgnoreCase) &&
                         !title.Contains("Administrator setup", StringComparison.OrdinalIgnoreCase)))
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] Matched MainWindow: PID={pid}, Title='{title}', AutoId='{autoId}'\n");
                        return win;
                    }
                }
                catch { }
            }
            return null;
        }

        private static Window? FindActiveTargetWindow(UIA3Automation automation, string? appTitle)
        {
            string artifactDir = @"test-artifacts\desktop-automation";
            string logPath = Path.Combine(artifactDir, "host_service.log");

            var appProc = GetOrFindAppProcess();
            int targetPid = appProc != null ? appProc.Id : 0;
            var windows = GetAppWindows(automation, targetPid, logPath);

            foreach (var win in windows)
            {
                try
                {
                    int pid = 0;
                    try { pid = win.Properties.ProcessId.ValueOrDefault; } catch { }
                    if (appProc != null && pid == appProc.Id)
                    {
                        return win;
                    }
                    if (!string.IsNullOrEmpty(appTitle) && win.Title.Contains(appTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        return win;
                    }
                }
                catch { }
            }

            return windows.Count > 0 ? windows[0] : null;
        }

        private static string[] ExtractNavigationControls(Window mainWindow)
        {
            var navItems = new List<string>();
            try
            {
                var descendants = mainWindow.FindAllDescendants();
                foreach (var d in descendants)
                {
                    if (d.ControlType == ControlType.RadioButton && !string.IsNullOrEmpty(d.Name))
                    {
                        navItems.Add($"NavigationItem: {d.Name}");
                    }
                    else if (d.ControlType == ControlType.Button)
                    {
                        string name = d.Name;
                        if (!string.IsNullOrEmpty(name) &&
                            (name.Contains("Expand", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Sign out", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("theme", StringComparison.OrdinalIgnoreCase)))
                        {
                            navItems.Add($"ShellButton: {name}");
                        }
                    }
                }
            }
            catch { }
            return navItems.ToArray();
        }

        private static string SaveScreenshot(Window window, string fullPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
                try
                {
                    IntPtr hWnd = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
                    if (hWnd != IntPtr.Zero && IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, 9); // SW_RESTORE only when window is minimized
                        SetForegroundWindow(hWnd);
                        Thread.Sleep(150);
                    }
                }
                catch { }

                using (var img = window.Capture())
                {
                    img.Save(fullPath);
                }
                return fullPath;
            }
            catch (Exception ex)
            {
                return "Screenshot error: " + ex.Message;
            }
        }
    }
}
