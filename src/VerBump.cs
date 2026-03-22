using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// ── Settings model ────────────────────────────────────────────────────────────

public class ProjectEntry {
    [JsonPropertyName("path")]        public string       Path        { get; set; } = "";
    [JsonPropertyName("name")]        public string       Name        { get; set; } = "";
    [JsonPropertyName("icon")]        public string       Icon        { get; set; } = "";
    [JsonPropertyName("scheme")]      public string       Scheme      { get; set; } = "semver";
    [JsonPropertyName("format")]      public string       Format      { get; set; } = "";
    [JsonPropertyName("resetOnBump")] public bool         ResetOnBump { get; set; } = true;
    [JsonPropertyName("backup")]      public bool         Backup      { get; set; } = false;
    [JsonPropertyName("ignoreDirs")]  public List<string> IgnoreDirs  { get; set; } = [];
    [JsonPropertyName("ignoreFiles")] public List<string> IgnoreFiles { get; set; } = [];
}

public class Settings {
    [JsonPropertyName("ignoreDirs")]  public List<string>    IgnoreDirs  { get; set; } = [];
    [JsonPropertyName("ignoreFiles")] public List<string>    IgnoreFiles { get; set; } = [];
    [JsonPropertyName("paths")]       public List<ProjectEntry> Paths    { get; set; } = [];
}

// ── Version schemes ───────────────────────────────────────────────────────────

public interface IVersionScheme {
    List<string> GetButtonLabels();
    string Bump(string current, int partIndex);
    string Refresh(string current) => current;
}

public class SemVerScheme : IVersionScheme {
    readonly bool _resetOnBump;
    public SemVerScheme(bool resetOnBump = true) { _resetOnBump = resetOnBump; }
    public List<string> GetButtonLabels() => new() { "Major", "Minor", "Patch" };

    public string Bump(string current, int partIndex) {
        var parts = current.Split('.');
        if (parts.Length < 3) parts = new[] { "0", "0", "0" };
        int val = int.TryParse(parts[partIndex], out int v) ? v : 0;
        parts[partIndex] = (val + 1).ToString();
        if (_resetOnBump)
            for (int i = partIndex + 1; i < parts.Length; i++) parts[i] = "0";
        return string.Join(".", parts);
    }
}

public class SequentialScheme : IVersionScheme {
    readonly string[] _tokens;
    readonly bool _resetOnBump;

    public SequentialScheme(string format, bool resetOnBump) {
        _tokens = format.Split('.');
        _resetOnBump = resetOnBump;
    }

    public List<string> GetButtonLabels() => _tokens.ToList();

    public string Bump(string current, int partIndex) {
        var parts = current.Split('.');
        if (parts.Length != _tokens.Length) parts = _tokens.Select(_ => "0").ToArray();
        int val = int.TryParse(parts[partIndex], out int v) ? v : 0;
        parts[partIndex] = (val + 1).ToString();
        if (_resetOnBump)
            for (int i = partIndex + 1; i < parts.Length; i++) parts[i] = "0";
        return string.Join(".", parts);
    }
}

public class CalVerScheme : IVersionScheme {
    readonly string[] _tokens;
    readonly bool _resetOnBump;
    static readonly HashSet<string> DateTokens = new(StringComparer.OrdinalIgnoreCase)
        { "YYYY", "YY", "MM", "DD" };

    public CalVerScheme(string format, bool resetOnBump) {
        _tokens = format.Split('.');
        _resetOnBump = resetOnBump;
    }

    public List<string> GetButtonLabels() =>
        _tokens.Select(t => DateTokens.Contains(t) ? null : t).ToList();

    string DateValue(string token) => token.ToUpper() switch {
        "YYYY" => DateTime.Today.Year.ToString(),
        "YY"   => (DateTime.Today.Year % 100).ToString("D2"),
        "MM"   => DateTime.Today.Month.ToString("D2"),
        "DD"   => DateTime.Today.Day.ToString("D2"),
        _      => "0"
    };

    public string Refresh(string current) {
        var parts = current.Split('.');
        if (parts.Length != _tokens.Length) parts = _tokens.Select(_ => "0").ToArray();
        for (int i = 0; i < _tokens.Length; i++)
            if (DateTokens.Contains(_tokens[i].ToUpper()))
                parts[i] = DateValue(_tokens[i]);
        return string.Join(".", parts);
    }

    public string Bump(string current, int partIndex) {
        var parts = current.Split('.');
        if (parts.Length != _tokens.Length) parts = _tokens.Select(_ => "0").ToArray();
        for (int i = 0; i < _tokens.Length; i++)
            if (DateTokens.Contains(_tokens[i].ToUpper()))
                parts[i] = DateValue(_tokens[i]);
        if (!DateTokens.Contains(_tokens[partIndex].ToUpper())) {
            int val = int.TryParse(parts[partIndex], out int v) ? v : 0;
            parts[partIndex] = (val + 1).ToString();
            if (_resetOnBump)
                for (int i = partIndex + 1; i < parts.Length; i++)
                    if (!DateTokens.Contains(_tokens[i].ToUpper()))
                        parts[i] = "0";
        }
        return string.Join(".", parts);
    }
}

public static class SchemeFactory {
    public static IVersionScheme Create(ProjectEntry entry) => entry.Scheme.ToLower() switch {
        "semver"     => new SemVerScheme(entry.ResetOnBump),
        "calver"     => new CalVerScheme(
            string.IsNullOrWhiteSpace(entry.Format) ? "YY.MM.patch" : entry.Format,
            entry.ResetOnBump),
        "sequential" => new SequentialScheme(
            string.IsNullOrWhiteSpace(entry.Format) ? "major.minor.patch" : entry.Format,
            entry.ResetOnBump),
        _ => new SemVerScheme()
    };
}

// ── Phosphor Icons ─────────────────────────────────────────────────────────────

public static class Ph {
    static readonly PrivateFontCollection _pfc = new();
    static FontFamily _family;
    static FontFamily _familyBold;

    public const string Info        = "\ue2ce";
    public const string Gear        = "\ue270";
    public const string Warning     = "\ue4e0";
    public const string Eye         = "\ue220";
    public const string EyeSlash    = "\ue224";
    public const string CheckCircle = "\ue184";

    public static void Init(System.Reflection.Assembly asm) {
        LoadFont(asm, "VerBump.Phosphor.ttf",     ref _family);
        LoadFont(asm, "VerBump.Phosphor-Bold.ttf", ref _familyBold);
    }

