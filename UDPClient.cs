using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


public class UDPClient
{
    private readonly IPAddress _serverIpAddress;
    private readonly ushort _serverPort;
    private readonly ushort _UDPConfTimeout;
    private readonly byte _maxUDPRetr;

    private Helper.State _state = Helper.State.Start;

    private HashSet<ushort> _seenMessageIDs = new HashSet<ushort>();

    private string _displayName = "";

    private bool _sendBYE = false;
    private bool _sendERR = false;
    private bool _receivedERR = false;
    private bool _receievedBYE = false;

    private ManualResetEvent _sendEvent = new ManualResetEvent(false);
    private ManualResetEvent _receiveEvent = new ManualResetEvent(false);
    private ManualResetEvent _confirmReceivedEvent = new ManualResetEvent(false);
    private ManualResetEvent _confirmWaitingEvent = new ManualResetEvent(false);
    private ManualResetEvent _endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent _receivedReplyEvent = new ManualResetEvent(false);
    private ManualResetEvent _ctrlCEvent = new ManualResetEvent(false);
    private ManualResetEvent _threadsTerminatedEvent = new ManualResetEvent(false);

    private Helper _helper = new Helper();
    private HelperUDP _helperUDP = new HelperUDP();

    public UDPClient(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this._serverIpAddress = serverIpAddress;
        this._serverPort = serverPort;
        this._UDPConfTimeout = UDPConfTimeout;
        this._maxUDPRetr = maxUDPRetr;
    }

