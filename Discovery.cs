using System.Net;
using System.Net.Sockets;
using System.Text;

namespace transferIt;

// Lets the sender find the receiver on the LAN with only the port number.
// The sender broadcasts a UDP "ping" to the whole network on `port`;
// the receiver answers, and the answer's source address is the receiver's IP.
// (UDP and TCP ports are separate, so the same number is reused for both.)
public static class Discovery
{
  static readonly byte[] ping = Encoding.ASCII.GetBytes("TRANSFERIT_DISCOVER");
  static readonly byte[] pong = Encoding.ASCII.GetBytes("TRANSFERIT_HERE");

  // Receiver side: answers pings in the background until the returned client is disposed.
  public static UdpClient startResponder(int port)
  {
    UdpClient udp = new(new IPEndPoint(IPAddress.Any, port));
    Task.Run(() =>
    {
      try
      {
        while (true)
        {
          IPEndPoint? from = null;
          byte[] msg = udp.Receive(ref from);
          if (msg.AsSpan().SequenceEqual(ping))
            udp.Send(pong, pong.Length, from);
        }
      }
      catch (Exception e) when (e is ObjectDisposedException or SocketException)
      {
        // udp was closed: transfer is over, stop answering.
      }
    });
    return udp;
  }

  // Sender side: returns the receiver's IP, or null if nobody answered.
  public static IPAddress? findReceiver(int port, int attempts = 3)
  {
    using UdpClient udp = new() { EnableBroadcast = true };
    udp.Client.ReceiveTimeout = 1000; // ms per attempt
    IPEndPoint broadcast = new(IPAddress.Broadcast, port);

    for (int i = 0; i < attempts; i++)
    {
      udp.Send(ping, ping.Length, broadcast);
      try
      {
        while (true)
        {
          IPEndPoint? from = null;
          byte[] msg = udp.Receive(ref from);
          if (msg.AsSpan().SequenceEqual(pong))
            return from.Address;
        }
      }
      catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
      {
        // no answer this round; try again
      }
    }
    return null;
  }
}
