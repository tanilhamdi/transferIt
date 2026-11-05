namespace transferIt;

// Everything about ~/.config/transferit/: creating it, reading it, editing it.
public static class Config
{
  static readonly string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  public static readonly string dir = Path.Combine(home, ".config", "transferit");
  public static readonly string senderCfg = Path.Combine(dir, "sender.cfg");
  public static readonly string receiverCfg = Path.Combine(dir, "receiver.cfg");

  // Creates the config folder and both files (empty) if they don't exist yet.
  public static void ensureExists()
  {
    Directory.CreateDirectory(dir);
    foreach (string f in new[] { senderCfg, receiverCfg })
    {
      if (!File.Exists(f))
      {
        File.WriteAllText(f, "");
        Console.WriteLine($"Created config: {f}");
      }
    }
  }

  // Turns "~/x", "./x" or "x" into a full absolute path.
  public static string fullPath(string path)
  {
    path = path.Trim();
    if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
      path = home + path.Substring(1);
    return Path.GetFullPath(path);
  }

  static List<string> readLines(string file)
  {
    ensureExists();
    return File.ReadAllLines(file)
      .Where(l => !string.IsNullOrWhiteSpace(l))
      .Select(l => l.Trim())
      .ToList();
  }

  // ---- sender.cfg: one file path per line ----

  public static List<string> getSendFiles() => readLines(senderCfg);

  // Adds files to sender.cfg. Skips ones that don't exist or are already listed.
  public static void addSendFiles(IEnumerable<string> paths)
  {
    List<string> files = getSendFiles();
    foreach (string p in paths)
    {
      string full = fullPath(p);
      if (Directory.Exists(full))
        Console.Error.WriteLine($"[WARN]: Skipping folder (only files can be sent): {full}");
      else if (!File.Exists(full))
        Console.Error.WriteLine($"[WARN]: Skipping, file not found: {full}");
      else if (files.Contains(full))
        Console.WriteLine($"Already in list: {full}");
      else
      {
        files.Add(full);
        Console.WriteLine($"Added: {full}");
      }
    }
    File.WriteAllLines(senderCfg, files);
  }

  public static void clearSendFiles()
  {
    ensureExists();
    File.WriteAllText(senderCfg, "");
    Console.WriteLine("Send list cleared.");
  }

  // ---- receiver.cfg: one line, the folder to save into ----

  public static string? getSaveDir() => readLines(receiverCfg).FirstOrDefault();

  public static string setSaveDir(string path)
  {
    string full = fullPath(path);
    Directory.CreateDirectory(full);
    ensureExists();
    File.WriteAllText(receiverCfg, full + Environment.NewLine);
    Console.WriteLine($"Files will be saved to: {full}");
    return full;
  }
}
