using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibOrbisPkg.GP4;

namespace PkgEditorLinux
{
  /// <summary>
  /// Native (portal / GTK) file pickers and small reusable dialogs.
  /// Remembers the last chosen directory across sessions.
  /// </summary>
  public static class Dialogs
  {
    /// <summary>Creates a file type filter. Patterns are matched case-insensitively on Linux.</summary>
    public static FilePickerFileType Type(string name, params string[] patterns)
    {
      var all = patterns.Concat(patterns.Select(p => p.ToUpperInvariant())).Distinct().ToList();
      return new FilePickerFileType(name) { Patterns = all };
    }

    public static readonly FilePickerFileType AllFiles = new FilePickerFileType("All files") { Patterns = new[] { "*" } };

    private static readonly string LastDirFile = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "LibOrbisPkg", "last_dir.txt");

    private static string _lastDir;

    /// <summary>Last directory used by a file/folder picker (persisted).</summary>
    public static string LastDirectory
    {
      get
      {
        if (_lastDir != null) return _lastDir;
        try
        {
          if (File.Exists(LastDirFile))
          {
            var d = File.ReadAllText(LastDirFile).Trim();
            if (Directory.Exists(d)) _lastDir = d;
          }
        }
        catch { /* ignore */ }
        return _lastDir;
      }
      private set
      {
        if (string.IsNullOrEmpty(value)) return;
        var dir = Directory.Exists(value) ? Path.GetFullPath(value)
          : Path.GetDirectoryName(Path.GetFullPath(value));
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        _lastDir = dir;
        try
        {
          Directory.CreateDirectory(Path.GetDirectoryName(LastDirFile));
          File.WriteAllText(LastDirFile, dir);
        }
        catch { /* ignore */ }
      }
    }

    private static void RememberPath(string path)
    {
      if (!string.IsNullOrEmpty(path)) LastDirectory = path;
    }

    private static async Task<IStorageFolder> FolderOf(Window owner, string path)
    {
      try
      {
        if (string.IsNullOrEmpty(path)) return null;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        return await owner.StorageProvider.TryGetFolderFromPathAsync(dir);
      }
      catch { return null; }
    }

    private static async Task<IStorageFolder> StartFolder(Window owner, string preferred = null)
    {
      var folder = await FolderOf(owner, preferred);
      if (folder != null) return folder;
      return await FolderOf(owner, LastDirectory);
    }

    public static async Task<string> OpenFile(Window owner, string title, params FilePickerFileType[] types)
    {
      var r = await OpenFiles(owner, title, false, types);
      return r.FirstOrDefault();
    }

