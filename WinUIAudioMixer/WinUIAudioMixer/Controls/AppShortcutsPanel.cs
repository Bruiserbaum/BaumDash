using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using Microsoft.Win32;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Grid of app shortcut tiles with a "+" tile to add more from installed programs.
/// </summary>
public sealed class AppShortcutsPanel : UserControl
{
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "app-shortcuts.json");

    private List<AppShortcut> _shortcuts = new();
    private readonly FlowLayoutPanel _grid;

    public AppShortcutsPanel()
    {
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);

        _grid = new FlowLayoutPanel
        {
            BackColor    = Color.Transparent,
            AutoScroll   = true,
            WrapContents = true,
            Padding      = new Padding(8, 8, 8, 8),
        };

        Controls.Add(_grid);
        Resize += (_, _) => _grid.SetBounds(0, 44, Width, Height - 44);
        _grid.SetBounds(0, 44, Width, Height - 44);

        LoadShortcuts();
        RebuildGrid();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void LoadShortcuts()
    {
        try
        {
            if (File.Exists(ConfigPath))
                _shortcuts = JsonSerializer.Deserialize<List<AppShortcut>>(
                    File.ReadAllText(ConfigPath)) ?? new();
        }
        catch { _shortcuts = new(); }
    }

    private void SaveShortcuts()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_shortcuts,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ── Grid ──────────────────────────────────────────────────────────────────

    private void RebuildGrid()
    {
        _grid.SuspendLayout();
        // Dispose old tiles to release icon handles
        foreach (Control c in _grid.Controls)
            c.Dispose();
        _grid.Controls.Clear();

        foreach (var sc in _shortcuts.ToList())
        {
            var tile = new ShortcutTile(sc.Name, sc.ExePath);
            tile.Launched         += () => Launch(sc.ExePath);
            tile.RemoveRequested  += () => { _shortcuts.Remove(sc); SaveShortcuts(); RebuildGrid(); };
            _grid.Controls.Add(tile);
        }

        _grid.Controls.Add(new AddTile { Cursor = Cursors.Hand });
        ((AddTile)_grid.Controls[^1]).Click += (_, _) => ShowAddDialog();

        _grid.ResumeLayout(true);
    }

    private void ShowAddDialog()
    {
        using var dlg = new AddAppDialog();
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK || dlg.SelectedShortcut == null)
            return;

        var selected = dlg.SelectedShortcut;
        if (_shortcuts.Any(s => s.ExePath.Equals(selected.ExePath, StringComparison.OrdinalIgnoreCase)))
            return;

        _shortcuts.Add(selected);
        SaveShortcuts();
        RebuildGrid();
    }

    private static void Launch(string exePath)
    {
        try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); }
        catch { }
    }

    // ── Paint header ──────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("APP SHORTCUTS", AppTheme.FontSectionHeader, mutedBrush, 12, 14);
        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, 12, 36, ClientSize.Width - 12, 36);
    }
}

// ── Shortcut tile ─────────────────────────────────────────────────────────────

file sealed class ShortcutTile : Control
{
    private readonly string _name;
    private readonly string _exePath;
    private Icon?   _icon;
    private bool    _hover;

    public event Action? Launched;
    public event Action? RemoveRequested;

    public ShortcutTile(string name, string exePath)
    {
        _name    = name;
        _exePath = exePath;
        Size     = new Size(84, 90);
        Cursor   = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);

        try { if (File.Exists(exePath)) _icon = Icon.ExtractAssociatedIcon(exePath); }
        catch { }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Remove", null, (_, _) => RemoveRequested?.Invoke());
        ContextMenuStrip = menu;

        MouseEnter += (_, _) => { _hover = true;  Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        Click      += (_, _) => Launched?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hover)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(40, AppTheme.Accent));
            g.FillRoundedRectangle(hoverBrush, 2, 2, Width - 4, Height - 4, 8);
        }

        // Icon (48×48 centred in top portion)
        const int iconSize = 40;
        int ix = (Width - iconSize) / 2, iy = 6;
        if (_icon != null)
            g.DrawIcon(_icon, new Rectangle(ix, iy, iconSize, iconSize));
        else
        {
            using var ph = new SolidBrush(AppTheme.BgCard);
            g.FillRoundedRectangle(ph, ix, iy, iconSize, iconSize, 6);
            using var f  = new Font("Segoe UI", 18f);
            using var tb = new SolidBrush(AppTheme.TextMuted);
            g.DrawString("?", f, tb,
                new RectangleF(ix, iy, iconSize, iconSize),
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        // Name label
        using var textBrush = new SolidBrush(AppTheme.TextSecondary);
        g.DrawString(_name, AppTheme.FontSmall, textBrush,
            new RectangleF(2, iy + iconSize + 4, Width - 4, Height - iy - iconSize - 6),
            new StringFormat
            {
                Alignment = StringAlignment.Center,
                Trimming  = StringTrimming.EllipsisCharacter,
            });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon?.Dispose();
        base.Dispose(disposing);
    }
}

