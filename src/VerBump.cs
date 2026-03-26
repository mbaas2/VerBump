using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

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
    [JsonPropertyName("lists")]       public Dictionary<string, string[]> Lists { get; set; } = new();
}

public class FavoriteEntry {
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("path")]  public string Path  { get; set; } = "";
}

public class AppConfig {
    [JsonPropertyName("historyMaxLength")]  public int                  HistoryMaxLength  { get; set; } = 6;
    [JsonPropertyName("recentSettings")]   public List<string>          RecentSettings   { get; set; } = [];
    [JsonPropertyName("recentVersions")]   public List<string>          RecentVersions   { get; set; } = [];
    [JsonPropertyName("favoriteSettings")] public List<FavoriteEntry>   FavoriteSettings { get; set; } = [];
    [JsonPropertyName("favoriteVersions")] public List<FavoriteEntry>   FavoriteVersions { get; set; } = [];
}

public class VerBumpPolicy {
    [JsonPropertyName("allowHookBypass")] public bool AllowHookBypass { get; set; } = true;
}

// ── Version schemes ───────────────────────────────────────────────────────────

public record TokenHint(string[] Values, bool IsList, string Label, int Start, int Length);

public interface IVersionScheme {
    List<string> GetButtonLabels();
    string Bump(string current, int partIndex);
    string Refresh(string current) => current;
    bool HasDateTokens => false;
    bool Matches(string version) => true;
    TokenHint GetTokenAt(string version, int cursorPos) => null;
    // Map the numeric components of sourceVersion onto this scheme's numeric tokens;
    // list/date tokens keep their current values / auto-fill as usual.
    string SyncFrom(string sourceVersion, string currentVersion) => sourceVersion;
}

// ── Version format tokens ─────────────────────────────────────────────────────

public abstract record FormatToken;
public record LiteralToken(string Text)            : FormatToken;
public record DateToken(string Spec)               : FormatToken;   // {YYYY} {YY} {Y} {MM} {DD}
public record NumericToken(string Name)            : FormatToken;   // {#name}
public record InlineListToken(string[] Values)     : FormatToken;   // {a|b|c}
public record NamedListToken(string ListName)      : FormatToken;   // {listname}
public record FreeTextToken(int MaxLen)            : FormatToken;   // [*N]
public record ResetGroupToken(FormatToken[] Inner) : FormatToken;   // [{#a}.{#b}.{#c}]

// ── Format parser ─────────────────────────────────────────────────────────────

public static class FormatParser {
    static readonly HashSet<string> DateSpecs =
        new(StringComparer.OrdinalIgnoreCase) { "YYYY", "YYY", "YY", "Y", "MM", "DD", "YYYYMMDD", "YYYYMM" };

    public static FormatToken[] Parse(string fmt) {
        if (string.IsNullOrWhiteSpace(fmt))
            return [new ResetGroupToken([
                new NumericToken("major"), new LiteralToken("."),
                new NumericToken("minor"), new LiteralToken("."),
                new NumericToken("patch")])];
        var result = new List<FormatToken>();
        int i = 0;
        while (i < fmt.Length) {
            if (fmt[i] == '[') {
                int close = FindMatching(fmt, i, '[', ']');
                string inner = fmt[(i + 1)..close];
                if (inner.Equals("sem", StringComparison.OrdinalIgnoreCase))
                    result.Add(new ResetGroupToken([
                        new NumericToken("major"), new LiteralToken("."),
                        new NumericToken("minor"), new LiteralToken("."),
                        new NumericToken("patch")]));
                else if (inner.StartsWith('*') && int.TryParse(inner[1..], out int ml))
                    result.Add(new FreeTextToken(ml));
                else
                    result.Add(new ResetGroupToken(ParseSimple(inner)));
                i = close + 1;
            } else if (fmt[i] == '{') {
                int close = fmt.IndexOf('}', i);
                if (close < 0) { result.Add(new LiteralToken(fmt[i..])); break; }
                result.Add(ParseBrace(fmt[(i + 1)..close]));
                i = close + 1;
            } else {
                int j = i;
                while (j < fmt.Length && fmt[j] != '[' && fmt[j] != '{') j++;
                if (j > i) result.Add(new LiteralToken(fmt[i..j]));
                i = j;
            }
        }
        return [.. result];
    }

    static FormatToken[] ParseSimple(string fmt) {
        // Bare [a|b|c] shorthand — no braces but pipe-separated → treat as InlineListToken
        if (!fmt.Contains('{') && fmt.Contains('|'))
            return [new InlineListToken(fmt.Split('|'))];
        var result = new List<FormatToken>();
        int i = 0;
        while (i < fmt.Length) {
            if (fmt[i] == '{') {
                int close = fmt.IndexOf('}', i);
                if (close < 0) { result.Add(new LiteralToken(fmt[i..])); break; }
                result.Add(ParseBrace(fmt[(i + 1)..close]));
                i = close + 1;
            } else {
                int j = i;
                while (j < fmt.Length && fmt[j] != '{') j++;
                if (j > i) result.Add(new LiteralToken(fmt[i..j]));
                i = j;
            }
        }
        return [.. result];
    }

    static FormatToken ParseBrace(string inner) {
        if (DateSpecs.Contains(inner))  return new DateToken(inner.ToUpper());
        if (inner.StartsWith('#'))      return new NumericToken(inner[1..]);
        if (inner.Contains('|'))        return new InlineListToken(inner.Split('|'));
        return new NamedListToken(inner);
    }

    static int FindMatching(string s, int open, char openCh, char closeCh) {
        int depth = 0;
        for (int i = open; i < s.Length; i++) {
            if (s[i] == openCh) depth++;
            else if (s[i] == closeCh && --depth == 0) return i;
        }
        return s.Length - 1;
    }
}

// ── Format scheme ─────────────────────────────────────────────────────────────

public class FormatScheme : IVersionScheme {
    record TokenCtx(FormatToken Token, int ActionIdx, int? GroupId, int GroupPos);

    readonly FormatToken[] _tokens;
    readonly List<TokenCtx> _interactive;
    readonly List<(FormatToken Token, int ActionIdx)> _parsePlan;
    readonly Dictionary<string, string[]> _lists;

    public bool HasDateTokens { get; }

    string[] Resolve(NamedListToken n) =>
        _lists.TryGetValue(n.ListName, out var v) && v.Length > 0 ? v : new[] { "?" };

