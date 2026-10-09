using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LibOrbisPkg.GP4;
using LibOrbisPkg.Kimie;
using LibOrbisPkg.PKG;
using LibOrbisPkg.PFS;
using LibOrbisPkg.SFO;
using LibOrbisPkg.Util;
using System.IO.MemoryMappedFiles;

namespace PkgEditorLinux.Views
{
  public partial class PkgView : ViewBase
  {
    public class EntryRow
    {
      public string Id { get; set; }
      public string Size { get; set; }
      public string Offset { get; set; }
      public string Encrypted { get; set; }
      public string KeyIndex { get; set; }
      public MetaEntry Entry { get; set; }
    }

    private Pkg pkg;
    private MemoryMappedFile pkgFile;
    private MemoryMappedViewAccessor va;
    private string passcode;
    private byte[] ekpfs, data, tweak;
    private bool exporting;

    public PkgView() => InitializeComponent();

    public PkgView(string path)
    {
      InitializeComponent();
      Title = Path.GetFileName(path);
      if (string.IsNullOrEmpty(path)) return;

      TryPasscodeBtn.Click += TryPasscode_Click;
      TryEkpfsBtn.Click += TryEkpfs_Click;
      TryXtsBtn.Click += TryXts_Click;
      ExportGp4Btn.Click += ExportGp4_Click;
      ExtractEntryBtn.Click += async (_, _) => await ExtractSelected(false);
      ExtractDecryptBtn.Click += async (_, _) => await ExtractSelected(true);
      EntriesGrid.SelectionChanged += (_, _) => UpdateEntryButtons();

      pkgFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      using (var s = pkgFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
        ObjectTree.Populate(PkgHeaderTreeView, new PkgReader(s).ReadHeader());
      using (var s = pkgFile.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
        pkg = new PkgReader(s).ReadPkg();

      try
      {
        using var s = pkgFile.CreateViewStream((long)pkg.Header.pfs_image_offset, (long)pkg.Header.pfs_image_size, MemoryMappedFileAccess.Read);
        ObjectTree.Populate(PfsHeaderTreeView, PfsHeader.ReadFromStream(s));
      }
      catch (Exception ex)
      {
        _ = Dialogs.Error(Owner, "Error loading outer PFS: " + ex.Message);
      }

      ContentIdTextBox.Text = pkg.Header.content_id;
      TitleTextBox.Text = pkg.ParamSfo?.ParamSfo?["TITLE"]?.ToString() ?? "";
      SizeLabel.Text = Dialogs.HumanReadableFileSize((long)pkg.Header.package_size);
      var category = pkg.ParamSfo?.ParamSfo?["CATEGORY"]?.ToString() ?? "";
      TypeLabel.Text = SfoData.SfoTypes.FirstOrDefault(x => x.Category == category)?.Description ?? "Unknown";
      VersionLabel.Text = pkg.ParamSfo?.ParamSfo?["VERSION"]?.ToString() ?? "";
      if (pkg.ParamSfo?.ParamSfo?["APP_VER"] is Utf8Value v)
        AppVerLabel.Text = v.Value;
      else
      {
        AppVerLabelLabel.IsVisible = false;
        AppVerLabel.IsVisible = false;
      }

      if (pkg.ParamSfo != null)
        SfoTab.Content = new SFOView(pkg.ParamSfo.ParamSfo, true);

      LoadCoverArt();

      EntriesGrid.ItemsSource = pkg.Metas.Metas.Select(e => new EntryRow
      {
        Id = e.id.ToString(),
        Size = $"0x{e.DataSize:X}",
        Offset = $"0x{e.DataOffset:X}",
        Encrypted = e.Encrypted ? "Yes" : "No",
        KeyIndex = e.KeyIndex.ToString(),
        Entry = e,
      }).ToList();

      ResolveKeys();
      if (!ReopenFileView())
      {
        ekpfs = null;
        passcode = null;
        data = null;
        tweak = null;
      }
    }

    private void LoadCoverArt()
    {
      CoverFrame.IsVisible = false;
      CoverImage.Source = null;

      var iconEntry = pkg.Metas?.Metas?.FirstOrDefault(e => e.id == EntryId.ICON0_PNG)
        ?? pkg.Metas?.Metas?.FirstOrDefault(e => e.id == EntryId.ICON0_00_PNG);
      if (iconEntry == null || iconEntry.DataSize == 0) return;

      try
      {
        using var s = pkgFile.CreateViewStream(iconEntry.DataOffset, iconEntry.DataSize, MemoryMappedFileAccess.Read);
        using var ms = new MemoryStream((int)iconEntry.DataSize);
        s.CopyTo(ms);
        ms.Position = 0;
        CoverImage.Source = new Bitmap(ms);
        CoverFrame.IsVisible = true;
      }
      catch
      {
        CoverFrame.IsVisible = false;
      }
    }

    private void ResolveKeys()
    {
      if (pkg.CheckPasscode("00000000000000000000000000000000"))
      {
        passcode = "00000000000000000000000000000000";
        ekpfs = Crypto.ComputeKeys(pkg.Header.content_id, passcode, 1);
      }
      else if (KeyDB.Instance.Passcodes.TryGetValue(pkg.Header.content_id, out var dbPass)
               && pkg.CheckPasscode(dbPass))
      {
        passcode = dbPass;
        ekpfs = Crypto.ComputeKeys(pkg.Header.content_id, passcode, 1);
      }
      else if (pkg.GetEkpfs() is byte[] ek && ek != null)
      {
        ekpfs = ek;
      }
      else if (KeyDB.Instance.EKPFS.TryGetValue(pkg.Header.content_id, out var dbEk)
               && dbEk.FromHexCompact() is byte[] ekBytes
               && pkg.CheckEkpfs(ekBytes))
      {
        ekpfs = ekBytes;
      }
      else if ((KeyDB.Instance.XTS.TryGetValue(
                 pkg.Header.content_id + "-" + pkg.Header.pfs_image_digest.ToHexCompact()[..8],
                 out var xts)
               || KeyDB.Instance.XTS.TryGetValue(pkg.Header.content_id, out xts))
               && !string.IsNullOrEmpty(xts?.Data)
               && !string.IsNullOrEmpty(xts?.Tweak))
      {
        data = xts.Data.FromHexCompact();
        tweak = xts.Tweak.FromHexCompact();
      }
    }

    private bool ReopenFileView()
    {
      if (!pkg.CheckEkpfs(ekpfs) && (data == null || tweak == null))
        return false;
      if (va != null) return false;
      try
      {
        va = pkgFile.CreateViewAccessor((long)pkg.Header.pfs_image_offset, (long)pkg.Header.pfs_image_size, MemoryMappedFileAccess.Read);
        var outerPfs = new PfsReader(va, pkg.Header.pfs_flags, ekpfs, tweak, data);
        var innerPfsView = new PFSCReader(outerPfs.GetFile("pfs_image.dat").GetView());
        var inner = new PfsReader(innerPfsView);
        var view = new FileView();
        view.AddRoot(outerPfs, "Outer PFS Image");
        view.AddRoot(inner, "Inner PFS Image");
        FilesTab.Content = view;
        return true;
      }
      catch
      {
        va?.Dispose();
        va = null;
        return false;
      }
    }

    private async void TryPasscode_Click(object sender, RoutedEventArgs e)
    {
      if (!pkg.CheckPasscode(PasscodeTextBox.Text))
      {
        await Dialogs.Error(Owner, "Invalid passcode.");
        return;
      }
      passcode = PasscodeTextBox.Text;
      ekpfs = Crypto.ComputeKeys(pkg.Header.content_id, passcode, 1);
      if (ReopenFileView())
      {
        KeyDB.Instance.Passcodes[pkg.Header.content_id] = passcode;
        KeyDB.Instance.Save();
      }
      else
        await Dialogs.Error(Owner, "Could not open PFS with that passcode.");
    }

    private async void TryEkpfs_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        ekpfs = EkpfsTextBox.Text.FromHexCompact();
        if (!ReopenFileView())
        {
          ekpfs = null;
          await Dialogs.Error(Owner, "Invalid EKPFS.");
        }
        else
        {
          KeyDB.Instance.EKPFS[pkg.Header.content_id] = ekpfs.ToHexCompact();
          KeyDB.Instance.Save();
        }
      }
      catch (Exception ex)
      {
        await Dialogs.Error(Owner, "Invalid EKPFS: " + ex.Message);
      }
    }

