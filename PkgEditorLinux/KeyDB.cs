using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PkgEditorLinux
{
  /// <summary>
  /// Stores encryption keys in plain or hex-encoded strings.
  /// On Linux the database lives in ~/.config/LibOrbisPkg/keydb.json
  /// </summary>
  [DataContract]
  class KeyDB
  {
    [DataContract]
    public class XTSKey
    {
      [DataMember]
      public string Tweak { get; set; }
      [DataMember]
      public string Data { get; set; }
    }
    /// <summary>
    /// Plaintext passcodes indexed by Content ID
    /// </summary>
    [DataMember]
    public Dictionary<string, string> Passcodes { get; set; }
    /// <summary>
    /// Compact hexadecimal encoded PFS encryption keys indexed by Content ID
    /// </summary>
    [DataMember]
    public Dictionary<string, string> EKPFS { get; set; }
    /// <summary>
    /// Compact hexadecimal encoded XTS keys indexed by Content ID + first 4 bytes of PFS Image Digest
    /// </summary>
    [DataMember]
    public Dictionary<string, XTSKey> XTS { get; set; }

    public void Save(string path = null)
    {
      if (path == null) path = FileName;
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      // File.Create truncates; the original File.OpenWrite could leave trailing garbage.
      using (var keydb = File.Create(path))
      {
        var serializer = new DataContractJsonSerializer(typeof(KeyDB), new DataContractJsonSerializerSettings()
        {
          UseSimpleDictionaryFormat = true,
        });
        serializer.WriteObject(keydb, this);
      }
    }
    private KeyDB()
    {
      Passcodes = new Dictionary<string, string>();
      EKPFS = new Dictionary<string, string>();
      XTS = new Dictionary<string, XTSKey>();
    }
    private static KeyDB instance = null;
    public static KeyDB Instance { get => instance ?? (instance = Load()); }
    public static string FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LibOrbisPkg", "keydb.json");
    private static KeyDB Load()
    {
      if (!File.Exists(FileName))
      {
        Directory.CreateDirectory(Path.GetDirectoryName(FileName));
        return new KeyDB();
      }
      try
      {
        using (var keydb = File.OpenRead(FileName))
        {
          var serializer = new DataContractJsonSerializer(typeof(KeyDB), new DataContractJsonSerializerSettings()
          {
            UseSimpleDictionaryFormat = true
          });
          var db = serializer.ReadObject(keydb) as KeyDB ?? new KeyDB();
          db.Passcodes ??= new Dictionary<string, string>();
          db.EKPFS ??= new Dictionary<string, string>();
          db.XTS ??= new Dictionary<string, XTSKey>();
          return db;
        }
      }
      catch
      {
        return new KeyDB();
      }
    }
  }
}