    public FormatScheme(string format, Dictionary<string, string[]> lists = null) {
        _lists       = lists != null
            ? new Dictionary<string, string[]>(lists, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        _tokens      = FormatParser.Parse(format);
        _interactive = [];
        _parsePlan   = [];
        int actionIdx = 0, groupIdCtr = 0;

        void Walk(FormatToken t, int? gId, ref int gPos) {
            switch (t) {
                case ResetGroupToken rg: {
                    int thisGroup = groupIdCtr++;
                    int pos = 0;
                    foreach (var inner in rg.Inner) Walk(inner, thisGroup, ref pos);
                    break;
                }
                case LiteralToken lt: _parsePlan.Add((lt, -1)); break;
                case DateToken dt:    _parsePlan.Add((dt, -1)); break;
                default:
                    _interactive.Add(new(t, actionIdx, gId, gPos));
                    _parsePlan.Add((t, actionIdx));
                    actionIdx++;
                    if (gId.HasValue) gPos++;
                    break;
            }
        }
        int dummy = 0;
        foreach (var t in _tokens) Walk(t, null, ref dummy);
        HasDateTokens = _parsePlan.Any(p => p.Token is DateToken);
    }

    public List<string> GetButtonLabels() =>
        _interactive.Select(ctx => ctx.Token switch {
            NumericToken n     => n.Name,
            InlineListToken il => string.Join("|", il.Values),
            NamedListToken nl  => nl.ListName,           // show list name, not all values
            _                  => null
        }).ToList();

    public string Bump(string current, int actionIdx) {
        var values = ParseVersion(current);
        values[actionIdx] = BumpValue(_interactive[actionIdx].Token, values[actionIdx]);
        var ctx = _interactive[actionIdx];
        if (ctx.GroupId.HasValue) {
            for (int i = 0; i < _interactive.Count; i++) {
                var other = _interactive[i];
                if (other.GroupId == ctx.GroupId && other.GroupPos > ctx.GroupPos)
                    values[i] = DefaultValue(other.Token);
            }
        }
        return Render(values);
    }

    public string Refresh(string current) => Render(ParseVersion(current));

    public string SyncFrom(string sourceVersion, string currentVersion) {
        // Extract integer segments from source in order: "3.0.1" → ["3","0","1"]
        var srcNums = System.Text.RegularExpressions.Regex.Matches(
            sourceVersion.Trim(), @"\d+").Select(m => m.Value).ToArray();
        // Start from current parsed values so list-token selections are preserved
        var values = ParseVersion(currentVersion);
        // Map source integers onto numeric tokens in declaration order; extras → "0"
        int si = 0;
        for (int i = 0; i < _interactive.Count; i++) {
            if (_interactive[i].Token is NumericToken)
                values[i] = si < srcNums.Length ? srcNums[si++] : "0";
        }
        return Render(values);
    }

    string BuildPattern() {
        var pat = new System.Text.StringBuilder("^");
        foreach (var (tok, ai) in _parsePlan) {
            switch (tok) {
                case LiteralToken lt:
                    pat.Append(System.Text.RegularExpressions.Regex.Escape(lt.Text)); break;
                case DateToken dt:
                    int dlen = dt.Spec switch {
                        "YYYYMMDD" => 8, "YYYYMM" => 6,
                        "YYYY" => 4, "YYY" => 3, "MM" => 2, "DD" => 2, _ => 2 };
                    pat.Append($@"\d{{{dlen}}}"); break;
                case NumericToken:
                    pat.Append($@"(?<g{ai}>\d+)"); break;
                case InlineListToken il: {
                    var alts = string.Join("|", il.Values.Where(v => v.Length > 0).Select(System.Text.RegularExpressions.Regex.Escape));
                    pat.Append($"(?<g{ai}>{alts}|)"); break;   // always optional: existing files may lack a stage
                }
                case NamedListToken nl: {
                    var alts = string.Join("|", Resolve(nl).Where(v => v.Length > 0).Select(System.Text.RegularExpressions.Regex.Escape));
                    pat.Append($"(?<g{ai}>{alts}|)"); break;   // always optional: existing files may lack a stage
                }
                default:
                    pat.Append($@"(?<g{ai}>\S*)"); break;
            }
        }
        pat.Append('$');
        return pat.ToString();
    }

    public bool Matches(string version) {
        try { return System.Text.RegularExpressions.Regex.IsMatch(version.Trim(), BuildPattern(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        catch { return true; }
    }

    public TokenHint GetTokenAt(string version, int cursorPos) {
        try {
            var m = System.Text.RegularExpressions.Regex.Match(version, BuildPattern(),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            TokenHint MakeHint(FormatToken tok, System.Text.RegularExpressions.Group g) => tok switch {
                InlineListToken il => new TokenHint(il.Values.Where(v => v.Length > 0).ToArray(), true,
                    string.Join(" | ", il.Values.Where(v => v.Length > 0)), g.Index, g.Length),
                NamedListToken nl  => new TokenHint(Resolve(nl).Where(v => v.Length > 0).ToArray(), true,
                    $"{nl.ListName}: {string.Join(" | ", Resolve(nl).Where(v => v.Length > 0))}", g.Index, g.Length),
                NumericToken nt    => new TokenHint(null, false, nt.Name, g.Index, g.Length),
                _                  => new TokenHint(null, false, "", g.Index, g.Length),
            };
            // Two-pass: prefer list tokens (they may have empty match at token boundary)
            for (int i = 0; i < _interactive.Count; i++) {
                var g = m.Groups[$"g{i}"];
                if (!g.Success || cursorPos < g.Index || cursorPos > g.Index + g.Length) continue;
                if (_interactive[i].Token is InlineListToken or NamedListToken)
                    return MakeHint(_interactive[i].Token, g);
            }
            for (int i = 0; i < _interactive.Count; i++) {
                var g = m.Groups[$"g{i}"];
                if (!g.Success || cursorPos < g.Index || cursorPos > g.Index + g.Length) continue;
                return MakeHint(_interactive[i].Token, g);
            }
        } catch { }
        return null;
    }

    string[] ParseVersion(string version) {
        version ??= "";
        var values = _interactive.Select(ctx =>
            ctx.Token is InlineListToken or NamedListToken ? "" : DefaultValue(ctx.Token)).ToArray();
        try {
            var m = System.Text.RegularExpressions.Regex.Match(version, BuildPattern(),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) {
                for (int i = 0; i < _interactive.Count; i++) {
                    var g = m.Groups[$"g{i}"];
                    if (g.Success) values[i] = g.Value;
                }
                return values;
            }
        } catch { }

        var srcNums = System.Text.RegularExpressions.Regex.Matches(version, @"\d+")
            .Select(m => m.Value).ToArray();
        int numIdx = 0;
        for (int i = 0; i < _interactive.Count; i++) {
            switch (_interactive[i].Token) {
                case NumericToken:
                    if (numIdx < srcNums.Length) values[i] = srcNums[numIdx++];
                    break;
                case InlineListToken il:
                    values[i] = GuessListValue(il.Values, version, values[i]);
                    break;
                case NamedListToken nl:
                    values[i] = GuessListValue(Resolve(nl), version, values[i]);
                    break;
            }
        }
        return values;
    }

    static string GuessListValue(string[] values, string version, string fallback) {
        if (values == null || values.Length == 0) return fallback;
        string match = values
            .Where(v => !string.IsNullOrEmpty(v))
            .OrderByDescending(v => v.Length)
            .FirstOrDefault(v => version.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0);
        if (match != null) return match;
        return values.Any(v => v == "") ? "" : fallback;
    }


    string Render(string[] values) {
        var sb = new System.Text.StringBuilder();
        foreach (var (tok, ai) in _parsePlan) {
            switch (tok) {
                case LiteralToken lt: sb.Append(lt.Text); break;
                case DateToken dt:    sb.Append(DateValue(dt.Spec)); break;
                default: if (ai >= 0 && ai < values.Length) sb.Append(values[ai]); break;
            }
        }
        return sb.ToString();
    }

    // Cycle through non-empty list values.
    // If the list contains an empty entry (trailing comma = optional field):
    //   "" → first → … → last → "" → first → …
    // Otherwise (required field):
    //   first → … → last → first → …  (wraps; starting from "" goes to first)
    static string BumpListValue(string[] rawVals, string current) {
        var vals     = rawVals.Where(v => v.Length > 0).ToArray();
        bool hasEmpty = rawVals.Any(v => v.Length == 0);
        if (vals.Length == 0) return current;
        int idx = Array.IndexOf(vals, current);
        if (idx < 0) return vals[0];                                        // "" or unknown → first value
        int next = idx + 1;
        if (next < vals.Length) return vals[next];
        return hasEmpty ? "" : vals[0];                                     // last → "" (optional) or wrap (required)
    }

    string BumpValue(FormatToken t, string current) {
        if (t is NumericToken)
            return (int.TryParse(current, out int v) ? v + 1 : 1).ToString();
        if (t is InlineListToken il) return BumpListValue(il.Values, current);
        if (t is NamedListToken nl)  return BumpListValue(Resolve(nl), current);
        return current;
    }

    string DefaultValue(FormatToken t) => t switch {
        InlineListToken il when il.Values.Length > 0 => il.Values[0],
        NamedListToken n                             => Resolve(n)[0],
        _                                            => "0",
    };

    string DateValue(string spec) => spec switch {
        "YYYYMMDD" => DateTime.Today.ToString("yyyyMMdd"),
        "YYYYMM"   => DateTime.Today.ToString("yyyyMM"),
        "YYYY" => DateTime.Today.Year.ToString("D4"),
        "YYY"  => (DateTime.Today.Year % 1000).ToString("D3"),
        "YY"   => (DateTime.Today.Year % 100).ToString("D2"),
        "Y"    => (DateTime.Today.Year % 10).ToString(),
        "MM"   => DateTime.Today.Month.ToString("D2"),
        "DD"   => DateTime.Today.Day.ToString("D2"),
        _      => "0"
    };
}

// ── Scheme factory ────────────────────────────────────────────────────────────

public static class SchemeFactory {
    public static IVersionScheme Create(ProjectEntry entry, Dictionary<string, string[]> lists = null) =>
        new FormatScheme(FormatFor(entry), lists);

    public static string FormatFor(ProjectEntry entry) {
        // New-style format string already contains { or [
        if (!string.IsNullOrWhiteSpace(entry.Format) &&
            (entry.Format.Contains('{') || entry.Format.Contains('[')))
            return entry.Format;
        // Old dot-separated format (calver/sequential migration)
        if (!string.IsNullOrWhiteSpace(entry.Format))
            return MigrateOldFormat(entry.Format, entry.Scheme, entry.ResetOnBump);
        // Derive from scheme name
        return entry.Scheme?.ToLower() switch {
            "calver"     => "{YY}.{MM}.{#patch}",
            "sequential" => entry.ResetOnBump ? "[{#major}.{#minor}.{#patch}]"
                                              : "{#major}.{#minor}.{#patch}",
            _            => entry.ResetOnBump ? "[sem]"
                                              : "{#major}.{#minor}.{#patch}",
        };
    }

    static readonly HashSet<string> OldDateParts =
        new(StringComparer.OrdinalIgnoreCase) { "YYYY", "YYY", "YY", "Y", "MM", "DD" };

    static string MigrateOldFormat(string old, string scheme, bool reset) {
        var parts = old.Split('.');
        bool hasDate = parts.Any(p => OldDateParts.Contains(p));
        string converted = string.Join(".", parts.Select(p =>
            OldDateParts.Contains(p) ? $"{{{p.ToUpper()}}}" : $"{{#{p}}}"));
        return reset && !hasDate ? $"[{converted}]" : converted;
    }
}

// ── Localisation ──────────────────────────────────────────────────────────────

public static class L {
    static Dictionary<string, string> _d = new();
    public static string Lang { get; private set; } = "en";

    public static void Load(string baseDir, string forceLang = null) {
        string lang = forceLang ?? WindowsUILanguage();
        // 1. Datei-System (ermöglicht Custom-Übersetzungen neben der EXE)
        string FindFile(string l) {
            foreach (var dir in new[] { baseDir, Path.GetDirectoryName(Environment.ProcessPath),
                                        AppContext.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory }) {
                if (dir == null) continue;
                var p = Path.Combine(dir, $"lang.{l}.json");
                if (File.Exists(p)) return File.ReadAllText(p);
            }
            return null;
        }
        // 2. Embedded Resource (immer verfügbar, egal wie die EXE aufgerufen wird)
        string FindEmbedded(string l) {
            const string ResourcePrefix = "VerBump.lang_";
            const string ResourceSuffix = ".json";

            static string ReadResource(System.Reflection.Assembly sourceAsm, string resourceName) {
                using var stream = sourceAsm.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string resourceName = $"{ResourcePrefix}{l}{ResourceSuffix}";

            string json = ReadResource(asm, resourceName);
            if (json != null) return json;

            try {
                var satellite = asm.GetSatelliteAssembly(new System.Globalization.CultureInfo(l));
                return ReadResource(satellite, resourceName);
            } catch {
                return null;
            }
        }
        string json = FindFile(lang);
        if (json == null) { json = FindFile("en");      if (json != null) lang = "en"; }
        if (json == null) { json = FindEmbedded(lang);  }
        if (json == null) { json = FindEmbedded("en");  if (json != null) lang = "en"; }
        if (json == null) return;
        try {
            _d = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
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
        public TextBox     VersionBox;
        public string      FilePath;
        public string      OriginalVersion;
        public IVersionScheme Scheme;
        public bool        Backup;
        public ProjectEntry Entry;
        public bool?            HasIssues;      // null = noch am Scannen
        public string           StaleInfo;      // Tooltip-Text wenn veraltet
        public bool             IsUnsaved;      // true = via Kontextmenü geöffnet, noch nicht in settings.json
        public ContextMenuStrip ListDropdown;   // list picker popup (for screenshot mode)
    }

// ── Theme (OS-aware dark/light) ───────────────────────────────────────────────

static class Theme {
    public static bool IsDark { get; private set; } = true;

    public static void Detect() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            IsDark = (int)(key?.GetValue("AppsUseLightTheme") ?? 0) == 0;
        } catch { IsDark = true; }
    }

    public static Color BgMid    => IsDark ? Color.FromArgb(45, 45, 48)    : Color.FromArgb(240, 240, 242);
    public static Color BgDark   => IsDark ? Color.FromArgb(30, 30, 30)    : Color.FromArgb(225, 225, 228);
    public static Color BgLight  => IsDark ? Color.FromArgb(60, 60, 60)    : Color.FromArgb(255, 255, 255);
    public static Color BgBtn    => IsDark ? Color.FromArgb(70, 70, 70)    : Color.FromArgb(225, 225, 228);
    public static Color Fg       => IsDark ? Color.White                    : Color.Black;
    public static Color FgDim    => IsDark ? Color.LightGray                : Color.DimGray;
    public static Color FgMuted  => IsDark ? Color.FromArgb(160, 160, 165) : Color.FromArgb(100, 100, 110);
    public static Color Sep      => IsDark ? Color.FromArgb(80, 80, 85)    : Color.FromArgb(200, 200, 205);
    public static Color Hover    => IsDark ? Color.FromArgb(70, 70, 80)    : Color.FromArgb(210, 210, 220);
    public static Color Selected => IsDark ? Color.FromArgb(60, 60, 70)    : Color.FromArgb(210, 215, 230);
    public static Color Accent   => Color.FromArgb(0, 122, 204);
}

class ThemedColorTable : ProfessionalColorTable {
    public override Color ToolStripGradientBegin              => Theme.BgMid;
    public override Color ToolStripGradientMiddle             => Theme.BgMid;
    public override Color ToolStripGradientEnd                => Theme.BgMid;
    public override Color ButtonSelectedGradientBegin         => Theme.Hover;
    public override Color ButtonSelectedGradientMiddle        => Theme.Hover;
    public override Color ButtonSelectedGradientEnd           => Theme.Hover;
    public override Color ButtonPressedGradientBegin          => Theme.Hover;
    public override Color ButtonPressedGradientMiddle         => Theme.Hover;
    public override Color ButtonPressedGradientEnd            => Theme.Hover;
    public override Color ButtonCheckedGradientBegin          => Theme.Hover;
    public override Color ButtonCheckedGradientMiddle         => Theme.Hover;
    public override Color ButtonCheckedGradientEnd            => Theme.Hover;
    public override Color ButtonSelectedBorder                => Theme.Sep;
    public override Color SeparatorDark                       => Theme.Sep;
    public override Color SeparatorLight                      => Theme.Sep;
    public override Color ImageMarginGradientBegin            => Theme.BgMid;
    public override Color ImageMarginGradientMiddle           => Theme.BgMid;
    public override Color ImageMarginGradientEnd              => Theme.BgMid;
    public override Color MenuStripGradientBegin              => Theme.BgMid;
    public override Color MenuStripGradientEnd                => Theme.BgMid;
    public override Color MenuItemBorder                      => Theme.Sep;
    public override Color MenuItemSelected                    => Theme.Hover;
    public override Color MenuItemSelectedGradientBegin       => Theme.Hover;
    public override Color MenuItemSelectedGradientEnd         => Theme.Hover;
    public override Color MenuItemPressedGradientBegin        => Theme.BgMid;
    public override Color MenuItemPressedGradientMiddle       => Theme.BgMid;
    public override Color MenuItemPressedGradientEnd          => Theme.BgMid;
    public override Color MenuBorder                          => Theme.Sep;
    public override Color ToolStripDropDownBackground         => Theme.BgMid;
}

    static string       OverrideSettingsPath = null;
    static List<string> InitialVersionPaths  = new();
    static int          SilentBumpPart       = -1;   // 0-based; -1 = no silent bump
    static bool         CheckMode            = false;
    static bool         ShouldRestart        = false;
    static string       ForceLang            = null; // --lang=XX overrides OS language
#if DEMO
    static string       ScreenshotDir        = null; // --screenshot=<dir>
    static int          ScreenshotEntry      = 0;    // --screenshot-entry=N  (0-based)
    static int          ScreenshotRow        = -1;   // --screenshot-row=N    (show dropdown on row N)
    static bool         ScreenshotHelp       = false;// --screenshot-help     (open ? window)
#endif

    [STAThread]
    public static void Main(string[] args) {
        if (args.Any(a => a == "--help" || a == "-h" || a == "/?")) {
            // Attach to the parent console so output is visible when launched from a terminal
            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            static extern bool AttachConsole(uint dwProcessId);
            AttachConsole(unchecked((uint)-1)); // ATTACH_PARENT_PROCESS
            string helpText = """

VerBump — Version file manager
Usage:  VerBump.exe [path…] [options]

Arguments:
  path                     VERSION file or project folder to open

Options:
  --settings=<file>        Use a specific settings.json
  --lang=<xx>              Force UI language (e.g. en, de)
  --bump=<1-4>             Silent bump: 1=major 2=minor 3=patch 4=list (no GUI)
  --check                  Git pre-commit hook mode

  -h, --help               Show this help
""";
#if DEMO
            helpText += """

Screenshot automation:
  --screenshot=<dir>       Take screenshots and write them to <dir>, then exit
  --screenshot-row=<N>     Select row N in the main window and open its list dropdown (0-based)
  --screenshot-entry=<N>   Select entry N in the settings dialog (0-based)
  --screenshot-help        Open the Lists ? help window in the settings screenshot
""";
#endif
            Console.WriteLine(helpText);
            return;
        }
        foreach (var arg in args) {
            if (arg.StartsWith("--bump=", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(arg[7..], out int b) && b >= 1 && b <= 4)
                    SilentBumpPart = b - 1;
            } else if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) {
                ForceLang = arg[7..].ToLower();
#if DEMO
            } else if (arg.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase)) {
                ScreenshotDir = arg[13..].Trim('"');
            } else if (arg.StartsWith("--screenshot-entry=", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(arg[19..], out int se)) ScreenshotEntry = Math.Max(0, se);
            } else if (arg.StartsWith("--screenshot-row=", StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(arg[17..], out int sr)) ScreenshotRow = Math.Max(0, sr);
            } else if (arg.Equals("--screenshot-help", StringComparison.OrdinalIgnoreCase)) {
                ScreenshotHelp = true;
#endif
            } else if (arg.Equals("--check", StringComparison.OrdinalIgnoreCase)) {
                CheckMode = true;
            } else {
                string p   = arg.Trim('"');
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if ((ext == ".json" || ext == ".json5") && File.Exists(p))
                    OverrideSettingsPath = NormalizeHistoryPath(p);
                else if (File.Exists(p) && Path.GetFileName(p).Equals("VERSION", StringComparison.OrdinalIgnoreCase))
                    InitialVersionPaths.Add(p);
                else if (Directory.Exists(p)) {
                    string v = Path.Combine(p, "VERSION");
                    if (File.Exists(v)) InitialVersionPaths.Add(v);
                }
            }
        }
        SetForegroundWindow(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        do {
            ShouldRestart = false;
            Run();
        } while (ShouldRestart);
    }

    public static void Run() {
        try { SetCurrentProcessExplicitAppUserModelID("VerBump.1.0"); } catch (Exception ex) { Log.Write("Run/AppUserModelID", ex); }

        // Environment.ProcessPath gibt den echten EXE-Pfad zurück (auch bei Single-File-Publish)
        string baseDir    = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerBump");
        Directory.CreateDirectory(appDataDir);
        Log.Init(appDataDir);
        Theme.Detect();
        L.Load(baseDir, VerBump.ForceLang);
        var appConfig = LoadAppConfig();
        var policy    = LoadPolicy();

        // ── Theme colors for this Run() invocation ──
        Color bgMid    = Theme.BgMid;
        Color bgDark   = Theme.BgDark;
        Color bgLight  = Theme.BgLight;
        Color bgBtn    = Theme.BgBtn;
        Color fgW      = Theme.Fg;
        Color fgDim    = Theme.FgDim;
        Color selBg    = Theme.Selected;

        string appVersion = "?";
        try {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("VerBump.VERSION");
            if (stream != null) {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                appVersion = reader.ReadToEnd().Trim();
            }
        } catch (Exception ex) { Log.Write("Run/version", ex); }

        // unsaved mode: VERSION/folder args without --bump/--check → skip settings file entirely
        bool unsavedMode = InitialVersionPaths.Count > 0 && OverrideSettingsPath == null
                        && SilentBumpPart < 0 && !CheckMode;

        string   jsonPath = null;
        Settings settings = new();

        if (!unsavedMode) {
            jsonPath = OverrideSettingsPath ?? Path.Combine(appDataDir, "VerBump-settings.json");
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
                    BackColor = bgMid, ForeColor = fgW,
                };
                new Label { Parent = md, Left = 16, Top = 16, Width = 480, Height = 52,
                    Text = L.T("settings.created", jsonPath),
                    Font = new Font("Segoe UI", 9F), ForeColor = fgW };
                var mdOk = new Button { Parent = md, Text = L.T("btn.ok"), Left = 412, Top = 74, Width = 80, Height = 28,
                    FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = fgW,
                    DialogResult = DialogResult.OK };
                md.AcceptButton = mdOk;
                md.ShowDialog();
                return;
            }
            try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(jsonPath), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new Settings(); }
            catch (Exception ex) { Log.Write("Run/json", ex); MessageBox.Show(L.T("error.json", ex.Message)); return; }
            if (OverrideSettingsPath != null) { AddToHistory(appConfig.RecentSettings, OverrideSettingsPath, appConfig.HistoryMaxLength); SaveAppConfig(appConfig); }
        }

        // ── Silent bump from Explorer context menu (--bump=N) ─────────────────
        string singleVersionPath = InitialVersionPaths.Count > 0 ? InitialVersionPaths[0] : null;
        if (SilentBumpPart >= 0 && singleVersionPath != null) {
            RunSilentBump(settings, singleVersionPath, SilentBumpPart);
            return;
        }

        // ── Check mode (--check): used by git pre-commit hook ─────────────────
        if (CheckMode && singleVersionPath == null) {
            string v = Path.Combine(Directory.GetCurrentDirectory(), "VERSION");
            if (File.Exists(v)) singleVersionPath = v;
        }
        HashSet<string> checkIgnoreDirs  = null;
        List<string>    checkIgnoreFiles = null;
        int             checkStagedCount = -1;   // ≥0 = git available; -1 = fallback
        if (CheckMode && singleVersionPath != null) {
            string vf = Path.GetFullPath(singleVersionPath);
            var matchingEntry = settings.Paths?.FirstOrDefault(p => {
                try { return string.Equals(Path.GetFullPath(Path.Combine(p.Path.Trim('"'), "VERSION")), vf, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
            var ce = matchingEntry ?? new ProjectEntry { Path = Path.GetDirectoryName(vf) ?? "", Scheme = "semver" };
            checkIgnoreDirs  = BuildEffectiveIgnoreDirs(settings, ce);
            checkIgnoreFiles = BuildEffectiveIgnoreFiles(settings, ce);

            // Fast path: ask git whether VERSION is already staged for this commit.
            // If yes, the user already bumped – no UI needed.
            string projectDir = Path.GetDirectoryName(vf);
            var staged = GetGitStagedFiles(projectDir);
            if (staged != null) {
                // Compare using the full path relative to the git root so that
                // in monorepos a different project's VERSION doesn't give a false positive.
                string gitRoot      = TryGetGitRoot(projectDir);
                string vfNorm       = vf.Replace('\\', '/');
                string gitRootNorm  = gitRoot?.Replace('\\', '/').TrimEnd('/');
                string vfRelative   = (gitRootNorm != null && vfNorm.StartsWith(gitRootNorm + "/", StringComparison.OrdinalIgnoreCase))
                                      ? vfNorm[(gitRootNorm.Length + 1)..]
                                      : Path.GetFileName(vf); // fallback: filename only
                bool versionStaged = staged.Any(f =>
                    string.Equals(f.Trim().Replace('\\', '/'), vfRelative,
                                  StringComparison.OrdinalIgnoreCase));
                if (versionStaged) { Environment.Exit(0); return; }
                checkStagedCount = staged.Count; // save for banner
                // VERSION not staged → fall through to open UI
            } else if (GetNewerFiles(vf, 1, checkIgnoreDirs, checkIgnoreFiles).Count == 0) {
                // Fallback (no git): mtime comparison
                Environment.Exit(0); return;
            }
            // Show only the stale project, not all projects from settings.json
            settings = new Settings {
                Paths       = [matchingEntry ?? ce],
                IgnoreDirs  = settings.IgnoreDirs,
                IgnoreFiles = settings.IgnoreFiles,
                Lists       = settings.Lists
            };
        }

        bool willHaveUnsavedEntry = unsavedMode;

        string formTitle = $"VerBump  v{appVersion}";
        if (unsavedMode) {
            formTitle = InitialVersionPaths.Count == 1
                ? $"VerBump  v{appVersion}  —  {Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(InitialVersionPaths[0])))}"
                : $"VerBump  v{appVersion}  —  {L.T("form.projects_count", InitialVersionPaths.Count)}";
        } else if (OverrideSettingsPath != null) {
            formTitle = $"VerBump  v{appVersion}  —  {Path.GetFileName(OverrideSettingsPath)}";
        }
        int MeasureRowWidth(ProjectEntry entry) {
            IVersionScheme measureScheme = SchemeFactory.Create(entry, settings.Lists);
            var labels = measureScheme.GetButtonLabels();
            using var btnFont = new Font("Segoe UI", 8F);
            int buttonsWidth = 5;
            foreach (var label in labels) {
                if (label == null) continue;
                int btnWidth = Math.Max(50, TextRenderer.MeasureText(label + "+", btnFont).Width + 16);
                buttonsWidth += btnWidth + 6;
            }
            return 42 + 22 + 172 + 120 + buttonsWidth + 24;
        }

        int requestedRowWidth = 680;
        foreach (var entry in willHaveUnsavedEntry ? [] : settings.Paths) {
            string cleanPath = entry.Path.Trim().TrimEnd(Path.DirectorySeparatorChar, '/');
            string vFile = Path.Combine(cleanPath, "VERSION");
            if (!File.Exists(vFile)) continue;
            requestedRowWidth = Math.Max(requestedRowWidth, MeasureRowWidth(entry));
        }
        if (willHaveUnsavedEntry) foreach (string versionPath in InitialVersionPaths) {
            if (!File.Exists(versionPath)) continue;
            string target = Path.GetFullPath(versionPath);
            string cleanPath = Path.GetDirectoryName(target);
            requestedRowWidth = Math.Max(requestedRowWidth, MeasureRowWidth(new ProjectEntry { Path = cleanPath, Scheme = "semver", ResetOnBump = true }));
        }
        int requestedContentWidth = requestedRowWidth;
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 900);
        int maxFormWidth = Math.Max(720, workingArea.Width - 40);
        int requestedFormWidth = Math.Min(Math.Max(720, requestedContentWidth + 40), maxFormWidth);
        if (CheckMode && singleVersionPath != null)
            formTitle = L.T("form.git_hook_title", Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(singleVersionPath))) ?? "?");
        const int hookBannerH = 50;
        int extraH = (CheckMode && singleVersionPath != null) ? hookBannerH : 0;
        using var form = new Form {
            Text = formTitle,
            Width = requestedFormWidth, Height = 80 + (willHaveUnsavedEntry ? InitialVersionPaths.Count : settings.Paths.Count) * 55 + 138 + extraH,
            StartPosition = FormStartPosition.CenterScreen,
            KeyPreview = true,
            BackColor = bgMid, ForeColor = fgW
        };

        try {
            string iconPath = Path.Combine(baseDir, "verbump.ico");
            form.Icon = File.Exists(iconPath) ? new Icon(iconPath) :
                Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        } catch { form.Icon = SystemIcons.Application; }

        bool hookBypassAllowed = CheckMode && policy.AllowHookBypass;
        var btnOk  = new Button {
            Text = CheckMode ? L.T("btn.commit") : L.T("btn.save"),
            Top = 15, Width = 120, Height = 35,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        var btnCan = new Button {
            Text = CheckMode ? L.T("btn.block_commit") : L.T("btn.cancel"),
            Top = 15, Width = 120, Height = 35,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = CheckMode ? Color.FromArgb(160, 40, 40) : bgBtn,
            ForeColor = CheckMode ? Color.White : fgW };
        if (!CheckMode) {
            new ToolTip().SetToolTip(btnOk,  L.T("btn.save_tip"));
            new ToolTip().SetToolTip(btnCan, L.T("btn.cancel_tip"));
        }

        form.AcceptButton = btnOk;
        form.CancelButton = btnCan;

        // ── Statuszeile ──
        var statusLabel = new Label {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = fgDim,
            Padding = new Padding(8, 0, 0, 0),
        };
        var statusPanel = new Panel {
            Dock = DockStyle.Bottom, Height = 24,
            BackColor = bgDark,
        };
        statusPanel.Controls.Add(statusLabel);

        var statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        Action<string, bool> setStatus = (msg, isError) => {
            statusLabel.ForeColor = isError ? Color.Salmon : fgDim;
            statusLabel.Text = msg;
            if (isError) { statusTimer.Stop(); statusTimer.Start(); }
        };

        var mainPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = bgDark, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        mainPanel.AutoScrollMinSize = new Size(requestedContentWidth + 20, 0);
        var uiEntries = new List<ProjectUI>();
        int selectedIndex = 0;
        Action updateSelection = null;
        var undoStack = new Stack<(int entryIdx, string oldVersion, string label)>();
        Action updateUndoItem = () => { };

        // ── Version-box hints (cursor position → status + context menu) ───────
        ContextMenuStrip AttachVersionHints(TextBox vtb, IVersionScheme sch) {
            void updateHint() {
                var hint = sch.GetTokenAt(vtb.Text, vtb.SelectionStart);
                if (hint == null) {
                    if (!sch.Matches(vtb.Text.Trim()))
                        setStatus(L.T("status.hint_mismatch"), true);
                    return;
                }
                string msg = hint.IsList
                    ? L.T("status.hint_list", hint.Label)
                    : L.T("status.hint_number", hint.Label);
                setStatus(msg, false);
            }

            var cms = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new ThemedColorTable()) };
            cms.Opening += (s, e) => {
                cms.Items.Clear();
                // cms.Tag can carry an explicit cursor position (set by screenshot mode)
                int cursorForHint = cms.Tag is int tagPos ? tagPos : vtb.SelectionStart;
                cms.Tag = null;
                var hint = sch.GetTokenAt(vtb.Text, cursorForHint);
                if (hint?.Values == null || !hint.IsList) { e.Cancel = true; return; }
                cms.Items.Add(new ToolStripMenuItem($"— {hint.Label} —") { Enabled = false, ForeColor = fgDim });
                cms.Items.Add(new ToolStripSeparator());
                foreach (var val in hint.Values) {
                    string v = val;
                    var item = new ToolStripMenuItem(v) { ForeColor = fgW };
                    item.Click += (si, ei) => {
                        var h = sch.GetTokenAt(vtb.Text, vtb.SelectionStart);
                        if (h == null) return;
                        vtb.Text = vtb.Text[..h.Start] + v + vtb.Text[(h.Start + h.Length)..];
                        vtb.SelectionStart = h.Start + v.Length;
                    };
                    cms.Items.Add(item);
                }
            };
            // right-click keeps the default cut/copy/paste menu; list picker via Alt+Down
            vtb.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) updateHint(); };
            vtb.KeyUp   += (s, e) => updateHint();
            vtb.KeyDown += (s, e) => {
                if (e.Alt && e.KeyCode == Keys.Down) {
                    cms.Show(vtb, new Point(0, vtb.Height));
                    e.Handled = true; e.SuppressKeyPress = true;
                }
            };
            return cms;
        }

