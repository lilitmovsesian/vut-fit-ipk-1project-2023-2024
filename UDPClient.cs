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
    /* Declares instantion fields. */
    private readonly IPAddress _serverIpAddress;
    private readonly ushort _serverPort;
    private readonly ushort _UDPConfTimeout;
    private readonly byte _maxUDPRetr;

    /* Current state. */
    private Helper.State _state = Helper.State.Start;

    /* HashSet to store seen message IDs.*/
    private HashSet<ushort> _seenMessageIDs = new HashSet<ushort>();

    /* Display name. */
    private string _displayName = "";

    /* Flags. */
    private bool _sendBYE = false;
    private bool _sendERR = false;
    private bool _receivedERR = false;
    private bool _receievedBYE = false;

    /* ManualResetEvents. */
    private ManualResetEvent _sendEvent = new ManualResetEvent(false);
    private ManualResetEvent _receiveEvent = new ManualResetEvent(false);
    private ManualResetEvent _confirmReceivedEvent = new ManualResetEvent(false);
    private ManualResetEvent _confirmWaitingEvent = new ManualResetEvent(false);
    private ManualResetEvent _endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent _receivedReplyEvent = new ManualResetEvent(false);
    private ManualResetEvent _ctrlCEvent = new ManualResetEvent(false);
    private ManualResetEvent _threadsTerminatedEvent = new ManualResetEvent(false);

    /* Instantiation of objects. */
    private Helper _helper = new Helper();
    private HelperUDP _helperUDP = new HelperUDP();

    /* Constructor method. */
    public UDPClient(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this._serverIpAddress = serverIpAddress;
        this._serverPort = serverPort;
        this._UDPConfTimeout = UDPConfTimeout;
        this._maxUDPRetr = maxUDPRetr;
    }

    public void Connect()
    {
        try
        {
            /* Creates an UDP socket. */
            Socket UDPSocket = new Socket(_serverIpAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint sendEndPoint = new IPEndPoint(_serverIpAddress, _serverPort);

            bool authSent = false;
            ushort messageID = 0;

            /* Handles Ctrl+C and Ctrl+D event. If cancel key is pressed, a semaphore _ctrlCEvent is set.
             If the current state is Open state, the semaphores for receive thread pause are set and awaited.
            Bye message is sent and confirmed. If the current state is Open state, the handler waits for both 
            threads to terminate and exits the program. */
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
                if (_state == Helper.State.Open)
                {
                    _threadsTerminatedEvent.WaitOne();
                }
                Environment.Exit(0);
            };

            /* Main loop for client operation. */
            while (true)
            {
                /* If the current state is Start state, the stdin is read and all posible user 
                 * inputs are handled. */
                if (_state == Helper.State.Start)
                {
                    string? input = null;
                    input = Console.ReadLine();
                    if (input != null)
                    {
                        /* If it is a command and not a message. */
                        if (_helper.IsValidCommand(input))
                        {
                            /*If the input is / auth command, it is sent to the server, the current
                            * state changes to Auth state, the flag authSent is set for further use.*/
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
                            /*If any other command except for / help is received, the user is informed that /auth command is required.*/
                            else
                            {
                                Console.Error.WriteLine("ERR: /auth command is required. Use /auth {Username} {Secret} {DisplayName}.");
                                continue;
                            }
                        }
                        /* If the user tries to send a MSG message, he is informed with the error message. */
                        else
                        {
                            Console.Error.WriteLine("ERR: Error sending a message in non-open state.");
                            continue;
                        }
                    }
                    /*If the input is null (end of input), bye is sent and state is changed to End. */
                    else
                    {
                        ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                        _state = Helper.State.End;
                    }
                }
                /* The Auth state duplicates the Start state code in case if the flag authSent is not set,
                 * which means that all previous auth messages got a negative reply.*/
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
                        }
                    }
                    /* Gets a reply message for the AUTH and set a new port. */
                    byte[] receivedMessage = new byte[1024];
                    EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int receivedBytes = UDPSocket.ReceiveFrom(receivedMessage, 0, receivedMessage.Length, SocketFlags.None, ref receiveEndPoint);
                    sendEndPoint.Port = (ushort)((IPEndPoint)receiveEndPoint).Port;
                    _helperUDP.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);

                    if (receivedBytes > 0)
                    {
                        ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                        /* If it is not a delayed/duplicated packet, the message is processed. */
                        if (!_seenMessageIDs.Contains(receivedMsgID))
                        {
                            /* Adds the message ID to the set of seen IDs. */
                            _seenMessageIDs.Add(receivedMsgID);

                            /* If reply is positive, the current state is Open state. */
                            if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                            {
                                if (_helperUDP.PrintReceivedReply(receivedMessage, ref messageID))
                                {
                                    _state = Helper.State.Open;
                                    continue;
                                }
                                /* Otherwise, the state remains and authSent flag is reset. */
                                else
                                {
                                    authSent = false;
                                    continue;
                                }
                            }
                            /* If an error is received, BYE is sent and current state is changed to End state. */
                            else if (receivedMessage[0] == (byte)Helper.MessageType.ERR)
                            {
                                _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                                ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                                _state = Helper.State.End;
                            }
                            /* If any other message types are received, the state is changed to Error. */
                            else
                            {
                                _state = Helper.State.Error;
                            }
                        }
                    }
                    /* If no reply is received, BYE is sent and current state is changed to End state. */
                    else
                    {
                        ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                        _state = Helper.State.End;
                    }
                }
                if (_state == Helper.State.Open)
                {
                    /* Resets ManualResetEvents required for the synchronization. */
                    _sendEvent.Reset();
                    _receiveEvent.Reset();
                    _confirmReceivedEvent.Reset();
                    _endOfInputEvent.Reset();
                    _receivedReplyEvent.Reset();

                    /* The threads creation. */
                    Thread receiveThread = new Thread(() => ReceiveMessageUDP(UDPSocket, sendEndPoint, ref messageID));
                    Thread sendThread = new Thread(() => SendMessageUDP(UDPSocket, sendEndPoint, ref messageID));
                    sendThread.Start();
                    receiveThread.Start();

                    /* The threads termination and corresponding semaphore setting. */
                    receiveThread.Join();
                    sendThread.Join();
                    _threadsTerminatedEvent.Set();

                    /* If this flag is set inside the Open state threads, BYE is sent and current state is changed to End state. */
                    if (_sendBYE == true)
                    {
                        ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                        _sendBYE = false;
                        _state = Helper.State.End;
                    }
                    /* If this flag is set inside the Open state threads, ERR is sent and the current state is set to Error state. */
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
                /* In the Error state the BYE is sent and current state is changed to End state. */
                if (_state == Helper.State.Error)
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                    _state = Helper.State.End;
                }
                /* Exit from the main loop, the socket is closed and the client is terminated. */
                if (_state == Helper.State.End)
                {
                    UDPSocket.Close();
                    break;
                }
            }
        }
        catch (Exception)
        {
            Console.Error.WriteLine("ERR: Error connecting to the server.");
        }
    }

    /* A method for a thread for message sending. */
    private void SendMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        /* A loop for message sending while the semaphore for cancel events is not set. */
        while (!_ctrlCEvent.WaitOne(0))
        {
            /* Sets the required semaphores for a receiving thread, waits for a semaphore for sending. */
            _receiveEvent.Set();
            _confirmReceivedEvent.Set();
            _sendEvent.WaitOne();
            /* If one of the flags is set or a state has changed inside the receive thread, the loop
             is exited. */
            if (_state == Helper.State.End || _receivedERR || _receievedBYE || _sendBYE || _sendERR)
            {
                _receiveEvent.Set();
                _confirmReceivedEvent.Set();
                break;
            }
            /* An if statement to prevent the blocking of program waiting for an input. */
            if (_helper.CheckKey())
            {
                string? input = Console.ReadLine();
                if (input != null)
                {
                    /* Handles different user commands. */
                    if (input.StartsWith("/join"))
                    {
                        string[] parts = input.Split(' ');
                        if (parts.Length != 2)
                        {
                            Console.Error.WriteLine("ERR: Use join {ChannelID}.");
                            continue;
                        }
                        /* If a message should be sent, the semaphores ensure the pause of receive thread to 
                         receive a confirmation outside the receive thread. */
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
                        /* Waits for a reply. */
                        if (!_receivedReplyEvent.WaitOne(5000))
                        {
                            Console.Error.WriteLine("ERR: Timeout waiting for REPLY to JOIN message.");
                            continue;
                        }
                    }
                    /* Renames a user. */
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
                        /* Doesn't allow an empty input. */
                        if (input.Length == 0)
                        {
                            Console.Error.WriteLine("ERR: Enter non-empty input.");
                            continue;
                        }
                        /* Sets the semaphores described above and sends a message. */
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
                    /* If the input is null, the BYE is sent, the state is changed, the end of input semaphore is set and 
                     * all semaphores required for receive thread are set.*/
                    _confirmReceivedEvent.Reset();
                    _confirmWaitingEvent.Reset();
                    _confirmWaitingEvent.WaitOne();
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID);
                    _state = Helper.State.End;
                    _receiveEvent.Set();
                    _confirmReceivedEvent.Set();
                    _endOfInputEvent.Set();
                    break;
                }
            }
            else
            {
                /* Avoids blocking of program in several cases that I tested. */
                Thread.Sleep(100);
            }
        }
    }

    /* A method for a thread for message receiving. */
    private void ReceiveMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        /* A loop for message receiving while the semaphore for cancel events is not set. */
        while (!_ctrlCEvent.WaitOne(0))
        {
            /* Waits for the required semaphores. */
            _receiveEvent.WaitOne();
            _confirmReceivedEvent.WaitOne();
            /* If the state has changed to end or the end of input semaphore is set, sets 
             * the required for sending thread semaphores and exits. */
            if (_state == Helper.State.End || _endOfInputEvent.WaitOne(0))
            {
                _sendEvent.Set();
                _confirmWaitingEvent.Set();
                break;
            }
            /* Try-Catch to receive a message or continue. */
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
                /* Sends confirmation to every message except for the confirmation from server. 
                 * Normally confirm message won't be received inside receive thread, but in case 
                 * it is received, the confirmation is not sent. */
                if (receivedMessage[0] != (byte)Helper.MessageType.CONFIRM)
                {
                    _helperUDP.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);
                }
                ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                /* If it is not a delayed/duplicated packet, the message is processed. */
                if (!_seenMessageIDs.Contains(receivedMsgID))
                {
                    /* Adds the message ID to the set of seen IDs */
                    _seenMessageIDs.Add(receivedMsgID);
                    /* If a reply to JOIN is received, the output is printed and a semaphore is set. */ 
                    if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                    {
                        _helperUDP.PrintReceivedReply(receivedMessage, ref messageID);
                        _receivedReplyEvent.Set();
                    }
                    /* Prints an output based on the received message. */
                    else if (receivedMessage[0] == (byte)Helper.MessageType.MSG)
                    {
                        _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                    }
                    /* If the ERR is received, the flags are set, output is printed, semaphores
                     * required for sending are set and the thread is terminated. */
                    else if (receivedMessage[0] == (byte)Helper.MessageType.ERR)
                    {
                        _sendBYE = true;
                        _receivedERR = true;
                        _helperUDP.PrintReceivedErrorOrMessage(receivedMessage);
                        _sendEvent.Set();
                        _confirmWaitingEvent.Set();
                        break;
                    }
                    /* If the BYE is received, the flag is set, state is changed,
                     * output is printed, semaphores required for sending are set and the thread is terminated.  */
                    else if (receivedMessage[0] == (byte)Helper.MessageType.BYE)
                    {
                        _receievedBYE = true;
                        _state = Helper.State.End;
                        _sendEvent.Set();
                        _confirmWaitingEvent.Set();
                        break;
                    }
                    /* Ignores the possible confirmation. */
                    else if (receivedMessage[0] == (byte)Helper.MessageType.CONFIRM)
                    {
                        ;
                    }
                    /* If anything else is received, the flags are set and the thread is terminated. */
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
            /* Sets the semaphores for sending thread. */
            _sendEvent.Set();
            _confirmWaitingEvent.Set();
        }
    }

    /* A method to construct a BYE byte array with a message type and message ID in Big Endian. */
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

    /* A method to validate an /auth command, construct a byte array for AUTH and send. */
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

    /* A method for sending messages, possible retransmissions and receiving of confirmation. */
    private bool SendAndConfirm(byte[] message, Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID)
    {
        int retryCount = 0;
        bool isConfirmed = false;

        /* While one initial sending + maximum retransmission number is not reached. */
        while (retryCount < (_maxUDPRetr + 1))
        {
            /* Try-catch for sending of packet and receiving of its confirmation from the server. */
            try
            {
                UDPSocket.SendTo(message, 0, message.Length, SocketFlags.None, sendEndPoint);
                byte[] confirmMessage = new byte[1024];
                UDPSocket.ReceiveTimeout = _UDPConfTimeout;
                EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int confirmBytes = UDPSocket.ReceiveFrom(confirmMessage, 0, confirmMessage.Length, SocketFlags.None, ref receiveEndPoint);
                /* Breaks if the correct confirmation is received. */
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
        /* If maximum retransmission number is reached and the packet is not confirmed, returns false. */
        if (!isConfirmed && retryCount == _maxUDPRetr)
        {
            return false;
        }
        return true;
    }
}