    static void LoadFont(System.Reflection.Assembly asm, string resource, ref FontFamily target) {
        try {
            using var stream = asm.GetManifestResourceStream(resource);
            if (stream == null) return;
            var bytes  = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            int before = _pfc.Families.Length;
            try   { _pfc.AddMemoryFont(handle.AddrOfPinnedObject(), bytes.Length); }
            finally { handle.Free(); }
            if (_pfc.Families.Length > before)
                target = _pfc.Families[_pfc.Families.Length - 1];
        } catch (Exception ex) { Log.Write("Ph.LoadFont", ex); }
    }

    public static Font Font(float size) =>
        _family != null ? new Font(_family, size) : new Font("Segoe UI Symbol", size);

    public static Font BoldFont(float size) =>
        _familyBold != null ? new Font(_familyBold, size)
        : _family   != null ? new Font(_family, size, FontStyle.Bold)
        : new Font("Segoe UI Symbol", size, FontStyle.Bold);

    public static Bitmap ToBitmap(string glyph, float size, Color color, bool bold = false) {
        int px  = (int)Math.Ceiling(size * 2);
        var bmp = new Bitmap(px, px);
        using var g     = Graphics.FromImage(bmp);
        using var font  = bold ? BoldFont(size) : Font(size);
        using var brush = new SolidBrush(color);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, px, px), sf);
        return bmp;
    }
}

// ── Localisation ──────────────────────────────────────────────────────────────

public static class L {
    static Dictionary<string, string> _d = new();
    public static string Lang { get; private set; } = "en";

    public static void Load(string baseDir) {
        string lang = WindowsUILanguage();
        string path = Path.Combine(baseDir, $"lang.{lang}.json");
        if (!File.Exists(path)) { lang = "en"; path = Path.Combine(baseDir, "lang.en.json"); }
        if (!File.Exists(path)) return;
        try {
            _d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            Lang = lang;
        } catch (Exception ex) { Log.Write("L.Load", ex); }
    }

    static string WindowsUILanguage() {
        try {
            string locale = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Control Panel\International", "LocaleName", null) as string;
            if (!string.IsNullOrEmpty(locale)) return locale.Split('-')[0].ToLower();
        } catch (Exception ex) { Log.Write("L.WindowsUILanguage", ex); }
        return "en";
    }

    public static string T(string key, params object[] args) {
        string v = _d.TryGetValue(key, out var s) ? s : key;
        return args.Length > 0 ? string.Format(v, args) : v;
    }
}

// ── Logging ───────────────────────────────────────────────────────────────────

public static class Log {
    public static string Path { get; private set; }

    public static void Init(string appDataDir) {
        Path = System.IO.Path.Combine(appDataDir, "verbump.log");
    }

    public static void Write(string context, Exception ex) {
        if (Path == null) return;
        try {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{context}]  {ex.GetType().Name}: {ex.Message}{Environment.NewLine}";
            File.AppendAllText(Path, line);
            // Auf ~100 KB begrenzen: obere Hälfte wegwerfen
            var fi = new FileInfo(Path);
            if (fi.Length > 102400) {
                var lines = File.ReadAllLines(Path);
                File.WriteAllLines(Path, lines.Skip(lines.Length / 2));
            }
        } catch { }
    }
}

// ── Main program ──────────────────────────────────────────────────────────────

public static class VerBump {

    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public class ProjectUI {
        public Panel       SelectionPanel;
        public Panel       StatusStrip;
        public Control     InfoIcon;
        public TextBox     VersionBox;
        public string      FilePath;
        public string      OriginalVersion;
        public IVersionScheme Scheme;
        public bool        Backup;
        public ProjectEntry Entry;
        public bool?       HasIssues;   // null = noch am Scannen
    }

class DarkColorTable : ProfessionalColorTable {
        static readonly Color Bg    = Color.FromArgb(45, 45, 48);
        static readonly Color Hover = Color.FromArgb(70, 70, 80);
        static readonly Color Sep   = Color.FromArgb(80, 80, 85);
        public override Color ToolStripGradientBegin          => Bg;
        public override Color ToolStripGradientMiddle         => Bg;
        public override Color ToolStripGradientEnd            => Bg;
        public override Color ButtonSelectedGradientBegin     => Hover;
        public override Color ButtonSelectedGradientMiddle    => Hover;
        public override Color ButtonSelectedGradientEnd       => Hover;
        public override Color ButtonPressedGradientBegin      => Hover;
        public override Color ButtonPressedGradientMiddle     => Hover;
        public override Color ButtonPressedGradientEnd        => Hover;
        public override Color ButtonCheckedGradientBegin      => Hover;
        public override Color ButtonCheckedGradientMiddle     => Hover;
        public override Color ButtonCheckedGradientEnd        => Hover;
        public override Color ButtonSelectedBorder            => Sep;
        public override Color SeparatorDark                   => Sep;
        public override Color SeparatorLight                  => Sep;
        public override Color ImageMarginGradientBegin        => Bg;
        public override Color ImageMarginGradientMiddle       => Bg;
        public override Color ImageMarginGradientEnd          => Bg;
    }