// ── "Add" tile ────────────────────────────────────────────────────────────────

file sealed class AddTile : Control
{
    private bool _hover;

    public AddTile()
    {
        Size = new Size(84, 90);
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
        MouseEnter += (_, _) => { _hover = true;  Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Background card
        using var bgBrush = new SolidBrush(_hover ? AppTheme.Accent : AppTheme.BgCard);
        g.FillRoundedRectangle(bgBrush, 4, 4, Width - 8, Width - 8, 10);

        // "+" symbol
        using var plusBrush = new SolidBrush(_hover ? Color.White : AppTheme.TextMuted);
        using var pf = new Font("Segoe UI", 26f, FontStyle.Regular);
        g.DrawString("+", pf, plusBrush,
            new RectangleF(0, 2, Width, Width - 4),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        // Label
        using var lblBrush = new SolidBrush(_hover ? Color.White : AppTheme.TextMuted);
        g.DrawString("Add App", AppTheme.FontSmall, lblBrush,
            new RectangleF(0, Width - 2, Width, Height - Width + 2),
            new StringFormat { Alignment = StringAlignment.Center });
    }
}

// ── Add-app dialog ────────────────────────────────────────────────────────────

file sealed class AddAppDialog : Form
{
    public AppShortcut? SelectedShortcut { get; private set; }

    private readonly TextBox  _search;
    private readonly ListBox  _list;
    private readonly Button   _btnAdd;
    private readonly Label    _loadingLabel;

    private List<(string Name, string ExePath)> _allApps  = new();
    private List<(string Name, string ExePath)> _filtered = new();

    public AddAppDialog()
    {
        Text            = "Add App Shortcut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor       = AppTheme.BgDeep;
        ForeColor       = AppTheme.TextPrimary;
        ClientSize      = new Size(480, 520);
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;

        var searchLabel = new Label
        {
            Text      = "Search installed programs:",
            Font      = AppTheme.FontSectionHeader,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(12, 12),
        };

        _search = new TextBox
        {
            BackColor   = AppTheme.BgCard,
            ForeColor   = AppTheme.TextPrimary,
            Font        = AppTheme.FontLabel,
            BorderStyle = BorderStyle.FixedSingle,
            Location    = new Point(12, 34),
            Size        = new Size(456, 26),
        };
        _search.TextChanged += (_, _) => ApplyFilter();

        _loadingLabel = new Label
        {
            Text      = "Loading installed programs…",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(12, 72),
        };

        _list = new ListBox
        {
            BackColor     = AppTheme.BgCard,
            ForeColor     = AppTheme.TextPrimary,
            Font          = AppTheme.FontLabel,
            BorderStyle   = BorderStyle.None,
            Location      = new Point(12, 72),
            Size          = new Size(456, 360),
            Sorted        = false,
            Visible       = false,
        };
        _list.DoubleClick += (_, _) => TryConfirm();

        var sep = new Panel { BackColor = AppTheme.Border };
        sep.SetBounds(0, 442, 480, 1);

        var btnBrowse = MakeButton("Browse for .exe…", AppTheme.BgCard);
        btnBrowse.SetBounds(12, 450, 140, 30);
        btnBrowse.Click += OnBrowse;

        _btnAdd = MakeButton("Add", AppTheme.Accent);
        _btnAdd.SetBounds(326, 450, 68, 30);
        _btnAdd.Enabled = false;
        _btnAdd.Click  += (_, _) => TryConfirm();

        var btnCancel = MakeButton("Cancel", AppTheme.BgCard);
        btnCancel.SetBounds(400, 450, 68, 30);
        btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[]
        {
            searchLabel, _search, _loadingLabel, _list,
            sep, btnBrowse, _btnAdd, btnCancel,
        });

        _list.SelectedIndexChanged += (_, _) => _btnAdd.Enabled = _list.SelectedIndex >= 0;

        // Load apps asynchronously so dialog opens instantly
        Task.Run(LoadInstalledApps).ContinueWith(_ =>
        {
            if (IsHandleCreated) BeginInvoke(OnAppsLoaded);
        });
    }

    // ── App discovery ─────────────────────────────────────────────────────────

    private void LoadInstalledApps()
    {
        var apps = new HashSet<(string, string)>();

        // Registry uninstall keys
        var keyPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var keyPath in keyPaths)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key == null) continue;
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = key.OpenSubKey(subName);
                            if (sub == null) continue;
                            if (sub.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                            // Skip system components and updates
                            if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;
                            if (sub.GetValue("ReleaseType") is string rt && rt is "Update" or "Hotfix") continue;

                            var exePath = ResolveExePath(sub);
                            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                                apps.Add((name.Trim(), exePath));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // Start menu shortcuts (.lnk → exe via Shell)
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        })
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                {
                    var target = ResolveLnk(lnk);
                    if (target != null && File.Exists(target) &&
                        target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var appName = Path.GetFileNameWithoutExtension(lnk);
                        apps.Add((appName, target));
                    }
                }
            }
            catch { }
        }

        _allApps = apps
            .OrderBy(a => a.Item1, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(a => a.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveExePath(RegistryKey key)
    {
        // DisplayIcon is most reliable: "C:\path\app.exe,0"
        if (key.GetValue("DisplayIcon") is string icon)
        {
            var path = icon.Split(',')[0].Trim('"', ' ');
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                return path;
        }
        // InstallLocation: look for single exe at root
        if (key.GetValue("InstallLocation") is string loc && Directory.Exists(loc))
        {
            var exes = Directory.GetFiles(loc, "*.exe", SearchOption.TopDirectoryOnly);
            if (exes.Length == 1) return exes[0];
        }
        return null;
    }

    private static string? ResolveLnk(string lnkPath)
    {
        // Use WScript.Shell COM to resolve shortcut target
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell   = Activator.CreateInstance(shellType)!;
            dynamic sc      = shell.CreateShortcut(lnkPath);
            string  target  = sc.TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }

    private void OnAppsLoaded()
    {
        _loadingLabel.Visible = false;
        _list.Visible         = true;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = _search.Text.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var (name, _) in _filtered)
            _list.Items.Add(name);
        _list.EndUpdate();

        _btnAdd.Enabled = false;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void TryConfirm()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _filtered.Count) return;
        var (name, exePath) = _filtered[_list.SelectedIndex];
        SelectedShortcut  = new AppShortcut { Name = name, ExePath = exePath };
        DialogResult      = DialogResult.OK;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title            = "Select Application",
            Filter           = "Applications (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var exePath = dlg.FileName;
        var name    = Path.GetFileNameWithoutExtension(exePath);
        SelectedShortcut = new AppShortcut { Name = name, ExePath = exePath };
        DialogResult     = DialogResult.OK;
    }

    private static Button MakeButton(string text, Color bg) => new()
    {
        Text      = text,
        Font      = AppTheme.FontBold,
        ForeColor = Color.White,
        BackColor = bg,
        FlatStyle = FlatStyle.Flat,
        Cursor    = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };
}
