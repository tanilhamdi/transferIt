using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using QRCoder;

namespace transferIt;

// `transferit serve`: shares the send list over HTTP so any device on the LAN
// (phones, tablets, other PCs) can download the files from a web browser.
// Only the files in the list are reachable, addressed by index (/f/0, /f/1, ...),
// so a browser can never request an arbitrary path on this computer.
public class WebShare
{
  List<string> files = new();
  int port;
  int downloads;

  public int askPort()
  {
    port = Input.askPort("Port to share on: ");
    return port;
  }

  public bool serve()
  {
    files = Config.getSendFiles().Where(f =>
    {
      if (File.Exists(f)) return true;
      Console.Error.WriteLine($"[WARN]: Skipping, file not found: {f}");
      return false;
    }).ToList();

    if (files.Count == 0)
    {
      Console.Error.WriteLine("[ERROR]: No files to share! Add some with: transferit add <file> [more files...]");
      return false;
    }

    HttpListener listener = new();
    listener.Prefixes.Add($"http://*:{port}/");
    try
    {
      listener.Start();
    }
    catch (HttpListenerException e)
    {
      Console.Error.WriteLine($"[ERROR]: Can't use port {port}: {e.Message}");
      return false;
    }

    printAddresses();
    Console.WriteLine($"Sharing {files.Count} file(s). Press Ctrl+C to stop.\n");

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; listener.Stop(); };

    while (listener.IsListening)
    {
      HttpListenerContext ctx;
      try { ctx = listener.GetContext(); }
      catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
      {
        break; // stopped with Ctrl+C
      }
      Task.Run(() => handle(ctx)); // so several downloads can run at once
    }

