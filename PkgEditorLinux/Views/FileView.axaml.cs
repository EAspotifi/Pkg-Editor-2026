using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibOrbisPkg.PFS;
using PfsDir = LibOrbisPkg.PFS.PfsReader.Dir;
using PfsFile = LibOrbisPkg.PFS.PfsReader.File;
using PfsNode = LibOrbisPkg.PFS.PfsReader.Node;

namespace PkgEditorLinux.Views
{
  /// <summary>
  /// Browses the contents of one or more PFS images (port of FileView).
  /// </summary>
  public partial class FileView : UserControl
  {
    public class Row
    {
      public string Icon { get; set; }
      public string Name { get; set; }
      public string CompSize { get; set; }
      public string Size { get; set; }
      public PfsNode Node { get; set; }
    }

    private readonly List<TreeViewItem> roots = new List<TreeViewItem>();
    private readonly Dictionary<PfsDir, TreeViewItem> nodeMap = new Dictionary<PfsDir, TreeViewItem>();
    private PfsDir currentDir;
    private bool busy;

    public FileView()
    {
      InitializeComponent();
      DirTree.ItemsSource = roots;
      DirTree.SelectionChanged += (_, _) =>
      {
        if (DirTree.SelectedItem is TreeViewItem t && t.Tag is PfsDir d) LoadDirectory(d);
      };
      FilesGrid.SelectionChanged += (_, _) => UpdateButtons();
      FilesGrid.DoubleTapped += FilesGrid_DoubleTapped;
      ExtractButton.Click += async (_, _) => await ExtractSelection(false);
      ExtractCompressedButton.Click += async (_, _) => await ExtractSelection(true);
      CtxExtract.Click += async (_, _) => await ExtractSelection(false);
      CtxExtractCompressed.Click += async (_, _) => await ExtractSelection(true);
    }

    private Window Owner => TopLevel.GetTopLevel(this) as Window;

    public void AddRoot(PfsReader p, string name)
    {
      var superroot = p.GetSuperRoot();
      var root = BuildNode(superroot, name);
      root.IsExpanded = true;
      roots.Add(root);
      DirTree.ItemsSource = null;
      DirTree.ItemsSource = roots;
      if (roots.Count == 1)
      {
        var first = (root.ItemsSource as List<TreeViewItem>)?.FirstOrDefault() ?? root;
        DirTree.SelectedItem = first;
        if (first.Tag is PfsDir d) LoadDirectory(d);
      }
    }

    private TreeViewItem BuildNode(PfsDir d, string header)
    {
      var item = new TreeViewItem { Header = "📁  " + header, Tag = d };
      nodeMap[d] = item;
      var children = d.children.OfType<PfsDir>().Select(c => BuildNode(c, c.name)).ToList();
      if (children.Count > 0) item.ItemsSource = children;
      return item;
    }

    private void LoadDirectory(PfsDir directory)
    {
      currentDir = directory;
      PathText.Text = string.IsNullOrEmpty(directory.FullName) ? "/" : directory.FullName;
      FilesGrid.ItemsSource = directory.children
        .OrderBy(c => c is PfsDir ? 0 : 1)
        .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase)
        .Select(child => new Row
        {
          Icon = child is PfsDir ? "📁" : "📄",
          Name = child.name,
          CompSize = child is PfsDir ? "" : Dialogs.HumanReadableFileSize(child.compressed_size),
          Size = child is PfsDir ? "" : Dialogs.HumanReadableFileSize(child.size),
          Node = child,
        }).ToList();
      StatusText.Text = $"{directory.children.Count} item(s)";
      UpdateButtons();
    }

    private void FilesGrid_DoubleTapped(object sender, TappedEventArgs e)
    {
      if (FilesGrid.SelectedItem is Row r && r.Node is PfsDir d)
      {
        if (nodeMap.TryGetValue(d, out var item))
        {
          ExpandParents(item);
          DirTree.SelectedItem = item;
        }
        LoadDirectory(d);
      }
    }

    private void ExpandParents(TreeViewItem target)
    {
      // Expand all ancestors so the selection becomes visible.
      if (target.Tag is PfsDir td)
        for (var p = td.parent; p != null; p = p.parent)
          if (nodeMap.TryGetValue(p, out var pi)) pi.IsExpanded = true;
    }

    private List<PfsNode> SelectedNodes()
      => FilesGrid.SelectedItems.OfType<Row>().Select(r => r.Node).ToList();

    private void UpdateButtons()
    {
      var sel = SelectedNodes();
      var compressedOk = sel.Count == 1 && sel[0] is PfsFile f && f.compressed_size != f.size;
      ExtractCompressedButton.IsEnabled = CtxExtractCompressed.IsEnabled = compressedOk && !busy;
      ExtractButton.IsEnabled = CtxExtract.IsEnabled = !busy && (sel.Count > 0 || currentDir != null);
      ExtractButton.Content = sel.Count switch
      {
        0 => "⬇  Extract folder",
        1 => "⬇  Extract",
        _ => $"⬇  Extract {sel.Count} items",
      };
    }

    private async Task ExtractSelection(bool compressed)
    {
      var owner = Owner;
      if (owner == null || busy) return;
      var sel = SelectedNodes();
      try
      {
        if (sel.Count == 0 && currentDir != null)
        {
          await ExtractNode(currentDir, compressed);
        }
        else if (sel.Count == 1)
        {
          await ExtractNode(sel[0], compressed);
        }
        else if (sel.Count > 1)
        {
          var dir = await Dialogs.PickFolder(owner, "Choose a destination folder");
          if (dir == null) return;
          await RunBusy("Extracting " + sel.Count + " items…", () => SaveTo(sel, dir));
        }
      }
      catch (Exception ex)
      {
        await Dialogs.Error(owner, "Extraction failed: " + ex.Message);
      }
    }

    private async Task ExtractNode(PfsNode n, bool compressed)
    {
      var owner = Owner;
      if (n is PfsDir d)
      {
        var dir = await Dialogs.PickFolder(owner, $"Extract \"{(string.IsNullOrEmpty(d.name) ? "root" : d.name)}\" into…");
        if (dir == null) return;
        var target = string.IsNullOrEmpty(d.name) ? dir : Path.Combine(dir, d.name);
        await RunBusy($"Extracting {d.name}…", () =>
        {
          Directory.CreateDirectory(target);
          SaveTo(d.children, target);
        });
      }
      else if (n is PfsFile f)
      {
        var file = await Dialogs.SaveFile(owner, "Extract file", f.name);
        if (file == null) return;
        await RunBusy($"Extracting {f.name}…", () => f.Save(file, !compressed));
      }
    }

    private async Task RunBusy(string message, Action work)
    {
      busy = true;
      UpdateButtons();
      StatusText.Text = message;
      try
      {
        await Task.Run(work);
        StatusText.Text = "✔ Extraction complete";
      }
      catch (Exception ex)
      {
        StatusText.Text = "✖ " + ex.Message;
        throw;
      }
      finally
      {
        busy = false;
        UpdateButtons();
      }
    }

    private void SaveTo(IEnumerable<PfsNode> nodes, string path)
    {
      foreach (var n in nodes)
      {
        if (n is PfsFile f)
        {
          var name = n.name;
          Dispatcher.UIThread.Post(() => StatusText.Text = "Extracting " + name + "…");
          f.Save(Path.Combine(path, n.name), true);
        }
        else if (n is PfsDir d)
        {
          var newPath = Path.Combine(path, d.name);
          Directory.CreateDirectory(newPath);
          SaveTo(d.children, newPath);
        }
      }
    }
  }
}
