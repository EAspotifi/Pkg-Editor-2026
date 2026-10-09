using Avalonia.Controls;
using LibOrbisPkg.PFS;
using LibOrbisPkg.Util;
using System.IO.MemoryMappedFiles;

namespace PkgEditorLinux.Views
{
  public class PfsView : ViewBase
  {
    private MemoryMappedFile pfsFile;
    private MemoryMappedViewAccessor va;
    private PfsReader reader;

    public PfsView(string filename)
    {
      Title = Path.GetFileName(filename);
      Content = Dialogs.Text("Opening…", "muted");
      AttachedToVisualTree += async (_, _) =>
      {
        if (reader != null) return;
        await InitAsync(filename);
      };
    }

    private async Task InitAsync(string filename)
    {
      pfsFile = MemoryMappedFile.CreateFromFile(filename, FileMode.Open, mapName: null, 0, MemoryMappedFileAccess.Read);
      va = pfsFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
      va.Read(0, out int val);

      if (val == PFSCReader.Magic)
      {
        reader = new PfsReader(new PFSCReader(va));
      }
      else
      {
        PfsHeader header;
        using (var h = pfsFile.CreateViewStream(0, 0x600, MemoryMappedFileAccess.Read))
          header = PfsHeader.ReadFromStream(h);

        if (header.Mode.HasFlag(PfsMode.Encrypted))
        {
          var dataHex = await Dialogs.Input(Owner, "PFS encrypted", "Enter data key (hex):", "", 64);
          if (dataHex == null) { Content = Dialogs.Text("Cancelled — encrypted PFS not opened.", "muted"); return; }
          var tweakHex = await Dialogs.Input(Owner, "PFS encrypted", "Enter tweak key (hex):", "", 64);
          if (tweakHex == null) { Content = Dialogs.Text("Cancelled — encrypted PFS not opened.", "muted"); return; }
          try
          {
            var data = dataHex.FromHexCompact();
            var tweak = tweakHex.FromHexCompact();
            reader = new PfsReader(va, data: data, tweak: tweak);
          }
          catch (Exception ex)
          {
            Content = Dialogs.Text("Failed to open encrypted PFS: " + ex.Message, "muted");
            return;
          }
        }
        else
        {
          reader = new PfsReader(va);
        }
      }

      var fileView = new FileView();
      fileView.AddRoot(reader, filename);
      Content = fileView;
    }

    public override void Close()
    {
      va?.Dispose();
      pfsFile?.Dispose();
      base.Close();
    }
  }
}
