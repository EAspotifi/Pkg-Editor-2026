using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using LibOrbisPkg.GP4;
using LibOrbisPkg.PKG;
using LibOrbisPkg.SFO;
using PkgEditorLinux.Views;

namespace PkgEditorLinux
{
  public partial class MainWindow : Window
  {
    private TabControl viewTabs;
    private Border welcomePanel;
    private MenuItem menuSave;
    private MenuItem menuSaveAs;

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] args)
    {
      InitializeComponent();
      viewTabs = this.FindControl<TabControl>("ViewTabs");
      welcomePanel = this.FindControl<Border>("WelcomePanel");
      menuSave = this.FindControl<MenuItem>("MenuSave");
      menuSaveAs = this.FindControl<MenuItem>("MenuSaveAs");
      viewTabs.Classes.Add("docs");
      viewTabs.SelectionChanged += (_, _) => UpdateSaveMenus();

      AddHandler(DragDrop.DropEvent, OnDrop);
      AddHandler(DragDrop.DragOverEvent, OnDragOver);

      foreach (var a in args ?? Array.Empty<string>())
      {
        if (File.Exists(a)) OpenPath(a);
      }
      RefreshWelcome();
    }

    private ViewBase CurrentView =>
      (viewTabs.SelectedItem as TabItem)?.Content as ViewBase;

    private void RefreshWelcome()
    {
      bool empty = viewTabs.ItemCount == 0;
      welcomePanel.IsVisible = empty;
      viewTabs.IsVisible = !empty;
    }

    private void UpdateSaveMenus()
    {
      var v = CurrentView;
      menuSave.IsEnabled = v?.CanSave == true;
      menuSaveAs.IsEnabled = v?.CanSaveAs == true;
    }

    public void OpenTab(ViewBase view, string title)
    {
      view.MainWin = this;
      view.Title = title;
      view.SaveStatusChanged += (_, _) => UpdateSaveMenus();
      view.TitleChanged += (_, _) =>
      {
        if (view.Parent is TabItem ti && ti.Header is Panel p)
        {
          if (p.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock tb)
            tb.Text = view.Title;
        }
      };

      var header = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        VerticalAlignment = VerticalAlignment.Center,
      };
      var label = new TextBlock
      {
        Text = title,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 220,
        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
      };
      var close = new Button { Content = "✕", Classes = { "tabclose" } };
      header.Children.Add(label);
      header.Children.Add(close);

      var tab = new TabItem { Header = header, Content = view };
      close.Click += (_, _) => CloseTab(tab);
      viewTabs.Items.Add(tab);
      viewTabs.SelectedItem = tab;
      RefreshWelcome();
      UpdateSaveMenus();
    }

    private void CloseTab(TabItem tab)
    {
      if (tab.Content is ViewBase v) v.Close();
      viewTabs.Items.Remove(tab);
      RefreshWelcome();
      UpdateSaveMenus();
    }

    private void OpenPath(string path)
    {
      var ext = Path.GetExtension(path).ToLowerInvariant();
      switch (ext)
      {
        case ".pkg":
          OpenTab(new PkgView(path), Path.GetFileName(path));
          break;
        case ".gp4":
          using (var fs = File.OpenRead(path))
          {
            var proj = Gp4Project.ReadFrom(fs);
            OpenTab(new GP4View(proj, path), Path.GetFileName(path));
          }
          break;
        case ".pfs":
        case ".dat":
          OpenTab(new PfsView(path), Path.GetFileName(path));
          break;
        case ".sfo":
          using (var fs = File.OpenRead(path))
          {
            var sfo = ParamSfo.FromStream(fs);
            OpenTab(new SFOView(sfo, false, path), Path.GetFileName(path));
          }
          break;
        default:
          _ = Dialogs.Error(this, "Unsupported file type: " + ext);
          break;
      }
    }

    private async void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
      var path = await Dialogs.OpenFile(this, "Open file",
        Dialogs.Type("All supported", "*.pkg", "*.pfs", "*.dat", "*.gp4", "*.sfo"),
        Dialogs.Type("PKG Files", "*.pkg"),
        Dialogs.Type("PFS Images", "*.pfs", "*.dat"),
        Dialogs.Type("GP4 Projects", "*.gp4"),
        Dialogs.Type("SFO Files", "*.sfo"));
      if (!string.IsNullOrEmpty(path)) OpenPath(path);
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
      if (viewTabs.SelectedItem is TabItem tab) CloseTab(tab);
    }

    private async void MenuSave_Click(object sender, RoutedEventArgs e)
    {
      if (CurrentView != null) await CurrentView.Save();
      UpdateSaveMenus();
    }

    private async void MenuSaveAs_Click(object sender, RoutedEventArgs e)
    {
      if (CurrentView != null) await CurrentView.SaveAs();
      UpdateSaveMenus();
    }

    private async void MenuNewGp4_Click(object sender, RoutedEventArgs e)
    {
      var path = await Dialogs.SaveFile(this, "Choose project location…", "project.gp4",
        Dialogs.Type("GP4 Projects", "*.gp4"));
      if (string.IsNullOrEmpty(path)) return;
      var proj = Gp4Project.Create(VolumeType.pkg_ps4_ac_data);
      var view = new GP4View(proj, path) { Modified = true };
      OpenTab(view, "*" + Path.GetFileName(path));
    }

    private void MenuNewSfo_Click(object sender, RoutedEventArgs e)
    {
      OpenTab(new SFOView(new ParamSfo()), "New SFO");
    }

    private async void MenuCombinePkg_Click(object sender, RoutedEventArgs e)
    {
      var part0 = await Dialogs.OpenFile(this, "Select part 0",
        Dialogs.Type("PKG Parts", "*_0.pkg"));
      if (string.IsNullOrEmpty(part0)) return;

      var filenames = new List<string> { part0 };
      ulong pkgSize;
      long remaining;
      using (var s = File.OpenRead(part0))
      {
        var hdr = new PkgReader(s).ReadHeader();
        pkgSize = hdr.package_size;
        remaining = (long)hdr.package_size - s.Length;
      }
      if (remaining <= 0)
      {
        await Dialogs.Error(this, "Reported package size was less than part file size.");
        return;
      }

      var baseName = part0[..^6];
      var target = await Dialogs.SaveFile(this, "Select output file", baseName + ".pkg",
        Dialogs.Type("PKG Files", "*.pkg"));
      if (string.IsNullOrEmpty(target)) return;

      var i = 0;
      while (remaining > 0)
      {
        var next = $"{baseName}_{++i}.pkg";
        if (!File.Exists(next))
        {
          await Dialogs.Error(this, $"Missing part {i}; still need {remaining} bytes.");
          return;
        }
        filenames.Add(next);
        remaining -= new FileInfo(next).Length;
      }

      var log = new LogWindow("Combine PKG Parts");
      log.Show(this);
      try
      {
        await using var fo = File.Create(target);
        fo.SetLength((long)pkgSize);
        log.Log("Merging files to {0}…", target);
        foreach (var fn in filenames)
        {
          log.Log("Copying {0}", fn);
          await using var fi = File.OpenRead(fn);
          await fi.CopyToAsync(fo);
        }
        log.Finish(true, "Saved to " + target);
      }
      catch (Exception ex)
      {
        log.Log("Error: " + ex);
        log.Finish(false, "Failed");
      }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private async void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
      var libVer = typeof(Pkg).Assembly.GetName().Version?.ToString() ?? "?";
      var appVer = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?";
      await Dialogs.Message(this, "About Pkg Editor",
        $"Pkg Editor (Linux / Avalonia)\nLibOrbisPkg {libVer}\nPkgEditor {appVer}\n\nOriginal by Maxton\nEAspotifi . Linux Port");
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
      e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
      if (!e.Data.Contains(DataFormats.Files)) return;
      foreach (var item in e.Data.GetFiles() ?? Array.Empty<IStorageItem>())
      {
        var path = item.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
          OpenPath(path);
      }
    }
  }
}
