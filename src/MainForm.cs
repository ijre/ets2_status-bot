using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Funbit.Ets.Telemetry.Server.Controllers;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using Funbit.Ets.Telemetry.Server.Setup;
using Microsoft.Owin.Hosting;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Funbit.Ets.Telemetry.Server
{
    public partial class MainForm : Form
    {
        IDisposable _server;
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly HttpClient _broadcastHttpClient = new HttpClient();
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly string BroadcastUrl = ConfigurationManager.AppSettings["BroadcastUrl"];
        static readonly string BroadcastUserId = Convert.ToBase64String(
            Utf8.GetBytes(ConfigurationManager.AppSettings["BroadcastUserId"] ?? ""));
        static readonly string BroadcastUserPassword = Convert.ToBase64String(
            Utf8.GetBytes(ConfigurationManager.AppSettings["BroadcastUserPassword"] ?? ""));
        static readonly int BroadcastRateInSeconds = Math.Min(Math.Max(1,
            Convert.ToInt32(ConfigurationManager.AppSettings["BroadcastRate"])), 86400);
        static readonly bool UseTestTelemetryData = Convert.ToBoolean(
            ConfigurationManager.AppSettings["UseEts2TestTelemetryData"]);

        public MainForm()
        {
            InitializeComponent();
        }

        static string IpToEndpointUrl(string host)
        {
            return $"http://{host}:{ConfigurationManager.AppSettings["Port"]}";
        }

        void Setup()
        {
            try
            {
                if (Program.UninstallMode && SetupManager.Steps.All(s => s.Status == SetupStatus.Uninstalled))
                {
                    MessageBox.Show(this, @"Server is not installed, nothing to uninstall.", @"Done",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.Exit(0);
                }

                if (Program.UninstallMode || SetupManager.Steps.Any(s => s.Status != SetupStatus.Installed))
                {
                    // we wait here until setup is complete
                    var result = new SetupForm().ShowDialog(this);
                    if (result == DialogResult.Abort)
                        Environment.Exit(0);
                }

                // raise priority to make server more responsive (it does not eat CPU though!)
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Setup error");
            }
        }

        void Start()
        {
            try
            {
                // load list of available network interfaces
                var networkInterfaces = NetworkHelper.GetAllActiveNetworkInterfaces();
                interfacesDropDown.Items.Clear();
                foreach (var networkInterface in networkInterfaces)
                    interfacesDropDown.Items.Add(networkInterface);
                // select remembered interface or default
                var rememberedInterface = networkInterfaces.FirstOrDefault(
                    i => i.Id == Settings.Instance.DefaultNetworkInterfaceId);
                if (rememberedInterface != null)
                    interfacesDropDown.SelectedItem = rememberedInterface;
                else
                    interfacesDropDown.SelectedIndex = 0; // select default interface

                // bind to all available interfaces
                _server = WebApp.Start<Startup>(IpToEndpointUrl("+"));

                // start ETS2 process watchdog timer
                statusUpdateTimer.Enabled = true;

                // turn on broadcasting if set
                if (!string.IsNullOrEmpty(BroadcastUrl))
                {
                    _broadcastHttpClient.DefaultRequestHeaders.Add("X-UserId", BroadcastUserId);
                    _broadcastHttpClient.DefaultRequestHeaders.Add("X-UserPassword", BroadcastUserPassword);
                    broadcastTimer.Interval = BroadcastRateInSeconds * 1000;
                    broadcastTimer.Enabled = true;
                }

                // show tray icon
                trayIcon.Visible = true;

                // make sure that form is visible
                Activate();

                StartDiscordBot();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Network error", MessageBoxIcon.Exclamation);
            }
        }

        void MainForm_Load(object sender, EventArgs e)
        {
            // log current version for debugging
            Log.InfoFormat("Running application on {0} ({1}) {2}", Environment.OSVersion,
                Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                Program.UninstallMode ? "[UNINSTALL MODE]" : "");
            Text += @" " + AssemblyHelper.Version;

            // install or uninstall server if needed
            Setup();

            // start WebApi server
            Start();
        }

        void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _server?.Dispose();
            trayIcon.Visible = false;
        }

        void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
        }

        void statusUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (UseTestTelemetryData)
                {
                    serverStatusLabel.Text = @"Connected to Ets2TestTelemetry.json";
                    serverStatusLabel.ForeColor = Color.DarkGreen;
                }
                else if (Ets2ProcessHelper.IsEts2Running && Ets2TelemetryDataReader.Instance.IsConnected)
                {
                    serverStatusLabel.Text = $"Connected to the simulator ({Ets2ProcessHelper.LastRunningGameName})";
                    serverStatusLabel.ForeColor = Color.DarkGreen;
                }
                else if (Ets2ProcessHelper.IsEts2Running)
                {
                    serverStatusLabel.Text = $"Simulator is running ({Ets2ProcessHelper.LastRunningGameName})";
                    serverStatusLabel.ForeColor = Color.Teal;
                }
                else
                {
                    serverStatusLabel.Text = @"Simulator is not running";
                    serverStatusLabel.ForeColor = Color.FromArgb(240, 55, 30);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Process error");
                statusUpdateTimer.Enabled = false;
            }
        }

        void apiUrlLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessHelper.OpenUrl(((LinkLabel)sender).Text);
        }

        void appUrlLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessHelper.OpenUrl(((LinkLabel)sender).Text);
        }

        void MainForm_Resize(object sender, EventArgs e)
        {
            ShowInTaskbar = WindowState != FormWindowState.Minimized;
            if (!ShowInTaskbar && trayIcon.Tag == null)
            {
                trayIcon.ShowBalloonTip(1000, @"ETS2/ATS Telemetry Server", @"Double-click to restore.", ToolTipIcon.Info);
                trayIcon.Tag = "Already shown";
            }
        }

        void interfaceDropDown_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedInterface = (NetworkInterfaceInfo)interfacesDropDown.SelectedItem;
            appUrlLabel.Text = IpToEndpointUrl(selectedInterface.Ip) + Ets2AppController.TelemetryAppUriPath;
            apiUrlLabel.Text = IpToEndpointUrl(selectedInterface.Ip) + Ets2TelemetryController.TelemetryApiUriPath;
            ipAddressLabel.Text = selectedInterface.Ip;
            Settings.Instance.DefaultNetworkInterfaceId = selectedInterface.Id;
            Settings.Instance.Save();
        }

        async void broadcastTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                broadcastTimer.Enabled = false;
                await _broadcastHttpClient.PostAsJsonAsync(BroadcastUrl, Ets2TelemetryDataReader.Instance.Read());
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            broadcastTimer.Enabled = true;
        }

        void uninstallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string exeFileName = Process.GetCurrentProcess().MainModule.FileName;
            var startInfo = new ProcessStartInfo
            {
                Arguments = $"/C ping 127.0.0.1 -n 2 && \"{exeFileName}\" -uninstall",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                FileName = "cmd.exe"
            };
            Process.Start(startInfo);
            Application.Exit();
        }

        void donateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessHelper.OpenUrl("http://funbit.info/ets2/donate.htm");
        }

        void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessHelper.OpenUrl("https://github.com/Funbit/ets2-telemetry-server");
        }

        void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO: implement later
        }

        #region Discord
        async Task<int> StartDiscordBot()
        {
            try
            {
                DiscordClient client = new DiscordClient(new DiscordConfiguration
                {
                    Token = File.ReadAllText("./id.txt")
                });

                await client.ConnectAsync();
                await client.InitializeAsync();

                client.MessageCreated += Client_MessageCreated;

                client.Ready += (ReadyEventArgs args) =>
                {
                    discordStatusLabel.Text = "Connected!";
                    discordStatusLabel.ForeColor = Color.DarkGreen;
                    return Task.CompletedTask;
                };
            }
            catch (FileNotFoundException)
            {
                if (File.Exists("../id.txt"))
                    File.Copy("../id.txt", "./id.txt");
                else
                {
                    var box = MessageBox.Show("Bot token (id.txt) not found. If it exists, would you like to input the path to the file?", "Token not found", MessageBoxButtons.YesNo, MessageBoxIcon.Error);

                    if (box == DialogResult.Yes)
                    {
                        using (OpenFileDialog diag = new OpenFileDialog())
                        {
                            diag.ShowDialog();
                            File.Copy(diag.FileName, "./id.txt");
                        };
                    }
                    else
                    {
                        Application.Exit();
                        return 1;
                    }
                }

                Process.Start(Application.ExecutablePath, "-restart");
                Application.Exit();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                ex.ShowAsMessageBox(this, @"Discord Error");
            }

            return 0;
        }

        private readonly String[] categories = new String[3] { "game", "truck", "job" };

        async Task<int> Client_MessageCreated(MessageCreateEventArgs e)
        {
            var message = e.Message;

            try
            {
                if (message.Content.IndexOf("?") == 0 && message.Content != "?" && !message.Author.IsBot)
                    if (message.Content == "?commands")
                        await message.Channel.SendMessageAsync(
                            "If you want multiple categories at once, just put a space inbetween the words.\n\n" +
                            "Game (tells you:\nIf their game is launched,\nIf they're paused,\nTheir SDK version + their SDK plugin version\nPlus a few other misc items.),\n\n" +
                            "Truck (tells you: \nMake and model,\nTheir speed in KPH, \ntransmission stats, and much, much more.),\n\n" +
                            "Job (tells you: \nHow much the contract is worth,\nThe deadline,\nHow much time is left until the deadline,\nThe city + company the contract came from,\nThe city + company the contract goes to,\nThe cargo's damage, contents, weight, and whether or not it's attached.)");
                    else
                    {
                        string[] split = message.Content.Split(' ');
                        for (int i = 0; i < 3; i++)
                            for (int i2 = 0; i2 < split.Length; i2++)
                                if (split[i2] == categories[i] || split[i2].Substring(1) == categories[i])
                                {
                                    if (split[i2].IndexOf("?") == 0)
                                        split[i2] = split[i2].Substring(1);

                                    switch (split[i2])
                                    {
                                        case "game":
                                            await message.Channel.SendMessageAsync(Ets2TelemetryDataReader.Instance.Read().Game.Everything);
                                            break;
                                        case "truck":
                                            await message.Channel.SendMessageAsync(Ets2TelemetryDataReader.Instance.Read().Truck.Everything);
                                            break;
                                        case "job":
                                            await message.Channel.SendMessageAsync(Ets2TelemetryDataReader.Instance.Read().Job.Everything);
                                            break;
                                    }
                                }
                    }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                await message.Channel.SendMessageAsync("Error: " + ex.Message);
            }

            return 0;
        }
        #endregion
    }
}