        // ── Row context menu ──────────────────────────────────────────────────
        int ctxTargetIndex = -1;
        var rowCtx           = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new ThemedColorTable()) };
        var ctxEdit          = new ToolStripMenuItem(L.T("menu.edit_settings"))  { ForeColor = fgW };
        var ctxExplore       = new ToolStripMenuItem(L.T("menu.open_explorer")) { ForeColor = fgW };
        var ctxAddVersionFav = new ToolStripMenuItem(L.T("menu.add_version_fav")) { ForeColor = Color.FromArgb(255, 200, 60) };
        var ctxTagSep        = new ToolStripSeparator();
        var ctxTag           = new ToolStripMenuItem() { ForeColor = Color.FromArgb(72, 199, 142) };
        var ctxTagPush       = new ToolStripMenuItem() { ForeColor = Color.FromArgb(72, 199, 142) };
        var ctxAddToSettings = new ToolStripMenuItem(L.T("toolbar.add_project")) { ForeColor = fgW };
        var ctxBottomSep    = new ToolStripSeparator();
        rowCtx.Items.Add(ctxEdit);
        rowCtx.Items.Add(ctxExplore);
        rowCtx.Items.Add(ctxAddVersionFav);
        rowCtx.Items.Add(ctxTagSep);
        rowCtx.Items.Add(ctxTag);
        rowCtx.Items.Add(ctxTagPush);
        rowCtx.Items.Add(ctxBottomSep);
        rowCtx.Items.Add(ctxAddToSettings);
        rowCtx.BackColor           = bgMid;
        ctxEdit.BackColor          = bgMid;
        ctxExplore.BackColor       = bgMid;
        ctxAddVersionFav.BackColor = bgMid;
        ctxTag.BackColor           = bgMid;
        ctxTagPush.BackColor       = bgMid;
        ctxAddToSettings.BackColor = bgMid;

        rowCtx.Opening += (s, e) => {
            bool ok = ctxTargetIndex >= 0 && ctxTargetIndex < uiEntries.Count;
            ctxEdit.Visible          = ok && !uiEntries[ctxTargetIndex].IsUnsaved;
            ctxExplore.Visible       = ok;
            ctxAddToSettings.Visible = ok && uiEntries[ctxTargetIndex].IsUnsaved;
            if (ok) {
                string fp = uiEntries[ctxTargetIndex].FilePath;
                bool alreadyFav = fp != null && appConfig.FavoriteVersions.Any(
                    f => string.Equals(f.Path, fp, StringComparison.OrdinalIgnoreCase));
                ctxAddVersionFav.Visible = fp != null;
                ctxAddVersionFav.Enabled = !alreadyFav;
                ctxAddVersionFav.Text    = alreadyFav
                    ? L.T("menu.remove_version_fav")
                    : L.T("menu.add_version_fav");
                // Tag items: only show when project is inside a git repo
                string tagDir = fp != null ? Path.GetDirectoryName(fp) : null;
                bool hasGit   = tagDir != null && FindGitDir(tagDir) != null;
                string ver    = uiEntries[ctxTargetIndex].VersionBox.Text.Trim();
                string tagName = ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : "v" + ver;
                ctxTagSep.Visible  = hasGit;
                ctxTag.Visible     = hasGit;
                ctxTagPush.Visible = hasGit;
                if (hasGit) {
                    ctxTag.Text     = L.T("menu.tag_head",      tagName);
                    ctxTagPush.Text = L.T("menu.tag_head_push", tagName);
                }
            } else {
                ctxAddVersionFav.Visible = false;
                ctxTagSep.Visible        = false;
                ctxTag.Visible           = false;
                ctxTagPush.Visible       = false;
            }
            ctxBottomSep.Visible = ctxAddToSettings.Visible;
        };
        ctxEdit.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            var ui = uiEntries[ctxTargetIndex];
            if (ui.IsUnsaved || jsonPath == null) return;
            int idx = settings.Paths.IndexOf(ui.Entry);
            ShowSettingsDialog(form, settings, jsonPath, Math.Max(0, idx));
        };
        ctxExplore.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            string folder = Path.GetDirectoryName(uiEntries[ctxTargetIndex].FilePath);
            if (folder != null && Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        };
        ctxTag.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            var ui = uiEntries[ctxTargetIndex];
            DoGitTag(Path.GetDirectoryName(ui.FilePath), ui.VersionBox.Text.Trim(), false, form);
        };
        ctxTagPush.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            var ui = uiEntries[ctxTargetIndex];
            DoGitTag(Path.GetDirectoryName(ui.FilePath), ui.VersionBox.Text.Trim(), true, form);
        };
        ctxAddToSettings.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            var ui = uiEntries[ctxTargetIndex];
            if (!ui.IsUnsaved) return;

            // ── Ziel-Auswahl-Dialog ───────────────────────────────────────────
            string pickedJson   = null;
            bool   switchToFile = false;

            int dlgH = jsonPath != null ? 152 : 112;
            using var addDlg = new Form {
                Text = L.T("toolbar.add_project"), Width = 420, Height = dlgH,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, KeyPreview = true,
                BackColor = bgMid, ForeColor = fgW,
            };
            new Label { Parent = addDlg, Left = 12, Top = 10, Width = 390, Height = 20,
                Text = L.T("settings.add_target_label"), Font = new Font("Segoe UI", 9F), ForeColor = fgW };

            if (jsonPath != null) {
                string shortPath = Path.GetFileName(Path.GetDirectoryName(jsonPath))
                                   + "/" + Path.GetFileName(jsonPath);
                var btnActive = new Button {
                    Parent = addDlg, Left = 12, Top = 34, Width = 388, Height = 28,
                    Text = L.T("settings.active_settings", shortPath),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 122, 204), ForeColor = fgW,
                    Font = new Font("Segoe UI", 9F),
                };
                btnActive.Click += (bs, be) => { pickedJson = jsonPath; switchToFile = false; addDlg.Close(); };
                addDlg.AcceptButton = btnActive;
            }

            int row2 = jsonPath != null ? 68 : 34;
            var btnOther = new Button {
                Parent = addDlg, Left = 12, Top = row2, Width = 194, Height = 28,
                Text = L.T("settings.other_or_new_file"),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgBtn, ForeColor = fgW,
                Font = new Font("Segoe UI", 9F),
            };
            btnOther.Click += (bs, be) => {
                string initDir = jsonPath != null ? Path.GetDirectoryName(jsonPath)
                               : InitialVersionPaths.Count > 0 ? Path.GetDirectoryName(InitialVersionPaths[0])
                               : appDataDir;
                using var sfd = new SaveFileDialog {
                    Title = L.T("settings.choose_or_create"),
                    Filter = "JSON|*.json", FileName = "settings.json",
                    InitialDirectory = initDir,
                };
                if (sfd.ShowDialog(addDlg) == DialogResult.OK) { pickedJson = sfd.FileName; switchToFile = true; }
                addDlg.Close();
            };
            if (jsonPath == null) addDlg.AcceptButton = btnOther;

            var btnAbort = new Button {
                Parent = addDlg, Left = 212, Top = row2, Width = 188, Height = 28,
                Text = L.T("btn.cancel"),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgBtn, ForeColor = fgW,
                Font = new Font("Segoe UI", 9F), DialogResult = DialogResult.Cancel,
            };
            addDlg.CancelButton = btnAbort;
            addDlg.KeyDown += (ks, ke) => { if (ke.KeyCode == Keys.Escape) addDlg.Close(); };
            addDlg.ShowDialog(form);

            if (pickedJson == null) return;

            // ── In Zieldatei speichern ────────────────────────────────────────
            Settings targetSettings = new();
            if (File.Exists(pickedJson)) {
                try { targetSettings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(pickedJson),
                    new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new(); }
                catch { targetSettings = new(); }
            }
            targetSettings.Paths.Add(ui.Entry);
            try {
                File.WriteAllText(pickedJson, JsonSerializer.Serialize(targetSettings,
                    new JsonSerializerOptions { WriteIndented = true }));
            } catch (Exception ex) { setStatus(L.T("error.save", ex.Message), true); return; }

            if (switchToFile) {
                LoadSettingsFile(pickedJson);   // Neustart mit neuer Datei
            } else {
                ui.IsUnsaved = false;
                ui.StatusStrip.BackColor = Color.FromArgb(80, 80, 80);
                setStatus(L.T("toolbar.add_project_ok"), false);
            }
        };

        var toolTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 200 };

        foreach (var entry in willHaveUnsavedEntry ? [] : settings.Paths) {
            string cleanPath = entry.Path.Trim().TrimEnd(Path.DirectorySeparatorChar, '/');
            string vFile = Path.Combine(cleanPath, "VERSION");
            if (!File.Exists(vFile)) continue;

            string currentV = File.ReadAllText(vFile).Trim();
            string projectName = !string.IsNullOrWhiteSpace(entry.Name)
                ? entry.Name
                : Path.GetFileName(cleanPath) ?? L.T("project.unnamed");
            IVersionScheme scheme = SchemeFactory.Create(entry, settings.Lists);

            if (scheme.HasDateTokens)
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

            var selectionPanel = new Panel { Width = requestedRowWidth, Height = 50, Margin = new Padding(0, 3, 0, 3), BackColor = Color.Transparent };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(14, 3, 3, 3) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));   // Icon
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));   // Hotkey
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));  // Name
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));  // Version
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // Buttons

            int hotkeyIndex = uiEntries.Count;
            string hotkeyChar = hotkeyIndex < 26 ? ((char)('A' + hotkeyIndex)).ToString()
                              : hotkeyIndex < 36 ? ((char)('0' + hotkeyIndex - 26)).ToString()
                              : "";
            var lblHotkey = new Label {
                Text = hotkeyChar,
                Width = 18, Height = 44,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Theme.FgMuted,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            var lbl = new Label { Text = projectName, Width = 172, Height = 44, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            toolTip.SetToolTip(lbl, vFile);

            var tb = new TextBox {
                Text = currentV,
                Width = 110, Height = 23,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = bgLight, ForeColor = fgW,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10F),
            };

            int entryIndex = uiEntries.Count;
            tb.Enter   += (s, e) => { selectedIndex = entryIndex; updateSelection(); };
            tb.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; btnOk.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; btnCan.PerformClick(); }
            };
            tb.TextChanged += (s, e) => {
                if (tb.BackColor == Color.DarkGreen) return;
                tb.BackColor = scheme.Matches(tb.Text.Trim()) ? bgLight : Color.FromArgb(110, 50, 0);
            };
            var rowDropdown = AttachVersionHints(tb, scheme);

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(5, 5, 0, 0) };
            var labels = scheme.GetButtonLabels();
            for (int i = 0; i < labels.Count; i++) {
                if (labels[i] == null) continue;
                int partIndex = i;
                var tbCaptured = tb;
                var schemeCaptured = scheme;
                var btnFont = new Font("Segoe UI", 8F);
                int btnW = Math.Max(50, TextRenderer.MeasureText(labels[i] + "+", btnFont).Width + 16);
                var btn = new Button {
                    Text = labels[i] + "+",
                    Width = btnW, Height = 28,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = bgBtn,
                    Font = btnFont
                };
                btn.Click += (s, e) => {
                    selectedIndex = entryIndex;
                    updateSelection();
                    string before = tbCaptured.Text.Trim();
                    string after  = schemeCaptured.Bump(before, partIndex);
                    undoStack.Push((entryIndex, before, labels[partIndex] + "+"));
                    updateUndoItem();
                    tbCaptured.Text = after;
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
            var strip = new Panel { Width = 5, Dock = DockStyle.Left, BackColor = Theme.Sep, Cursor = Cursors.Help };
            selectionPanel.Controls.Add(strip);
            selectionPanel.Controls.Add(table);
            mainPanel.Controls.Add(selectionPanel);
            int rowIdx = uiEntries.Count;
            void AttachRowEvents(Control c) {
                c.MouseDoubleClick += (s, e) => {
                    if (rowIdx >= uiEntries.Count || uiEntries[rowIdx].IsUnsaved || jsonPath == null) return;
                    int idx = settings.Paths.IndexOf(uiEntries[rowIdx].Entry);
                    ShowSettingsDialog(form, settings, jsonPath, Math.Max(0, idx));
                };
                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left) {
                        selectedIndex = rowIdx; updateSelection();
                    } else if (e.Button == MouseButtons.Right) {
                        ctxTargetIndex = rowIdx; selectedIndex = rowIdx; updateSelection();
                        rowCtx.Show((Control)s, e.Location);
                    }
                };
            }
            AttachRowEvents(selectionPanel); AttachRowEvents(table);
            AttachRowEvents(lbl); AttachRowEvents(lblHotkey); AttachRowEvents(iconBox); AttachRowEvents(strip);
            AttachRowEvents(buttonPanel);
            uiEntries.Add(new ProjectUI { SelectionPanel = selectionPanel, StatusStrip = strip, VersionBox = tb, FilePath = vFile, OriginalVersion = currentV, Scheme = scheme, Backup = entry.Backup, Entry = entry, ListDropdown = rowDropdown });
        }

        // ── Unsaved entries: VERSION paths passed via CLI (unsaved mode) ──────────
        if (willHaveUnsavedEntry) foreach (string versionPath in InitialVersionPaths) {
            if (!File.Exists(versionPath)) continue;
            string target      = Path.GetFullPath(versionPath);
            string cleanPath   = Path.GetDirectoryName(target);
            string currentV    = File.ReadAllText(target).Trim();
            string projectName = Path.GetFileName(cleanPath) ?? target;
            var entry  = new ProjectEntry { Path = cleanPath, Scheme = "semver", ResetOnBump = true };
            IVersionScheme scheme = SchemeFactory.Create(entry, settings.Lists);

            var selectionPanel = new Panel { Width = requestedRowWidth, Height = 50, Margin = new Padding(0, 3, 0, 3), BackColor = Color.Transparent };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(14, 3, 3, 3) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            int hotkeyIndex = uiEntries.Count;
            string hotkeyChar = hotkeyIndex < 26 ? ((char)('A' + hotkeyIndex)).ToString()
                              : hotkeyIndex < 36 ? ((char)('0' + hotkeyIndex - 26)).ToString()
                              : "";
            var lblHotkey = new Label { Text = hotkeyChar, Width = 18, Height = 44,
                Font = new Font("Segoe UI", 8F), ForeColor = Theme.FgMuted,
                TextAlign = ContentAlignment.MiddleCenter };
            var lbl = new Label { Text = projectName, Width = 172, Height = 44,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            toolTip.SetToolTip(lbl, target);
            var tb = new TextBox { Text = currentV, Width = 110, Height = 23, Margin = new Padding(0, 10, 0, 0),
                BackColor = bgLight, ForeColor = fgW,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10F) };
            int entryIndex = uiEntries.Count;
            tb.Enter   += (s, e) => { selectedIndex = entryIndex; updateSelection(); };
            tb.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; btnOk.PerformClick(); }
                if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; btnCan.PerformClick(); }
            };
            tb.TextChanged += (s, e) => {
                if (tb.BackColor == Color.DarkGreen) return;
                tb.BackColor = scheme.Matches(tb.Text.Trim()) ? bgLight : Color.FromArgb(110, 50, 0);
            };
            var rowDropdown2 = AttachVersionHints(tb, scheme);
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(5, 5, 0, 0) };
            var labels = scheme.GetButtonLabels();
            for (int i = 0; i < labels.Count; i++) {
                if (labels[i] == null) continue;
                int partIndex = i; var tbC = tb; var schC = scheme;
                var btnFont2 = new Font("Segoe UI", 8F);
                int btnW2 = Math.Max(50, TextRenderer.MeasureText(labels[i] + "+", btnFont2).Width + 16);
                var btn = new Button { Text = labels[i] + "+", Width = btnW2, Height = 28,
                    FlatStyle = FlatStyle.Flat, BackColor = bgBtn, Font = btnFont2 };
                btn.Click += (s, e) => {
                    selectedIndex = entryIndex;
                    updateSelection();
                    string before = tbC.Text.Trim();
                    undoStack.Push((entryIndex, before, labels[partIndex] + "+"));
                    updateUndoItem();
                    tbC.Text = schC.Bump(before, partIndex);
                    tbC.BackColor = Color.DarkGreen;
                };
                buttonPanel.Controls.Add(btn);
            }
            var iconBox = new PictureBox { Width = 36, Height = 36, Margin = new Padding(0, 7, 0, 0),
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Visible = false };
            table.Controls.Add(iconBox, 0, 0);
            table.Controls.Add(lblHotkey, 1, 0);
            table.Controls.Add(lbl, 2, 0);
            table.Controls.Add(tb, 3, 0);
            table.Controls.Add(buttonPanel, 4, 0);
            var strip = new Panel { Width = 5, Dock = DockStyle.Left, BackColor = Color.FromArgb(80, 130, 200), Cursor = Cursors.Default };
            selectionPanel.Controls.Add(strip);
            selectionPanel.Controls.Add(table);
            mainPanel.Controls.Add(selectionPanel);
            int rowIdx2 = uiEntries.Count;
            void AttachRowEvents2(Control c) {
                c.MouseDoubleClick += (s, e) => { };   // unsaved: no settings to open
                c.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left) {
                        selectedIndex = rowIdx2; updateSelection();
                    } else if (e.Button == MouseButtons.Right) {
                        ctxTargetIndex = rowIdx2; selectedIndex = rowIdx2; updateSelection();
                        rowCtx.Show((Control)s, e.Location);
                    }
                };
            }
            AttachRowEvents2(selectionPanel); AttachRowEvents2(table);
            AttachRowEvents2(lbl); AttachRowEvents2(lblHotkey); AttachRowEvents2(iconBox); AttachRowEvents2(strip);
            AttachRowEvents2(buttonPanel);
            foreach (Control c in buttonPanel.Controls) AttachRowEvents2(c);
            uiEntries.Add(new ProjectUI { SelectionPanel = selectionPanel, StatusStrip = strip, VersionBox = tb,
                FilePath = target, OriginalVersion = currentV, Scheme = scheme, Backup = false, Entry = entry, IsUnsaved = true, ListDropdown = rowDropdown2 });
            selectedIndex = uiEntries.Count - 1;
        }

        if (uiEntries.Count == 0) {
            var msgFont = new Font("Segoe UI", 9F);
            string noFilesMsg = L.T("error.nofiles", jsonPath);
            int pathPx  = TextRenderer.MeasureText(jsonPath, msgFont).Width;
            int formW   = Math.Max(360, Math.Min(720, pathPx + 96));
            using var md = new Form {
                Text = "VerBump", Width = formW, Height = 170,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, KeyPreview = true,
                BackColor = bgMid, ForeColor = fgW,
            };
            new PictureBox { Parent = md, Left = 16, Top = 16, Width = 32, Height = 32,
                Image = SystemIcons.Warning.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom };
            new Label { Parent = md, Left = 56, Top = 12, Width = formW - 72, Height = 80,
                Text = noFilesMsg, Font = msgFont, ForeColor = fgW };
            var mdYes = new Button { Parent = md, Text = L.T("btn.yes", "Ja"),
                Left = formW - 202, Top = 96, Width = 88, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = fgW,
                DialogResult = DialogResult.Yes };
            new Button { Parent = md, Text = L.T("btn.no", "Nein"),
                Left = formW - 108, Top = 96, Width = 88, Height = 28,
                FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW,
                DialogResult = DialogResult.No };
            md.AcceptButton = mdYes;
            md.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.C) { Clipboard.SetText(noFilesMsg); e.Handled = true; }
            };
            if (md.ShowDialog() == DialogResult.Yes) ShowSettingsDialog(form, settings, jsonPath);
            return;
        }


        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 65, BackColor = bgMid };
        bottomPanel.Controls.Add(btnOk);
        bottomPanel.Controls.Add(btnCan);
        Button btnBypass = null;
        if (hookBypassAllowed) {
            btnBypass = new Button {
                Text = L.T("btn.bypass_hook"),
                Top = 15, Width = 130, Height = 35,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(160, 100, 0), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F),
            };
            btnBypass.Click += (s, e) => Environment.Exit(0);
            bottomPanel.Controls.Add(btnBypass);
        }
        // Reposition buttons flush to the right edge on every layout pass.
        bottomPanel.Layout += (s, e) => {
            int r = bottomPanel.ClientSize.Width - 10;
            btnOk.Left  = r - btnOk.Width;
            btnCan.Left = btnOk.Left - 10 - btnCan.Width;
            if (btnBypass != null)
                btnBypass.Left = btnCan.Left - 10 - btnBypass.Width;
        };
        bool showOrange = true, showGreen = true;
        Action applyFilter = null;
        ToolStripMenuItem menuSync = null;

        // ── MenuStrip ──────────────────────────────────────────────────────────
        var menuStrip  = new MenuStrip {
            Dock      = DockStyle.Top,
            BackColor = bgMid,
            ForeColor = fgW,
            Renderer  = new ToolStripProfessionalRenderer(new ThemedColorTable()),
            Font      = new Font("Segoe UI", 9F),
        };
        Color darkBg = bgMid;

        // ── Datei ──
        var menuFile     = new ToolStripMenuItem(L.T("menu.file"))           { ForeColor = fgW };
        var menuEditSett = new ToolStripMenuItem(L.T("menu.edit_settings"))  { ForeColor = fgW, BackColor = darkBg };
        var menuLoadSett = new ToolStripMenuItem(L.T("menu.load_settings"))  { ForeColor = fgW, BackColor = darkBg };
        var menuLoadVer  = new ToolStripMenuItem(L.T("menu.load_version"))   { ForeColor = fgW, BackColor = darkBg };
        var menuExit     = new ToolStripMenuItem(L.T("menu.exit"))           { ForeColor = fgW, BackColor = darkBg };
        menuFile.DropDownItems.AddRange(new ToolStripItem[] {
            menuLoadSett, menuLoadVer, new ToolStripSeparator(), menuExit });

        // ── Bearbeiten ──
        var menuEdit   = new ToolStripMenuItem(L.T("menu.edit"))             { ForeColor = fgW };
        var menuUndo   = new ToolStripMenuItem(L.T("menu.undo"))             { ForeColor = fgW, BackColor = darkBg,
                             ShortcutKeys = Keys.Control | Keys.Z, Enabled = false };
        menuSync       = new ToolStripMenuItem(L.T("toolbar.sync_versions")) { ForeColor = fgW, BackColor = darkBg,
                             Enabled = uiEntries.Count(u => u.SelectionPanel.Visible) > 1 };
        menuEdit.DropDownItems.AddRange(new ToolStripItem[] { menuUndo, new ToolStripSeparator(), menuSync, new ToolStripSeparator(), menuEditSett });

        // ── Ansicht ──
        var menuView       = new ToolStripMenuItem(L.T("menu.view"))         { ForeColor = fgW };
        var menuShowOrange = new ToolStripMenuItem(L.T("menu.show_stale"))   { ForeColor = fgW, BackColor = darkBg,
                                 Checked = true, CheckOnClick = true };
        var menuShowGreen  = new ToolStripMenuItem(L.T("menu.show_current")) { ForeColor = fgW, BackColor = darkBg,
                                 Checked = true, CheckOnClick = true };
        menuView.DropDownItems.AddRange(new ToolStripItem[] { menuShowOrange, menuShowGreen });

        // ── ? ──
        var menuHelp    = new ToolStripMenuItem("?") { ForeColor = fgW };
        var menuWebsite = new ToolStripMenuItem(L.T("menu.website")) { ForeColor = fgW, BackColor = darkBg };
        var menuSponsor = new ToolStripMenuItem(L.T("info.sponsor"))            { ForeColor = Color.FromArgb(255, 120, 150), BackColor = darkBg };
        var menuReport  = new ToolStripMenuItem(L.T("info.report"))             { ForeColor = Color.FromArgb(255, 190, 80),  BackColor = darkBg };
        var menuSource  = new ToolStripMenuItem(L.T("info.source"))             { ForeColor = Color.FromArgb(130, 180, 255), BackColor = darkBg };
        var menuAbout   = new ToolStripMenuItem(L.T("menu.about"))              { ForeColor = fgW, BackColor = darkBg };
        menuWebsite.Click += (s, e) => OpenUrl("https://mbaas2.github.io/VerBump/");
        menuSponsor.Click += (s, e) => OpenUrl("https://github.com/sponsors/mbaas2");
        menuReport.Click  += (s, e) => OpenUrl("https://github.com/mbaas2/VerBump/issues");
        menuSource.Click  += (s, e) => OpenUrl("https://github.com/mbaas2/VerBump");
        menuHelp.DropDownItems.AddRange(new ToolStripItem[] {
            menuWebsite, menuSponsor, menuReport, menuSource,
            new ToolStripSeparator(), menuAbout });

        menuStrip.Items.AddRange(new ToolStripItem[] { menuFile, menuEdit, menuView, menuHelp });

        // ── updateUndoItem (forward-declared — must be assigned before first bump) ──
        updateUndoItem = () => {
            bool hasUndo = undoStack.Count > 0;
            menuUndo.Enabled = hasUndo;
            menuUndo.Text = hasUndo
                ? L.T("menu.undo") + ": " + undoStack.Peek().label
                : L.T("menu.undo");
        };

        // ── Datei handlers ──
        menuEditSett.Click += (s, e) => ShowSettingsDialog(form, settings, jsonPath);

        void LoadSettingsFile(string path) {
            path = NormalizeHistoryPath(path);
            OverrideSettingsPath = path;
            AddToHistory(appConfig.RecentSettings, path, appConfig.HistoryMaxLength);
            SaveAppConfig(appConfig);
            ShouldRestart = true;
            form.Close();
        }

        void LoadVersionFile(string path) {
            path = NormalizeHistoryPath(path);
            InitialVersionPaths.Clear();
            InitialVersionPaths.Add(path);
            OverrideSettingsPath = null;
            AddToHistory(appConfig.RecentVersions, path, appConfig.HistoryMaxLength);
            SaveAppConfig(appConfig);
            ShouldRestart = true;
            form.Close();
        }

        void RebuildSettingsMenu() {
            menuLoadSett.DropDownItems.Clear();
            var colFg  = fgW;
            var colDim = Theme.FgMuted;
            var browse = new ToolStripMenuItem(L.T("toolbar.load_browse")) { ForeColor = colFg, BackColor = darkBg };
            browse.Click += (s, e) => {
                using var ofd = new OpenFileDialog { Filter = "JSON|*.json", Title = L.T("menu.load_settings"), FileName = "settings.json" };
                if (ofd.ShowDialog(form) == DialogResult.OK) LoadSettingsFile(ofd.FileName);
            };
            menuLoadSett.DropDownItems.Add(browse);
            if (appConfig.FavoriteSettings.Count > 0) {
                menuLoadSett.DropDownItems.Add(new ToolStripSeparator());
                foreach (var fav in appConfig.FavoriteSettings) {
                    string favPath = fav.Path;
                    var favItem = new ToolStripMenuItem($"\u2605 {fav.Label}") {
                        ForeColor = Color.FromArgb(255, 200, 60), BackColor = darkBg, ToolTipText = favPath };
                    favItem.Click += (s, e) => LoadSettingsFile(favPath);
                    menuLoadSett.DropDownItems.Add(favItem);
                }
            }
            if (appConfig.RecentSettings.Count > 0) {
                menuLoadSett.DropDownItems.Add(new ToolStripSeparator());
                foreach (var p in appConfig.RecentSettings) {
                    string lbl2    = p.Length > 65 ? "\u2026" + p[^62..] : p;
                    string current = p;
                    bool isCur = string.Equals(p, jsonPath, StringComparison.OrdinalIgnoreCase);
                    var item = new ToolStripMenuItem(lbl2) { ForeColor = isCur ? colDim : colFg, BackColor = darkBg, ToolTipText = p };
                    item.Click += (s, e) => LoadSettingsFile(current);
                    menuLoadSett.DropDownItems.Add(item);
                }
            }
            menuLoadSett.DropDownItems.Add(new ToolStripSeparator());
            bool alreadyFav = jsonPath != null && appConfig.FavoriteSettings.Any(f =>
                string.Equals(f.Path, jsonPath, StringComparison.OrdinalIgnoreCase));
            var addFav = new ToolStripMenuItem(L.T("menu.add_favorite")) {
                ForeColor = Color.FromArgb(255, 200, 60), BackColor = darkBg, Enabled = jsonPath != null && !alreadyFav };
            addFav.Click += (s, e) => {
                if (jsonPath == null) return;
                string lbl3 = Path.GetFileName(Path.GetDirectoryName(jsonPath)) ?? jsonPath;
                appConfig.FavoriteSettings.Add(new FavoriteEntry { Label = lbl3, Path = jsonPath });
                SaveAppConfig(appConfig);
            };
            menuLoadSett.DropDownItems.Add(addFav);
        }

        void RebuildVersionMenu() {
            menuLoadVer.DropDownItems.Clear();
            var colFg  = fgW;
            var browse = new ToolStripMenuItem(L.T("toolbar.load_browse")) { ForeColor = colFg, BackColor = darkBg };
            browse.Click += (s, e) => {
                using var ofd = new OpenFileDialog { Filter = "VERSION|VERSION|All files|*.*", Title = L.T("menu.load_version"), FileName = "VERSION" };
                if (ofd.ShowDialog(form) == DialogResult.OK) LoadVersionFile(ofd.FileName);
            };
            menuLoadVer.DropDownItems.Add(browse);
            if (appConfig.FavoriteVersions.Count > 0) {
                menuLoadVer.DropDownItems.Add(new ToolStripSeparator());
                foreach (var fav in appConfig.FavoriteVersions) {
                    string favPath = fav.Path;
                    var favItem = new ToolStripMenuItem($"\u2605 {fav.Label}") {
                        ForeColor = Color.FromArgb(255, 200, 60), BackColor = darkBg, ToolTipText = favPath };
                    favItem.Click += (s, e) => LoadVersionFile(favPath);
                    menuLoadVer.DropDownItems.Add(favItem);
                }
            }
            if (appConfig.RecentVersions.Count > 0) {
                menuLoadVer.DropDownItems.Add(new ToolStripSeparator());
                foreach (var p in appConfig.RecentVersions) {
                    string lbl2    = p.Length > 65 ? "\u2026" + p[^62..] : p;
                    string current = p;
                    var item = new ToolStripMenuItem(lbl2) { ForeColor = colFg, BackColor = darkBg, ToolTipText = p };
                    item.Click += (s, e) => LoadVersionFile(current);
                    menuLoadVer.DropDownItems.Add(item);
                }
            }
            menuLoadVer.DropDownItems.Add(new ToolStripSeparator());
            string vpath = InitialVersionPaths.Count > 0 ? InitialVersionPaths[0] : null;
            bool alreadyFav = vpath != null && appConfig.FavoriteVersions.Any(f =>
                string.Equals(f.Path, vpath, StringComparison.OrdinalIgnoreCase));
            var addFav = new ToolStripMenuItem(L.T("menu.add_favorite")) {
                ForeColor = Color.FromArgb(255, 200, 60), BackColor = darkBg, Enabled = vpath != null && !alreadyFav };
            addFav.Click += (s, e) => {
                if (vpath == null) return;
                string lbl3 = Path.GetFileName(Path.GetDirectoryName(vpath)) ?? vpath;
                appConfig.FavoriteVersions.Add(new FavoriteEntry { Label = lbl3, Path = vpath });
                SaveAppConfig(appConfig);
            };
            menuLoadVer.DropDownItems.Add(addFav);
        }

        menuLoadSett.DropDownOpening += (s, e) => RebuildSettingsMenu();
        menuLoadVer.DropDownOpening  += (s, e) => RebuildVersionMenu();
        RebuildSettingsMenu();
        RebuildVersionMenu();

        ctxAddVersionFav.Click += (s, e) => {
            if (ctxTargetIndex < 0 || ctxTargetIndex >= uiEntries.Count) return;
            string fp = uiEntries[ctxTargetIndex].FilePath;
            if (fp == null) return;
            bool alreadyFav = appConfig.FavoriteVersions.Any(
                f => string.Equals(f.Path, fp, StringComparison.OrdinalIgnoreCase));
            if (alreadyFav) {
                appConfig.FavoriteVersions.RemoveAll(
                    f => string.Equals(f.Path, fp, StringComparison.OrdinalIgnoreCase));
            } else {
                string lbl = uiEntries[ctxTargetIndex].Entry?.Name;
                if (string.IsNullOrWhiteSpace(lbl))
                    lbl = Path.GetFileName(Path.GetDirectoryName(fp)) ?? fp;
                appConfig.FavoriteVersions.Add(new FavoriteEntry { Label = lbl, Path = fp });
            }
            SaveAppConfig(appConfig);
            RebuildVersionMenu();
        };

        menuExit.Click  += (s, e) => btnCan.PerformClick();
        menuAbout.Click += (s, e) => ShowAboutDialog(form, appVersion);

        // ── Bearbeiten handlers ──
        menuUndo.Click += (s, e) => {
            if (undoStack.Count == 0) return;
            var (idx, oldVer, _) = undoStack.Pop();
            if (idx >= 0 && idx < uiEntries.Count) {
                uiEntries[idx].VersionBox.Text      = oldVer;
                uiEntries[idx].VersionBox.BackColor = bgLight;
                selectedIndex = idx;
                updateSelection();
            }
            updateUndoItem();
        };

        menuSync.Click += (s, e) => {
            var visible = uiEntries.Where(u => u.SelectionPanel.Visible).ToList();
            if (visible.Count < 2) return;
            foreach (var ui in visible)
                undoStack.Push((uiEntries.IndexOf(ui), ui.VersionBox.Text.Trim(), L.T("toolbar.sync_versions")));
            updateUndoItem();
            var maxEntry = visible
                .OrderByDescending(u => u.VersionBox.Text.Trim(), Comparer<string>.Create(CompareVersionStrings))
                .First();
            string maxVer = maxEntry.VersionBox.Text.Trim();
            foreach (var ui in visible.Where(u => u != maxEntry)) {
                string synced = ui.Scheme.SyncFrom(maxVer, ui.VersionBox.Text.Trim());
                if (ui.VersionBox.Text.Trim() == synced) continue;
                ui.VersionBox.Text      = synced;
                ui.VersionBox.BackColor = Color.DarkGreen;
            }
        };

        // ── Ansicht handlers ──
        menuShowOrange.Click += (s, e) => { showOrange = menuShowOrange.Checked; applyFilter(); };
        menuShowGreen.Click  += (s, e) => { showGreen  = menuShowGreen.Checked;  applyFilter(); };

        form.Controls.Add(mainPanel);
        form.Controls.Add(bottomPanel);
        form.Controls.Add(statusPanel);

        // ── Hook-mode info banner (between menu and project rows) ──────────────
        if (CheckMode && singleVersionPath != null) {
            var bannerBg  = Color.FromArgb(90, 55, 0);
            var bannerFg  = Color.FromArgb(255, 210, 100);
            var hookBanner = new Panel {
                Dock = DockStyle.Top, Height = hookBannerH,
                BackColor = bannerBg, Padding = new Padding(10, 0, 10, 0),
            };
            string line1 = checkStagedCount >= 0
                ? L.T("hook.banner_staged", checkStagedCount)
                : L.T("hook.banner_fallback");
            var lbl1 = new Label {
                Text = line1, Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = bannerFg, BackColor = bannerBg,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 0, 0),
            };
            var lbl2 = new Label {
                Text = L.T("hook.banner_hint"), Dock = DockStyle.Top, Height = 20,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(220, 185, 100), BackColor = bannerBg,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 0, 0),
            };
            // add in reverse (DockStyle.Top stacks last-added at top)
            hookBanner.Controls.Add(lbl2);
            hookBanner.Controls.Add(lbl1);
            form.Controls.Add(hookBanner);
        }

        form.Controls.Add(menuStrip);
        form.MainMenuStrip = menuStrip;

        updateSelection = () => {
            for (int i = 0; i < uiEntries.Count; i++) {
                uiEntries[i].SelectionPanel.BackColor = (i == selectedIndex) ? selBg : Color.Transparent;
                if (i == selectedIndex) {
                    uiEntries[i].VersionBox.Focus();
                    var lbls = uiEntries[i].Scheme.GetButtonLabels();
                    var schemeNames = lbls.Where(l => l != null).Select((l, idx) => L.T("status.shortcut", idx + 1, l));
                    string backupStatus = uiEntries[i].Backup ? L.T("status.backup_on") : L.T("status.backup_off");
                    string fmtDisplay = SchemeFactory.FormatFor(uiEntries[i].Entry);
                    setStatus($"{fmtDisplay}   {string.Join("  ", schemeNames)}   {backupStatus}", false);
                }
            }
            updateUndoItem();
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
            int visCount = uiEntries.Count(u => u.SelectionPanel.Visible);
            menuSync.Enabled = visCount > 1;
        };

        statusTimer.Tick += (s, e) => {
            statusTimer.Stop();
            updateSelection(); // Normalzustand wiederherstellen
        };

        form.KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; btnCan.PerformClick(); return; }
            if (e.KeyCode == Keys.Return) { e.Handled = true; e.SuppressKeyPress = true; btnOk.PerformClick(); return; }

            if (e.Control && e.KeyCode == Keys.I) {
                e.Handled = true; e.SuppressKeyPress = true;
                if (selectedIndex >= 0 && selectedIndex < uiEntries.Count) {
                    var ui = uiEntries[selectedIndex];
                    string tip = ui.StaleInfo;
                    if (!string.IsNullOrEmpty(tip))
                        MessageBox.Show(tip, ui.FilePath, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else if (ui.HasIssues == false)
                        MessageBox.Show(L.T("status.ok"), ui.FilePath, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            if (e.KeyCode == Keys.F2 && selectedIndex >= 0 && selectedIndex < uiEntries.Count) {
                var ui = uiEntries[selectedIndex];
                if (!ui.IsUnsaved && jsonPath != null) {
                    int idx = settings.Paths.IndexOf(ui.Entry);
                    ShowSettingsDialog(form, settings, jsonPath, Math.Max(0, idx));
                }
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
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
                        setStatus(L.T("error.bump_unavailable", part + 1, SchemeFactory.FormatFor(settings.Paths[selectedIndex])), true);
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

            // ── Update-Check (feuert im Hintergrund, blockiert nichts) ────────────
            Task.Run(async () => {
                try {
                    using var http = new System.Net.Http.HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(8);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("VerBump/" + appVersion);
                    var json = await http.GetStringAsync(
                        "https://api.github.com/repos/mbaas2/VerBump/releases/latest");
                    using var doc  = System.Text.Json.JsonDocument.Parse(json);
                    string tag     = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                    string latest  = tag.TrimStart('v');
                    if (Version.TryParse(latest, out var vLatest) &&
                        Version.TryParse(appVersion, out var vCurrent) &&
                        vLatest > vCurrent) {
                        string url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
                        form.BeginInvoke(new Action(() =>
                            ShowUpdateToast(form, latest, url)));
                    }
                } catch { /* kein Netz, kein Problem */ }
            });

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
                            uiCaptured.StatusStrip.Width = 8;
                            string tip = L.T("status.newer_files", newerFiles.Count) + "\n" +
                                         string.Join("\n", newerFiles);
                            uiCaptured.StaleInfo = tip;
                            toolTip.SetToolTip(uiCaptured.StatusStrip, tip);
                        } else {
                            uiCaptured.HasIssues = false;
                            uiCaptured.StaleInfo  = null;
                            uiCaptured.StatusStrip.BackColor = Color.FromArgb(0, 170, 80);
                            uiCaptured.StatusStrip.Width = 5;
                            toolTip.SetToolTip(uiCaptured.StatusStrip, null);
                        }
                        done++;
                        applyFilter();
                        if (done == total) updateSelection(); // Statuszeile wiederherstellen
                    }));
                });
            }
        };

                        // ── Screenshot mode ───────────────────────────────────────────────────
