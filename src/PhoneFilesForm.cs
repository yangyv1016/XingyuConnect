using System.Drawing.Drawing2D;
using System.Net.Sockets;

namespace XingyuConnect;

internal sealed class PhoneFilesForm : ModernForm
{
    private readonly PhoneContentService content;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private TaskCompletionSource? operationCompletion;
    private CancellationTokenSource? thumbnails;
    private readonly RoundedChoiceGroup mode;
    private readonly ListView files = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowItemToolTips = true, BackColor = Color.White };
    private readonly ImageList images = new() { ImageSize = new Size(112, 88), ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<Image> ownedImages = [];
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox address = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 6 };
    private readonly ActionButton cancel;
    private readonly ActionButton previous;
    private readonly ActionButton next;
    private readonly FlowLayoutPanel actions;
    private readonly FlowLayoutPanel locations;
    private List<PhoneFile> entries = [];
    private string directory = "/sdcard";
    private int page;
    private int revision;
    private bool busy;
    private bool closing;
    public Task ActiveOperation => operationCompletion?.Task ?? Task.CompletedTask;
    private const int AlbumPageSize = 12;
    public event Action<string>? LogMessage;

    public PhoneFilesForm(DeviceBridge bridge, AndroidDevice device, bool album = false)
    {
        content = new PhoneContentService(bridge, device.Serial);
        Text = device.DisplayName + " · 文件与相册";
        BackColor = Ui.Background;
        ForeColor = Ui.Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        ClientSize = new Size(1000, 720);
        MinimumSize = new Size(840, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24, 0, 24, 14) };
        foreach (var height in new[] { 88f, 48f, 44f, 0f, 9f, 48f }) root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = new Label { Text = "文件与相册", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Location = new Point(0, 17) };
        var subtitle = new Label { Text = device.DisplayName + " · " + device.Serial, AutoSize = true, ForeColor = Ui.Muted, Location = new Point(2, 57) };
        header.Controls.AddRange([title, subtitle, CreateWindowActions()]);
        EnableWindowDrag(header, title, subtitle);
        root.Controls.Add(header, 0, 0);
        locations = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var (label, path) in new[] { ("手机文件", "/sdcard"), ("相机", "/sdcard/DCIM"), ("图片", "/sdcard/Pictures"), ("下载", "/sdcard/Download") })
        {
            var button = Ui.Button(label, 100);
            button.Click += async (_, _) => await NavigateAsync(path);
            locations.Controls.Add(button);
        }
        mode = new RoundedChoiceGroup([("文件", ""), ("相册", "")], album ? 1 : 0) { Width = 210, Height = 40, Dock = DockStyle.None, Margin = new Padding(18, 0, 0, 0) };
        mode.SelectedIndexChanged += async (_, _) =>
        {
            if (mode.SelectedIndex == 1 && directory == "/sdcard") await NavigateAsync("/sdcard/DCIM");
            else { page = 0; RenderFiles(); }
        };
        locations.Controls.Add(mode);
        root.Controls.Add(locations, 0, 1);
        actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var up = Ui.Button("上一级", 86);
        up.Click += async (_, _) => await NavigateAsync(directory == "/sdcard" ? "/sdcard" : directory[..directory.LastIndexOf('/')]);
        var refresh = Ui.Button("刷新", 72);
        refresh.Click += async (_, _) => await NavigateAsync(directory);
        var upload = Ui.Button("上传文件", 110, true);
        upload.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "上传到当前手机文件夹", Multiselect = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await UploadFilesAsync(dialog.FileNames);
        };
        var download = Ui.Button("保存到电脑", 120);
        download.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择保存位置", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await DownloadSelectedAsync(dialog.SelectedPath);
        };
        previous = Ui.Button("上一页", 82);
        previous.Click += (_, _) => { page--; RenderFiles(); };
        next = Ui.Button("下一页", 82);
        next.Click += (_, _) => { page++; RenderFiles(); };
        actions.Controls.AddRange([up, refresh, upload, download, previous, next]);
        root.Controls.Add(actions, 0, 2);
        var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 5, 0, 5) };
        var listing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        listing.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        listing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listing.Controls.Add(address, 0, 0);
        files.Columns.Add("名称", 420);
        files.Columns.Add("大小", 110);
        files.Columns.Add("修改时间", 190);
        files.LargeImageList = images;
        files.AccessibleName = "手机文件列表";
        files.DoubleClick += async (_, _) =>
        {
            if (files.SelectedItems.Count != 1) return;
            var entry = (PhoneFile)files.SelectedItems[0].Tag!;
            if (entry.IsDirectory) await NavigateAsync(entry.Path);
            else if (IsImage(entry.Name)) await PreviewAsync(entry);
            else status.Text = "选中文件后，点击“保存到电脑”即可下载";
        };
        files.AllowDrop = true;
        files.DragEnter += (_, e) => e.Effect = !busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        files.DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await UploadFilesAsync(paths); };
        listing.Controls.Add(files, 0, 1);
        surface.Controls.Add(listing);
        root.Controls.Add(surface, 0, 3);
        root.Controls.Add(progress, 0, 4);
        var footer = new Panel { Dock = DockStyle.Fill };
        cancel = Ui.Button("取消传输", 106);
        cancel.Dock = DockStyle.Right;
        cancel.Enabled = false;
        cancel.Click += (_, _) => operation?.Cancel();
        footer.Controls.Add(status);
        footer.Controls.Add(cancel);
        root.Controls.Add(footer, 0, 5);
        Shown += async (_, _) => await NavigateAsync(album ? "/sdcard/DCIM" : "/sdcard");
    }

    private static bool IsImage(string name) => Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".heic" or ".heif";

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, string message)
    {
        if (busy) return;
        busy = true;
        operationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        thumbnails?.Cancel();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        actions.Enabled = locations.Enabled = files.Enabled = false;
        cancel.Enabled = true;
        progress.Value = 0;
        status.Text = message;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) status.Text = "已取消本次操作"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (!lifetime.IsCancellationRequested) { status.Text = ex.Message; LogMessage?.Invoke(ex.Message); }
        }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            if (!lifetime.IsCancellationRequested) { actions.Enabled = locations.Enabled = files.Enabled = true; cancel.Enabled = false; }
            operationCompletion.SetResult();
        }
    }

    private async Task NavigateAsync(string path)
    {
        await RunOperationAsync(async token =>
        {
            var listed = await content.ListFilesAsync(path, token);
            token.ThrowIfCancellationRequested();
            directory = path;
            entries = listed;
            page = 0;
            RenderFiles();
        }, "正在读取手机文件…");
    }

    private void RenderFiles()
    {
        thumbnails?.Cancel();
        thumbnails?.Dispose();
        thumbnails = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var currentRevision = ++revision;
        files.BeginUpdate();
        files.Items.Clear();
        images.Images.Clear();
        foreach (var image in ownedImages) image.Dispose();
        ownedImages.Clear();
        var album = mode.SelectedIndex == 1;
        files.View = album ? View.LargeIcon : View.Details;
        var visible = album ? entries.Where(entry => entry.IsDirectory || IsImage(entry.Name)).OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Modified).ToList() : entries;
        var shown = album ? visible.Skip(page * AlbumPageSize).Take(AlbumPageSize).ToList() : visible;
        foreach (var entry in shown)
        {
            var item = new ListViewItem(entry.Name) { Tag = entry };
            item.SubItems.Add(entry.IsDirectory ? "文件夹" : entry.Size < 1048576 ? $"{entry.Size / 1024d:F1} KB" : $"{entry.Size / 1048576d:F1} MB");
            item.SubItems.Add(entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
            if (album)
            {
                var image = new Bitmap(112, 88);
                using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Color.FromArgb(244, 247, 251)); Ui.DrawIcon(graphics, entry.IsDirectory ? UiIcon.Folder : UiIcon.Display, new RectangleF(40, 27, 32, 32), Ui.Muted); }
                ownedImages.Add(image);
                images.Images.Add(image);
                item.ImageIndex = images.Images.Count - 1;
            }
            files.Items.Add(item);
        }
        files.EndUpdate();
        previous.Visible = next.Visible = album;
        previous.Enabled = page > 0;
        next.Enabled = (page + 1) * AlbumPageSize < visible.Count;
        address.Text = directory;
        status.Text = visible.Count == 0 ? "这个文件夹还没有可显示的内容" : album ? $"共 {visible.Count} 项 · 第 {page + 1} 页 · 双击图片预览" : $"共 {visible.Count} 项 · 拖入上传，多选保存 · 投屏和传音优先";
        if (album) _ = LoadThumbnailsAsync(currentRevision, thumbnails.Token);
    }

    private async Task LoadThumbnailsAsync(int currentRevision, CancellationToken token)
    {
        foreach (var item in files.Items.Cast<ListViewItem>().ToArray())
        {
            var entry = (PhoneFile)item.Tag!;
            if (entry.IsDirectory || entry.Size > 16 * 1024 * 1024) continue;
            try
            {
                using var stream = new MemoryStream();
                await content.ReadFileAsync(entry.Path, stream, null, token);
                token.ThrowIfCancellationRequested();
                if (revision != currentRevision) return;
                stream.Position = 0;
                using var original = Image.FromStream(stream);
                var bitmap = new Bitmap(112, 88);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.FromArgb(244, 247, 251));
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var ratio = Math.Min(112f / original.Width, 88f / original.Height);
                    var width = original.Width * ratio;
                    var height = original.Height * ratio;
                    graphics.DrawImage(original, (112 - width) / 2, (88 - height) / 2, width, height);
                }
                ownedImages.Add(bitmap);
                images.Images[item.ImageIndex] = bitmap;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or SocketException or ArgumentException or OutOfMemoryException)
            {
                if (token.IsCancellationRequested) return;
                item.ToolTipText = "暂不能预览，可保存到电脑查看：" + ex.Message;
            }
        }
    }

    private async Task PreviewAsync(PhoneFile entry)
    {
        await RunOperationAsync(async token =>
        {
            if (entry.Size > 64 * 1024 * 1024) throw new IOException("图片较大，请保存到电脑后查看。");
            using var stream = new MemoryStream();
            await content.ReadFileAsync(entry.Path, stream, null, token);
            token.ThrowIfCancellationRequested();
            stream.Position = 0;
            Image image;
            try { using var original = Image.FromStream(stream); image = new Bitmap(original); }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { throw new IOException("此图片格式暂不支持预览，请保存到电脑后查看。", ex); }
            using (image)
            using (var preview = new PhotoPreviewForm(image, entry.Name)) preview.ShowDialog(this);
            status.Text = "图片预览已关闭";
        }, "正在载入图片…");
    }

    private async Task UploadFilesAsync(string[] paths)
    {
        await RunOperationAsync(async token =>
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) throw new IOException("请选择文件；此处暂不支持整个文件夹上传。");
                var name = Path.GetFileName(path);
                if (entries.Any(entry => entry.Name == name) && MessageBox.Show(this, "手机已有“" + name + "”，是否替换？", "上传文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                status.Text = "正在上传 · " + name + " · 投屏和传音优先，链路繁忙时暂停";
                var reporter = new Progress<double>(value => { if (!lifetime.IsCancellationRequested) progress.Value = (int)Math.Clamp(value, 0, 100); });
                await content.UploadAsync(path, PhoneContentService.JoinPath(directory, name), reporter, token);
                LogMessage?.Invoke("已上传：" + name + " → " + directory);
            }
            entries = await content.ListFilesAsync(directory, token);
            token.ThrowIfCancellationRequested();
            RenderFiles();
            progress.Value = 100;
            status.Text = "上传完成 · 文件已保存到手机当前文件夹";
        }, "准备上传…");
    }

    private async Task DownloadSelectedAsync(string destination)
    {
        var selected = files.SelectedItems.Cast<ListViewItem>().Select(item => (PhoneFile)item.Tag!).ToArray();
        if (selected.Length == 0) { status.Text = "请先选中要保存的文件"; return; }
        await RunOperationAsync(async token =>
        {
            if (selected.Any(entry => entry.IsDirectory)) throw new IOException("请进入文件夹后选择文件；此处暂不支持整个文件夹下载。");
            foreach (var entry in selected)
            {
                if (entry.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Windows 无法保存此文件名：" + entry.Name);
                var path = Path.Combine(destination, entry.Name);
                if (File.Exists(path) && MessageBox.Show(this, "电脑已有“" + entry.Name + "”，是否替换？", "保存文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                status.Text = "正在下载 · " + entry.Name + " · 投屏和传音优先，链路繁忙时暂停";
                var reporter = new Progress<double>(value => { if (!lifetime.IsCancellationRequested) progress.Value = (int)Math.Clamp(value, 0, 100); });
                await content.DownloadAsync(entry.Path, path, reporter, token);
                LogMessage?.Invoke("已下载：" + entry.Path + " → " + path);
            }
            progress.Value = 100;
            status.Text = "保存完成 · " + destination;
        }, "准备保存到电脑…");
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (closing) { e.Cancel = true; return; }
        lifetime.Cancel();
        operation?.Cancel();
        thumbnails?.Cancel();
        foreach (var preview in OwnedForms) preview.Close();
        if (!ActiveOperation.IsCompleted)
        {
            e.Cancel = true;
            closing = true;
            Enabled = false;
            status.Text = "正在取消操作并清理临时文件…";
            await ActiveOperation;
            closing = false;
            Close();
            return;
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { lifetime.Cancel(); thumbnails?.Cancel(); images.Dispose(); foreach (var image in ownedImages) image.Dispose(); thumbnails?.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class PhotoPreviewForm : ModernForm
{
    public PhotoPreviewForm(Image image, string name)
    {
        Text = name;
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(480, 360);
        BackColor = Ui.Background;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var caption = new Panel { Dock = DockStyle.Top, Height = 48 };
        var title = new Label { Text = name, Dock = DockStyle.Fill, Padding = new Padding(16, 12, 0, 0), ForeColor = Ui.Ink };
        caption.Controls.Add(title);
        caption.Controls.Add(CreateWindowActions());
        EnableWindowDrag(caption, title);
        Controls.Add(new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom });
        Controls.Add(caption);
    }
}
