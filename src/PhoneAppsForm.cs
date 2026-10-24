using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XingyuConnect;

internal sealed class AppPreferences
{
    private sealed record SavedPreferences(HashSet<string> Favorites, HashSet<string> MirrorApps);
    private readonly string path;
    private readonly SemaphoreSlim writing = new(1);
    public HashSet<string> Favorites { get; private set; } = [];
    public HashSet<string> MirrorApps { get; private set; } = [];

    public AppPreferences(string serial)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)))[..20];
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingyuConnect", "apps-" + key + ".json");
    }

    public async Task LoadAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedPreferences>(await File.ReadAllTextAsync(path, token));
            if (saved is null || saved.Favorites is null || saved.MirrorApps is null) throw new InvalidDataException("应用收藏配置不完整，请保留原文件并检查：" + path);
            Favorites = saved.Favorites;
            MirrorApps = saved.MirrorApps;
        }
        catch (JsonException ex) { throw new IOException("应用收藏配置无法读取：" + path, ex); }
    }

    public async Task SaveAsync(string packageName, bool? favorite, bool? mirror, CancellationToken token)
    {
        await writing.WaitAsync(token);
        try
        {
            var favorites = new HashSet<string>(Favorites);
            var mirrors = new HashSet<string>(MirrorApps);
            if (favorite.HasValue) { if (favorite.Value) favorites.Add(packageName); else favorites.Remove(packageName); }
            if (mirror.HasValue) { if (mirror.Value) mirrors.Add(packageName); else mirrors.Remove(packageName); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new SavedPreferences(favorites, mirrors)), token);
            File.Move(temporary, path, true);
            Favorites = favorites;
            MirrorApps = mirrors;
        }
        finally { writing.Release(); }
    }
}

