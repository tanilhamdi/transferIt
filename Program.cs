namespace transferIt;

class Program
{
  static int Main(string[] args)
  {
    Config.ensureExists();
    string mode = args.Length > 0 ? args[0].ToLower() : "";

    switch (mode)
    {
      case "send":
        Sender sender = new();
        sender.readSendercfg();
        sender.askPort();
        return sender.sendFiles() ? 0 : 1;

      case "receive":
        Receiver receiver = new();
        receiver.readReceivercfg();
        receiver.askPort();
        return receiver.receiveFiles() ? 0 : 1;

      case "add":
        if (args.Length < 2) return usage();
        Config.addSendFiles(args.Skip(1));
        return 0;

      case "list":
        List<string> files = Config.getSendFiles();
        Console.WriteLine(files.Count == 0 ? "Send list is empty." : "Files to send:");
        files.ForEach(f => Console.WriteLine($"  {f}"));
        Console.WriteLine($"Save folder: {Config.getSaveDir() ?? "(not set)"}");
        return 0;

      case "clear":
        Config.clearSendFiles();
        return 0;

      case "savedir":
        if (args.Length != 2) return usage();
        Config.setSaveDir(args[1]);
        return 0;

      default:
        return usage();
    }
  }

  static int usage()
  {
    Console.Error.WriteLine($"""
      Usage: transferit <command>

        send                 send the files in your send list
        receive              wait for a sender and save its files

        add <file> [...]     add files to your send list
        list                 show the send list and save folder
        clear                empty the send list
        savedir <folder>     set the folder received files are saved to

      Configs live in: {Config.dir}
      """);
    return 1;
  }
}
