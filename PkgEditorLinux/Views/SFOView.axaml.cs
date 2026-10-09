using Avalonia.Controls;
using LibOrbisPkg.SFO;

namespace PkgEditorLinux.Views
{
  public partial class SFOView : ViewBase
  {
    public class SfoRow
    {
      public string Name { get; set; }
      public string Type { get; set; }
      public int Length { get; set; }
      public int MaxLength { get; set; }
      public string Value { get; set; }
    }

    private ParamSfo sfo;
    private string path;
    private readonly bool @readonly;
    private bool modified;

    public override bool CanSave => modified && !@readonly && !string.IsNullOrEmpty(path);
    public override bool CanSaveAs => !@readonly;

    public SFOView() => InitializeComponent();

    public SFOView(ParamSfo sfo, bool readOnly = false, string path = null)
    {
      InitializeComponent();
      this.sfo = sfo;
      this.path = path;
      @readonly = readOnly;
      Title = string.IsNullOrEmpty(path) ? "PARAM.SFO" : Path.GetFileName(path);
      Reload();
    }

    private void Reload()
    {
      var grid = this.FindControl<DataGrid>("SfoGrid");
      if (grid == null || sfo == null) return;
      grid.ItemsSource = sfo.Values.Select(v => new SfoRow
      {
        Name = v.Name,
        Type = v.Type.ToString(),
        Length = v.Length,
        MaxLength = v.MaxLength,
        Value = v.ToString(),
      }).ToList();
    }

    public override async Task Save()
    {
      if (!CanSave) { await SaveAs(); return; }
      await using var fs = File.Create(path);
      sfo.Write(fs);
      modified = false;
      OnSaveStatusChanged();
    }

    public override async Task SaveAs()
    {
      if (!CanSaveAs) return;
      var dest = await Dialogs.SaveFile(Owner, "Save SFO", path ?? "param.sfo",
        Dialogs.Type("SFO Files", "*.sfo"));
      if (string.IsNullOrEmpty(dest)) return;
      path = dest;
      Title = Path.GetFileName(path);
      modified = true;
      await Save();
    }
  }
}
