using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace JapanologyAlarm
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ClockForm());
        }
    }

    public sealed class AlarmConfig
    {
        public bool Enabled { get; set; }
        public string Time { get; set; }
        public string Sound { get; set; }
        public bool RequireStudy { get; set; }
        public int RequiredCorrect { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Mode { get; set; }
        public string Alphabet { get; set; }
        public bool Listening { get; set; }
        public bool KanjiExamples { get; set; }
        public bool Difficult { get; set; }
        public bool Furigana { get; set; }

        public AlarmConfig()
        {
            Enabled = false; Time = "07:00"; Sound = ""; RequiredCorrect = 5;
            From = "Alles"; To = "Alles"; Mode = "woorden"; Alphabet = "Romaji";
            Listening = true; KanjiExamples = false; Difficult = true; Furigana = true;
        }
    }

    public sealed class AppSettings
    {
        public AlarmConfig Alarm { get; set; }
        public string Username { get; set; }
        public string ProtectedPassword { get; set; }
        public int KeepAliveMinutes { get; set; }
        public List<string> StudyDates { get; set; }

        public AppSettings()
        {
            Alarm = new AlarmConfig(); Username = ""; ProtectedPassword = "";
            KeepAliveMinutes = 8; StudyDates = new List<string>();
        }
    }

    public sealed class SettingsStore
    {
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public SettingsStore()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JapanologyAlarm");
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "settings.json");
        }

        public AppSettings Load()
        {
            try { return _json.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings(); }
            catch { return new AppSettings(); }
        }

        public void Save(AppSettings settings)
        {
            File.WriteAllText(_path, _json.Serialize(settings));
        }

        public string Encrypt(string password)
        {
            if (String.IsNullOrEmpty(password)) return "";
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        public string Decrypt(string cipherText)
        {
            try
            {
                if (String.IsNullOrEmpty(cipherText)) return "";
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipherText), null, DataProtectionScope.CurrentUser));
            }
            catch { return ""; }
        }
    }

    public sealed class ClockForm : Form
    {
        private readonly Label _date = new Label();
        private readonly Label _days = new Label();
        private readonly Label _network = new Label();
        private readonly Label _clock = new Label();
        private readonly Timer _timer = new Timer();
        private readonly SettingsStore _store = new SettingsStore();
        private readonly LocalServer _server;
        private AppSettings _settings;
        private bool _firedToday;
        private bool _alarmActive;
        private DateTime _lastKeepAlive = DateTime.MinValue;
        private SoundPlayer _soundPlayer;
        private StudyForm _studyForm;

        public ClockForm()
        {
            _settings = _store.Load();
            _server = new LocalServer(_store, () => _settings, UpdateSettings, StopAlarm, StartAlarm);
            _server.Start();

            Text = "Japanology Alarm"; BackColor = Color.FromArgb(8, 8, 8); ForeColor = Color.Red;
            WindowState = FormWindowState.Maximized; FormBorderStyle = FormBorderStyle.None; TopMost = true;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) WindowState = FormWindowState.Normal; };

            _date.Dock = DockStyle.Top; _date.Height = 70; _date.Padding = new Padding(24, 15, 0, 0);
            _date.Font = new Font("Segoe UI", 22, FontStyle.Bold); _date.ForeColor = Color.FromArgb(255, 75, 50);
            _days.Dock = DockStyle.Top; _days.Height = 55; _days.Padding = new Padding(0, 0, 24, 0);
            _days.Font = new Font("Segoe UI", 18, FontStyle.Bold); _days.ForeColor = Color.FromArgb(255, 75, 50);
            _days.TextAlign = ContentAlignment.MiddleRight;
            _network.Dock = DockStyle.Bottom; _network.Height = 34; _network.Padding = new Padding(18, 0, 18, 7);
            _network.TextAlign = ContentAlignment.MiddleCenter; _network.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _network.ForeColor = Color.FromArgb(255, 75, 50); _network.Text = _server.NetworkAddressText();
            _clock.Dock = DockStyle.Fill; _clock.TextAlign = ContentAlignment.MiddleCenter;
            _clock.Font = new Font("Consolas", 190, FontStyle.Bold); _clock.ForeColor = Color.FromArgb(255, 22, 12);
            _clock.Text = "00:00:00";
            Controls.Add(_clock); Controls.Add(_network); Controls.Add(_days); Controls.Add(_date);
            _timer.Interval = 500; _timer.Tick += (s, e) => Tick(); _timer.Start();
            Tick();
        }

        private void UpdateSettings(AppSettings settings)
        {
            _settings = settings;
            _store.Save(_settings);
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            _clock.Text = now.ToString("HH:mm:ss");
            _date.Text = now.ToString("dddd, d MMMM yyyy");
            _days.Text = "Study days: " + _settings.StudyDates.Distinct().Count();
            string today = now.ToString("yyyy-MM-dd");
            if (now.Hour == 0 && now.Minute == 0) _firedToday = false;
            if (!_firedToday && _settings.Alarm.Enabled && now.ToString("HH:mm") == _settings.Alarm.Time)
            {
                _firedToday = true;
                StartAlarm();
            }
            if (!_alarmActive && _settings.KeepAliveMinutes > 0 && now - _lastKeepAlive > TimeSpan.FromMinutes(_settings.KeepAliveMinutes))
            {
                _lastKeepAlive = now;
                SystemSounds.Asterisk.Play();
            }
        }

        private void StartAlarm()
        {
            if (_alarmActive) return;
            _alarmActive = true;
            BeginInvoke((Action)(() => { TopMost = true; WindowState = FormWindowState.Maximized; Activate(); }));
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (_alarmActive)
                {
                    PlayConfiguredSound();
                    Thread.Sleep(750);
                }
            });
            if (_settings.Alarm.RequireStudy)
            {
                BeginInvoke((Action)(() =>
                {
                    _studyForm = new StudyForm(_settings.Alarm, _settings.Username, _store.Decrypt(_settings.ProtectedPassword), CompleteStudy);
                    _studyForm.Show(this);
                    _studyForm.BringToFront();
                }));
            }
        }

        private void PlayConfiguredSound()
        {
            try
            {
                string file = Path.Combine(_server.SoundsFolder, Path.GetFileName(_settings.Alarm.Sound ?? ""));
                if (File.Exists(file) && String.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    _soundPlayer = new SoundPlayer(file); _soundPlayer.PlaySync();
                }
                else { SystemSounds.Exclamation.Play(); Thread.Sleep(1800); }
            }
            catch { SystemSounds.Exclamation.Play(); }
        }

        private void CompleteStudy()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (!_settings.StudyDates.Contains(today)) _settings.StudyDates.Add(today);
            _store.Save(_settings);
            StopAlarm();
        }

        private void StopAlarm()
        {
            _alarmActive = false;
            try { if (_soundPlayer != null) _soundPlayer.Stop(); } catch { }
            BeginInvoke((Action)(() =>
            {
                if (_studyForm != null) { _studyForm.AllowClose(); _studyForm.Close(); _studyForm = null; }
                TopMost = false;
            }));
        }
    }

    // Embedded browser window: it can inspect Japanology's .goed score because it is hosted by the desktop application.
    public sealed class StudyForm : Form
    {
        private readonly AlarmConfig _config;
        private readonly string _username;
        private readonly string _password;
        private readonly Action _completed;
        private readonly WebBrowser _browser = new WebBrowser();
        private readonly Label _status = new Label();
        private int _stage;
        private int _bestCorrect;
        private bool _allowClose;
        private bool _done;

        public StudyForm(AlarmConfig config, string username, string password, Action completed)
        {
            _config = config; _username = username; _password = password; _completed = completed;
            Text = "Japanology Alarm Challenge"; WindowState = FormWindowState.Maximized;
            FormClosing += (s, e) => { if (!_allowClose) e.Cancel = true; };
            var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.DarkRed };
            _status.Dock = DockStyle.Fill; _status.Padding = new Padding(16, 0, 0, 0);
            _status.TextAlign = ContentAlignment.MiddleLeft; _status.ForeColor = Color.White;
            _status.Font = new Font("Segoe UI", 12, FontStyle.Bold); header.Controls.Add(_status);
            _browser.Dock = DockStyle.Fill; _browser.ScriptErrorsSuppressed = true;
            _browser.DocumentCompleted += BrowserCompleted;
            Controls.Add(_browser); Controls.Add(header);
            SetStatus("Opening Japanology...");
            _browser.Navigate("https://www.japanology.nl/inloggen.php");
        }

        public void AllowClose() { _allowClose = true; }
        private void SetStatus(string text) { _status.Text = "Japanology alarm — " + text; }

        private void BrowserCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (_done || _browser.Url == null || e.Url != _browser.Url) return;
            try
            {
                if (_stage == 0)
                {
                    HtmlElement email = Input("Emailadres"), password = Input("Wachtwoord");
                    if (email != null && password != null)
                    {
                        _stage = 1;
                        if (String.IsNullOrWhiteSpace(_password)) { SetStatus("Enter your password and sign in."); return; }
                        email.SetAttribute("value", _username); password.SetAttribute("value", _password);
                        HtmlElement form = email;
                        while (form != null && !String.Equals(form.TagName, "FORM", StringComparison.OrdinalIgnoreCase)) form = form.Parent;
                        if (form != null) form.InvokeMember("submit");
                        return;
                    }
                    _stage = 1;
                }
                if (_stage == 1) { _stage = 2; LaunchQuiz(); return; }
                int score = Score();
                if (score >= 0)
                {
                    _bestCorrect = Math.Max(_bestCorrect, score);
                    SetStatus("Correct answers: " + _bestCorrect + " / " + Math.Max(1, _config.RequiredCorrect));
                    if (_bestCorrect >= Math.Max(1, _config.RequiredCorrect))
                    {
                        _done = true; _allowClose = true;
                        MessageBox.Show(this, "Target reached. The alarm is stopping.", "Japanology Alarm");
                        _completed();
                    }
                }
                else SetStatus("Answer questions until you have " + Math.Max(1, _config.RequiredCorrect) + " correct answers.");
            }
            catch { SetStatus("Use Japanology normally. The score is checked after each answer."); }
        }

        private HtmlElement Input(string name)
        {
            foreach (HtmlElement element in _browser.Document.GetElementsByTagName("input"))
                if (String.Equals(element.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase)) return element;
            return null;
        }

        private int Score()
        {
            foreach (HtmlElement element in _browser.Document.GetElementsByTagName("span"))
                if ((element.GetAttribute("className") ?? "").Split(' ').Contains("goed"))
                {
                    int score; if (Int32.TryParse(element.InnerText, out score)) return score;
                }
            return -1;
        }

        private void LaunchQuiz()
        {
            string q = "'";
            string script = "(function(){var f=document.createElement('form');f.method='POST';f.action='https://www.japanology.nl/quiz.php';function a(n,v){var i=document.createElement('input');i.type='hidden';i.name=n;i.value=v;f.appendChild(i);}" +
                "a('vanaf'," + q + HttpUtility.JavaScriptStringEncode(_config.From) + q + ");a('totenmet'," + q + HttpUtility.JavaScriptStringEncode(_config.To) + q + ");a('modus'," + q + HttpUtility.JavaScriptStringEncode(_config.Mode) + q + ");a('overhoor_alfabet'," + q + HttpUtility.JavaScriptStringEncode(_config.Alphabet) + q + ");" +
                "a('vertalen'," + q + (_config.Mode == "vertalen" ? "Aan" : "Uit") + q + ");a('grammatica'," + q + (_config.Mode == "grammatica" ? "Aan" : "Uit") + q + ");a('partikels'," + q + (_config.Mode == "partikels" ? "Aan" : "Uit") + q + ");a('vervoegen'," + q + (_config.Mode == "vervoegen" ? "Aan" : "Uit") + q + ");a('radiomodus'," + q + (_config.Mode == "radio" ? "Aan" : "Uit") + q + ");" +
                "a('luisteroefeningen'," + q + (_config.Listening ? "Aan" : "Uit") + q + ");a('kanjivoorbeelden'," + q + (_config.KanjiExamples ? "Aan" : "Uit") + q + ");a('moeilijk'," + q + (_config.Difficult ? "Aan" : "Uit") + q + ");a('furigana'," + q + (_config.Furigana ? "Aan" : "Uit") + q + ");document.body.appendChild(f);f.submit();})();";
            _browser.Document.InvokeScript("eval", new object[] { script });
            SetStatus("Answer questions until you have " + Math.Max(1, _config.RequiredCorrect) + " correct answers.");
        }
    }

    public sealed class LocalServer
    {
        private readonly SettingsStore _store;
        private readonly Func<AppSettings> _get;
        private readonly Action<AppSettings> _save;
        private readonly Action _stopAlarm;
        private readonly Action _startAlarm;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private AlarmConfig _activeStudy;
        public string SoundsFolder { get; private set; }

        public LocalServer(SettingsStore store, Func<AppSettings> get, Action<AppSettings> save, Action stopAlarm, Action startAlarm)
        {
            _store = store; _get = get; _save = save; _stopAlarm = stopAlarm; _startAlarm = startAlarm;
            SoundsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JapanologyAlarm", "sounds");
            Directory.CreateDirectory(SoundsFolder);
        }

        public void Start()
        {
            // + accepts requests from localhost and every local-network adapter.
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://+:8123/");
            try { _listener.Start(); }
            catch (HttpListenerException ex)
            {
                MessageBox.Show("The network control server could not start on port 8123. Close the app, then run StartJapanologyAlarm.bat again and accept the Windows permission prompt.\n\n" + ex.Message, "Japanology Alarm");
                return;
            }
            _running = true; _thread = new Thread(Listen) { IsBackground = true }; _thread.Start();
        }

        public void BeginStudySession(AlarmConfig config) { _activeStudy = config; }

        private List<string> NetworkUrls()
        {
            var addresses = new List<string>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                        {
                            string url = "http://" + address.Address + ":8123";
                            if (!addresses.Contains(url)) addresses.Add(url);
                        }
                    }
                }
            }
            catch { }
            return addresses;
        }

        public string NetworkAddressText()
        {
            var addresses = NetworkUrls();
            return addresses.Count == 0 ? "Connect tablet to Wi-Fi to enable network controls" : "Network controls: " + String.Join("   ", addresses);
        }

        private string NetworkControlLinks()
        {
            var addresses = NetworkUrls();
            if (addresses.Count == 0)
                return "<span class='warning'>No Wi-Fi or Ethernet address was found. Connect the tablet to your local network.</span>";
            return String.Join("<br>", addresses.Select(url => "<a href='" + H(url) + "'>" + H(url) + "</a>"));
        }

        private void Listen()
        {
            while (_running)
            {
                try { Handle(_listener.GetContext()); }
                catch { }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath.ToLowerInvariant();
                if (path == "/") Write(context, Page());
                else if (path == "/save") Save(context);
                else if (path == "/upload") Upload(context);
                else if (path == "/delete") Delete(context);
                else if (path == "/test") { _startAlarm(); Redirect(context, "/"); }
                else if (path == "/stop") { _stopAlarm(); Redirect(context, "/"); }
                else if (path == "/study") Write(context, StudyPage());
                else if (path == "/study-complete") CompleteStudy(context);
                else { context.Response.StatusCode = 404; context.Response.Close(); }
            }
            catch (Exception ex) { Write(context, "<h1>Error</h1><pre>" + H(ex.ToString()) + "</pre>", 500); }
        }

        private void Save(HttpListenerContext c)
        {
            var s = _get(); var f = Form(c);
            s.Alarm.Enabled = V(f, "enabled") == "on";
            s.Alarm.Time = V(f, "time", "07:00"); s.Alarm.Sound = V(f, "sound");
            s.Alarm.RequireStudy = V(f, "study") == "on";
            s.Alarm.RequiredCorrect = Int(V(f, "correct", "5"), 5);
            s.Alarm.From = V(f, "from", "Alles"); s.Alarm.To = V(f, "to", "Alles");
            s.Alarm.Mode = V(f, "mode", "woorden"); s.Alarm.Alphabet = V(f, "alphabet", "Romaji");
            s.Alarm.Listening = V(f, "listening") == "on"; s.Alarm.KanjiExamples = V(f, "kanjiExamples") == "on";
            s.Alarm.Difficult = V(f, "difficult") == "on"; s.Alarm.Furigana = V(f, "furigana") == "on";
            s.Username = V(f, "username");
            if (!String.IsNullOrWhiteSpace(V(f, "password"))) s.ProtectedPassword = _store.Encrypt(V(f, "password"));
            s.KeepAliveMinutes = Int(V(f, "keepalive", "8"), 8);
            _save(s); Redirect(c, "/");
        }

        private void Upload(HttpListenerContext c)
        {
            if (c.Request.Files.Count > 0)
            {
                var file = c.Request.Files[0]; string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext == ".wav") file.SaveAs(Path.Combine(SoundsFolder, Path.GetFileName(file.FileName)));
            }
            Redirect(c, "/");
        }

        private void Delete(HttpListenerContext c)
        {
            string name = Path.GetFileName(c.Request.QueryString["file"] ?? "");
            string file = Path.Combine(SoundsFolder, name);
            if (File.Exists(file)) File.Delete(file);
            Redirect(c, "/");
        }

        private void CompleteStudy(HttpListenerContext c)
        {
            var s = _get(); string today = DateTime.Today.ToString("yyyy-MM-dd");
            if (!s.StudyDates.Contains(today)) s.StudyDates.Add(today);
            _save(s); _stopAlarm();
            Write(c, "<html><body style='font-family:Segoe UI;text-align:center;padding:50px'><h1>Done — alarm stopped.</h1><p>You completed your Japanology target for today.</p></body></html>");
        }

        private string StudyPage()
        {
            if (_activeStudy == null || !_activeStudy.RequireStudy)
                return "<html><body><h1>No study-required alarm is active.</h1></body></html>";
            var a = _activeStudy; int target = Math.Max(1, a.RequiredCorrect);
            string password = _store.Decrypt(_get().ProtectedPassword);
            // The page logs in and launches Japanology in an iframe. Its score observer reads only .goed.
            return @"<!doctype html><html><head><meta charset='utf-8'><title>Japanology Alarm Challenge</title><style>body{margin:0;font:16px Segoe UI;background:#111;color:#eee}#bar{height:56px;display:flex;align-items:center;gap:20px;padding:0 18px;background:#a00;font-weight:bold}iframe{border:0;width:100%;height:calc(100vh - 56px)}button{padding:7px 12px}</style></head><body><div id='bar'>Correct answers: <span id='score'>0 / " + target + @"</span><span id='status'>Opening Japanology…</span></div><iframe id='quiz' name='quiz'></iframe>
<form id='login' method='POST' action='https://www.japanology.nl/inloggen.php' target='quiz' style='display:none'><input name='Emailadres'><input name='Wachtwoord'><input name='overhoring' value='Aanmelden overhoring'></form>
<form id='launch' method='POST' action='https://www.japanology.nl/quiz.php' target='quiz' style='display:none'><input name='vanaf'><input name='totenmet'><input name='modus'><input name='overhoor_alfabet'><input name='vertalen'><input name='grammatica'><input name='partikels'><input name='vervoegen'><input name='radiomodus'><input name='luisteroefeningen'><input name='kanjivoorbeelden'><input name='moeilijk'><input name='furigana'></form>
<script>
(function(){
 var target=" + target + @", last=0, frame=document.getElementById('quiz'), login=document.getElementById('login'), launch=document.getElementById('launch');
 function set(form,n,v){form.querySelector('[name='+n+']').value=v;}
 set(login,'Emailadres','" + J(_get().Username) + "'); set(login,'Wachtwoord','" + J(password) + "'); login.submit();
 var launched=false;
 frame.onload=function(){
   if(!launched){ launched=true; set(launch,'vanaf','" + J(a.From) + "'); set(launch,'totenmet','" + J(a.To) + "'); set(launch,'modus','" + J(a.Mode) + "'); set(launch,'overhoor_alfabet','" + J(a.Alphabet) + "'); set(launch,'vertalen','" + (a.Mode == "vertalen" ? "Aan" : "Uit") + "'); set(launch,'grammatica','" + (a.Mode == "grammatica" ? "Aan" : "Uit") + "'); set(launch,'partikels','" + (a.Mode == "partikels" ? "Aan" : "Uit") + "'); set(launch,'vervoegen','" + (a.Mode == "vervoegen" ? "Aan" : "Uit") + "'); set(launch,'radiomodus','" + (a.Mode == "radio" ? "Aan" : "Uit") + "'); set(launch,'luisteroefeningen','" + (a.Listening ? "Aan" : "Uit") + "'); set(launch,'kanjivoorbeelden','" + (a.KanjiExamples ? "Aan" : "Uit") + "'); set(launch,'moeilijk','" + (a.Difficult ? "Aan" : "Uit") + "'); set(launch,'furigana','" + (a.Furigana ? "Aan" : "Uit") + "'); launch.submit(); document.getElementById('status').textContent='Answer questions in the Japanology page.'; }
   try { var good=frame.contentDocument.querySelector('.goed'); if(good){var n=parseInt(good.textContent,10)||0; if(n>last) last=n; document.getElementById('score').textContent=last+' / '+target; if(last>=target) location.href='/study-complete';} } catch(e) { document.getElementById('status').textContent='Japanology blocks score reading in an iframe. Use the completion button after '+target+' correct answers.'; }
 };
})();
</script></body></html>";
        }

        private string Page()
        {
            var s = _get(); var a = s.Alarm;
            string sounds = String.Join("", Directory.GetFiles(SoundsFolder, "*.wav").Select(f => "<option" + (Path.GetFileName(f) == a.Sound ? " selected" : "") + " value='" + H(Path.GetFileName(f)) + "'>" + H(Path.GetFileName(f)) + "</option>"));
            string deletes = String.Join("", Directory.GetFiles(SoundsFolder, "*.wav").Select(f => "<li>" + H(Path.GetFileName(f)) + " <a href='/delete?file=" + HttpUtility.UrlEncode(Path.GetFileName(f)) + "'>delete</a></li>"));
            return @"<!doctype html><html><head><meta charset='utf-8'><title>Japanology Alarm</title><style>body{max-width:850px;margin:30px auto;padding:0 18px;font:16px Segoe UI;color:#202124}h1{color:#a00}fieldset{margin:18px 0;padding:18px;border:1px solid #ccc}label{display:block;margin:9px 0}input,select,button{font:inherit;padding:6px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:0 22px}.note{color:#666;font-size:.9em}.network{margin:18px 0;padding:14px;background:#fff4e5;border-left:5px solid #d97706;line-height:1.7}.network a{font-size:1.1em;font-weight:bold}.warning{color:#a00}</style></head><body><h1>Japanology Alarm</h1><p>Clock display is running on this tablet.</p><div class='network'><b>Open the controls from another device on the same Wi-Fi/Ethernet network:</b><br>" + NetworkControlLinks() + @"</div><p class='note'>Use one of the addresses above from the other PC. Both devices must be on the same local network. This server is limited by Windows Firewall to Private networks; do not create router port forwarding for port 8123.</p><form action='/save' method='post'><fieldset><legend>Alarm</legend><label><input type='checkbox' name='enabled' " + C(a.Enabled) + @"> Enable daily alarm</label><label>Time <input type='time' name='time' value='" + H(a.Time) + @"'></label><label>Alarm sound <select name='sound'><option value=''>Windows alert (fallback)</option>" + sounds + @"</select></label><p><a href='/test'>Test / start alarm now</a> · <a href='/stop'>Stop alarm</a></p></fieldset><fieldset><legend>Japanology challenge</legend><label><input type='checkbox' name='study' " + C(a.RequireStudy) + @"> Require Japanology study to stop the alarm</label><label>Correct answers required <input type='number' min='1' max='999' name='correct' value='" + a.RequiredCorrect + @"'></label><div class='grid'><label>From <input name='from' value='" + H(a.From) + @"'></label><label>To <input name='to' value='" + H(a.To) + @"'></label><label>Exercise type <select name='mode'>" + Options(new[] { "woorden", "vertalen", "grammatica", "partikels", "vervoegen", "radio" }, a.Mode) + @"</select></label><label>Question script <select name='alphabet'>" + Options(new[] { "Romaji", "Kana", "Kanji", "Audio" }, a.Alphabet) + @"</select></label></div><label><input type='checkbox' name='listening' " + C(a.Listening) + @"> Listening</label><label><input type='checkbox' name='kanjiExamples' " + C(a.KanjiExamples) + @"> Kanji examples</label><label><input type='checkbox' name='difficult' " + C(a.Difficult) + @"> Mix difficult words</label><label><input type='checkbox' name='furigana' " + C(a.Furigana) + @"> Furigana</label><p class='note'>When the alarm rings, open <a href='/study' target='_blank'>the study challenge</a>. Japanology may prevent a local page from reading its score because it is a different website, so the final score integration needs a small Japanology-side redirect/API endpoint or extension.</p></fieldset><fieldset><legend>Japanology login</legend><label>Email <input type='email' name='username' value='" + H(s.Username) + @"'></label><label>Password <input type='password' name='password' placeholder='Leave empty to keep saved password'></label><p class='note'>Password is encrypted for this Windows user with DPAPI.</p></fieldset><fieldset><legend>Bluetooth speaker keep-alive</legend><label>Play a quiet Windows notification every <input type='number' name='keepalive' min='0' max='120' value='" + s.KeepAliveMinutes + @"'> minutes (0 disables it)</label><p class='note'>Pair and select the Bluetooth speaker as Windows' default audio device. The app plays through that device.</p></fieldset><button type='submit'>Save settings</button></form><fieldset><legend>Alarm sounds</legend><form action='/upload' method='post' enctype='multipart/form-data'><input type='file' name='sound' accept='.wav,audio/wav' required><button>Upload WAV</button></form><p class='note'>This Windows 8.1 build accepts WAV files without external audio libraries.</p><ul>" + deletes + @"</ul></fieldset></body></html>";
        }

        private static Dictionary<string, string> Form(HttpListenerContext c)
        {
            using (var reader = new StreamReader(c.Request.InputStream, c.Request.ContentEncoding))
            {
                var values = HttpUtility.ParseQueryString(reader.ReadToEnd());
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in values.AllKeys) if (key != null) result[key] = values[key] ?? "";
                return result;
            }
        }

        private static string V(Dictionary<string, string> form, string key, string fallback = "")
        {
            string value;
            return form.TryGetValue(key, out value) ? value : fallback;
        }

        private static void Write(HttpListenerContext c, string body, int status = 200)
        {
            byte[] data = Encoding.UTF8.GetBytes(body); c.Response.StatusCode = status; c.Response.ContentType = "text/html; charset=utf-8"; c.Response.ContentLength64 = data.Length; c.Response.OutputStream.Write(data, 0, data.Length); c.Response.Close();
        }
        private static void Redirect(HttpListenerContext c, string location) { c.Response.StatusCode = 302; c.Response.RedirectLocation = location; c.Response.Close(); }
        private static string H(string s) { return HttpUtility.HtmlEncode(s ?? ""); }
        private static string J(string s) { return HttpUtility.JavaScriptStringEncode(s ?? ""); }
        private static string C(bool value) { return value ? "checked" : ""; }
        private static int Int(string s, int fallback) { int n; return Int32.TryParse(s, out n) ? n : fallback; }
        private static string Options(IEnumerable<string> values, string selected) { return String.Join("", values.Select(v => "<option" + (v == selected ? " selected" : "") + " value='" + H(v) + "'>" + H(v) + "</option>")); }
    }
}