#if DEMO
        if (ScreenshotDir != null) {
            string scDir  = ScreenshotDir;
            string scVer  = appVersion;
            string scLang = L.Lang;
            form.TopMost  = true;
            form.Shown += async (s, e) => {
                await Task.Delay(400); // let staleness-check painting finish
                Directory.CreateDirectory(scDir);
                form.TopMost = true;
                Application.DoEvents();

                // Select target row and position cursor on the list token (if any)
                TokenHint dropHint  = null;
                Rectangle anchorScr = default;
                if (ScreenshotRow >= 0 && ScreenshotRow < uiEntries.Count) {
                    selectedIndex = ScreenshotRow; updateSelection();
                    var ui = uiEntries[ScreenshotRow];
                    string snapVer = ui.VersionBox.Text;
                    for (int p = snapVer.Length; p >= 0; p--) {
                        var th = ui.Scheme.GetTokenAt(snapVer, p);
                        if (th?.IsList == true && th.Values?.Length > 0) { dropHint = th; break; }
                    }
                    // Cursor at end of list token (= end of text when token is empty-matched)
                    int cursorPos = dropHint != null ? dropHint.Start + dropHint.Length : snapVer.Length;
                    ui.VersionBox.Focus();
                    ui.VersionBox.SelectionLength = 0;
                    ui.VersionBox.SelectionStart  = cursorPos;
                    Application.DoEvents(); // commit selection for PrintWindow
                    anchorScr = ui.VersionBox.RectangleToScreen(ui.VersionBox.ClientRectangle);
                }

                string mainPath = Path.Combine(scDir, $"main-{scLang}-{scVer}.png");
                SaveCompositeScreenshot(mainPath, Color.White, form);
                form.TopMost = false;

                // Paint the dropdown as a bitmap overlay (avoids all WinForms popup-capture issues)
                if (dropHint != null)
                    PaintDropdownOnPng(mainPath, form.Bounds, anchorScr, dropHint.Label, dropHint.Values);

                // open settings dialog (it will auto-close and screenshot itself)
                ShowSettingsDialog(form, settings, jsonPath, ScreenshotEntry);
                // write current.js so the website knows which version to load
                File.WriteAllText(Path.Combine(scDir, "current.js"),
                    $"var VERBUMP_VERSION = \"{scVer}\";\n");
                form.DialogResult = DialogResult.Cancel;
                form.Close();
            };
        }
