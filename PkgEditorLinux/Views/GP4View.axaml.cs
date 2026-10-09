using Avalonia.Controls;
using Avalonia.Interactivity;
using LibOrbisPkg.GP4;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;

namespace PkgEditorLinux.Views
{
  public partial class GP4View : ViewBase
  {
    private Gp4Project proj;
    private string path;
    private bool loaded;
    private bool modified;
    private Dir currentDir;
    private string listViewPath = "";

    public class FileRow
    {
      public string TargetName { get; set; }
      public string OrigPath { get; set; }
      public object Node { get; set; }
    }

    public bool Modified
    {
      get => modified;
      set
      {
        if (value == modified) return;
        modified = value;
        Title = (modified ? "*" : "") + Path.GetFileName(path);
        OnSaveStatusChanged();
      }
    }

    public override bool CanSave => Modified;
    public override bool CanSaveAs => true;

    public GP4View() => InitializeComponent();

    public GP4View(Gp4Project proj, string path)
    {
      InitializeComponent();
      this.proj = proj;
      this.path = path;
      Title = Path.GetFileName(path);

      ContentIdTextBox.TextChanged += OnPackageFieldChanged;
      PasscodeTextBox.TextChanged += OnPackageFieldChanged;
      EntitlementKeyTextbox.TextChanged += OnPackageFieldChanged;
      PkgTypeDropdown.SelectionChanged += PkgTypeChanged;
      DirsTreeView.SelectionChanged += (_, _) => OnDirSelected();
      BuildPkgButton.Click += BuildPkg_Click;
      BuildPfsButton.Click += BuildPfs_Click;

      ReloadView();
    }

    private void ReloadView()
    {
      loaded = false;
      switch (proj.volume.Type)
      {
        case VolumeType.pkg_ps4_app:
          PkgTypeDropdown.SelectedIndex = 0;
          EntitlementKeyTextbox.IsEnabled = false;
          break;
        case VolumeType.pkg_ps4_ac_data:
          PkgTypeDropdown.SelectedIndex = 1;
          EntitlementKeyTextbox.IsEnabled = true;
          break;
        case VolumeType.pkg_ps4_ac_nodata:
          PkgTypeDropdown.SelectedIndex = 2;
          EntitlementKeyTextbox.IsEnabled = true;
          break;
      }
      ContentIdTextBox.Text = proj.volume.Package.ContentId;
      PasscodeTextBox.Text = proj.volume.Package.Passcode;
      EntitlementKeyTextbox.Text = proj.volume.Package.EntitlementKey ?? "";
      PopulateDirs();
      loaded = true;
    }

    public override Task Save()
    {
      using var fs = File.OpenWrite(path);
      fs.SetLength(0);
      Gp4Project.WriteTo(proj, fs);
      Modified = false;
      return Task.CompletedTask;
    }

    public override async Task SaveAs()
    {
      var dest = await Dialogs.SaveFile(Owner, "Save GP4 project", path,
        Dialogs.Type("GP4 Projects", "*.gp4"));
      if (string.IsNullOrEmpty(dest)) return;
      path = dest;
      Title = Path.GetFileName(path);
      await Save();
    }

    private void OnPackageFieldChanged(object sender, TextChangedEventArgs e)
    {
      if (!loaded) return;
      Modified = true;
      proj.volume.Package.ContentId = ContentIdTextBox.Text;
      proj.volume.Package.Passcode = PasscodeTextBox.Text;
      proj.volume.Package.EntitlementKey = string.IsNullOrEmpty(EntitlementKeyTextbox.Text)
        ? null
        : EntitlementKeyTextbox.Text;
    }

    private void PkgTypeChanged(object sender, SelectionChangedEventArgs e)
    {
      if (!loaded) return;
      switch (PkgTypeDropdown.SelectedIndex)
      {
        case 0: proj.SetType(VolumeType.pkg_ps4_app); break;
        case 1: proj.SetType(VolumeType.pkg_ps4_ac_data); break;
        case 2: proj.SetType(VolumeType.pkg_ps4_ac_nodata); break;
        default: return;
      }
      Modified = true;
      ReloadView();
    }

