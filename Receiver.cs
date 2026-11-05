using System.Net;
using System.Net.Sockets;
using System.Text;

namespace transferIt;

public class Receiver
{
  string targetDir = "";
  int port;

  // receiver.cfg holds one line: the folder incoming files are saved to.
  // If it isn't set yet, asks the user once and saves the answer.
  public string readReceivercfg()
  {
    string? dir = Config.getSaveDir();
    if (dir is null)
    {
      Console.WriteLine("No save folder set yet.");
      dir = Config.setSaveDir(askFolder());
    }

    targetDir = dir;
    Directory.CreateDirectory(targetDir);
    Console.WriteLine($"Saving files to: {targetDir}");
    return targetDir;
  }

  static string askFolder()
  {
    while (true)
    {
      Console.Write("Folder to save received files to: ");
      string? input = Console.ReadLine();
      if (input is null) Environment.Exit(1); // stdin closed
      if (!string.IsNullOrWhiteSpace(input)) return input;
    }
  }

  public int askPort()
  {
    port = Input.askPort("Port to listen on: ");
    return port;
  }

  // Waits for one sender, receives everything it sends, then returns.
  // Reads the format written by Sender.sendFiles().
  public bool receiveFiles()
  {
    TcpListener listener = new(IPAddress.Any, port); // reachable from the LAN
    UdpClient? responder = null;
    try
    {
      listener.Start();
      responder = Discovery.startResponder(port);
      Console.WriteLine($"Waiting for a sender on port {port}...");

      using TcpClient client = listener.AcceptTcpClient();
      Console.WriteLine($"Connected: {client.Client.RemoteEndPoint}");

      using NetworkStream stream = client.GetStream();
      using BinaryReader reader = new(stream);

      int fileCount = reader.ReadInt32();
      if (fileCount < 0) throw new InvalidDataException($"Bad file count: {fileCount}");

      for (int i = 0; i < fileCount; i++)
      {
        int nameLength = reader.ReadInt32();
        if (nameLength <= 0 || nameLength > 1024)
          throw new InvalidDataException($"Bad file name length: {nameLength}");

        // GetFileName strips any folders so a sender can't write outside targetDir.
        string name = Path.GetFileName(Encoding.UTF8.GetString(reader.ReadBytes(nameLength)));
        if (name.Length == 0) throw new InvalidDataException("Empty file name");

        long size = reader.ReadInt64();
        if (size < 0) throw new InvalidDataException($"Bad file size: {size}");

        string outPath = Path.Combine(targetDir, name);
        using (FileStream file = File.Create(outPath))
        {
          copyExactly(stream, file, size);
        }
        Console.WriteLine($"Received {name} ({size} bytes)");
      }
      return true;
    }
    catch (Exception e) when (e is IOException or SocketException or InvalidDataException or UnauthorizedAccessException)
    {
      Console.Error.WriteLine($"[ERROR]: Receive failed: {e.Message}");
      return false;
    }
    finally
    {
      responder?.Dispose();
      listener.Stop();
    }
  }

  // Copies exactly `count` bytes; throws if the connection ends early.
  static void copyExactly(Stream from, Stream to, long count)
  {
    byte[] buffer = new byte[81920];
    while (count > 0)
    {
      int read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
      if (read == 0) throw new EndOfStreamException("Connection closed mid-file");
      to.Write(buffer, 0, read);
      count -= read;
    }
  }
}