#endif

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

        // ── Check mode: re-check after UI and signal the hook ─────────────────
        if (CheckMode && singleVersionPath != null) {
            string vf         = Path.GetFullPath(singleVersionPath);
            string projectDir = Path.GetDirectoryName(vf);
            // Was VERSION modified after the last commit?  (i.e. user just bumped it)
            var lastCommit = GetLastGitCommitTime(projectDir);
            if (lastCommit.HasValue) {
                bool bumped = File.GetLastWriteTimeUtc(vf) > lastCommit.Value;
                if (bumped) {
                    // Stage VERSION so the bump lands in the same commit ("Bump & Commit")
                    TryRunGit(projectDir, $"add \"{vf}\"");
                    // Option C: offer to auto-tag HEAD after the commit via post-commit hook
                    string newVer = "";
                    try { newVer = File.ReadAllText(vf).Trim(); } catch { }
                    if (newVer.Length > 0) {
                        string tagName = newVer.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                            ? newVer : "v" + newVer;
                        var ask = MessageBox.Show(
                            L.T("tag.prompt_hook", tagName),
                            "VerBump",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2);
                        if (ask == DialogResult.Yes) {
                            if (!HasGitPostHook(projectDir)) InstallGitPostHook(projectDir);
                            string gitDir = FindGitDir(projectDir) ?? Path.Combine(projectDir, ".git");
                            try { File.WriteAllText(Path.Combine(gitDir, "VERBUMP_PENDING_TAG"), newVer); }
                            catch { }
                        }
                    }
                }
                Environment.Exit(bumped ? 0 : 1);
            } else {
                // Fallback: no git → mtime comparison
                var newerFiles = GetNewerFiles(vf, 1, checkIgnoreDirs, checkIgnoreFiles);
                Environment.Exit(newerFiles.Count > 0 ? 1 : 0);
            }
        }
    }

    #if DEMO
