using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


public class TCPClient
{
    private readonly IPAddress _serverIpAddress;
    private readonly ushort _serverPort;

    private Helper.State _state = Helper.State.Start;
    private string _displayName = "";

    private bool _sendBYE = false;
    private bool _sendERR = false;
    private bool _receivedERR = false;
    private bool _receievedBYE = false;

    private ManualResetEvent _sendEvent = new ManualResetEvent(false);
    private ManualResetEvent _receiveEvent = new ManualResetEvent(false);
    private ManualResetEvent _endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent _receivedReplyEvent = new ManualResetEvent(false);
    private ManualResetEvent _ctrlCEvent = new ManualResetEvent(false);
    private ManualResetEvent _threadsTerminatedEvent = new ManualResetEvent(false);
    private ManualResetEvent _receiveThreadPausedEvent = new ManualResetEvent(false);

    private Helper _helper = new Helper();

    public TCPClient(IPAddress serverIpAddress, ushort serverPort)
    {
        this._serverIpAddress = serverIpAddress;
        this._serverPort = serverPort;
    }

    public void Connect()
    {
        Socket? TCPSocket = null;
        NetworkStream? stream = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;
        try
        {
            TCPSocket = new Socket(_serverIpAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint TCPEndPoint = new IPEndPoint(_serverIpAddress, _serverPort);
            TCPSocket.Connect(TCPEndPoint);
            stream = new NetworkStream(TCPSocket);

            writer = new StreamWriter(stream);
            reader = new StreamReader(stream);
            /*I set receive timeout in tcp but don't use it in udp to check if the
            connection to the server is OK, in udp the confirm messages are responsible for this*/
            TCPSocket.ReceiveTimeout = 2500;
            bool authSent = false;
            SetupCtrlCHandlerTCP(writer);
            while (true)
            {
                if (_state == Helper.State.Start)
                {

                    string? input = null;
                    input = Console.ReadLine();

                    if (input != null)
                    {
                        if (_helper.IsValidCommand(input))
                        {
                            if (input.StartsWith("/auth"))
                            {
                                string[] parts = input.Split(' ');
                                if (!_helper.IsValidAuth(parts))
                                {
                                    continue;
                                }
                                string username = parts[1];
                                string secret = parts[2];
                                _displayName = parts[3];
                                string message = string.Format("AUTH {0} AS {1} USING {2}\r\n", username.Trim(), _displayName.Trim(), secret.Trim());
                                writer.Write(message);
                                writer.Flush();
                                authSent = true;
                                _state = Helper.State.Auth;
                            }
                            else if (input.StartsWith("/help"))
                            {
                                Program.PrintUserHelp();
                                continue;
                            }
                            else
                            {
                                Console.Error.WriteLine("ERR: /auth command is required. Use /auth {Username} {Secret} {DisplayName}.");
                                continue;
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine("ERR: Error sending a message in non-open state.");
                            continue;
                        }
                    }
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        _state = Helper.State.End;

                    }
                }
                if (_state == Helper.State.Auth)
                {

                    if (!authSent)
                    {
                        string? input = null;
                        input = Console.ReadLine();
                        if (input != null)
                        {
                            if (_helper.IsValidCommand(input))
                            {
                                if (input.StartsWith("/auth"))
                                {
                                    string[] parts = input.Split(' ');
                                    if (!_helper.IsValidAuth(parts))
                                    {
                                        continue;
                                    }
                                    string username = parts[1];
                                    string secret = parts[2];
                                    _displayName = parts[3];
                                    string message = string.Format("AUTH {0} AS {1} USING {2}\r\n", username.Trim(), _displayName.Trim(), secret.Trim());
                                    writer.Write(message);
                                    writer.Flush();
                                    authSent = true;
                                }
                                else if (input.StartsWith("/help"))
                                {
                                    Program.PrintUserHelp();
                                    continue;
                                }
                                else
                                {
                                    Console.Error.WriteLine("ERR: /auth command is required. Use /auth {Username} {Secret} {DisplayName}.");
                                    continue;
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine("ERR: Error sending a message in non-open state.");
                                continue;
                            }
                        }
                        else
                        {
                            writer.Write("BYE\r\n");
                            writer.Flush();
                            _state = Helper.State.End;
                            break;
                        }
                    }
                    string? receivedMessage = reader.ReadLine();
                    if (!string.IsNullOrEmpty(receivedMessage))
                    {
                        if (receivedMessage.ToUpper().StartsWith("REPLY"))
                        {
                            string[] parts = receivedMessage.Split(' ');
                            int isIndex = Array.IndexOf(parts, "IS");
                            string? reason = null;
                            if (isIndex + 1 < parts.Length)
                            {
                                reason = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                            }

                            if (parts[1] == "OK")
                            {
                                Console.Error.WriteLine("Success: " + reason);
                                _state = Helper.State.Open;
                                continue;
                            }
                            else if (parts[1] == "NOK")
                            {
                                Console.Error.WriteLine("Failure: " + reason);
                                authSent = false;
                                continue;
                            }
                        }
                        else if (receivedMessage.ToUpper().StartsWith("ERR"))
                        {
                            string[] parts = receivedMessage.Split(' ');
                            int isIndex = Array.IndexOf(parts, "IS");
                            string receivedDisplayName = parts[2];
                            string? messageContent = null;
                            if (isIndex + 1 < parts.Length)
                            {
                                messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                            }
                            Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
                            writer.Write("BYE\r\n");
                            writer.Flush();
                            _state = Helper.State.End;
                        }
                        else
                        {
                            _state = Helper.State.Error;
                        }
                    }
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        _state = Helper.State.End;
                    }
                }
                if (_state == Helper.State.Open)
                {
                    _sendEvent.Reset();
                    _receiveEvent.Reset();
                    _receivedReplyEvent.Reset();
                    _endOfInputEvent.Reset();
                    TCPSocket.ReceiveTimeout = 250;

                    Thread sendThread = new Thread(() => SendMessageTCP(writer));
                    Thread receiveThread = new Thread(() => ReceiveMessageTCP(reader));
                    sendThread.Start();
                    receiveThread.Start();

                    sendThread.Join();
                    receiveThread.Join();
                    _threadsTerminatedEvent.Set();

                    if (_sendBYE == true)
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        _sendBYE = false;
                        _state = Helper.State.End;
                        break;
                    }
                    if (_sendERR == true)
                    {
                        string message = string.Format("ERR FROM {0} IS Incoming message from {1}:{2} failed to be parsed.\r\n", _displayName, _serverIpAddress, _serverPort);
                        writer.Write(message);
                        writer.Flush();
                        _sendERR = false;
                        _state = Helper.State.Error;
                    }
                }
                if (_state == Helper.State.Error)
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    _state = Helper.State.End;
                    break;
                }
                if (_state == Helper.State.End)
                {
                    break;
                }

            }
        }
        catch (Exception)
        {
            Console.Error.WriteLine("ERR: Error connecting to the server.");
        }
        finally
        {
            if (TCPSocket != null)
                TCPSocket.Close();
            if (stream != null)
                stream.Close();
            if (writer != null)
                writer.Close();
            if (reader != null)
                reader.Close();
        }
    }

    private void ReceiveMessageTCP(StreamReader reader)
    {
        while (_state != Helper.State.End && _state != Helper.State.Error && !_endOfInputEvent.WaitOne(0) && !_ctrlCEvent.WaitOne(0))
        {
            _receiveEvent.WaitOne();
            _receiveThreadPausedEvent.Reset();
            string? receivedMessage = null;
            try
            {
                receivedMessage = reader.ReadLine();
            }
            catch (Exception)
            {
                _sendEvent.Set();
                continue;
            }

            if (!string.IsNullOrEmpty(receivedMessage))
            {
                if (receivedMessage.ToUpper().StartsWith("REPLY"))
                {
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                    string? reason = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        reason = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }

                    if (parts[1] == "OK")
                    {
                        Console.Error.WriteLine("Success: " + reason);
                    }
                    else if (parts[1] == "NOK")
                    {
                        Console.Error.WriteLine("Failure: " + reason);
                    }
                    _receivedReplyEvent.Set();
                }
                else if (receivedMessage.ToUpper().StartsWith("MSG"))
                {
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.WriteLine(receivedDisplayName + ": " + messageContent);

                }
                else if (receivedMessage.ToUpper().StartsWith("ERR"))
                {
                    _sendBYE = true;
                    _receivedERR = true;
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
                    _sendEvent.Set();
                    _receiveThreadPausedEvent.Set();
                    break;
                }
                else if (receivedMessage.ToUpper().StartsWith("BYE"))
                {
                    _receievedBYE = true;
                    _state = Helper.State.End;
                    _sendEvent.Set();
                    _receiveThreadPausedEvent.Set();
                    break;
                }
                else
                {
                    _receivedERR = true;
                    _sendERR = true;
                    _sendEvent.Set();
                    _receiveThreadPausedEvent.Set();
                    break;
                }
            }
            _sendEvent.Set();
            _receiveThreadPausedEvent.Set();
        }
    }
    private void SendMessageTCP(StreamWriter writer)
    {
        while (!_receivedERR && !_receievedBYE && !_sendBYE && !_sendERR && !_ctrlCEvent.WaitOne(0))
        {
            _receiveEvent.Set();
            _sendEvent.WaitOne();
            _receiveThreadPausedEvent.WaitOne();
            if (_state == Helper.State.End)
            {
                _receiveEvent.Set();
                break;
            }
            if (_helper.CheckKey())
            {
                string? input = Console.ReadLine();
                if (input != null)
                {

                    if (input.StartsWith("/join"))
                    {
                        string[] parts = input.Split(' ');
                        if (parts.Length != 2)
                        {
                            Console.Error.WriteLine("ERR: Use join {ChannelID}.");
                            continue;
                        }
                        string channelId = parts[1];
                        string message = string.Format("JOIN {0} AS {1}\r\n", channelId.Trim(), _displayName.Trim());
                        writer.Write(message);
                        writer.Flush();
                        if (!_receivedReplyEvent.WaitOne(2500))
                        {
                            Console.Error.WriteLine("ERR: Timeout waiting for REPLY to JOIN message.");
                            continue;
                        }
                    }
                    else if (input.StartsWith("/rename"))
                    {
                        string[] parts = input.Split(' ');
                        if (parts.Length != 2)
                        {
                            Console.Error.WriteLine("ERR: Use /rename {DisplayName}.");
                            continue;
                        }
                        _displayName = parts[1];

                    }
                    else if (input.StartsWith("/help"))
                    {
                        Program.PrintUserHelp();
                    }
                    else if (input.StartsWith("/auth"))
                    {
                        Console.Error.WriteLine("ERR: User is already authorized.");
                        continue;
                    }
                    else
                    {
                        if (input.Length == 0)
                        {
                            Console.Error.WriteLine("ERR: Enter non-empty input.");
                            continue;
                        }
                        string messageContent = input;
                        string message = string.Format("MSG FROM {0} IS {1}\r\n", _displayName.Trim(), messageContent);
                        writer.Write(message);
                        writer.Flush();
                    }
                }
                else
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    _state = Helper.State.End;
                    _endOfInputEvent.Set();
                    _receiveEvent.Set();
                    break;
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }

    private void SetupCtrlCHandlerTCP(StreamWriter writer)
    {
        _ctrlCEvent.Reset();
        _threadsTerminatedEvent.Reset();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            writer.Write("BYE\r\n");
            writer.Flush();
            _threadsTerminatedEvent.WaitOne(1000);
            Environment.Exit(0);
        };
    }
}