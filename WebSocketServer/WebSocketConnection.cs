using System;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Linq;

using Common.Logging;

namespace WebSocketServer {
  public class DataReceivedEventArgs {
    public int Size { get; private set; }
    public string Data { get; private set; }
    public DataReceivedEventArgs(int size, string data) {
      Size = size;
      Data = data;
    }
  }

  public delegate void DataReceivedEventHandler(WebSocketConnection sender, DataReceivedEventArgs e);
  public delegate void WebSocketDisconnectedEventHandler(WebSocketConnection sender, EventArgs e);

  public class WebSocketConnection : IDisposable {

    #region Private members
    private static readonly ILog log = LogManager.GetCurrentClassLogger();
    private static readonly Int32 NumMaskBytes = 4;

    private byte[] dataBuffer;  // buffer to hold the data we are reading
    #endregion

    /// <summary>
    /// An event that is triggered whenever the connection has read some data from the client
    /// </summary>
    public event DataReceivedEventHandler DataReceived;

    public event WebSocketDisconnectedEventHandler Disconnected;

    /// <summary>
    /// Guid for the connection - thouhgt it might be usable in some way
    /// </summary>
    public System.Guid GUID { get; private set; }

    /// <summary>
    /// Gets the socket used for the connection
    /// </summary>
    public Socket ConnectionSocket { get; private set; }

    #region Constructors
    /// <summary>
    /// constructor
    /// </summary>
    /// <param name="socket">The socket on which to esablish the connection</param>
    public WebSocketConnection(Socket socket) : this(socket, 255) {}

    /// <summary>
    /// constructor
    /// </summary>
    /// <param name="socket">The socket on which to esablish the connection</param>
    /// <param name="bufferSize">The size of the buffer used to receive data</param>
    public WebSocketConnection(Socket socket, int bufferSize) {
      ConnectionSocket = socket;
      dataBuffer = new byte[bufferSize];
      GUID = System.Guid.NewGuid();
      Listen();
    }
    #endregion

    /// <summary>
    /// Invoke the DataReceived event, called whenever the client has finished sending data.
    /// </summary>
    protected virtual void OnDataReceived(DataReceivedEventArgs e) {
      if (DataReceived != null)
        DataReceived(this, e);
    }

    /// <summary>
    /// Listens for incomming data
    /// </summary>
    private void Listen() {
      ConnectionSocket.BeginReceive(dataBuffer, 0, dataBuffer.Length, 0, Read, null);
    }

    /// <summary>
    /// Send a string to the client
    /// </summary>
    /// <param name="str">the string to send to the client</param>
    public void Send(string str) {
      if (ConnectionSocket.Connected) {
        try {
          // send the string
          ConnectionSocket.Send(Encoding.UTF8.GetBytes(str));
          /*
          writer.Write(str);
          writer.Flush();*/
        }
        catch {
          if (Disconnected != null)
            Disconnected(this, EventArgs.Empty);
        }
      }
    }

    private Byte[] Decode(Byte[] dataBuffer, Int32 bytesReceived) {
      var secondByte = dataBuffer[1];
      Int32 length = secondByte & (Byte)127;
      var indexFirstMask = 2;
      if (length == 126) {
        indexFirstMask = 4;
      }
      else if (length == 127) {
        indexFirstMask = 10;
      }

      var mask = new ArraySegment<byte>(dataBuffer, indexFirstMask, NumMaskBytes);
      var indexFirstDataByte = indexFirstMask + NumMaskBytes;
      var decoded = new Byte[bytesReceived - indexFirstDataByte];
      for (Int32 i = indexFirstDataByte, j = 0; i < bytesReceived; i++, j++) {
        decoded[i] = (Byte)(dataBuffer[i] ^ mask.ElementAt<Byte>(j % NumMaskBytes));
      }
      return decoded;
    }

    /// <summary>
    /// reads the incomming data and triggers the DataReceived event when done
    /// </summary>
    private void Read(IAsyncResult ar) {
      try {
        int sizeOfReceivedData = ConnectionSocket.EndReceive(ar);
        if (sizeOfReceivedData > 1) {
          var decoded = Decode(dataBuffer, sizeOfReceivedData);
          var dataStr = Encoding.UTF8.GetString(decoded);
          OnDataReceived(new DataReceivedEventArgs(decoded.Length, dataStr));
          if (dataStr.Length == 0 || Disconnected != null) {
            Dispose();
            Disconnected(this, EventArgs.Empty);
          }
        }
      }
      catch (Exception e) {
        log.Error(e);
        Dispose();
      }
    }

    #region cleanup
    /// <summary>
    /// Closes the socket
    /// </summary>
    public void Close() {
      ConnectionSocket.Close();
    }

    /// <summary>
    /// Closes the socket
    /// </summary>
    public void Dispose() {
      Close();
    }
    #endregion
  }
}