internal sealed class PhoneAppsForm : ModernForm
{
    private readonly PhoneContentService content;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索应用名称或包名", BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox favoritesOnly = new() { Text = "只看收藏", AutoSize = true, Anchor = AnchorStyles.None };
    private readonly ListView apps = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Color.White };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly FlowLayoutPanel actions;
    private readonly ActionButton refresh;
    private readonly ActionButton favorite;
    private List<PhoneApp> allApps = [];
    private bool loading;
    private bool preferencesLoaded;
    public string Serial { get; }
    public AppPreferences Preferences { get; }
    public event Action<PhoneApp, bool>? OpenRequested;

    public PhoneAppsForm(DeviceBridge bridge, AndroidDevice device)
    {
        content = new PhoneContentService(bridge, device.Serial);
        Serial = device.Serial;
        Preferences = new AppPreferences(device.Serial);
        Text = device.DisplayName + " · 手机应用";
        ClientSize = new Size(920, 680);
        MinimumSize = new Size(780, 540);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24, 0, 24, 14) };
        foreach (var height in new[] { 88f, 44f, 0f, 52f, 34f }) root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = new Label { Text = "手机应用", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Location = new Point(0, 17) };
        var subtitle = new Label { Text = device.DisplayName + " · " + device.Serial, AutoSize = true, ForeColor = Ui.Muted, Location = new Point(2, 57) };
        header.Controls.AddRange([title, subtitle, CreateWindowActions()]);
        EnableWindowDrag(header, title, subtitle);
        root.Controls.Add(header, 0, 0);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        search.Margin = new Padding(0, 6, 12, 0);
        search.TextChanged += (_, _) => RenderApps();
        favoritesOnly.CheckedChanged += (_, _) => RenderApps();
        refresh = Ui.Button("刷新", 82);
        refresh.Click += async (_, _) => await LoadAppsAsync();
        filters.Controls.Add(search, 0, 0);
        filters.Controls.Add(favoritesOnly, 1, 0);
        filters.Controls.Add(refresh, 2, 0);
        root.Controls.Add(filters, 0, 1);
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 6, 0, 6) };
        apps.Columns.Add("应用", 180);
        apps.Columns.Add("包名", 330);
        apps.Columns.Add("收藏", 64);
        apps.Columns.Add("打开方式", 130);
        apps.AccessibleName = "手机应用列表";
        apps.DoubleClick += (_, _) => OpenSelected(true);
        card.Controls.Add(apps);
        root.Controls.Add(card, 0, 2);
        actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var open = Ui.Button("独立窗口打开", 144, true);
        open.Click += (_, _) => OpenSelected(true);
        var mirror = Ui.Button("普通投屏打开", 144);
        mirror.Click += (_, _) => OpenSelected(false);
        favorite = Ui.Button("收藏应用", 110);
        apps.SelectedIndexChanged += (_, _) => favorite.Text = apps.SelectedItems.Count == 1 && Preferences.Favorites.Contains(((PhoneApp)apps.SelectedItems[0].Tag!).PackageName) ? "取消收藏" : "收藏应用";
        favorite.Click += async (_, _) =>
        {
            if (apps.SelectedItems.Count != 1) { status.Text = "请先选择应用"; return; }
            var app = (PhoneApp)apps.SelectedItems[0].Tag!;
            try { await Preferences.SaveAsync(app.PackageName, !Preferences.Favorites.Contains(app.PackageName), null, lifetime.Token); RenderApps(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "收藏未能保存：" + ex.Message; }
        };
        var retry = Ui.Button("恢复独立窗口", 140);
        retry.Click += async (_, _) =>
        {
            if (apps.SelectedItems.Count != 1) { status.Text = "请先选择应用"; return; }
            var app = (PhoneApp)apps.SelectedItems[0].Tag!;
            try { await Preferences.SaveAsync(app.PackageName, null, false, lifetime.Token); RenderApps(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; }
        };
        actions.Controls.AddRange([open, mirror, favorite, retry]);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(status, 0, 4);
        actions.Enabled = false;
        Shown += async (_, _) => await LoadAppsAsync();
    }

    public void RenderApps()
    {
        if (lifetime.IsCancellationRequested) return;
        var selected = apps.SelectedItems.Count == 1 ? ((PhoneApp)apps.SelectedItems[0].Tag!).PackageName : null;
        var query = search.Text.Trim();
        apps.BeginUpdate();
        apps.Items.Clear();
        foreach (var app in allApps.Where(app => (!favoritesOnly.Checked || Preferences.Favorites.Contains(app.PackageName)) &&
            (app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || app.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(app => Preferences.Favorites.Contains(app.PackageName)).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ListViewItem(app.Name) { Tag = app };
            item.SubItems.Add(app.PackageName);
            item.SubItems.Add(Preferences.Favorites.Contains(app.PackageName) ? "★" : "");
            item.SubItems.Add(Preferences.MirrorApps.Contains(app.PackageName) ? "普通投屏" : "独立窗口");
            apps.Items.Add(item);
            if (app.PackageName == selected) item.Selected = true;
        }
        apps.EndUpdate();
        status.Text = $"显示 {apps.Items.Count} / {allApps.Count} 个应用 · 独立窗口不兼容时会回退普通投屏";
    }

    private void OpenSelected(bool independent)
    {
        if (apps.SelectedItems.Count != 1) { status.Text = "请先选择要打开的应用"; return; }
        var app = (PhoneApp)apps.SelectedItems[0].Tag!;
        OpenRequested?.Invoke(app, independent && !Preferences.MirrorApps.Contains(app.PackageName));
    }

    private async Task LoadAppsAsync()
    {
        if (loading) return;
        loading = true;
        refresh.Enabled = actions.Enabled = false;
        status.Text = "正在读取手机应用…";
        try
        {
            if (!preferencesLoaded) { await Preferences.LoadAsync(lifetime.Token); preferencesLoaded = true; }
            allApps = await content.ListAppsAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            RenderApps();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or System.ComponentModel.Win32Exception) { if (!lifetime.IsCancellationRequested) status.Text = ex.Message; }
        finally { loading = false; if (!lifetime.IsCancellationRequested) { refresh.Enabled = true; actions.Enabled = preferencesLoaded; } }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) { lifetime.Cancel(); base.OnFormClosing(e); }
    protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