    Console.WriteLine($"\nStopped. {downloads} download(s) served.");
    return true;
  }

  void handle(HttpListenerContext ctx)
  {
    HttpListenerRequest req = ctx.Request;
    HttpListenerResponse res = ctx.Response;
    string path = req.Url?.AbsolutePath ?? "/";
    string who = req.RemoteEndPoint?.Address.ToString() ?? "?";

    try
    {
      if (req.HttpMethod != "GET" && req.HttpMethod != "HEAD")
        sendText(res, 405, "Method not allowed");
      else if (path == "/")
        sendHtml(res, page());
      else if (path.StartsWith("/f/") && int.TryParse(path[3..], out int i) && i >= 0 && i < files.Count)
        sendFile(res, files[i], who, req.HttpMethod == "HEAD");
      else if (path == "/all.zip" && files.Count > 1)
        sendZip(res, who, req.HttpMethod == "HEAD");
      else
        sendText(res, 404, "Not found");
    }
    catch (Exception e) when (e is IOException or HttpListenerException or ObjectDisposedException)
    {
      Console.Error.WriteLine($"[WARN]: Download to {who} interrupted ({e.Message})");
    }
    finally
    {
      try { res.Close(); } catch { /* client already gone */ }
    }
  }

  void sendFile(HttpListenerResponse res, string path, string who, bool headOnly)
  {
    string name = Path.GetFileName(path);
    using FileStream file = File.OpenRead(path);

    res.ContentType = "application/octet-stream";
    res.ContentLength64 = file.Length;
    res.AddHeader("Content-Disposition", contentDisposition(name));
    if (headOnly) return;

    Console.WriteLine($"{who} is downloading {name} ({file.Length} bytes)...");
    file.CopyTo(res.OutputStream);
    Interlocked.Increment(ref downloads);
    Console.WriteLine($"{who} downloaded {name}");
  }

  // Streams every file into one zip on the fly (nothing is written to disk).
  void sendZip(HttpListenerResponse res, string who, bool headOnly)
  {
    res.ContentType = "application/zip";
    res.AddHeader("Content-Disposition", contentDisposition("transferit.zip"));
    if (headOnly) return;

    res.SendChunked = true; // size isn't known in advance
    Console.WriteLine($"{who} is downloading all files as zip...");

    using (ZipArchive zip = new(res.OutputStream, ZipArchiveMode.Create, leaveOpen: true))
    {
      HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
      foreach (string path in files)
      {
        // Two listed files can share a name (from different folders): keep both.
        string name = Path.GetFileName(path);
        string entryName = name;
        for (int n = 2; !used.Add(entryName); n++)
          entryName = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";

        // Already-compressed media (photos, videos) gains nothing from deflate.
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using Stream to = entry.Open();
        using FileStream from = File.OpenRead(path);
        from.CopyTo(to);
      }
    }
    Interlocked.Increment(ref downloads);
    Console.WriteLine($"{who} downloaded all files (zip)");
  }

  // filename="..." for old browsers (ASCII only), filename*= for the real UTF-8 name.
  static string contentDisposition(string name)
  {
    string ascii = new(name.Select(c => c < 128 && c != '"' && c != '\\' ? c : '_').ToArray());
    return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
  }

  static void sendHtml(HttpListenerResponse res, string html)
  {
    byte[] body = Encoding.UTF8.GetBytes(html);
    res.ContentType = "text/html; charset=utf-8";
    res.ContentLength64 = body.Length;
    res.OutputStream.Write(body);
  }

  static void sendText(HttpListenerResponse res, int status, string text)
  {
    byte[] body = Encoding.UTF8.GetBytes(text);
    res.StatusCode = status;
    res.ContentType = "text/plain; charset=utf-8";
    res.ContentLength64 = body.Length;
    res.OutputStream.Write(body);
  }

  string page()
  {
    StringBuilder rows = new();
    for (int i = 0; i < files.Count; i++)
    {
      string name = WebUtility.HtmlEncode(Path.GetFileName(files[i]));
      long size = new FileInfo(files[i]).Length;
      rows.Append($"""
        <a class="file" href="/f/{i}" download>
          <span class="name">{name}</span>
          <span class="size">{humanSize(size)}</span>
        </a>
        """);
    }

    string all = files.Count > 1
      ? """<a class="all" href="/all.zip" download>Download all (.zip)</a>"""
      : "";

    return $$"""
      <!doctype html>
      <html lang="en">
      <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>transferit</title>
        <style>
          :root { --bg:#f6f7f9; --card:#fff; --text:#1b1d21; --muted:#6b7280; --line:#e4e6ea; --accent:#2563eb; }
          @media (prefers-color-scheme: dark) {
            :root { --bg:#111315; --card:#1b1e22; --text:#e8eaed; --muted:#9aa0a6; --line:#2c3036; --accent:#5b8def; }
          }
          * { box-sizing: border-box; }
          body { margin:0; background:var(--bg); color:var(--text);
                 font: 16px/1.4 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
          main { max-width: 560px; margin: 0 auto; padding: 24px 16px 40px; }
          h1 { font-size: 22px; margin: 0 0 4px; }
          p  { color: var(--muted); margin: 0 0 20px; font-size: 14px; }
          .file { display:flex; justify-content:space-between; align-items:center; gap:12px;
                  padding: 14px 16px; background:var(--card); border:1px solid var(--line);
                  border-radius: 12px; margin-bottom: 8px; color:var(--text); text-decoration:none; }
          .file:active { transform: scale(.99); }
          .name { overflow-wrap:anywhere; font-weight:500; }
          .size { color:var(--muted); font-size:14px; white-space:nowrap; }
          .all { display:block; text-align:center; margin-top:16px; padding:14px; border-radius:12px;
                 background:var(--accent); color:#fff; text-decoration:none; font-weight:600; }
        </style>
      </head>
      <body>
        <main>
          <h1>transferit</h1>
          <p>Tap a file to download it.</p>
          {{rows}}
          {{all}}
        </main>
      </body>
      </html>
      """;
  }

  static string humanSize(long bytes)
  {
    string[] units = { "B", "KB", "MB", "GB", "TB" };
    double v = bytes;
    int u = 0;
    while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
    return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
  }

  void printAddresses()
  {
    List<IPAddress> ips = lanIPs();
    if (ips.Count == 0)
    {
      Console.WriteLine($"No network connection found. On this computer: http://localhost:{port}");
      return;
    }

    string url = $"http://{ips[0]}:{port}";
    Console.WriteLine("\nScan with your phone's camera, or open in a browser on the same Wi-Fi:\n");
    printQr(url);
    Console.WriteLine($"  {url}");
    foreach (IPAddress ip in ips.Skip(1))
      Console.WriteLine($"  http://{ip}:{port}   (other network)");
    Console.WriteLine();
  }

  static void printQr(string text)
  {
    using QRCodeGenerator gen = new();
    using QRCodeData data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
    // Half-block characters: 2 QR rows per terminal line, so it fits on screen.
    Console.WriteLine(new AsciiQRCode(data).GetGraphicSmall());
  }

  // IPv4 addresses of real network adapters, skipping loopback and virtual
  // ones (docker, VMs) that a phone can't reach.
  static List<IPAddress> lanIPs()
  {
    string[] virtualPrefixes = { "docker", "br-", "veth", "virbr", "vmnet", "vboxnet", "tun", "tap", "wg", "tailscale", "zt" };
    return NetworkInterface.GetAllNetworkInterfaces()
      .Where(n => n.OperationalStatus == OperationalStatus.Up
               && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
               && !virtualPrefixes.Any(p => n.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
      .SelectMany(n => n.GetIPProperties().UnicastAddresses)
      .Select(a => a.Address)
      .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
      .ToList();
  }
}