    public static async Task<string[]> OpenFiles(Window owner, string title, bool multiple, params FilePickerFileType[] types)
    {
      var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = title,
        AllowMultiple = multiple,
        FileTypeFilter = types.Length > 0 ? types.Append(AllFiles).ToList() : null,
        SuggestedStartLocation = await StartFolder(owner),
      });
      var paths = result.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToArray();
      if (paths.Length > 0) RememberPath(paths[0]);
      return paths;
    }

    public static async Task<string> SaveFile(Window owner, string title, string suggested, params FilePickerFileType[] types)
    {
      var opts = new FilePickerSaveOptions
      {
        Title = title,
        SuggestedFileName = suggested != null ? Path.GetFileName(suggested) : null,
        FileTypeChoices = types.Length > 0 ? types.ToList() : null,
        ShowOverwritePrompt = true,
        SuggestedStartLocation = await StartFolder(owner,
          suggested != null && Path.IsPathRooted(suggested) ? suggested : null),
      };
      var file = await owner.StorageProvider.SaveFilePickerAsync(opts);
      var path = file?.TryGetLocalPath();
      RememberPath(path);
      return path;
    }

    public static async Task<string> PickFolder(Window owner, string title)
    {
      var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
      {
        Title = title,
        AllowMultiple = false,
        SuggestedStartLocation = await StartFolder(owner),
      });
      var path = result.FirstOrDefault()?.TryGetLocalPath();
      RememberPath(path);
      return path;
    }

    public static Task<string> Message(Window owner, string title, string message, params string[] buttons)
    {
      if (buttons.Length == 0) buttons = new[] { "OK" };
      return new MessageDialog(title, message, buttons).ShowDialog<string>(owner);
    }

    public static Task Error(Window owner, string message) => Message(owner, "Error", message, "OK");

    public static Task<string> Input(Window owner, string title, string prompt, string initial = "", int maxLength = 0)
      => new InputDialog(title, prompt, initial, maxLength).ShowDialog<string>(owner);

    public static string HumanReadableFileSize(long size)
    {
      if (size > (1024 * 1024 * 1024))
        return (size / (double)(1024 * 1024 * 1024)).ToString("0.#") + " GiB";
      else if (size > (1024 * 1024))
        return (size / (double)(1024 * 1024)).ToString("0.#") + " MiB";
      else if (size > 1024)
        return (size / 1024.0).ToString("0.#") + " KiB";
      else
        return size.ToString() + " B";
    }

    internal static TextBlock Text(string text, params string[] classes)
    {
      var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
      foreach (var c in classes) if (!string.IsNullOrEmpty(c)) t.Classes.Add(c);
      return t;
    }

    internal static Button Btn(string text, params string[] classes)
    {
      var b = new Button { Content = text, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
      foreach (var c in classes) if (!string.IsNullOrEmpty(c)) b.Classes.Add(c);
      return b;
    }
  }

  /// <summary>Simple message box with arbitrary buttons. Returns the clicked button's text.</summary>
  public class MessageDialog : Window
  {
    public MessageDialog(string title, string message, string[] buttons)
    {
      Title = title;
      Width = 460;
      SizeToContent = SizeToContent.Height;
      CanResize = false;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ShowInTaskbar = false;

      var root = new StackPanel { Margin = new Thickness(26), Spacing = 16 };
      root.Children.Add(Dialogs.Text(title, "h2"));
      root.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, MaxHeight = 400 });
      var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
      for (int i = 0; i < buttons.Length; i++)
      {
        var label = buttons[i];
        var b = Dialogs.Btn(label, i == buttons.Length - 1 ? "accent" : "");
        b.Click += (_, _) => Close(label);
        row.Children.Add(b);
      }
      root.Children.Add(row);
      Content = root;
    }
  }

  /// <summary>Single-line text prompt. Returns null if cancelled.</summary>
  public class InputDialog : Window
  {
    public InputDialog(string title, string prompt, string initial, int maxLength)
    {
      Title = title;
      Width = 480;
      SizeToContent = SizeToContent.Height;
      CanResize = false;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ShowInTaskbar = false;

      var box = new TextBox { Text = initial ?? "", Classes = { "mono" } };
      if (maxLength > 0) box.MaxLength = maxLength;
      var ok = Dialogs.Btn("OK", "accent");
      var cancel = Dialogs.Btn("Cancel");
      ok.Click += (_, _) => Close(box.Text ?? "");
      cancel.Click += (_, _) => Close(null);
      box.KeyDown += (_, e) =>
      {
        if (e.Key == Avalonia.Input.Key.Enter) Close(box.Text ?? "");
        else if (e.Key == Avalonia.Input.Key.Escape) Close(null);
      };

      var root = new StackPanel { Margin = new Thickness(26), Spacing = 14 };
      root.Children.Add(Dialogs.Text(title, "h2"));
      root.Children.Add(Dialogs.Text(prompt, "muted"));
      root.Children.Add(box);
      var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
      row.Children.Add(cancel);
      row.Children.Add(ok);
      root.Children.Add(row);
      Content = root;
      Opened += (_, _) => { box.Focus(); box.SelectAll(); };
    }
  }

  /// <summary>Log output window used for long running operations (build, merge...).</summary>
  public class LogWindow : Window
  {
    private readonly TextBox log;
    private readonly ProgressBar progress;
    private readonly TextBlock status;
    private readonly Button closeBtn;

    public LogWindow(string title = "Log")
    {
      Title = title;
      Width = 760;
      Height = 460;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      log = new TextBox
      {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        Classes = { "mono" },
        FontSize = 12.5,
        VerticalContentAlignment = VerticalAlignment.Top,
      };
      ScrollViewer.SetHorizontalScrollBarVisibility(log, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
      progress = new ProgressBar { IsIndeterminate = true, Height = 6 };
      status = Dialogs.Text("Working…", "muted");
      closeBtn = Dialogs.Btn("Close");
      closeBtn.Click += (_, _) => Close();

      var grid = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
      var header = Dialogs.Text(title, "h2");
      grid.Children.Add(header);
      Grid.SetRow(progress, 1);
      grid.Children.Add(progress);
      Grid.SetRow(log, 2);
      grid.Children.Add(log);
      var bottom = new DockPanel();
      DockPanel.SetDock(closeBtn, Dock.Right);
      bottom.Children.Add(closeBtn);
      status.VerticalAlignment = VerticalAlignment.Center;
      bottom.Children.Add(status);
      Grid.SetRow(bottom, 3);
      grid.Children.Add(bottom);
      Content = grid;
    }

    /// <summary>Thread-safe log line append.</summary>
    public void Log(string line)
    {
      if (Dispatcher.UIThread.CheckAccess()) Append(line);
      else Dispatcher.UIThread.Post(() => Append(line));
    }

    public void Log(string fmt, params object[] args) => Log(string.Format(fmt, args));

    private void Append(string line)
    {
      log.Text += line + Environment.NewLine;
      log.CaretIndex = log.Text.Length;
    }

    public void Finish(bool ok, string message = null)
    {
      void f()
      {
        progress.IsIndeterminate = false;
        progress.Value = ok ? 100 : 0;
        status.Text = message ?? (ok ? "Done" : "Failed");
        status.Foreground = (IBrush)Application.Current.FindResource(ok ? "Success" : "Danger");
        closeBtn.Classes.Add("accent");
      }
      if (Dispatcher.UIThread.CheckAccess()) f(); else Dispatcher.UIThread.Post(f);
    }
  }

  /// <summary>Shows GP4 validation warnings/errors before building. Returns true to continue.</summary>
  public class ValidationDialog : Window
  {
    public ValidationDialog(List<ValidateResult> results)
    {
      Title = "Project validation";
      Width = 620;
      Height = 440;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      bool fatal = results.Any(r => r.Type == ValidateResult.ResultType.Fatal);

      var list = new StackPanel { Spacing = 8 };
      foreach (var r in results)
      {
        var isFatal = r.Type == ValidateResult.ResultType.Fatal;
        var color = (IBrush)Application.Current.FindResource(isFatal ? "Danger" : "Warning");
        var soft = (IBrush)Application.Current.FindResource(isFatal ? "DangerSoft" : "WarningSoft");
        var item = new Border
        {
          Background = soft,
          BorderBrush = color,
          BorderThickness = new Thickness(3, 0, 0, 0),
          CornerRadius = new CornerRadius(8),
          Padding = new Thickness(14, 10),
          Child = new StackPanel
          {
            Spacing = 4,
            Children =
            {
              new TextBlock { Text = r.Type.ToString(), FontWeight = FontWeight.Bold, Foreground = color },
              new SelectableTextBlock { Text = r.Message, TextWrapping = TextWrapping.Wrap },
            }
          }
        };
        list.Children.Add(item);
      }

      var ignore = Dialogs.Btn("Ignore and continue", "accent");
      ignore.IsEnabled = !fatal;
      ignore.Click += (_, _) => Close(true);
      var cancel = Dialogs.Btn("Cancel");
      cancel.Click += (_, _) => Close(false);

      var grid = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 10 };
      grid.Children.Add(Dialogs.Text("Your project has issues", "h2"));
      var sub = Dialogs.Text(fatal
        ? "Fatal errors were found. Fix them before building the package."
        : "Warnings were found. You can still build, but the result may not work as expected.", "muted");
      Grid.SetRow(sub, 1); grid.Children.Add(sub);
      var sv = new ScrollViewer { Content = list };
      Grid.SetRow(sv, 2); grid.Children.Add(sv);
      var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
      row.Children.Add(cancel); row.Children.Add(ignore);
      Grid.SetRow(row, 3); grid.Children.Add(row);
      Content = grid;
    }
  }
}