    public void Connect()
    {
        Socket UDPSocket = new Socket(_serverIpAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint sendEndPoint = new IPEndPoint(_serverIpAddress, _serverPort);

        bool authSent = false;
        ushort messageID = 0;


        _ctrlCEvent.Reset();
        _threadsTerminatedEvent.Reset();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _ctrlCEvent.Set();
            if (_state == Helper.State.Open)
            {
                _confirmReceivedEvent.Reset();
                _confirmWaitingEvent.Reset();
                _confirmWaitingEvent.WaitOne();
            }
            ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
            _threadsTerminatedEvent.WaitOne(1000);
            Environment.Exit(0);
        };


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
                            if (!AuthSendAndConfirm(input, UDPSocket, sendEndPoint, ref messageID))
                            {
                                continue;
                            }
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
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
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
                                if (!AuthSendAndConfirm(input, UDPSocket, sendEndPoint, ref messageID))
                                {
                                    continue;
                                }
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
                        ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                        _state = Helper.State.End;
                        break;
                    }
                }
                byte[] receivedMessage = new byte[1024];
                EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = UDPSocket.ReceiveFrom(receivedMessage, 0, receivedMessage.Length, SocketFlags.None, ref receiveEndPoint);
                sendEndPoint.Port = (ushort)((IPEndPoint)receiveEndPoint).Port;
                _helperUDP.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);

                if (receivedBytes > 0)
                {
                    ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                    if (!_seenMessageIDs.Contains(receivedMsgID))
                    {
                        //adds the message ID to the set of seen IDs
                        _seenMessageIDs.Add(receivedMsgID);

                        if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                        {
                            if (_helperUDP.PrintReceivedReply(receivedMessage, ref messageID))
                            {
                                _state = Helper.State.Open;
                                continue;
                            }
                            else
                            {
                                authSent = false;
                                continue;
                            }
                        }

                        else if (receivedMessage[0] == (byte)Helper.MessageType.ERR)
                        {
                            _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                            ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                            _state = Helper.State.End;
                        }
                        else
                        {
                            _state = Helper.State.Error;
                        }
                    }
                }
                else
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                    _state = Helper.State.End;
                }
            }
            if (_state == Helper.State.Open)
            {
                _sendEvent.Reset();
                _receiveEvent.Reset();
                _confirmReceivedEvent.Reset();
                _endOfInputEvent.Reset();
                _receivedReplyEvent.Reset();

                Thread receiveThread = new Thread(() => ReceiveMessageUDP(UDPSocket, sendEndPoint, ref messageID));
                Thread sendThread = new Thread(() => SendMessageUDP(UDPSocket, sendEndPoint, ref messageID));
                sendThread.Start();
                receiveThread.Start();

                receiveThread.Join();
                sendThread.Join();
                _threadsTerminatedEvent.Set();

                if (_sendBYE == true)
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                    _sendBYE = false;
                    _state = Helper.State.End;
                    break;
                }
                if (_sendERR == true)
                {
                    string messageContent = string.Format("Incoming message from {0}:{1} failed to be parsed.", _serverIpAddress, _serverPort);
                    byte[] errorMessage = _helperUDP.ConstructMessage(Helper.MessageType.ERR, messageID, _displayName, messageContent);
                    if (!(SendAndConfirm(errorMessage, UDPSocket, sendEndPoint, ref messageID)))
                    {
                        Console.Error.WriteLine("ERR: ERR message wasn't received by the host.");
                    }
                    _sendERR = false;
                    _state = Helper.State.Error;
                }
            }
            if (_state == Helper.State.Error)
            {
                ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                _state = Helper.State.End;
                break;
            }
            if (_state == Helper.State.End)
            {
                break;
            }
        }
        UDPSocket.Close();
    }

    private void SendMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        while (!_ctrlCEvent.WaitOne(0))
        {
            _receiveEvent.Set();
            _confirmReceivedEvent.Set();
            _sendEvent.WaitOne();
            if (_state == Helper.State.End || _receivedERR || _receievedBYE || _sendBYE || _sendERR)
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
                        byte[] joinMessage = _helperUDP.ConstructMessage(Helper.MessageType.JOIN, messageID, channelId, _displayName);
                        _confirmReceivedEvent.Reset();
                        _confirmWaitingEvent.Reset();
                        _confirmWaitingEvent.WaitOne();
                        if (!(SendAndConfirm(joinMessage, UDPSocket, sendEndPoint, ref messageID)))
                        {
                            Console.Error.WriteLine("ERR: JOIN message wasn't received by the host.");
                            continue;
                        }
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
                        byte[] message = _helperUDP.ConstructMessage(Helper.MessageType.MSG, messageID, _displayName, messageContent);
                        _confirmReceivedEvent.Reset();
                        _confirmWaitingEvent.Reset();
                        _confirmWaitingEvent.WaitOne();
                        if (!(SendAndConfirm(message, UDPSocket, sendEndPoint, ref messageID)))
                        {
                            Console.Error.WriteLine("ERR: MSG message wasn't received by the host.");
                            continue;
                        }
                    }
                }
                else
                {
                    _confirmReceivedEvent.Reset();
                    _confirmWaitingEvent.Reset();
                    _confirmWaitingEvent.WaitOne();
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                    _state = Helper.State.End;
                    _receiveEvent.Set();
                    _endOfInputEvent.Set();
                    break;
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }

    private void ReceiveMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        while (!_ctrlCEvent.WaitOne(0))
        {
            _receiveEvent.WaitOne();
            _confirmReceivedEvent.WaitOne();
            if (_state == Helper.State.End || _state == Helper.State.Error || _endOfInputEvent.WaitOne(0))
            {
                _sendEvent.Set();
                _confirmWaitingEvent.Set();
                break;
            }
            byte[] receivedMessage = new byte[1024];
            int receivedBytes = 0;
            EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                UDPSocket.ReceiveTimeout = 250;
                receivedBytes = UDPSocket.ReceiveFrom(receivedMessage, 0, receivedMessage.Length, SocketFlags.None, ref receiveEndPoint);
            }
            catch (Exception)
            {
                _sendEvent.Set();
                _confirmWaitingEvent.Set();
                continue;
            }
            if (receivedBytes > 0)
            {
                if (receivedMessage[0] != (byte)Helper.MessageType.CONFIRM)
                {
                    _helperUDP.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);
                }
                ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                if (!_seenMessageIDs.Contains(receivedMsgID))
                {
                    //adds the message ID to the set of seen IDs
                    _seenMessageIDs.Add(receivedMsgID);
                    if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                    {
                        _helperUDP.PrintReceivedReply(receivedMessage, ref messageID);
                        _receivedReplyEvent.Set();
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.MSG)
                    {
                        _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.ERR)
                    {
                        _sendBYE = true;
                        _receivedERR = true;
                        _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                        _sendEvent.Set();
                        _confirmWaitingEvent.Set();
                        break;
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.BYE)
                    {
                        _receievedBYE = true;
                        _state = Helper.State.End;
                        _sendEvent.Set();
                        _confirmWaitingEvent.Set();
                        break;
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.CONFIRM)
                    {
                        ;
                    }
                    else
                    {
                        _receivedERR = true;
                        _sendERR = true;
                        _sendEvent.Set();
                        _confirmWaitingEvent.Set();
                        break;
                    }
                }
            }
            _sendEvent.Set();
            _confirmWaitingEvent.Set();
        }
    }

    private void ByeSendAndConfirm(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        byte[] byeMessage = new byte[3];
        byeMessage[0] = (byte)Helper.MessageType.BYE;
        byeMessage[1] = (byte)(messageID >> 8);
        byeMessage[2] = (byte)(messageID);
        if (!(SendAndConfirm(byeMessage, UDPSocket, sendEndPoint, ref messageID)))
        {
            Console.Error.WriteLine("ERR: BYE message wasn't received by the host.");
        }
    }

    private bool AuthSendAndConfirm(string input, Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        string[] parts = input.Split(' ');
        if (!_helper.IsValidAuth(parts))
        {
            return false;
        }

        string username = parts[1];
        string secret = parts[2];
        _displayName = parts[3];

        byte[] authMessage = _helperUDP.ConstructMessage(Helper.MessageType.AUTH, messageID, username, _displayName, secret);

        if (!(SendAndConfirm(authMessage, UDPSocket, sendEndPoint, ref messageID)))
        {
            Console.Error.WriteLine("ERR: AUTH message wasn't received by the host.");
            return false;
        }
        return true;
    }

    private bool SendAndConfirm(byte[] message, Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        int retryCount = 0;
        bool isConfirmed = false;

        while (retryCount < (_maxUDPRetr + 1))
        {
            try
            {
                UDPSocket.SendTo(message, 0, message.Length, SocketFlags.None, sendEndPoint);
                byte[] confirmMessage = new byte[1024];
                UDPSocket.ReceiveTimeout = _UDPConfTimeout;
                EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int confirmBytes = UDPSocket.ReceiveFrom(confirmMessage, 0, confirmMessage.Length, SocketFlags.None, ref receiveEndPoint);

                ushort recMesID = (ushort)((confirmMessage[1] << 8) | confirmMessage[2]);
                if (confirmBytes > 0 && confirmMessage[0] == (byte)Helper.MessageType.CONFIRM && recMesID == messageID)
                {
                    isConfirmed = true;
                    _confirmReceivedEvent.Set();
                    break;
                }
            }
            catch (Exception)
            {
                retryCount++;
                continue;
            }
        }
        UDPSocket.ReceiveTimeout = 0;
        messageID++;
        if (!isConfirmed && retryCount == _maxUDPRetr)
        {
            return false;
        }
        return true;
    }
}