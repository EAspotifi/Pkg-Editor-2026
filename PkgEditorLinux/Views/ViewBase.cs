using System.Collections;
using Avalonia.Controls;

namespace PkgEditorLinux.Views
{
  /// <summary>
  /// Base class for all document views shown as tabs in the main window
  /// (port of PkgEditor.Views.View).
  /// </summary>
  public class ViewBase : UserControl
  {
    public event EventHandler SaveStatusChanged;
    public event EventHandler TitleChanged;

    private string title = "";
    public string Title
    {
      get => title;
      set
      {
        if (title != value)
        {
          title = value;
          TitleChanged?.Invoke(this, EventArgs.Empty);
        }
      }
    }

    public MainWindow MainWin { get; set; }
    public virtual bool CanSave => false;
    public virtual bool CanSaveAs => false;
    public virtual Task Save() => Task.CompletedTask;
    public virtual Task SaveAs() => Task.CompletedTask;
    public virtual void Close() { }

    protected void OnSaveStatusChanged() => SaveStatusChanged?.Invoke(this, EventArgs.Empty);

    protected Window Owner => (TopLevel.GetTopLevel(this) as Window) ?? MainWin;
  }

  /// <summary>
  /// Reflection based object inspector that fills a TreeView with an object's public fields
  /// (port of ObjectView / PkgView.ObjectPreview).
  /// </summary>
  public static class ObjectTree
  {
    public static void Populate(TreeView tv, object obj, bool hexNumbers = true)
    {
      var items = new List<TreeViewItem>();
      AddObjectNodes(obj, items, hexNumbers);
      tv.ItemsSource = items;
    }

    static string ToStr(object obj, bool hex)
    {
      if (!hex) return obj?.ToString();
      switch (obj)
      {
        case byte _:
        case ushort _:
        case uint _:
        case ulong _:
        case short _:
        case int _:
        case long _:
          return string.Format("0x{0:X}", obj);
        default:
          return obj?.ToString();
      }
    }

    static TreeViewItem Node(string header, List<TreeViewItem> children = null)
    {
      var n = new TreeViewItem { Header = header };
      if (children != null && children.Count > 0) n.ItemsSource = children;
      return n;
    }

    static void AddObjectNodes(object obj, List<TreeViewItem> nodes, bool hex)
    {
      if (obj == null) return;
      foreach (var f in obj.GetType().GetFields())
      {
        if (f.IsLiteral || f.IsStatic) continue;
        var val = f.GetValue(obj);
        if (val is byte[] b)
        {
          nodes.Add(Node(f.Name + " = " + LibOrbisPkg.Util.Crypto.AsHexCompact(b)));
        }
        else if (f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
        {
          if (val != null) nodes.Add(Node(f.Name + " = " + ToStr(val, hex)));
        }
        else if (f.FieldType.IsArray)
        {
          if (val is Array arr) AddArrayNodes(arr, f.Name, nodes, hex);
        }
        else if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
        {
          if (val is IList l) AddArrayNodes(l.Cast<object>().ToArray(), f.Name, nodes, hex);
        }
        else
        {
          var children = new List<TreeViewItem>();
          AddObjectNodes(val, children, hex);
          nodes.Add(Node(f.Name, children));
        }
      }
    }

    static void AddArrayNodes(Array arr, string name, List<TreeViewItem> nodes, bool hex)
    {
      var children = new List<TreeViewItem>();
      var eType = arr.GetType().GetElementType();
      if (eType.IsPrimitive || eType == typeof(string) || eType.IsEnum)
      {
        for (var i = 0; i < arr.Length; i++)
          children.Add(Node($"{name}[{i}] = {ToStr(arr.GetValue(i), hex)}"));
      }
      else
      {
        for (var i = 0; i < arr.Length; i++)
        {
          var myName = $"{name}[{i}]";
          var item = arr.GetValue(i);
          if (item is Array inner)
          {
            AddArrayNodes(inner, myName, children, hex);
          }
          else if (item != null)
          {
            var nameField = item.GetType().GetField("Name");
            if (nameField != null)
              myName += $" (Name: {nameField.GetValue(item)})";
            var sub = new List<TreeViewItem>();
            AddObjectNodes(item, sub, hex);
            children.Add(Node(myName, sub));
          }
        }
      }
      nodes.Add(Node($"{name} ({arr.Length})", children));
    }
  }
}