    [STAThread]
    public static void Main() {
        SetForegroundWindow(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Run();
    }

    public static void Run() {
        try { SetCurrentProcessExplicitAppUserModelID("VerBump.1.0"); } catch (Exception ex) { Log.Write("Run/AppUserModelID", ex); }

        string baseDir    = AppDomain.CurrentDomain.BaseDirectory;
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerBump");
        Directory.CreateDirectory(appDataDir);
        Log.Init(appDataDir);
        L.Load(baseDir);
        string appVersion = "?";
        try {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("VerBump.VERSION");
            if (stream != null) {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                appVersion = reader.ReadToEnd().Trim();
            }
        } catch (Exception ex) { Log.Write("Run/version", ex); }

        string jsonPath = Path.Combine(appDataDir, "VerBump-settings.json");
        if (!File.Exists(jsonPath)) {
            var dummy = new Settings {
                IgnoreDirs  = [..DefaultIgnoreDirs],
                IgnoreFiles = [..DefaultIgnoreFiles],
                Paths = new List<ProjectEntry> {
                    new ProjectEntry { Path = @"C:\Beispiel\Pfad1", Scheme = "semver" }
                }
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(dummy, new JsonSerializerOptions { WriteIndented = true }));
            using var md = new Form {
                Text = "VerBump", Width = 520, Height = 150,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White,
            };
            new Label { Parent = md, Left = 16, Top = 16, Width = 480, Height = 52,
                Text = L.T("settings.created", jsonPath),
                Font = new Font("Segoe UI", 9F), ForeColor = Color.White };
            var mdOk = new Button { Parent = md, Text = "OK", Left = 412, Top = 74, Width = 80, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
                DialogResult = DialogResult.OK };
            md.AcceptButton = mdOk;
            md.ShowDialog();
            return;
        }

        Settings settings;
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(jsonPath), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new Settings(); }
        catch (Exception ex) { Log.Write("Run/json", ex); MessageBox.Show(L.T("error.json", ex.Message)); return; }

        using var form = new Form {
            Text = $"VerBump  v{appVersion}",
            Width = 720, Height = 80 + settings.Paths.Count * 55 + 138,
            StartPosition = FormStartPosition.CenterScreen,
            KeyPreview = true,
            BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White
        };

        try {
            string iconPath = Path.Combine(baseDir, "verbump.ico");
            form.Icon = File.Exists(iconPath) ? new Icon(iconPath) :
                Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        } catch { form.Icon = SystemIcons.Application; }

        var btnOk  = new Button { Text = L.T("btn.save"),   Left = 470, Top = 15, Width = 110, Height = 35, DialogResult = DialogResult.OK,     FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        var btnCan = new Button { Text = L.T("btn.cancel"), Left = 590, Top = 15, Width = 110, Height = 35, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };

        form.AcceptButton = btnOk;
        form.CancelButton = btnCan;

        // ── Statuszeile ──
        var statusLabel = new Label {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.LightGray,
            Padding = new Padding(8, 0, 0, 0),
        };
        var statusPanel = new Panel {
            Dock = DockStyle.Bottom, Height = 24,
            BackColor = Color.FromArgb(30, 30, 30),
        };
        statusPanel.Controls.Add(statusLabel);

        var statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        Action<string, bool> setStatus = (msg, isError) => {
            statusLabel.ForeColor = isError ? Color.Salmon : Color.LightGray;
            statusLabel.Text = msg;
            if (isError) { statusTimer.Stop(); statusTimer.Start(); }
        };

        var mainPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = Color.FromArgb(30, 30, 30), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var uiEntries = new List<ProjectUI>();
        int selectedIndex = 0;
        Action updateSelection = null;

        Ph.Init(System.Reflection.Assembly.GetExecutingAssembly());

        foreach (var entry in settings.Paths) {
            string cleanPath = entry.Path.Trim().TrimEnd(Path.DirectorySeparatorChar, '/');
            string vFile = Path.Combine(cleanPath, "VERSION");
            if (!File.Exists(vFile)) continue;

            string currentV = File.ReadAllText(vFile).Trim();
            string projectName = !string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Name
                : Path.GetFileName(cleanPath) ?? L.T("project.unnamed");
            IVersionScheme scheme = SchemeFactory.Create(entry);

            if (entry.Scheme.ToLower() == "calver")
                currentV = scheme.Refresh(currentV);

            // ── Projekt-Icon laden (optional) ──
            Image projectIcon = null;
            if (!string.IsNullOrWhiteSpace(entry.Icon)) {
                string iconFull = Environment.ExpandEnvironmentVariables(entry.Icon);
                try {
                    if (File.Exists(iconFull)) {
                        string ext = Path.GetExtension(iconFull).ToLower();
                        if (ext == ".ico") {
                            using var ico = new Icon(iconFull, 36, 36);
                            projectIcon = ico.ToBitmap();
                        } else {
                            projectIcon = Image.FromFile(iconFull);
                        }
                    }
                } catch (Exception ex) { Log.Write($"Run/projectIcon/{entry.Icon}", ex); }
            }

            var selectionPanel = new Panel { Width = 680, Height = 50, Margin = new Padding(0, 3, 0, 3), BackColor = Color.Transparent };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(3) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));   // Icon
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));   // Hotkey
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));  // Name
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));  // Version
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // Buttons
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));   // Info

            int hotkeyIndex = uiEntries.Count;
            string hotkeyChar = hotkeyIndex < 26 ? ((char)('A' + hotkeyIndex)).ToString()
                              : hotkeyIndex < 36 ? ((char)('0' + hotkeyIndex - 26)).ToString()
                              : "";
            var lblHotkey = new Label {
                Text = hotkeyChar,
                Width = 18, Height = 44,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(130, 130, 140),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            var lbl = new Label { Text = projectName, Width = 172, Height = 44, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };

            var tb = new TextBox {
                Text = currentV,
                Width = 110, Height = 23,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F),
            };

            int entryIndex = uiEntries.Count;
            tb.Enter   += (s, e) => { selectedIndex = entryIndex; updateSelection(); };
            tb.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; btnOk.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; btnCan.PerformClick(); }
            };

            var infoIcon = new Label {
                Visible = false, Dock = DockStyle.Fill,
                Text = Ph.Info, Font = Ph.Font(16F),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(255, 140, 0),
                BackColor = Color.Transparent,
                Cursor = Cursors.Help,
            };
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5, 5, 0, 0) };
            var labels = scheme.GetButtonLabels();
            for (int i = 0; i < labels.Count; i++) {
                if (labels[i] == null) continue;
                int partIndex = i;
                var tbCaptured = tb;
                var schemeCaptured = scheme;
                var btn = new Button {
                    Text = labels[i] + "+",
                    Width = 70, Height = 28,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(70, 70, 70),
                    Font = new Font("Segoe UI", 8F)
                };
                btn.Click += (s, e) => {
                    tbCaptured.Text = schemeCaptured.Bump(tbCaptured.Text.Trim(), partIndex);
                    tbCaptured.BackColor = Color.DarkGreen;
                };
                buttonPanel.Controls.Add(btn);
            }

            var iconBox = new PictureBox {
                Width = 36, Height = 36,
                Margin = new Padding(0, 7, 0, 0),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = projectIcon,
                Visible = projectIcon != null,
            };
            table.Controls.Add(iconBox,   0, 0);
            table.Controls.Add(lblHotkey, 1, 0);
            table.Controls.Add(lbl,       2, 0);
            table.Controls.Add(tb,        3, 0);
            table.Controls.Add(buttonPanel, 4, 0);
            table.Controls.Add(infoIcon,  5, 0);
            var strip = new Panel { Width = 5, Dock = DockStyle.Left, BackColor = Color.FromArgb(80, 80, 80) };
            selectionPanel.Controls.Add(strip);
            selectionPanel.Controls.Add(table);
            mainPanel.Controls.Add(selectionPanel);
            uiEntries.Add(new ProjectUI { SelectionPanel = selectionPanel, StatusStrip = strip, InfoIcon = infoIcon, VersionBox = tb, FilePath = vFile, OriginalVersion = currentV, Scheme = scheme, Backup = entry.Backup, Entry = entry });
        }

        if (uiEntries.Count == 0) {
            var msgFont = new Font("Segoe UI", 9F);
            int pathPx  = TextRenderer.MeasureText(jsonPath, msgFont).Width;
            int formW   = Math.Max(360, Math.Min(720, pathPx + 96));
            using var md = new Form {
                Text = "VerBump", Width = formW, Height = 170,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White,
            };
            new PictureBox { Parent = md, Left = 16, Top = 16, Width = 32, Height = 32,
                Image = SystemIcons.Warning.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom };
            new Label { Parent = md, Left = 56, Top = 12, Width = formW - 72, Height = 80,
                Text = L.T("error.nofiles", jsonPath), Font = msgFont, ForeColor = Color.White };
            var mdYes = new Button { Parent = md, Text = L.T("btn.yes", "Ja"),
                Left = formW - 202, Top = 96, Width = 88, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White,
                DialogResult = DialogResult.Yes };
            new Button { Parent = md, Text = L.T("btn.no", "Nein"),
                Left = formW - 108, Top = 96, Width = 88, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = Color.White,
                DialogResult = DialogResult.No };
            md.AcceptButton = mdYes;
            if (md.ShowDialog() == DialogResult.Yes) ShowSettingsDialog(form, settings, jsonPath);
            return;
        }

        var toolTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 200 };

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 65 };
        bottomPanel.Controls.Add(btnOk);
        bottomPanel.Controls.Add(btnCan);
        var toolStrip = new ToolStrip {
            Dock             = DockStyle.Top,
            BackColor        = Color.FromArgb(45, 45, 48),
            ForeColor        = Color.White,
            GripStyle        = ToolStripGripStyle.Hidden,
            Renderer         = new ToolStripProfessionalRenderer(new DarkColorTable()),
            Font             = new Font("Segoe UI", 9F),
            ImageScalingSize = new Size(22, 22),
        };

        var tsSettings = new ToolStripButton(L.T("toolbar.settings")) {
            ForeColor = Color.White,
            Image = Ph.ToBitmap(Ph.Gear, 13F, Color.White),
            TextImageRelation = TextImageRelation.ImageBeforeText,
        };
        var tsSep  = new ToolStripSeparator();
        var tsInfo = new ToolStripButton(L.T("toolbar.info")) {
            ForeColor = Color.White,
            Image = Ph.ToBitmap(Ph.Info, 13F, Color.White),
            TextImageRelation = TextImageRelation.ImageBeforeText,
        };
        Color colOrange = Color.FromArgb(255, 140, 0);
        Color colGreen  = Color.FromArgb(0, 170, 80);
        Color colDim    = Color.FromArgb(80, 80, 85);
        static Bitmap FilterIcon(string glyph, Color color) => Ph.ToBitmap(glyph, 14F, color, bold: true);

        bool showOrange = true, showGreen = true;

        Action applyFilter = null;

        var tsFilterOrange = new ToolStripButton {
            Image       = FilterIcon(Ph.Warning,     colOrange),
            ToolTipText = "Zeilen mit neueren Dateien anzeigen/verbergen",
            Checked     = true,
        };
        var tsFilterGreen = new ToolStripButton {
            Image       = FilterIcon(Ph.CheckCircle, colGreen),
            ToolTipText = "Aktuelle Zeilen anzeigen/verbergen",
            Checked     = true,
        };

        toolStrip.Items.Add(tsSettings);
        toolStrip.Items.Add(tsSep);
        toolStrip.Items.Add(tsInfo);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(tsFilterOrange);
        toolStrip.Items.Add(tsFilterGreen);

        tsSettings.Click += (s, e) => ShowSettingsDialog(form, settings, jsonPath);

        tsInfo.Click += (s, e) => ShowAboutDialog(form, appVersion);

        form.Controls.Add(mainPanel);
        form.Controls.Add(bottomPanel);
        form.Controls.Add(statusPanel);
        form.Controls.Add(toolStrip);

        updateSelection = () => {
            for (int i = 0; i < uiEntries.Count; i++) {
                uiEntries[i].SelectionPanel.BackColor = (i == selectedIndex) ? Color.FromArgb(60, 60, 70) : Color.Transparent;
                if (i == selectedIndex) {
                    uiEntries[i].VersionBox.Focus();
                    var lbls = uiEntries[i].Scheme.GetButtonLabels();
                    var schemeNames = lbls.Where(l => l != null).Select((l, idx) => L.T("status.shortcut", idx + 1, l));
                    string backupStatus = uiEntries[i].Backup ? L.T("status.backup_on") : L.T("status.backup_off");
                    setStatus($"{L.T("status.scheme", uiEntries[i].Entry.Scheme)}   {string.Join("  ", schemeNames)}   {backupStatus}", false);
                }
            }
        };

        applyFilter = () => {
            for (int i = 0; i < uiEntries.Count; i++) {
                var ui = uiEntries[i];
                ui.SelectionPanel.Visible =
                    ui.HasIssues == null                        // noch am Scannen: immer zeigen
                    || (ui.HasIssues == true  && showOrange)
                    || (ui.HasIssues == false && showGreen);
            }
            // selectedIndex auf erste sichtbare Zeile korrigieren
            if (selectedIndex < uiEntries.Count && !uiEntries[selectedIndex].SelectionPanel.Visible) {
                int first = uiEntries.FindIndex(u => u.SelectionPanel.Visible);
                if (first >= 0) { selectedIndex = first; updateSelection(); }
            }
        };

        tsFilterOrange.Click += (s, e) => {
            showOrange = !showOrange;
            tsFilterOrange.Image = FilterIcon(Ph.Warning, showOrange ? colOrange : colDim);
            applyFilter();
        };
        tsFilterGreen.Click += (s, e) => {
            showGreen = !showGreen;
            tsFilterGreen.Image = FilterIcon(Ph.CheckCircle, showGreen ? colGreen : colDim);
            applyFilter();
        };

        statusTimer.Tick += (s, e) => {
            statusTimer.Stop();
            updateSelection(); // Normalzustand wiederherstellen
        };

        form.KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; btnCan.PerformClick(); return; }
            if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; btnOk.PerformClick(); return; }

            if (e.Control && e.KeyCode == Keys.Home) {
                selectedIndex = 0; updateSelection(); e.Handled = true; return;
            }
            if (e.Control && e.KeyCode == Keys.End) {
                selectedIndex = uiEntries.Count - 1; updateSelection(); e.Handled = true; return;
            }
            if (e.KeyCode == Keys.Up && selectedIndex > 0) {
                selectedIndex--; updateSelection(); e.Handled = true;
            } else if (e.KeyCode == Keys.Down && selectedIndex < uiEntries.Count - 1) {
                selectedIndex++; updateSelection(); e.Handled = true;
            }

            if (e.Alt && !e.Control && uiEntries.Count > 0) {
                int idx = e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z ? e.KeyCode - Keys.A
                        : e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9 ? 26 + (e.KeyCode - Keys.D0)
                        : -1;
                if (idx >= 0 && idx < uiEntries.Count) {
                    selectedIndex = idx; updateSelection();
                    e.Handled = true; e.SuppressKeyPress = true;
                }
            }

            if (e.Control && uiEntries.Count > 0) {
                int part = (e.KeyCode == Keys.D1) ? 0 : (e.KeyCode == Keys.D2 ? 1 : (e.KeyCode == Keys.D3 ? 2 : (e.KeyCode == Keys.D4 ? 3 : -1)));
                if (part != -1) {
                    var ui = uiEntries[selectedIndex];
                    var labels = ui.Scheme.GetButtonLabels();
                    if (part < labels.Count && labels[part] != null) {
                        ui.VersionBox.Text = ui.Scheme.Bump(ui.VersionBox.Text, part);
                        ui.VersionBox.BackColor = Color.DarkGreen;
                        e.Handled = true;
                    } else {
                        setStatus(L.T("error.bump_unavailable", part + 1, settings.Paths[selectedIndex].Scheme), true);
                        e.Handled = true;
                    }
                }
            }
        };

        form.Shown += (s, e) => {
            SetForegroundWindow(form.Handle);
            form.TopMost = true;
            form.TopMost = false;
            updateSelection();

            int total = uiEntries.Count;
            int done  = 0;
            foreach (var ui in uiEntries) {
                var uiCaptured = ui;
                Task.Run(() => {
                    string projectName = Path.GetFileNameWithoutExtension(
                        Path.GetDirectoryName(uiCaptured.FilePath));
                    form.BeginInvoke(new Action(() =>
                        statusLabel.Text = $"⟳ {projectName} …"));

                    var ignoreDirs  = BuildEffectiveIgnoreDirs(settings, uiCaptured.Entry);
                    var ignoreFiles = BuildEffectiveIgnoreFiles(settings, uiCaptured.Entry);
                    var newerFiles  = GetNewerFiles(uiCaptured.FilePath, 30, ignoreDirs, ignoreFiles);

                    form.BeginInvoke(new Action(() => {
                        if (newerFiles.Count > 0) {
                            uiCaptured.HasIssues = true;
                            uiCaptured.StatusStrip.BackColor = Color.FromArgb(255, 140, 0);
                            uiCaptured.InfoIcon.Visible = true;
                            string tip = $"{newerFiles.Count} Datei(en) neuer als VERSION:\n" +
                                         string.Join("\n", newerFiles);
                            toolTip.SetToolTip(uiCaptured.InfoIcon, tip);
                        } else {
                            uiCaptured.HasIssues = false;
                            uiCaptured.StatusStrip.BackColor = Color.FromArgb(0, 170, 80);
                        }
                        done++;
                        applyFilter();
                        if (done == total) updateSelection(); // Statuszeile wiederherstellen
                    }));
                });
            }
        };

        if (form.ShowDialog() == DialogResult.OK) {
            foreach (var ui in uiEntries) {
                if (ui.VersionBox.Text != ui.OriginalVersion) {
                    try {
                        if (ui.Backup) File.Copy(ui.FilePath, ui.FilePath + ".bak", true);
                        File.WriteAllText(ui.FilePath, ui.VersionBox.Text);
                    } catch (Exception ex) { MessageBox.Show(L.T("error.save", ex.Message)); }
                }
            }
        }

        statusTimer.Dispose();
    }

    static void ShowSettingsDialog(Form owner, Settings settings, string jsonPath) {
        var entries = settings.Paths.Select(e => new ProjectEntry {
            Path = e.Path, Name = e.Name, Icon = e.Icon, Scheme = e.Scheme,
            Format = e.Format, ResetOnBump = e.ResetOnBump, Backup = e.Backup,
            IgnoreDirs  = new List<string>(e.IgnoreDirs  ?? []),
            IgnoreFiles = new List<string>(e.IgnoreFiles ?? []),
        }).ToList();
        var globalIgnoreDirs  = new List<string>(settings.IgnoreDirs  ?? []);
        var globalIgnoreFiles = new List<string>(settings.IgnoreFiles ?? []);

        Color bgDark  = Color.FromArgb(30, 30, 30);
        Color bgMid   = Color.FromArgb(45, 45, 48);
        Color bgLight = Color.FromArgb(60, 60, 60);
        Color bgBtn   = Color.FromArgb(70, 70, 70);
        Color fgW     = Color.White;
        Font  fUI     = new Font("Segoe UI", 9F);
        Font  fMono   = new Font("Consolas", 9F);

        using var dlg = new Form {
            Text = L.T("settings.title"), Width = 620, Height = 660,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = bgMid, ForeColor = fgW,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
        };

        static string ListToText(List<string> list) => string.Join("\r\n", list ?? []);
        static List<string> TextToList(string text) =>
            (text ?? "").Split('\n').Select(s => s.Trim('\r', ' ')).Where(s => s.Length > 0).ToList();

        // ── Global ignore ──────────────────────────────────────────────────────
        var grpGlobal = new GroupBox {
            Parent = dlg, Text = "Global ignore  (one entry per line)", Left = 12, Top = 8, Width = 576, Height = 112,
            Font = fUI, ForeColor = Color.LightGray,
        };
        new Label { Parent = grpGlobal, Text = "Dirs:",  Left = 8, Top = 22, Width = 82, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var tbGlobalIgnoreDirs  = new TextBox { Parent = grpGlobal, Left = 96, Top = 20, Width = 468, Height = 36, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = ListToText(globalIgnoreDirs) };
        new Label { Parent = grpGlobal, Text = "Files:", Left = 8, Top = 68, Width = 82, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var tbGlobalIgnoreFiles = new TextBox { Parent = grpGlobal, Left = 96, Top = 66, Width = 468, Height = 36, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = ListToText(globalIgnoreFiles) };

        // ── Project list ──────────────────────────────────────────────────────
        new Label { Parent = dlg, Text = "Projects", Left = 12, Top = 130, Width = 100, Height = 18,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = fgW };

        Button SmallBtn(string t, int x) => new Button {
            Parent = dlg, Text = t, Left = x, Top = 124, Width = 32, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW,
            Font = new Font("Segoe UI", 10F) };
        var btnAdd    = SmallBtn("+",  452);
        var btnRemove = SmallBtn("−",  488);
        var btnUp     = SmallBtn("↑",  524);
        var btnDown   = SmallBtn("↓",  558);

        var lstEntries = new ListBox {
            Parent = dlg, Left = 12, Top = 152, Width = 576, Height = 90,
            BackColor = bgDark, ForeColor = fgW, Font = fUI,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // ── Detail group ──────────────────────────────────────────────────────
        var grp = new GroupBox {
            Parent = dlg, Text = "Entry", Left = 12, Top = 250, Width = 576, Height = 314,
            Font = fUI, ForeColor = Color.LightGray,
        };

        Label   GrpLbl(string t, int y) => new Label   { Parent = grp, Text = t, Left = 8, Top = y + 3, Width = 82, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        TextBox GrpTb (int y, int w)    => new TextBox  { Parent = grp, Left = 96, Top = y, Width = w, Height = 23, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono };
        Button  BrowseBtn(int y)        => new Button   { Parent = grp, Text = "…", Left = 472, Top = y, Width = 90, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW, Font = fUI };

        GrpLbl("Path:",    22); var tbPath   = GrpTb(22, 368); var btnBrowsePath = BrowseBtn(22);
        var lblStatus = new Label { Parent = grp, Left = 96, Top = 50, Width = 460, Height = 18, Font = new Font("Segoe UI", 8.5F) };
        GrpLbl("Name:",    76); var tbName   = GrpTb(76, 460);
        GrpLbl("Icon:",   108); var tbIcon   = GrpTb(108, 368); var btnBrowseIcon = BrowseBtn(108);
        GrpLbl("Scheme:", 138);
        var cbScheme = new ComboBox { Parent = grp, Left = 96, Top = 138, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = bgLight, ForeColor = fgW, FlatStyle = FlatStyle.Flat, Font = fUI };
        cbScheme.Items.AddRange(new object[] { "semver", "calver", "sequential" });
        var cbReset  = new CheckBox { Parent = grp, Text = "Reset on bump", Left = 230, Top = 140, Width = 130, Font = fUI, ForeColor = fgW };
        var cbBackup = new CheckBox { Parent = grp, Text = "Backup",        Left = 370, Top = 140, Width = 90,  Font = fUI, ForeColor = fgW };
        var lblFormat = GrpLbl("Format:", 170); var tbFormat = GrpTb(170, 460);

        // ── Per-project ignore ─────────────────────────────────────────────────
        new Label {
            Parent = grp, Left = 8, Top = 202, Width = 556, Height = 14,
            Text = "Per-project ignore  (prefix ! to re-include a global entry)",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 140, 145),
        };
        var tipIgnore = new ToolTip();
        tipIgnore.SetToolTip(GrpLbl("Dirs:",  218), "One entry per line.\nPrefix ! to re-include a global entry, e.g. !bin");
        var tbIgnoreDirs  = new TextBox { Parent = grp, Left = 96, Top = 216, Width = 460, Height = 38, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical };
        tipIgnore.SetToolTip(tbIgnoreDirs, "One entry per line.\nPrefix ! to re-include a global entry, e.g. !bin");
        tipIgnore.SetToolTip(GrpLbl("Files:", 262), "One pattern per line (e.g. *.bak).\nPrefix ! to re-include a global pattern.");
        var tbIgnoreFiles = new TextBox { Parent = grp, Left = 96, Top = 260, Width = 460, Height = 38, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical };
        tipIgnore.SetToolTip(tbIgnoreFiles, "One pattern per line (e.g. *.bak).\nPrefix ! to re-include a global pattern.");

        // ── Save / Cancel ─────────────────────────────────────────────────────
        var btnSave   = new Button { Parent = dlg, Text = L.T("btn.save"),   Left = 396, Top = 574, Width = 100, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = fgW, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        var btnCancel = new Button { Parent = dlg, Text = L.T("btn.cancel"), Left = 504, Top = 574, Width = 100, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW, Font = fUI, DialogResult = DialogResult.Cancel };
        dlg.CancelButton = btnCancel;

        // ── Logic ─────────────────────────────────────────────────────────────
        int  current = -1;
        bool loading = false;

        string DisplayName(ProjectEntry e) {
            if (!string.IsNullOrWhiteSpace(e.Name)) return e.Name;
            string n = Path.GetFileName(e.Path.Trim().TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(n) ? "(new)" : n;
        }

        void UpdateListButtons() {
            btnRemove.Enabled = entries.Count > 0;
            btnUp.Enabled     = current > 0;
            btnDown.Enabled   = current >= 0 && current < entries.Count - 1;
        }

        void RefreshList(int select = 0) {
            loading = true;
            lstEntries.Items.Clear();
            foreach (var e in entries) lstEntries.Items.Add(DisplayName(e));
            current = entries.Count > 0 ? Math.Max(0, Math.Min(select, entries.Count - 1)) : -1;
            if (current >= 0) lstEntries.SelectedIndex = current;
            loading = false;
            grp.Enabled = entries.Count > 0;
            UpdateListButtons();
            LoadEntry(current);
        }

        void ValidatePath() {
            string clean = tbPath.Text.Trim().TrimEnd('\\', '/');
            if (string.IsNullOrWhiteSpace(clean)) { lblStatus.Text = ""; return; }
            if (!Directory.Exists(clean)) {
                lblStatus.ForeColor = Color.Salmon;
                lblStatus.Text = "⚠  Directory not found"; return;
            }
            if (!File.Exists(Path.Combine(clean, "VERSION"))) {
                lblStatus.ForeColor = Color.FromArgb(255, 180, 0);
                lblStatus.Text = "⚠  No VERSION file in this directory"; return;
            }
            lblStatus.ForeColor = Color.FromArgb(80, 200, 80);
            lblStatus.Text = "✓  OK";
        }

        void LoadEntry(int idx) {
            if (idx < 0 || idx >= entries.Count) { grp.Enabled = false; return; }
            grp.Enabled = true;
            loading = true;
            var e = entries[idx];
            tbPath.Text   = e.Path;
            tbName.Text   = e.Name;
            tbIcon.Text   = e.Icon;
            cbScheme.SelectedItem = e.Scheme;
            if (cbScheme.SelectedIndex < 0) cbScheme.SelectedIndex = 0;
            tbFormat.Text    = e.Format;
            cbReset.Checked  = e.ResetOnBump;
            cbBackup.Checked = e.Backup;
            tbIgnoreDirs.Text  = ListToText(e.IgnoreDirs);
            tbIgnoreFiles.Text = ListToText(e.IgnoreFiles);
            bool showFmt = e.Scheme.ToLower() != "semver";
            lblFormat.Visible = tbFormat.Visible = showFmt;
            ValidatePath();
            loading = false;
        }

        void Flush() {
            if (loading || current < 0 || current >= entries.Count) return;
            var e = entries[current];
            e.Path        = tbPath.Text;
            e.Name        = tbName.Text;
            e.Icon        = tbIcon.Text;
            e.Scheme      = cbScheme.SelectedItem?.ToString() ?? "semver";
            e.Format      = tbFormat.Text;
            e.ResetOnBump = cbReset.Checked;
            e.Backup      = cbBackup.Checked;
            e.IgnoreDirs  = TextToList(tbIgnoreDirs.Text);
            e.IgnoreFiles = TextToList(tbIgnoreFiles.Text);
            if (current < lstEntries.Items.Count) {
                loading = true;
                lstEntries.Items[current] = DisplayName(e);
                loading = false;
            }
        }

        lstEntries.SelectedIndexChanged += (s, e) => {
            if (loading) return;
            Flush();
            current = lstEntries.SelectedIndex;
            LoadEntry(current);
            UpdateListButtons();
        };
        // DoubleClick: Pfad-Feld fokussieren (verhindert auch Click-Through zu Buttons)

        lstEntries.DoubleClick += (s, e) => tbPath.Focus();

        tbPath.Leave        += (s, e) => { Flush(); ValidatePath(); };
        tbName.Leave        += (s, e) => Flush();
        tbIcon.Leave        += (s, e) => Flush();
        tbFormat.Leave      += (s, e) => Flush();
        tbIgnoreDirs.Leave  += (s, e) => Flush();
        tbIgnoreFiles.Leave += (s, e) => Flush();
        cbReset.CheckedChanged  += (s, e) => { if (!loading) Flush(); };
        cbBackup.CheckedChanged += (s, e) => { if (!loading) Flush(); };
        cbScheme.SelectedIndexChanged += (s, e) => {
            if (loading) return;
            bool showFmt = cbScheme.SelectedItem?.ToString().ToLower() != "semver";
            lblFormat.Visible = tbFormat.Visible = showFmt;
            Flush();
        };

        btnBrowsePath.Click += (s, e) => {
            using var fbd = new FolderBrowserDialog { Description = "Select project folder", UseDescriptionForTitle = true };
            if (!string.IsNullOrWhiteSpace(tbPath.Text))
                try { fbd.SelectedPath = tbPath.Text.Trim(); } catch (Exception ex) { Log.Write("Settings/browsePath", ex); }
            if (fbd.ShowDialog(dlg) == DialogResult.OK) {
                tbPath.Text = fbd.SelectedPath; Flush(); ValidatePath();
            }
        };

        btnBrowseIcon.Click += (s, e) => {
            using var ofd = new System.Windows.Forms.OpenFileDialog { Title = "Select icon", Filter = "Icons & Images|*.ico;*.png;*.jpg;*.bmp|All files|*.*" };
            if (!string.IsNullOrWhiteSpace(tbIcon.Text))
                try { ofd.InitialDirectory = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(tbIcon.Text)); } catch (Exception ex) { Log.Write("Settings/browseIcon", ex); }
            if (ofd.ShowDialog(dlg) == DialogResult.OK) { tbIcon.Text = ofd.FileName; Flush(); }
        };

        btnAdd.Click += (s, e) => {
            Flush();
            entries.Add(new ProjectEntry { Scheme = "semver", ResetOnBump = true });
            RefreshList(entries.Count - 1);
        };

        btnRemove.Click += (s, e) => {
            if (current < 0) return;
            entries.RemoveAt(current);
            RefreshList(Math.Max(0, current - 1));
        };

        btnUp.Click += (s, e) => {
            if (current <= 0) return;
            Flush();
            (entries[current], entries[current - 1]) = (entries[current - 1], entries[current]);
            RefreshList(current - 1);
        };

        btnDown.Click += (s, e) => {
            if (current < 0 || current >= entries.Count - 1) return;
            Flush();
            (entries[current], entries[current + 1]) = (entries[current + 1], entries[current]);
            RefreshList(current + 1);
        };

        btnSave.Click += (s, e) => {
            Flush();
            var problems = entries
                .Where(en => {
                    string c = en.Path.Trim().TrimEnd('\\', '/');
                    return !Directory.Exists(c) || !File.Exists(Path.Combine(c, "VERSION"));
                })
                .Select(en => {
                    string c = en.Path.Trim().TrimEnd('\\', '/');
                    string reason = !Directory.Exists(c) ? "directory not found" : "no VERSION file";
                    return $"  • {DisplayName(en)}: {reason}";
                })
                .ToList();

            if (problems.Count > 0) {
                var r = MessageBox.Show(
                    "The following entries have issues:\n\n" + string.Join("\n", problems) + "\n\nSave anyway?",
                    "Validation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.No) return;
            }

            settings.Paths       = entries;
            settings.IgnoreDirs  = TextToList(tbGlobalIgnoreDirs.Text);
            settings.IgnoreFiles = TextToList(tbGlobalIgnoreFiles.Text);
            try {
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true }));
            } catch (Exception ex) {
                Log.Write("Settings/save", ex);
                MessageBox.Show(L.T("error.save", ex.Message));
                return;
            }
            dlg.DialogResult = DialogResult.OK;
        };

        RefreshList(0);
        LoadEntry(0);
        new Label {
            Parent = dlg, Left = 12, Top = 618, Width = 592, Height = 16,
            Text = jsonPath,
            Font = new Font("Segoe UI", 7.5F),
            ForeColor = Color.FromArgb(110, 110, 115),
            AutoEllipsis = true,
        };

        if (dlg.ShowDialog(owner?.Visible == true ? owner : null) == DialogResult.OK) {
            if (owner?.Visible == true) owner.Close();
            Run();
        }
    }

    static void ShowAboutDialog(Form owner, string version) {
        Color bgDark  = Color.FromArgb(30, 30, 30);
        Color bgMid   = Color.FromArgb(45, 45, 48);
        Color fgW     = Color.White;
        Color fgDim   = Color.FromArgb(160, 160, 165);
        Color accent  = Color.FromArgb(72, 199, 142);
        Font  fUI     = new Font("Segoe UI", 9F);

        using var dlg = new Form {
            Text            = L.T("info.title"),
            Width           = 360, Height = 320,
            StartPosition   = FormStartPosition.CenterParent,
            BackColor       = bgMid, ForeColor = fgW,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox     = false, MinimizeBox = false,
            Icon            = owner.Icon,
        };

        // ── Icon ───────────────────────────────────────────────────────────────
        var pic = new PictureBox {
            Parent   = dlg, Left = 24, Top = 20, Width = 64, Height = 64,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        try {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "verbump.ico");
            if (File.Exists(icoPath)) pic.Image = new Icon(icoPath, 64, 64).ToBitmap();
        } catch { /* ignore */ }

        // ── Title + version ────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Text = "VerBump", Left = 104, Top = 22,
            Width = 220, Height = 32,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = fgW,
            AutoSize = false,
        };
        new Label {
            Parent = dlg, Text = $"Version {version}", Left = 106, Top = 56,
            Width = 200, Height = 18, Font = fUI, ForeColor = accent,
        };
        new Label {
            Parent = dlg, Text = $"Language: {L.Lang.ToUpper()}", Left = 106, Top = 74,
            Width = 200, Height = 18, Font = fUI, ForeColor = fgDim,
        };

        // ── Separator ──────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Left = 24, Top = 100, Width = 294, Height = 1,
            BackColor = Color.FromArgb(70, 70, 75),
        };

        // ── Description ────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Text = "Version file manager for Windows developers.\nFree & open source — MIT License.",
            Left = 24, Top = 112, Width = 294, Height = 40,
            Font = fUI, ForeColor = fgDim, AutoSize = false,
        };

        // ── Links ──────────────────────────────────────────────────────────────
        static LinkLabel MakeLink(Form f, string text, string url, int top, Color col) {
            var lnk = new LinkLabel {
                Parent = f, Text = text, Left = 24, Top = top, Width = 294, Height = 20,
                Font = new Font("Segoe UI", 9F), BackColor = Color.Transparent,
                LinkColor = col, ActiveLinkColor = Color.White,
            };
            lnk.Click += (s, e) => {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            return lnk;
        }

        MakeLink(dlg, "🌐  mbaas2.github.io/VerBump",         "https://mbaas2.github.io/VerBump/",        162, accent);
        MakeLink(dlg, "❤  Sponsor this project on GitHub",   "https://github.com/sponsors/mbaas2",        184, Color.FromArgb(255, 120, 150));
        MakeLink(dlg, "⌥  View source on GitHub",            "https://github.com/mbaas2/VerBump",         206, Color.FromArgb(130, 180, 255));

        // ── Separator ──────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Left = 24, Top = 234, Width = 294, Height = 1,
            BackColor = Color.FromArgb(70, 70, 75),
        };

        // ── Copyright ─────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Text = $"© {DateTime.Today.Year} Michael Baas",
            Left = 24, Top = 244, Width = 200, Height = 18,
            Font = new Font("Segoe UI", 8F), ForeColor = fgDim,
        };

        // ── Close button ──────────────────────────────────────────────────────
        var btnClose = new Button {
            Parent = dlg, Text = "OK", Left = 244, Top = 238, Width = 74, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 65),
            ForeColor = fgW, Font = fUI, DialogResult = DialogResult.OK,
        };
        dlg.AcceptButton = btnClose;

        dlg.ShowDialog(owner?.Visible == true ? owner : null);
    }

    static bool OpenWithEditor(string editor, string filePath) {
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = Environment.ExpandEnvironmentVariables(editor),
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false,
            });
            return true;
        } catch (Exception ex) {
            MessageBox.Show(L.T("editor.error", ex.Message));
            return false;
        }
    }

    static readonly string[] DefaultIgnoreDirs =
        [".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__", "dist", "build"];

    static readonly string[] DefaultIgnoreFiles =
        ["*.bak", "*.tmp", "*.log", "Thumbs.db", ".DS_Store"];

    static HashSet<string> BuildEffectiveIgnoreDirs(Settings settings, ProjectEntry entry) {
        var source = settings.IgnoreDirs?.Count > 0
            ? settings.IgnoreDirs
            : (IEnumerable<string>)DefaultIgnoreDirs;
        var effective = new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
        foreach (var d in entry.IgnoreDirs ?? Enumerable.Empty<string>()) {
            if (d.StartsWith('!')) effective.Remove(d[1..]);
            else effective.Add(d);
        }
        return effective;
    }

    static List<string> BuildEffectiveIgnoreFiles(Settings settings, ProjectEntry entry) {
        var source = settings.IgnoreFiles?.Count > 0
            ? settings.IgnoreFiles
            : (IEnumerable<string>)DefaultIgnoreFiles;
        var effective = new List<string>(source);
        foreach (var p in entry.IgnoreFiles ?? Enumerable.Empty<string>()) {
            if (p.StartsWith('!')) effective.Remove(p[1..]);
            else if (!effective.Contains(p)) effective.Add(p);
        }
        return effective;
    }

    static List<string> GetNewerFiles(string versionFilePath, int max, HashSet<string> ignoreDirs, List<string> ignoreFiles) {
        var result = new List<string>();
        try {
            DateTime versionTime = File.GetLastWriteTimeUtc(versionFilePath);
            string projectDir    = Path.GetDirectoryName(versionFilePath);
            var gitignorePatterns = LoadGitignorePatterns(projectDir);
            CollectNewerFiles(projectDir, projectDir, versionTime, versionFilePath,
                              max, result, gitignorePatterns, ignoreDirs, ignoreFiles);
        } catch (Exception ex) { Log.Write("GetNewerFiles", ex); }
        return result;
    }

    static List<string> LoadGitignorePatterns(string projectDir) {
        var patterns = new List<string>();
        string gitignore = Path.Combine(projectDir, ".gitignore");
        if (!File.Exists(gitignore)) return patterns;
        try {
            foreach (string raw in File.ReadAllLines(gitignore)) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("!"))
                    continue;
                patterns.Add(line.TrimEnd('/'));   // "folder/" → "folder"
            }
        } catch (Exception ex) { Log.Write("LoadGitignorePatterns", ex); }
        return patterns;
    }

    static bool IsGitignored(string name, List<string> patterns) {
        foreach (string pattern in patterns) {
            if (pattern.Contains('*')) {
                // einfaches Glob: nur *.ext oder prefix*
                string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                                         .Replace("\\*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(name, regex,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            } else {
                if (string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    static void CollectNewerFiles(string rootDir, string dir, DateTime threshold,
                                  string skipFile, int max, List<string> result,
                                  List<string> gitignorePatterns, HashSet<string> ignoreDirs, List<string> ignoreFiles) {
        if (result.Count >= max) return;
        try {
            foreach (string file in Directory.EnumerateFiles(dir)) {
                if (result.Count >= max) return;
                string name = Path.GetFileName(file);
                if (file == skipFile) continue;
                if (IsGitignored(name, gitignorePatterns) || IsGitignored(name, ignoreFiles)) continue;
                if (File.GetLastWriteTimeUtc(file) > threshold)
                    result.Add(file.Substring(rootDir.Length).TrimStart('\\', '/'));
            }
            foreach (string sub in Directory.EnumerateDirectories(dir)) {
                if (result.Count >= max) return;
                string name = Path.GetFileName(sub);
                if (ignoreDirs.Contains(name) || IsGitignored(name, gitignorePatterns))
                    continue;
                CollectNewerFiles(rootDir, sub, threshold, skipFile, max, result, gitignorePatterns, ignoreDirs, ignoreFiles);
            }
        } catch (Exception ex) { Log.Write($"CollectNewerFiles/{dir}", ex); }
    }
}