    private void PopulateDirs()
    {
      TreeViewItem AddDir(Dir d)
      {
        var node = new TreeViewItem { Header = d.TargetName, Tag = d };
        var kids = d.Children.Select(AddDir).ToList();
        if (kids.Count > 0) node.ItemsSource = kids;
        if (d == currentDir) DirsTreeView.SelectedItem = node;
        return node;
      }

      var root = new TreeViewItem { Header = "Image0", Tag = proj.RootDir, IsExpanded = true };
      root.ItemsSource = proj.RootDir.Select(AddDir).ToList();
      DirsTreeView.ItemsSource = new[] { root };
      if (currentDir == null)
      {
        DirsTreeView.SelectedItem = root;
        listViewPath = "";
      }
      PopulateFiles();
    }

    private void OnDirSelected()
    {
      if (DirsTreeView.SelectedItem is not TreeViewItem item) return;
      if (item.Tag is Dir d)
      {
        listViewPath = d.Path;
        currentDir = d;
      }
      else if (item.Tag is List<Dir>)
      {
        listViewPath = "";
        currentDir = null;
      }
      PopulateFiles();
    }

    private void PopulateFiles()
    {
      var rows = new List<FileRow>();
      IEnumerable<Dir> dirs = currentDir == null ? proj.RootDir : currentDir.Children;
      foreach (var d in dirs)
      {
        rows.Add(new FileRow { TargetName = d.TargetName + "/", OrigPath = "", Node = d });
      }
      foreach (var f in proj.files.Items.Where(f =>
                 f.TargetPath.LastIndexOf('/') < listViewPath.Length && f.TargetPath.StartsWith(listViewPath)))
      {
        rows.Add(new FileRow { TargetName = f.FileName, OrigPath = f.OrigPath, Node = f });
      }
      FilesGrid.ItemsSource = rows;
    }

    private async void BuildPfs_Click(object sender, RoutedEventArgs e)
    {
      var dest = await Dialogs.SaveFile(Owner, "Choose output path for PFS", "pfs_image.dat",
        Dialogs.Type("PFS Image", "*.dat"));
      if (string.IsNullOrEmpty(dest)) return;

      var log = new LogWindow("Build PFS");
      log.Show(Owner);
      try
      {
        await Task.Run(() =>
        {
          using var fs = File.OpenWrite(dest);
          new PfsBuilder(
            PfsProperties.MakeInnerPFSProps(PkgProperties.FromGp4(proj, Path.GetDirectoryName(path))),
            msg => log.Log(msg)).WriteImage(fs);
        });
        log.Log("Done! Saved to {0}", dest);
        log.Finish(true);
      }
      catch (Exception ex)
      {
        log.Log("Error: " + ex);
        log.Finish(false);
      }
    }

    private async void BuildPkg_Click(object sender, RoutedEventArgs e)
    {
      var validateResults = Gp4Validator.ValidateProject(proj, Path.GetDirectoryName(path));
      if (validateResults.Count != 0)
      {
        var dlg = new ValidationDialog(validateResults);
        var cont = await dlg.ShowDialog<bool?>(Owner);
        if (cont != true) return;
      }

      var dest = await Dialogs.SaveFile(Owner, "Choose output path for PKG",
        proj.volume.Package.ContentId + ".pkg",
        Dialogs.Type("PKG Image", "*.pkg"));
      if (string.IsNullOrEmpty(dest)) return;

      var log = new LogWindow("Build PKG");
      log.Show(Owner);
      try
      {
        await Task.Run(() =>
        {
          new PkgBuilder(PkgProperties.FromGp4(proj, Path.GetDirectoryName(path)))
            .Write(dest, msg => log.Log(msg));
          log.Log("Saved to {0}", dest);
        });
        log.Finish(true);
      }
      catch (Exception ex)
      {
        log.Log("Error: " + ex);
        log.Finish(false);
      }
    }
  }
}