[System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    // Capture one or more windows (Forms, ContextMenuStrips, …) into a single PNG.
    // The bounding box of all visible windows is used; gaps are filled with 'fill'.
    static void SaveCompositeScreenshot(string path, Color fill, params Control[] windows) {
        const uint PW_RENDERFULLCONTENT = 2;
        Application.DoEvents();
        var visible = windows.Where(w => w != null && w.IsHandleCreated && w.Visible).ToList();
        if (visible.Count == 0) return;
        int left   = visible.Min(w => w.Bounds.Left);
        int top    = visible.Min(w => w.Bounds.Top);
        int right  = visible.Max(w => w.Bounds.Right);
        int bottom = visible.Max(w => w.Bounds.Bottom);
        using var bmp = new Bitmap(right - left, bottom - top, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(fill);
        foreach (var w in visible) {
            var b = w.Bounds;
            using var wb = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var wg = Graphics.FromImage(wb);
            if (w is ContextMenuStrip) {
                // PrintWindow cannot reliably capture popup/layered windows like ContextMenuStrip;
                // use CopyFromScreen instead (reads pixels directly from the screen).
                wg.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
            } else {
                IntPtr hdc = wg.GetHdc();
                PrintWindow(w.Handle, hdc, PW_RENDERFULLCONTENT);
                wg.ReleaseHdc(hdc);
            }
            g.DrawImage(wb, b.Left - left, b.Top - top);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Paint a CMS-style dropdown menu onto an already-saved PNG (avoids WinForms popup-capture issues).
    static void PaintDropdownOnPng(string pngPath, Rectangle formScreenRect,
                                   Rectangle anchorScreenRect, string label, string[] items) {
        Bitmap bmp;
        using (var fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            bmp = new Bitmap(fs);
        using (bmp)
        using (var g = Graphics.FromImage(bmp)) {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var fMenu = new Font("Segoe UI", 9F);
            Color bgColor  = Theme.BgMid;
            Color sepColor = Theme.Sep;
            Color fgColor  = Theme.Fg;
            Color dimColor = Theme.FgMuted;
            int itemH = 22, padX = 22, padY = 2;

            // Measure menu width from content
            int menuW = 0;
            foreach (var item in items) {
                int w = (int)Math.Ceiling(g.MeasureString(item.Length > 0 ? item : "(none)", fMenu).Width) + padX + 12;
                if (w > menuW) menuW = w;
            }
            menuW = Math.Max(menuW,
                (int)Math.Ceiling(g.MeasureString($"— {label} —", fMenu).Width) + padX + 12);
            menuW = Math.Max(menuW, 110);

            int menuH = itemH + 5 + items.Length * itemH + padY * 2;

            // Bitmap coordinates: just below the anchor (version text box)
            int bx = anchorScreenRect.Left - formScreenRect.Left;
            int by = anchorScreenRect.Bottom - formScreenRect.Top;
            // Clamp so the menu stays inside the bitmap
            bx = Math.Max(0, Math.Min(bx, bmp.Width  - menuW));
            by = Math.Max(0, Math.Min(by, bmp.Height - menuH));

            // Drop-shadow
            using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillRectangle(shadow, bx + 3, by + 3, menuW, menuH);
            // Background
            using var bgBrush = new SolidBrush(bgColor);
            g.FillRectangle(bgBrush, bx, by, menuW, menuH);
            // Border
            using var borderPen = new Pen(sepColor);
            g.DrawRectangle(borderPen, bx, by, menuW - 1, menuH - 1);

            int y = by + padY;
            // Header (disabled label)
            using var dimBrush = new SolidBrush(dimColor);
            g.DrawString($"— {label} —", fMenu, dimBrush, bx + padX, y + 1);
            y += itemH;
            // Separator
            using var sepPen = new Pen(sepColor);
            g.DrawLine(sepPen, bx + 1, y + 2, bx + menuW - 2, y + 2);
            y += 5;
            // Items
            using var fgBrush = new SolidBrush(fgColor);
            foreach (var item in items) {
                g.DrawString(item.Length > 0 ? item : "(none)", fMenu, fgBrush, bx + padX, y + 1);
                y += itemH;
            }
            bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

#endif

    static void ShowSettingsDialog(Form owner, Settings settings, string jsonPath, int initialEntryIndex = 0) {
        var appCfg = LoadAppConfig();

        var entries = settings.Paths.Select(e => new ProjectEntry {
            Path = e.Path, Name = e.Name, Icon = e.Icon, Scheme = e.Scheme,
            Format = e.Format, ResetOnBump = e.ResetOnBump, Backup = e.Backup,
            IgnoreDirs  = new List<string>(e.IgnoreDirs  ?? []),
            IgnoreFiles = new List<string>(e.IgnoreFiles ?? []),
        }).ToList();
        var globalIgnoreDirs  = new List<string>(settings.IgnoreDirs  ?? []);
        var globalIgnoreFiles = new List<string>(settings.IgnoreFiles ?? []);
        var globalLists = new Dictionary<string, string[]>(
            settings.Lists ?? new Dictionary<string, string[]>(), StringComparer.OrdinalIgnoreCase);

        Color bgDark  = Theme.BgDark;
        Color bgMid   = Theme.BgMid;
        Color bgLight = Theme.BgLight;
        Color bgBtn   = Theme.BgBtn;
        Color fgW     = Theme.Fg;
        Font  fUI     = new Font("Segoe UI", 9F);
        Font  fMono   = new Font("Consolas", 9F);

        using var dlg = new Form {
            Text = L.T("settings.title"), Width = 620, Height = 761,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = bgMid, ForeColor = fgW,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
        };

        static string ListToText(List<string> list) => string.Join("\r\n", list ?? []);
        static List<string> TextToList(string text) =>
            (text ?? "").Split('\n').Select(s => s.Trim('\r', ' ')).Where(s => s.Length > 0).ToList();
        static string DictToListsText(Dictionary<string, string[]> d) =>
            string.Join("\r\n", (d ?? new Dictionary<string, string[]>())
                .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"));
        // Expands "{N-M}" ranges within a single list entry.
        // "-alpha{1-3}" → ["-alpha1", "-alpha2", "-alpha3"]
        // "-rc{01-03}"  → ["-rc01", "-rc02", "-rc03"]  (zero-padded)
        static IEnumerable<string> ExpandListEntry(string val) {
            var m = System.Text.RegularExpressions.Regex.Match(val, @"^(.*)\{(\d+)-(\d+)\}(.*)$");
            if (!m.Success) { yield return val; yield break; }
            string pre = m.Groups[1].Value, fromS = m.Groups[2].Value,
                   toS = m.Groups[3].Value, suf   = m.Groups[4].Value;
            if (!int.TryParse(fromS, out int from) || !int.TryParse(toS, out int to) || from > to)
                { yield return val; yield break; }
            int width = (fromS.Length > 1 && fromS[0] == '0') ? fromS.Length : 0;
            for (int i = from; i <= to; i++)
                yield return pre + (width > 0 ? i.ToString().PadLeft(width, '0') : i.ToString()) + suf;
        }

        static Dictionary<string, string[]> ListsTextToDict(string text) {
            var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (text ?? "").Split('\n').Select(s => s.Trim('\r', ' '))) {
                int ci = line.IndexOf(':');
                if (ci <= 0) continue;
                var name = line[..ci].Trim();
                var vals = line[(ci + 1)..].Split(',')
                    .Select(s => s.Trim())
                    .SelectMany(s => s.Length > 0 ? ExpandListEntry(s) : new[] { "" })
                    .ToArray();
                if (name.Length > 0 && vals.Any(v => v.Length > 0)) d[name] = vals;
            }
            return d;
        }

        // ── Global ignore ──────────────────────────────────────────────────────
        var grpGlobal = new GroupBox {
            Parent = dlg, Text = L.T("settings.group_global"), Left = 12, Top = 8, Width = 576, Height = 192,
            Font = fUI, ForeColor = Theme.FgDim,
        };
        new Label { Parent = grpGlobal, Text = L.T("settings.ignore_dirs"),  Left = 8, Top = 22, Width = 104, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var tbGlobalIgnoreDirs  = new TextBox { Parent = grpGlobal, Left = 116, Top = 20, Width = 448, Height = 36, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = ListToText(globalIgnoreDirs) };
        new Label { Parent = grpGlobal, Text = L.T("settings.ignore_files"), Left = 8, Top = 68, Width = 104, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var tbGlobalIgnoreFiles = new TextBox { Parent = grpGlobal, Left = 116, Top = 66, Width = 448, Height = 36, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = ListToText(globalIgnoreFiles) };
        new Label { Parent = grpGlobal, Text = L.T("settings.lists"), Left = 8, Top = 116, Width = 104, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var tbGlobalLists = new TextBox { Parent = grpGlobal, Left = 116, Top = 114, Width = 418, Height = 36, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = DictToListsText(settings.Lists) };
        var btnListsHelp = new Button {
            Parent = grpGlobal, Text = "?", Left = 538, Top = 122, Width = 26, Height = 23,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = fgW, Font = fUI,
        };
        btnListsHelp.Click += (s, e) =>
            ToggleHelpWindow(L.T("settings.lists"), L.T("settings.lists_help"), tbGlobalLists, dlg, btnListsHelp);
        dlg.FormClosed += (s, e) => { if (_helpWindow != null && !_helpWindow.IsDisposed) _helpWindow.Close(); };
        var tipLists = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };
        tipLists.SetToolTip(tbGlobalLists, L.T("settings.lists_tip"));
        new Label { Parent = grpGlobal, Text = L.T("settings.history"), Left = 8, Top = 162, Width = 82, Height = 20,
            TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        var nudHistory = new NumericUpDown { Parent = grpGlobal, Left = 96, Top = 160, Width = 60, Height = 23,
            Minimum = 1, Maximum = 20, Value = appCfg.HistoryMaxLength,
            BackColor = bgLight, ForeColor = fgW, Font = fMono };
        new Label { Parent = grpGlobal, Left = 162, Top = 163, Width = 280, Height = 18,
            Text = L.T("settings.history_help"), Font = fUI, ForeColor = Color.FromArgb(160, 160, 165) };

        // ── Project list ──────────────────────────────────────────────────────
        new Label { Parent = dlg, Text = L.T("settings.projects"), Left = 12, Top = 210, Width = 100, Height = 18,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = fgW };

        Button SmallBtn(string t, int x) => new Button {
            Parent = dlg, Text = t, Left = x, Top = 204, Width = 32, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW,
            Font = new Font("Segoe UI", 10F) };
        var btnAdd    = SmallBtn("+",  452);
        var btnRemove = SmallBtn("−",  488);
        var btnUp     = SmallBtn("↑",  524);
        var btnDown   = SmallBtn("↓",  558);

        var lstEntries = new ListBox {
            Parent = dlg, Left = 12, Top = 232, Width = 576, Height = 90,
            BackColor = bgDark, ForeColor = fgW, Font = fUI,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // ── Detail group ──────────────────────────────────────────────────────
        var grp = new GroupBox {
            Parent = dlg, Text = L.T("settings.entry"), Left = 12, Top = 330, Width = 576, Height = 335,
            Font = fUI, ForeColor = Theme.FgDim,
        };

        Label   GrpLbl(string t, int y, int w = 82) => new Label   { Parent = grp, Text = t, Left = 8, Top = y + 3, Width = w, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = fUI, ForeColor = fgW };
        TextBox GrpTb (int y, int w)    => new TextBox  { Parent = grp, Left = 96, Top = y, Width = w, Height = 23, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono };
        Button  BrowseBtn(int y)        => new Button   { Parent = grp, Text = "…", Left = 472, Top = y, Width = 90, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW, Font = fUI };

        GrpLbl(L.T("settings.path"),    22); var tbPath   = GrpTb(22, 368); var btnBrowsePath = BrowseBtn(22);
        var lblStatus = new Label { Parent = grp, Left = 96, Top = 50, Width = 460, Height = 18, Font = new Font("Segoe UI", 8.5F) };
        GrpLbl(L.T("settings.name"),    76); var tbName   = GrpTb(76, 460);
        GrpLbl(L.T("settings.icon"),   108); var tbIcon   = GrpTb(108, 368); var btnBrowseIcon = BrowseBtn(108);
        var cbBackup = new CheckBox { Parent = grp, Text = L.T("settings.backup"), Left = 430, Top = 140, Width = 90, Font = fUI, ForeColor = fgW };
        var tipFmt   = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        var lblFormat = GrpLbl(L.T("settings.format"), 138); var tbFormat = GrpTb(138, 280);
        var btnFormatHelp = new Button {
            Parent = grp, Text = "?", Left = 382, Top = 138, Width = 26, Height = 23,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 70, 70), ForeColor = fgW, Font = fUI,
        };
        var lblPreview = new Label {
            Parent = grp, Left = 96, Top = 163, Width = 380, Height = 17,
            Font = new Font("Segoe UI", 8F, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 170, 100),
        };
        tipFmt.SetToolTip(tbFormat, L.T("settings.format_tip"));

        // ── Per-project ignore ─────────────────────────────────────────────────
        new Label {
            Parent = grp, Left = 8, Top = 187, Width = 556, Height = 14,
            Text = L.T("settings.per_project_ignore"),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 140, 145),
        };
        var tipIgnore = new ToolTip();
        tipIgnore.SetToolTip(GrpLbl(L.T("settings.ignore_dirs"),  203, 104), L.T("settings.ignore_dirs_tip"));
        var tbIgnoreDirs  = new TextBox { Parent = grp, Left = 116, Top = 201, Width = 440, Height = 38, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical };
        tipIgnore.SetToolTip(tbIgnoreDirs, L.T("settings.ignore_dirs_tip"));
        tipIgnore.SetToolTip(GrpLbl(L.T("settings.ignore_files"), 247, 104), L.T("settings.ignore_files_tip"));
        var tbIgnoreFiles = new TextBox { Parent = grp, Left = 116, Top = 245, Width = 440, Height = 38, BackColor = bgLight, ForeColor = fgW, BorderStyle = BorderStyle.FixedSingle, Font = fMono, Multiline = true, ScrollBars = ScrollBars.Vertical };
        tipIgnore.SetToolTip(tbIgnoreFiles, L.T("settings.ignore_files_tip"));

        // ── Git hook ──────────────────────────────────────────────────────────
        var btnHook = new Button {
            Parent = grp, Left = 96, Top = 295, Width = 220, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW, Font = fUI,
            Text = L.T("hook.install"),
        };
        var lblHookInfo = new Label {
            Parent = grp, Left = 326, Top = 298, Width = 226, Height = 20,
            Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(130, 130, 135),
            Text = L.T("hook.label"),
        };
        // ── Save / Cancel ─────────────────────────────────────────────────────
        var btnCancel = new Button { Parent = dlg, Text = L.T("btn.cancel"), Left = 396, Top = 675, Width = 100, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = bgBtn, ForeColor = fgW, Font = fUI, DialogResult = DialogResult.Cancel };
        var btnSave   = new Button { Parent = dlg, Text = L.T("btn.save"),   Left = 504, Top = 675, Width = 100, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 122, 204), ForeColor = fgW, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        new ToolTip().SetToolTip(btnSave,   L.T("btn.save_tip"));
        new ToolTip().SetToolTip(btnCancel, L.T("btn.cancel_tip"));
        dlg.CancelButton = btnCancel;
        dlg.AcceptButton = btnSave;
        dlg.KeyPreview = true;
        dlg.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.Enter) {
                btnSave.PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        // ── Logic ─────────────────────────────────────────────────────────────
        int     current              = -1;
        bool    loading              = false;
        Action  updateFormatPreview  = () => { };

        string DisplayName(ProjectEntry e) {
            if (!string.IsNullOrWhiteSpace(e.Name)) return e.Name;
            string n = Path.GetFileName(e.Path.Trim().TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(n) ? L.T("project.new") : n;
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
                lblStatus.Text = L.T("status.dir_not_found"); return;
            }
            if (!File.Exists(Path.Combine(clean, "VERSION"))) {
                lblStatus.ForeColor = Color.FromArgb(255, 180, 0);
                lblStatus.Text = L.T("status.no_version_file"); return;
            }
            lblStatus.ForeColor = Color.FromArgb(80, 200, 80);
            lblStatus.Text = L.T("status.ok_short");
        }

        void LoadEntry(int idx) {
            if (idx < 0 || idx >= entries.Count) { grp.Enabled = false; return; }
            grp.Enabled = true;
            loading = true;
            var e = entries[idx];
            tbPath.Text   = e.Path;
            tbName.Text   = e.Name;
            tbIcon.Text   = e.Icon;
            tbFormat.Text    = SchemeFactory.FormatFor(e);
            cbBackup.Checked = e.Backup;
            tbIgnoreDirs.Text  = ListToText(e.IgnoreDirs);
            tbIgnoreFiles.Text = ListToText(e.IgnoreFiles);
            string dir = e.Path.Trim('"');
            bool hasGit = Directory.Exists(Path.Combine(dir, ".git"));
            btnHook.Enabled = hasGit;
            btnHook.Text    = hasGit && HasGitHook(dir) ? L.T("hook.remove") : L.T("hook.install");
            ValidatePath();
            loading = false;
        }

        void Flush() {
            if (loading || current < 0 || current >= entries.Count) return;
            var e = entries[current];
            e.Path        = tbPath.Text;
            e.Name        = tbName.Text;
            e.Icon        = tbIcon.Text;
            e.Format      = tbFormat.Text;
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
        tbFormat.TextChanged += (s, e) => updateFormatPreview();
        tbGlobalLists.TextChanged += (s, e) => {
            globalLists = ListsTextToDict(tbGlobalLists.Text);
            updateFormatPreview();
        };
        btnFormatHelp.Click += (s, e) =>
            ToggleHelpWindow(L.T("settings.format"), L.T("settings.format_tip"), tbFormat, dlg, btnFormatHelp);
        updateFormatPreview = () => {
            string fmt = tbFormat.Text.Trim();
            if (string.IsNullOrEmpty(fmt)) { lblPreview.Text = ""; return; }
            try {
                string preview = new FormatScheme(fmt, globalLists).Refresh("");
                lblPreview.Text      = "→ " + preview;
                lblPreview.ForeColor = Color.FromArgb(100, 170, 100);
            } catch {
                lblPreview.Text      = L.T("settings.format_invalid");
                lblPreview.ForeColor = Color.FromArgb(200, 80, 80);
            }
        };
        btnHook.Click += (s, e) => {
            if (current < 0 || current >= entries.Count) return;
            string dir = entries[current].Path.Trim('"');
            if (!Directory.Exists(dir)) return;
            try {
                if (HasGitHook(dir)) { RemoveGitHook(dir); btnHook.Text = L.T("hook.install"); }
                else                  { InstallGitHook(dir); btnHook.Text = L.T("hook.remove"); }
            } catch (Exception ex) { MessageBox.Show(ex.Message, "VerBump"); }
        };
        cbBackup.CheckedChanged += (s, e) => { if (!loading) Flush(); };

        btnBrowsePath.Click += (s, e) => {
            using var fbd = new FolderBrowserDialog { Description = L.T("settings.select_project_folder"), UseDescriptionForTitle = true };
            if (!string.IsNullOrWhiteSpace(tbPath.Text))
                try { fbd.SelectedPath = tbPath.Text.Trim(); } catch (Exception ex) { Log.Write("Settings/browsePath", ex); }
            if (fbd.ShowDialog(dlg) == DialogResult.OK) {
                tbPath.Text = fbd.SelectedPath; Flush(); ValidatePath();
            }
        };

        btnBrowseIcon.Click += (s, e) => {
            using var ofd = new System.Windows.Forms.OpenFileDialog { Title = L.T("settings.select_icon"), Filter = L.T("settings.icon_filter") };
            if (!string.IsNullOrWhiteSpace(tbIcon.Text))
                try { ofd.InitialDirectory = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(tbIcon.Text)); } catch (Exception ex) { Log.Write("Settings/browseIcon", ex); }
            if (ofd.ShowDialog(dlg) == DialogResult.OK) { tbIcon.Text = ofd.FileName; Flush(); }
        };

        btnAdd.Click += (s, e) => {
            Flush();
            entries.Add(new ProjectEntry { Format = "[sem]", ResetOnBump = true });
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
                    string reason = !Directory.Exists(c) ? L.T("settings.reason_no_dir") : L.T("settings.reason_no_version");
                    return $"  • {DisplayName(en)}: {reason}";
                })
                .ToList();

            if (problems.Count > 0) {
                var r = MessageBox.Show(
                    L.T("settings.validation_msg", string.Join("\n", problems)),
                    L.T("settings.validation_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.No) return;
            }

            settings.Paths       = entries;
            settings.IgnoreDirs  = TextToList(tbGlobalIgnoreDirs.Text);
            settings.IgnoreFiles = TextToList(tbGlobalIgnoreFiles.Text);
            settings.Lists       = ListsTextToDict(tbGlobalLists.Text);
            appCfg.HistoryMaxLength = (int)nudHistory.Value;
            SaveAppConfig(appCfg);
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

        RefreshList(Math.Max(0, Math.Min(initialEntryIndex, entries.Count - 1)));
        LoadEntry(Math.Max(0, Math.Min(initialEntryIndex, entries.Count - 1)));
                new Label {
            Parent = dlg, Left = 12, Top = 688, Width = 592, Height = 16,
            Text = jsonPath,
            Font = new Font("Segoe UI", 7.5F),
            ForeColor = Color.FromArgb(110, 110, 115),
            AutoEllipsis = true,
        };

#if DEMO
        if (ScreenshotDir != null) {
            string scDir  = ScreenshotDir;
            string scLang = L.Lang;
            string scVer  = "";
            try {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("VerBump.VERSION");
                if (stream != null) scVer = new System.IO.StreamReader(stream).ReadToEnd().Trim();
            } catch { }
            dlg.TopMost = true;
            dlg.Shown += async (s, e) => {
                await Task.Delay(400);
                if (ScreenshotHelp) {
                    btnListsHelp.PerformClick();   // open the ? help window
                    await Task.Delay(200);         // let the window paint
                }
                // composite: dialog + help window (if open); white fill for any gap
                var settingsWindows = new List<Control> { dlg };
                if (_helpWindow != null && !_helpWindow.IsDisposed && _helpWindow.Visible)
                    settingsWindows.Add(_helpWindow);
                SaveCompositeScreenshot(Path.Combine(scDir, $"settings-{scLang}-{scVer}.png"),
                    Color.Transparent, settingsWindows.ToArray());
                dlg.DialogResult = DialogResult.Cancel;
                dlg.Close();
            };
        }
#endif

        if (dlg.ShowDialog(owner?.Visible == true ? owner : null) == DialogResult.OK) {
            if (owner?.Visible == true) { ShouldRestart = true; owner.Close(); }
        }
    }

    const string HookMarker = "# --- VerBump pre-commit ---";

    static string GitHookPath(string projectDir) =>
        Path.Combine(projectDir, ".git", "hooks", "pre-commit");

    static int CompareVersionStrings(string a, string b) {
        var pa = System.Text.RegularExpressions.Regex.Split(a ?? "", @"[^a-zA-Z0-9]+");
        var pb = System.Text.RegularExpressions.Regex.Split(b ?? "", @"[^a-zA-Z0-9]+");
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++) {
            string sa = i < pa.Length ? pa[i] : "0";
            string sb = i < pb.Length ? pb[i] : "0";
            int c = int.TryParse(sa, out int ia) && int.TryParse(sb, out int ib)
                ? ia.CompareTo(ib)
                : string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }

    static bool HasGitHook(string projectDir) {
        string hookFile = GitHookPath(projectDir);
        return File.Exists(hookFile) && File.ReadAllText(hookFile).Contains(HookMarker);
    }

    static void InstallGitHook(string projectDir) {
        string hooksDir = Path.Combine(projectDir, ".git", "hooks");
        if (!Directory.Exists(hooksDir)) return;
        string hookFile = GitHookPath(projectDir);
        string exePath  = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName.Replace('\\', '/');
        string block    = $"\n{HookMarker}\n\"{exePath}\" --check\n{HookMarker}\n";
        if (File.Exists(hookFile)) {
            string content = File.ReadAllText(hookFile);
            if (!content.Contains(HookMarker))
                File.AppendAllText(hookFile, block);
        } else {
            File.WriteAllText(hookFile, "#!/bin/sh" + block);
        }
    }

    static void RemoveGitHook(string projectDir) {
        string hookFile = GitHookPath(projectDir);
        if (!File.Exists(hookFile)) return;
        string content = File.ReadAllText(hookFile);
        if (!content.Contains(HookMarker)) return;
        var lines   = content.Split('\n').ToList();
        bool inBlock = false;
        var result  = new List<string>();
        foreach (var line in lines) {
            if (line.TrimEnd() == HookMarker) { inBlock = !inBlock; continue; }
            if (!inBlock) result.Add(line);
        }
        string newContent = string.Join("\n", result).Trim();
        if (newContent == "#!/bin/sh" || newContent.Length == 0) File.Delete(hookFile);
        else File.WriteAllText(hookFile, newContent + "\n");
    }

    // ── Post-commit hook (auto-tagging via VERBUMP_PENDING_TAG) ───────────────

    const string PostHookMarker = "# --- VerBump post-commit ---";

    static string GitPostHookPath(string projectDir) =>
        Path.Combine(projectDir, ".git", "hooks", "post-commit");

    static bool HasGitPostHook(string projectDir) {
        string hookFile = GitPostHookPath(projectDir);
        return File.Exists(hookFile) && File.ReadAllText(hookFile).Contains(PostHookMarker);
    }

    static void InstallGitPostHook(string projectDir) {
        string hooksDir = Path.Combine(projectDir, ".git", "hooks");
        if (!Directory.Exists(hooksDir)) return;
        string hookFile = GitPostHookPath(projectDir);
        string block = $"\n{PostHookMarker}\n" +
                       "TAG_FILE=\"$(git rev-parse --git-dir)/VERBUMP_PENDING_TAG\"\n" +
                       "if [ -f \"$TAG_FILE\" ]; then\n" +
                       "  ver=$(cat \"$TAG_FILE\")\n" +
                       "  rm \"$TAG_FILE\"\n" +
                       "  git tag \"v$ver\" 2>/dev/null && echo \"VerBump: Tagged as v$ver\" || true\n" +
                       "fi\n" +
                       $"{PostHookMarker}\n";
        if (File.Exists(hookFile)) {
            if (!File.ReadAllText(hookFile).Contains(PostHookMarker))
                File.AppendAllText(hookFile, block);
        } else {
            File.WriteAllText(hookFile, "#!/bin/sh" + block);
            // Make executable on Unix-like systems (no-op on Windows but harmless)
            try { TryRunGit(projectDir, $"update-index --chmod=+x .git/hooks/post-commit"); } catch { }
        }
    }

    static void RemoveGitPostHook(string projectDir) {
        string hookFile = GitPostHookPath(projectDir);
        if (!File.Exists(hookFile)) return;
        string content = File.ReadAllText(hookFile);
        if (!content.Contains(PostHookMarker)) return;
        var lines    = content.Split('\n').ToList();
        bool inBlock = false;
        var result   = new List<string>();
        foreach (var line in lines) {
            if (line.TrimEnd() == PostHookMarker) { inBlock = !inBlock; continue; }
            if (!inBlock) result.Add(line);
        }
        string newContent = string.Join("\n", result).Trim();
        if (newContent == "#!/bin/sh" || newContent.Length == 0) File.Delete(hookFile);
        else File.WriteAllText(hookFile, newContent + "\n");
    }

    // ── Recent settings files ──────────────────────────────────────────────────

    static string AppConfigFile() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerBump", "VerBump-config.json");

    static AppConfig LoadAppConfig() {
        try {
            string f = AppConfigFile();
            if (File.Exists(f)) {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(f),
                    new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
                cfg.RecentSettings = NormalizeHistoryList(cfg.RecentSettings, cfg.HistoryMaxLength);
                cfg.RecentVersions = NormalizeHistoryList(cfg.RecentVersions, cfg.HistoryMaxLength);
                return cfg;
            }
            // Migrate from old recent-settings.json
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerBump", "recent-settings.json");
            if (File.Exists(old)) {
                var recent = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(old)) ?? new();
                return new AppConfig { RecentSettings = NormalizeHistoryList(recent) };
            }
        } catch { }
        return new();
    }

    static void SaveAppConfig(AppConfig cfg) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConfigFile()));
            File.WriteAllText(AppConfigFile(), JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        } catch { }
    }

    static VerBumpPolicy LoadPolicy() {
        try {
            string f = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "VerBump", "policy.json");
            if (File.Exists(f))
                return JsonSerializer.Deserialize<VerBumpPolicy>(File.ReadAllText(f),
                    new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
        } catch { }
        return new();   // default: AllowHookBypass = true
    }

    static string NormalizeHistoryPath(string path) {
        string trimmed = (path ?? "").Trim().Trim('"');
        if (trimmed.Length == 0) return "";
        try {
            return Path.GetFullPath(trimmed)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        } catch {
            return trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }

    static List<string> NormalizeHistoryList(IEnumerable<string> paths, int maxLen = int.MaxValue) {
        var result = new List<string>();
        foreach (string path in paths ?? []) {
            string normalized = NormalizeHistoryPath(path);
            if (normalized.Length == 0) continue;
            if (result.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(normalized);
            if (result.Count >= maxLen) break;
        }
        return result;
    }

    static void AddToHistory(List<string> list, string path, int maxLen) {
        string normalized = NormalizeHistoryPath(path);
        list.RemoveAll(p => string.Equals(NormalizeHistoryPath(p), normalized, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, normalized);
        if (list.Count > maxLen) list.RemoveRange(maxLen, list.Count - maxLen);
    }

    static void RunSilentBump(Settings settings, string versionFilePath, int partIndex) {
        string versionFile = Path.GetFullPath(versionFilePath);

        // Find matching project entry (for scheme + backup settings)
        ProjectEntry entry = settings.Paths?.FirstOrDefault(p => {
            try {
                string pv = Path.GetFullPath(Path.Combine(p.Path.Trim('"'), "VERSION"));
                return string.Equals(pv, versionFile, StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }) ?? new ProjectEntry(); // default: SemVer

        IVersionScheme scheme = SchemeFactory.Create(entry, settings.Lists);

        string current;
        try { current = File.ReadAllText(versionFile).Trim(); }
        catch (Exception ex) {
            MessageBox.Show(L.T("error.read_version_file", ex.Message), "VerBump", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string next = scheme.Bump(current, partIndex);

        try {
            if (entry.Backup) File.Copy(versionFile, versionFile + ".bak", true);
            File.WriteAllText(versionFile, next);
        } catch (Exception ex) {
            MessageBox.Show(L.T("error.write_version_file", ex.Message), "VerBump", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string projectName = !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : Path.GetFileName(Path.GetDirectoryName(versionFile)) ?? "?";

        ShowToast($"{current}  →  {next}", projectName);
    }

    static void ShowUpdateToast(Form owner, string latest, string url) {
        var colBg  = Color.FromArgb(28, 28, 40);
        var colFg  = Color.FromArgb(72, 199, 142);
        var colDim = Color.FromArgb(160, 160, 180);
        var screen = Screen.FromControl(owner).WorkingArea;

        var toast = new Form {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            BackColor = colBg, Width = 320, Height = 64, TopMost = true, ShowInTaskbar = false,
        };
        toast.Location = new Point(screen.Right - toast.Width - 12, screen.Bottom - toast.Height - 12);

        new Label {
            Parent = toast, Left = 12, Top = 8, Width = 296, Height = 18, ForeColor = colFg,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Text = L.T("update.available", latest),
        };
        var lnk = new LinkLabel {
            Parent = toast, Left = 12, Top = 32, Width = 200, Height = 18,
            Font = new Font("Segoe UI", 8.5F), ForeColor = colDim, LinkColor = Color.FromArgb(100, 210, 255),
            Text = L.T("update.download"),
        };
        lnk.LinkClicked += (s, e) => {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            toast.Close();
        };
        new Label {
            Parent = toast, Left = 295, Top = 4, Width = 20, Height = 16,
            ForeColor = colDim, Font = new Font("Segoe UI", 8F), Text = "✕", Cursor = Cursors.Hand,
        }.Click += (s, e) => toast.Close();

        toast.Show(owner);
        var t = new System.Windows.Forms.Timer { Interval = 8000 };
        t.Tick += (s, e) => { t.Stop(); if (!toast.IsDisposed) toast.Close(); };
        t.Start();
    }

    // ── Persistent help window (non-modal, stays open while user types) ────────
    static Form   _helpWindow = null;
    static Button _helpButton = null;
    static readonly Color _helpBtnOff = Color.FromArgb(70, 70, 70);
    static readonly Color _helpBtnOn  = Color.FromArgb(0, 100, 180);

    static void ToggleHelpWindow(string title, string content, Control anchor, Form owner, Button caller) {
        if (_helpWindow != null && !_helpWindow.IsDisposed) {
            _helpWindow.Close(); // FormClosed handler resets state
            return;
        }
        var bg = Color.FromArgb(22, 22, 32);
        var fg = Color.FromArgb(220, 220, 230);

        var win = new Form {
            Text            = title,
            Width           = 480, Height = 320,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            StartPosition   = FormStartPosition.Manual,
            ShowInTaskbar   = false,
            BackColor       = bg,
            ForeColor       = fg,
        };

        // Position to the right of the owner; fall back to left if screen too narrow
        var screen = Screen.FromControl(owner).WorkingArea;
        int x = owner.Right + 8;
        if (x + win.Width > screen.Right) x = owner.Left - win.Width - 8;
        int y = anchor != null
            ? Math.Min(anchor.PointToScreen(Point.Empty).Y, screen.Bottom - win.Height)
            : owner.Top;
        win.Location = new Point(Math.Max(screen.Left, x), Math.Max(screen.Top, y));

        var rtb = new RichTextBox {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BackColor   = bg,
            ForeColor   = fg,
            BorderStyle = BorderStyle.None,
            Font        = new Font("Consolas", 9F),
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            Padding     = new Padding(8),
            WordWrap    = false,
            Text        = content,
        };
        win.Controls.Add(rtb);

        // Mark button as active
        _helpButton = caller;
        if (caller != null) caller.BackColor = _helpBtnOn;

        owner.FormClosed += (s, e) => { if (!win.IsDisposed) win.Close(); };
        win.FormClosed   += (s, e) => {
            _helpWindow = null;
            if (_helpButton != null && !_helpButton.IsDisposed)
                _helpButton.BackColor = _helpBtnOff;
            _helpButton = null;
        };

        win.Show(owner);
        _helpWindow = win;
    }

    static void ShowToast(string message, string project) {
        var colBg  = Color.FromArgb(28, 28, 40);
        var colFg  = Color.FromArgb(72, 199, 142);
        var colDim = Color.FromArgb(160, 160, 180);

        var toast = new Form {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition   = FormStartPosition.Manual,
            Width = 300, Height = 70,
            BackColor = colBg,
            TopMost = true,
            Opacity = 0.95,
            ShowInTaskbar = false,
        };
        new Label {
            Parent = toast, AutoSize = false,
            Left = 12, Top = 8, Width = 276, Height = 24,
            Text = $"VerBump  ✓  {message}",
            ForeColor = colFg,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        new Label {
            Parent = toast, AutoSize = false,
            Left = 12, Top = 36, Width = 276, Height = 18,
            Text = project,
            ForeColor = colDim,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var screen = Screen.PrimaryScreen.WorkingArea;
        toast.Location = new Point(screen.Right - toast.Width - 16, screen.Bottom - toast.Height - 16);

        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (s, e) => { timer.Stop(); toast.Close(); };
        toast.Shown += (s, e) => timer.Start();
        toast.Click += (s, e) => toast.Close();
        foreach (Control c in toast.Controls) c.Click += (s, e) => toast.Close();

        Application.Run(toast);
    }

    static void ShowAboutDialog(Form owner, string version) {
        Color bgMid   = Theme.BgMid;
        Color fgW     = Theme.Fg;
        Color fgDim   = Theme.FgMuted;
        Color accent  = Color.FromArgb(72, 199, 142);
        Font  fUI     = new Font("Segoe UI", 9F);

        using var dlg = new Form {
            Text            = L.T("info.title"),
            Width           = 360, Height = 260,
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
            string icoPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "verbump.ico");
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
            Parent = dlg, Text = L.T("info.version", version), Left = 106, Top = 56,
            Width = 200, Height = 18, Font = fUI, ForeColor = accent,
        };
        new Label {
            Parent = dlg, Text = L.T("info.language", L.Lang.ToUpper()), Left = 106, Top = 74,
            Width = 200, Height = 18, Font = fUI, ForeColor = fgDim,
        };

        // ── Separator ──────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Left = 24, Top = 100, Width = 294, Height = 1,
            BackColor = Theme.Sep,
        };

        // ── Description ────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Text = L.T("info.description"),
            Left = 24, Top = 112, Width = 294, Height = 40,
            Font = fUI, ForeColor = fgDim, AutoSize = false, UseMnemonic = false,
        };

        // ── Separator ──────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Left = 24, Top = 158, Width = 294, Height = 1,
            BackColor = Theme.Sep,
        };

        // ── Copyright ─────────────────────────────────────────────────────────
        new Label {
            Parent = dlg, Text = $"© {DateTime.Today.Year} Michael Baas",
            Left = 24, Top = 167, Width = 200, Height = 18,
            Font = new Font("Segoe UI", 8F), ForeColor = fgDim,
        };
        var lnkMail = new LinkLabel {
            Parent = dlg, Text = "✉ verbump@mbaas.de",
            Left = 24, Top = 185, Width = 200, Height = 18,
            Font = new Font("Segoe UI", 8F), BackColor = Color.Transparent,
            LinkColor = fgDim, ActiveLinkColor = fgW,
        };
        lnkMail.Click += (s, e) => OpenUrl("mailto:verbump@mbaas.de");

        // ── Close button ──────────────────────────────────────────────────────
        var btnClose = new Button {
            Parent = dlg, Text = L.T("btn.ok"), Left = 244, Top = 170, Width = 74, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = Theme.BgLight,
            ForeColor = fgW, Font = fUI, DialogResult = DialogResult.OK,
        };
        dlg.AcceptButton = btnClose;

        dlg.ShowDialog(owner?.Visible == true ? owner : null);
    }

    static void OpenUrl(string url) {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
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

    // ── Git helpers ────────────────────────────────────────────────────────────

    static string TryRunGit(string workDir, string arguments, int timeoutMs = 5000) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo("git", arguments) {
                WorkingDirectory        = workDir,
                RedirectStandardOutput  = true,
                RedirectStandardError   = true,
                UseShellExecute         = false,
                CreateNoWindow          = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(); return null; }
            return p.ExitCode == 0 ? output : null;
        } catch { return null; }
    }

    // Fast local check — no process spawn. Walks up from dir looking for .git/.
    static string FindGitDir(string dir) {
        while (!string.IsNullOrEmpty(dir)) {
            string git = Path.Combine(dir, ".git");
            if (Directory.Exists(git)) return git;
            string parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }

    // Create a git tag for the given version, optionally push it.
    static void DoGitTag(string projectDir, string version, bool push, Form owner) {
        if (string.IsNullOrWhiteSpace(version)) return;
        string tag = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
        // Check whether tag already exists
        string existing = TryRunGit(projectDir, $"tag -l \"{tag}\"");
        if (existing != null && existing.Trim().Equals(tag, StringComparison.OrdinalIgnoreCase)) {
            MessageBox.Show(L.T("tag.exists", tag), "VerBump", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (TryRunGit(projectDir, $"tag \"{tag}\"") == null) {
            MessageBox.Show(L.T("tag.error_create"), "VerBump", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (push) {
            if (TryRunGit(projectDir, $"push origin \"{tag}\"", 30_000) == null)
                ShowToast(L.T("tag.error_push", tag), tag);
            else
                ShowToast(L.T("tag.created_pushed", tag), tag);
        } else {
            ShowToast(L.T("tag.created", tag), tag);
        }
    }

    /// Returns the absolute path of the git repository root that contains
    /// <paramref name="dir"/>, or null when not in a git repo / git unavailable.
    static string TryGetGitRoot(string dir) {
        string raw = TryRunGit(dir, "rev-parse --show-toplevel");
        return raw?.Trim().Replace('/', Path.DirectorySeparatorChar);
    }

    /// Returns the UTC time of the last git commit in <paramref name="projectDir"/>,
    /// or null when git is unavailable or there are no commits yet.
    static DateTime? GetLastGitCommitTime(string projectDir) {
        string output = TryRunGit(projectDir, "log -1 --format=%ct HEAD");
        if (output == null) return null;
        if (long.TryParse(output.Trim(), out long epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }

    /// Returns the list of staged file paths (relative to repo root) for the
    /// project at <paramref name="projectDir"/>, or null when git is unavailable.
    static List<string> GetGitStagedFiles(string projectDir) {
        string output = TryRunGit(projectDir, "diff --cached --name-only");
        if (output == null) return null;
        return output.Split('\n')
                     .Select(s => s.Trim())
                     .Where(s => s.Length > 0)
                     .ToList();
    }

    static List<string> GetNewerFiles(string versionFilePath, int max, HashSet<string> ignoreDirs, List<string> ignoreFiles) {
        var result = new List<string>();
        try {
            string projectDir     = Path.GetDirectoryName(versionFilePath);
            var gitignorePatterns = LoadGitignorePatterns(projectDir);

            // Prefer git-based threshold: last commit time
            var lastCommit = GetLastGitCommitTime(projectDir);
            if (lastCommit.HasValue) {
                // VERSION is fine if it was modified after the last commit
                if (File.GetLastWriteTimeUtc(versionFilePath) > lastCommit.Value)
                    return result; // empty = OK
                // Show files changed since last commit so the user sees what's pending
                CollectNewerFiles(projectDir, projectDir, lastCommit.Value, versionFilePath,
                                  max, result, gitignorePatterns, ignoreDirs, ignoreFiles);
                return result;
            }

            // Fallback: compare against VERSION mtime (no git available)
            DateTime versionTime = File.GetLastWriteTimeUtc(versionFilePath);
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




