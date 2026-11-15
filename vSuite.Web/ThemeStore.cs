using System.Text.Json;

namespace vSuite.Web;

/// <summary>
/// The colours the app's vShell plugin sent (theme.json in the data folder - PUT /api/v1/theme); pages use them unless Options > Look
/// says otherwise (IWebSettings.VShellColours). Layouts redraw on Changed.
/// </summary>
public sealed class ThemeStore
{
    sealed class Saved
    {
        public WebTheme Theme { get; set; }
        public DateTimeOffset At { get; set; }
        public string From { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true, PropertyNameCaseInsensitive = true };
    readonly string _file;
    Saved _saved;
    public event Action Changed;

    public ThemeStore(string file = null)
    {
        _file = file ?? Paths.File("theme.json");
        try { if (File.Exists(_file)) _saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_file), Json); } catch { _saved = null; }
    }

    /// <summary>vShell's colours, when its plugin has sent them.</summary>
    public WebTheme Sent { get { return _saved?.Theme; } }
    public DateTimeOffset? SentAt { get { return _saved?.At; } }
    public string SentFrom { get { return _saved?.From; } }

    public WebTheme For(IWebSettings s) { return s.VShellColours && Sent != null ? Sent : WebTheme.Default; }

    /// <summary>The CSS variables for these settings (the colours and the text size) - what a layout puts in its &lt;style&gt;.</summary>
    public string Css(IWebSettings s) { return For(s).Css(s.TextSize); }

    public void Set(WebTheme t, string from)
    {
        _saved = new Saved { Theme = t, At = DateTimeOffset.Now, From = from };
        try { File.WriteAllText(_file, JsonSerializer.Serialize(_saved, Json)); } catch (Exception ex) { Log.Write("Couldn't save theme.json: " + ex.Message); }
        Changed?.Invoke();
    }
}
