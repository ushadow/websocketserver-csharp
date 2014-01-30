using System;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

using Common.Logging;

namespace WebSocket {

  public class ClientConnectedEventArgs {
    public WebSocketConnection Client { get; private set; }
    public ClientConnectedEventArgs(WebSocketConnection client) {
      Client = client;
    }
  }

  public delegate void ClientConnectedEventHandler(WebSocketServer sender,
      ClientConnectedEventArgs e);

  public class WebSocketServer {
    private static readonly ILog Log = LogManager.GetCurrentClassLogger();
    private static readonly String MagicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private static readonly String SocketKeyPattern = "Sec-WebSocket-Key: (.*)";

    public event ClientConnectedEventHandler ClientConnected;

    private TcpListener tcpListener;
    private bool active = false;

    /// <summary>
    /// Creates a WebSocket server instance.
    /// </summary>
    /// <param name="ipAddress">Server IP address.</param>
    /// <param name="port"></param>
    public WebSocketServer(String ipAddress, Int32 port) {
      tcpListener = new TcpListener(IPAddress.Parse(ipAddress), port);
    }

    /// <summary>
    /// Starts the server to listen for client connection asynchronously. Does nothing is the 
    /// server had already been started.
    /// </summary>
    public void Start() {
      try {
        if (!active) {
          tcpListener.Start();
          Log.InfoFormat("WebSocketServer started on {0}", tcpListener.LocalEndpoint);
          ListenForClients();
          active = true;
        } else {
          Log.InfoFormat("WebSocketServer has already started on {0}", tcpListener.LocalEndpoint);
        }
      } catch (SocketException se) {
        Log.Error(se.Message);
      }
    }

    /// <summary>
    /// Stops listening for client connection.
    /// </summary>
    public void Stop() {
      if (active) {
        tcpListener.Stop();
        active = false;
      }
    }

    private void ListenForClients() {
      try {
        tcpListener.BeginAcceptTcpClient(new AsyncCallback(OnClientConnect), null);
      } catch (ObjectDisposedException ode) {
        Log.Info(ode.Message);
      }
    }

    private void OnClientConnect(IAsyncResult asyn) {
      try {
        var client = tcpListener.EndAcceptTcpClient(asyn);
        Log.InfoFormat("New connection from {0}", client.Client.RemoteEndPoint);
        ShakeHands(client);

        var clientConnection = new WebSocketConnection(client.Client);
        clientConnection.Disconnected += new WebSocketDisconnectedEventHandler(OnClientDisconnect);
        if (ClientConnected != null)
          ClientConnected(this, new ClientConnectedEventArgs(clientConnection));
        ListenForClients();
      } catch (ObjectDisposedException ode) {
        Log.Info(ode.Message);
      }
    }

    private void OnClientDisconnect(WebSocketConnection sender, EventArgs e) {
      Console.WriteLine("Client disconnected.");
    }

    private void ShakeHands(TcpClient client) {
      var stream = client.GetStream();
      while (!stream.DataAvailable) ;

      var bytes = new Byte[client.Available];
      stream.Read(bytes, 0, bytes.Length);
      var request = Encoding.UTF8.GetString(bytes);
      if (new Regex("^GET").IsMatch(request)) {
        StringBuilder sb = new StringBuilder();
        sb.Append("HTTP/1.1 101 Switching Protocols" + Environment.NewLine);
        sb.Append("Connection: Upgrade" + Environment.NewLine);
        sb.Append("Upgrade: WebSocket" + Environment.NewLine);
        sb.Append("Sec-WebSocket-Accept: " + replyHash(request) + Environment.NewLine);
        sb.Append(Environment.NewLine);
        var response = Encoding.UTF8.GetBytes(sb.ToString());
        stream.Write(response, 0, response.Length);
      }
    }

    private String replyHash(String request) {
      var replyStr = new Regex(SocketKeyPattern).Match(request).Groups[1].Value.Trim() + MagicString;
      var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(replyStr));
      return Convert.ToBase64String(hash);
    }
  }

}
