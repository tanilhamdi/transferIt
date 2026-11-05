using System.Net;
using System.Net.Sockets;
using System.Text;

namespace transferIt;

public class Sender
{
  IPAddress? host;
  int port;
  List<string> fileList = new();
  TcpClient? client;

  public int askPort()
  {
    port = Input.askPort("Port to send to: ");
    return port;
  }

  public List<string> readSendercfg()
  {
    fileList = Config.getSendFiles();
    if (fileList.Count == 0)
    {
      Console.Error.WriteLine("[ERROR]: No files to send! Add some with: transferit add <file> [more files...]");
      Environment.Exit(1);
    }

    Console.WriteLine("Files to send:");

    for (int i = 0; i < fileList.Count; i++)
    {
      Console.WriteLine($"  {fileList[i]}");
    }
    return fileList;
  }

  public bool connect()
  {
    Console.WriteLine($"Looking for a receiver on port {port}...");
    host = Discovery.findReceiver(port);
    if (host is null)
    {
      Console.Error.WriteLine($"[ERROR]: No receiver found on port {port}. Is it running on the same network?");
      return false;
    }
    Console.WriteLine($"Found receiver at {host}");

    client = new TcpClient();
    try
    {
      // Timeout so an unreachable LAN address doesn't hang for a long time.
      if (client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(5)))
        return true;

      Console.Error.WriteLine($"Could not connect to {host}:{port} (timed out)");
    }
    catch (AggregateException e) when (e.InnerException is SocketException se)
    {
      Console.Error.WriteLine($"Could not connect to {host}:{port} ({se.Message})");
    }
    client.Close();
    client = null;
    return false;
  }

  // Wire format (all integers little-endian):
  //   int32  fileCount
  //   per file:
  //     int32  nameLength (bytes)
  //     byte[] name (UTF-8, file name only, no directories)
  //     int64  fileSize
  //     byte[] fileContents
  public bool sendFiles()
  {
    List<string> existing = fileList.Where(f =>
    {
      if (File.Exists(f)) return true;
      Console.Error.WriteLine($"[WARN]: Skipping, file not found: {f}");
      return false;
    }).ToList();

    if (existing.Count == 0)
    {
      Console.Error.WriteLine("[ERROR]: None of the listed files exist!");
      return false;
    }

    if (!connect()) return false;

    try
    {
      using NetworkStream stream = client!.GetStream();
      using BinaryWriter writer = new(stream);

      writer.Write(existing.Count);

      foreach (string path in existing)
      {
        byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(path));
        using FileStream file = File.OpenRead(path);

        writer.Write(name.Length);
        writer.Write(name);
        writer.Write(file.Length);
        writer.Flush();
        file.CopyTo(stream);

        Console.WriteLine($"Sent {Path.GetFileName(path)} ({file.Length} bytes)");
      }
      return true;
    }
    catch (Exception e) when (e is IOException or SocketException or UnauthorizedAccessException)
    {
      Console.Error.WriteLine($"[ERROR]: Transfer failed: {e.Message}");
      return false;
    }
    finally
    {
      client?.Close();
      client = null;
    }
  }
}