    private async void TryXts_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        data = XtsDataTextBox.Text.FromHexCompact();
        tweak = XtsTweakTextBox.Text.FromHexCompact();
        if (!ReopenFileView())
        {
          data = tweak = null;
          await Dialogs.Error(Owner, "Invalid XTS keys.");
        }
        else
        {
          KeyDB.Instance.XTS[pkg.Header.content_id] = new KeyDB.XTSKey
          {
            Data = data.ToHexCompact(),
            Tweak = tweak.ToHexCompact(),
          };
          KeyDB.Instance.Save();
        }
      }
      catch (Exception ex)
      {
        await Dialogs.Error(Owner, "Invalid XTS keys: " + ex.Message);
      }
    }

    private void UpdateEntryButtons()
    {
      var row = EntriesGrid.SelectedItem as EntryRow;
      ExtractEntryBtn.IsEnabled = row != null;
      ExtractDecryptBtn.IsEnabled = row?.Entry?.Encrypted == true;
    }

    private async void ExportGp4_Click(object sender, RoutedEventArgs e)
    {
      if (exporting) return;

      var outputDir = await Dialogs.PickFolder(Owner, "Choose extraction folder for GP4 project");
      if (string.IsNullOrEmpty(outputDir)) return;

      exporting = true;
      ExportGp4Btn.IsEnabled = false;
      ExportStatusText.Text = "Preparing…";
      ExportProgressBar.Value = 0;
      Count.Count1 = 0;

      try
      {
        int total = await Task.Run(() => new Counter().CountPkfFiles(pkgFile, passcode));
        if (total < 1) total = 1;
        ExportProgressBar.Maximum = total;
        ExportStatusText.Text = $"0 / {total}";

        using var cts = new CancellationTokenSource();
        var progressTask = Task.Run(async () =>
        {
          while (!cts.Token.IsCancellationRequested)
          {
            var current = Count.Count1;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
              ExportProgressBar.Value = Math.Min(current, total);
              ExportStatusText.Text = $"{Math.Min(current, total)} / {total}";
            });
            await Task.Delay(150, cts.Token).ContinueWith(_ => { });
          }
        }, cts.Token);

        await Task.Run(() =>
        {
          Gp4Creator.CreateProjectFromPKG(outputDir, pkgFile, passcode);
          ExtractPlayGoChunkDat(Path.Combine(outputDir, "sce_sys", "playgo-chunk.dat"));
        });

        cts.Cancel();
        try { await progressTask; } catch { /* cancelled */ }

        ExportProgressBar.Value = total;
        ExportStatusText.Text = "Done";
        await Dialogs.Message(Owner, "Export complete",
          $"GP4 project exported to:\n{Path.Combine(outputDir, "Project.gp4")}");
      }
      catch (Exception ex)
      {
        ExportStatusText.Text = "Failed";
        await Dialogs.Error(Owner, "Export failed: " + ex.Message);
      }
      finally
      {
        Count.Count1 = 0;
        exporting = false;
        ExportGp4Btn.IsEnabled = true;
        ExportProgressBar.Value = 0;
        ExportStatusText.Text = "";
      }
    }

    /// <summary>Extract PLAYGO_CHUNK_DAT into sce_sys (parity with Windows PkgView).</summary>
    private void ExtractPlayGoChunkDat(string fileName)
    {
      var entry = pkg.Metas?.Metas?.FirstOrDefault(m => m.id == EntryId.PLAYGO_CHUNK_DAT);
      if (entry == null) return;

      Directory.CreateDirectory(Path.GetDirectoryName(fileName));
      var total = entry.Encrypted ? (entry.DataSize + 15) & ~15 : entry.DataSize;
      using var f = File.Create(fileName);
      using var entryStream = pkgFile.CreateViewStream(entry.DataOffset, total, MemoryMappedFileAccess.Read);
      if (entry.Encrypted && (passcode != null || entry.KeyIndex == 3))
      {
        var tmp = new byte[total];
        entryStream.Read(tmp, 0, tmp.Length);
        tmp = entry.KeyIndex == 3
          ? Entry.Decrypt(tmp, pkg, entry)
          : Entry.Decrypt(tmp, pkg.Header.content_id, passcode, entry);
        f.Write(tmp, 0, (int)entry.DataSize);
      }
      else
      {
        entryStream.CopyTo(f);
      }
    }

    private async Task ExtractSelected(bool decrypt)
    {
      if (EntriesGrid.SelectedItem is not EntryRow row) return;
      var entry = row.Entry;
      if (decrypt && entry.Encrypted && passcode == null && entry.KeyIndex != 3)
      {
        while (true)
        {
          var code = await Dialogs.Input(Owner, "Passcode", "Enter the package passcode:", "", 32);
          if (code == null) return;
          if (pkg.CheckPasscode(code))
          {
            passcode = code;
            ekpfs = Crypto.ComputeKeys(pkg.Header.content_id, passcode, 1);
            ReopenFileView();
            break;
          }
          var choice = await Dialogs.Message(Owner, "Invalid Passcode",
            "Passcode incorrect.\nAbort = cancel, Ignore = extract encrypted, Retry = try again.",
            "Abort", "Ignore", "Retry");
          if (choice == "Abort") return;
          if (choice == "Ignore") break;
        }
      }

      var name = entry.NameTableOffset != 0 ? pkg.EntryNames.GetName(entry.NameTableOffset) : entry.id.ToString();
      var dest = await Dialogs.SaveFile(Owner, "Extract entry", name);
      if (string.IsNullOrEmpty(dest)) return;

      var total = entry.Encrypted ? (entry.DataSize + 15) & ~15 : entry.DataSize;
      await using var f = File.OpenWrite(dest);
      using var entryStream = pkgFile.CreateViewStream(entry.DataOffset, total, MemoryMappedFileAccess.Read);
      if (entry.Encrypted && decrypt && (passcode != null || entry.KeyIndex == 3))
      {
        var tmp = new byte[total];
        entryStream.Read(tmp, 0, tmp.Length);
        tmp = entry.KeyIndex == 3
          ? Entry.Decrypt(tmp, pkg, entry)
          : Entry.Decrypt(tmp, pkg.Header.content_id, passcode, entry);
        f.Write(tmp, 0, (int)entry.DataSize);
      }
      else
      {
        await entryStream.CopyToAsync(f);
      }
    }

    public override void Close()
    {
      va?.Dispose();
      pkgFile?.Dispose();
    }
  }
}
