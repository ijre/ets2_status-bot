﻿using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Helpers;
using Microsoft.Win32;

namespace Funbit.Ets.Telemetry.Server.Setup
{
    public class PluginSetup : ISetup
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        SetupStatus _status = SetupStatus.Uninstalled;
        const string TelemetryDllName = "ets2-telemetry-server.dll";

        static string GetDefaultSteamPath()
        {
            var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey != null)
                return steamKey.GetValue("SteamPath") as string;
            return null;
        }

        static bool ValidateEts2Path(string ets2Path)
        {
            if (string.IsNullOrEmpty(ets2Path))
                return false;
            var baseScsPath = Path.Combine(ets2Path, "base.scs");
            var binPath = Path.Combine(ets2Path, "bin");
            return File.Exists(baseScsPath) && Directory.Exists(binPath);
        }

        static string GetPluginPath(string ets2Path, bool x64)
        {
            return Path.Combine(ets2Path, x64 ? @"bin\win_x64\plugins" : @"bin\win_x86\plugins");
        }
        
        static string GetTelemetryPluginDllFileName(string ets2Path, bool x64)
        {
            string path = GetPluginPath(ets2Path, x64);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return Path.Combine(path, TelemetryDllName);
        }

        static string LocalEts2X86TelemetryPluginDllFileName
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, @"Ets2Plugins\win_x86\plugins\", TelemetryDllName);
            }
        }

        static string LocalEts2X64TelemetryPluginDllFileName
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, @"Ets2Plugins\win_x64\plugins", TelemetryDllName);
            }
        }

        public PluginSetup()
        {
            try
            {
                Log.Info("Checking plugin DLL files...");
                string gamePath = Settings.Instance.Ets2GamePath;
                if (ValidateEts2Path(gamePath))
                {
                    bool pluginsExist = File.Exists(GetTelemetryPluginDllFileName(gamePath, x64: true)) &&
                                        File.Exists(GetTelemetryPluginDllFileName(gamePath, x64: false));
                    // make sure that we disable old plugin (2.1.0 and earlier)
                    // ReSharper disable once AssignNullToNotNullAttribute
                    string oldX86PluginDllFileName = Path.Combine(GetPluginPath(gamePath, x64: false),
                        "ets2-telemetry.dll");
                    // ReSharper disable once AssignNullToNotNullAttribute
                    string oldX64PluginDllFileName = Path.Combine(GetPluginPath(gamePath, x64: true),
                        "ets2-telemetry.dll");
                    if (File.Exists(oldX86PluginDllFileName))
                        File.Move(oldX86PluginDllFileName,
                            Path.ChangeExtension(oldX86PluginDllFileName, ".deprecated.bak"));
                    if (File.Exists(oldX64PluginDllFileName))
                        File.Move(oldX64PluginDllFileName,
                            Path.ChangeExtension(oldX64PluginDllFileName, ".deprecated.bak"));
                    _status = pluginsExist ? SetupStatus.Installed : SetupStatus.Uninstalled;
                }
                else
                {
                    _status = SetupStatus.Uninstalled;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
            }
        }

        public SetupStatus Status
        {
            get { return _status; }
        }

        public SetupStatus Install(IWin32Window owner)
        {
            if (_status == SetupStatus.Installed)
                return _status;

            try
            {
                string gamePath = Settings.Instance.Ets2GamePath;
                if (string.IsNullOrEmpty(gamePath))
                    gamePath = GetDefaultSteamPath();
                if (!string.IsNullOrEmpty(gamePath))
                    gamePath = Path.Combine(
                        gamePath.Replace('/', '\\'), @"SteamApps\common\Euro Truck Simulator 2");
                if (!ValidateEts2Path(gamePath))
                {
                    while (!ValidateEts2Path(gamePath))
                    {
                        MessageBox.Show(owner,
                            @"Could not detect ETS2 game path. " +
                            @"Please click OK and select it manually." + Environment.NewLine + Environment.NewLine +
                            @"For example:" + Environment.NewLine + @"D:\STEAM\SteamApps\common\Euro Truck Simulator 2",
                            @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        var browser = new FolderBrowserDialog();
                        browser.Description = @"Select ETS2 game path";
                        browser.ShowNewFolderButton = false;
                        var result = browser.ShowDialog(owner);
                        if (result == DialogResult.Cancel)
                            Environment.Exit(1);
                        gamePath = browser.SelectedPath;
                    }
                }

                Settings.Instance.Ets2GamePath = gamePath;
                Settings.Instance.Save();

                string x64DllFileName = GetTelemetryPluginDllFileName(gamePath, x64: true);
                string x86DllFileName = GetTelemetryPluginDllFileName(gamePath, x64: false);

                Log.InfoFormat("Copying x86 plugin DLL file to: {0}", x86DllFileName);
                if (!File.Exists(x86DllFileName))
                    File.Copy(LocalEts2X86TelemetryPluginDllFileName, x86DllFileName);

                Log.InfoFormat("Copying x64 plugin DLL file to: {0}", x64DllFileName);
                if (!File.Exists(x64DllFileName))
                    File.Copy(LocalEts2X64TelemetryPluginDllFileName, x64DllFileName);

                _status = SetupStatus.Installed;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _status = SetupStatus.Failed;
                throw;
            }
            
            return _status;
        }

        public SetupStatus Uninstall(IWin32Window owner)
        {
            if (_status == SetupStatus.Uninstalled)
                return _status;

            SetupStatus status;
            try
            {
                // rename plugin dll files to .bak
                Log.Info("Backing up plugin DLL files...");
                string x64DllFileName = GetTelemetryPluginDllFileName(Settings.Instance.Ets2GamePath, x64: true);
                string x86DllFileName = GetTelemetryPluginDllFileName(Settings.Instance.Ets2GamePath, x64: false);
                string x86BakFileName = Path.ChangeExtension(x86DllFileName, ".bak");
                string x64BakFileName = Path.ChangeExtension(x64DllFileName, ".bak");
                if (File.Exists(x86BakFileName))
                    File.Delete(x86BakFileName);
                if (File.Exists(x64BakFileName))
                    File.Delete(x64BakFileName);
                File.Move(x86DllFileName, x86BakFileName);
                File.Move(x64DllFileName, x64BakFileName);
                status = SetupStatus.Uninstalled;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                status = SetupStatus.Failed;
            }
            return status;
        }
    }
}