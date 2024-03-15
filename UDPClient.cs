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
    private readonly IPAddress serverIpAddress;
    private readonly ushort serverPort;
    private readonly ushort UDPConfTimeout;
    private readonly byte maxUDPRetr;

    Helper.State state = Helper.State.Start;

    private HashSet<ushort> seenMessageIDs = new HashSet<ushort>();
    bool sendBYE = false;
    bool sendERR = false;
    string displayName = "";
    bool receivedERR = false;
    bool receievedBYE = false;
    private ManualResetEvent sendEvent = new ManualResetEvent(false);
    private ManualResetEvent receiveEvent = new ManualResetEvent(false);
    private ManualResetEvent confirmReceivedEvent = new ManualResetEvent(false);
    private ManualResetEvent endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent receivedReplyEvent = new ManualResetEvent(false);

    Helper helper = new Helper();

    public UDPClient(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this.serverIpAddress = serverIpAddress;
        this.serverPort = serverPort;
        this.UDPConfTimeout = UDPConfTimeout;
        this.maxUDPRetr = maxUDPRetr;
    }

    public void Connect()
    {
        Socket UDPSocket = new Socket(serverIpAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint sendEndPoint = new IPEndPoint(serverIpAddress, serverPort);

        bool authSent = false;
        ushort messageID = 0;
        while (true)
        {

            if (state == Helper.State.Start)
            {
                string? input = null;
                input = Console.ReadLine();
                if (input != null)
                {
                    if (helper.IsValidCommand(input))
                    {
                        if (input.StartsWith("/auth"))
                        {
                            if (!AuthSendAndConfirm(input, UDPSocket, sendEndPoint, ref messageID, serverIpAddress))
                            {
                                continue;
                            }
                            authSent = true;
                            state = Helper.State.Auth;
                        }
                        else if (input.StartsWith("/help"))
                        {
                            Program.PrintUserHelp();
                            continue;
                        }
                        else
                        {
                            Console.Error.WriteLine("ERR: /auth command is required. Use: /auth {Username} {Secret} {DisplayName}.");
                            continue;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("ERR: Error sending a message in non-open state.");
                        continue;
                    }
                }
            }
            if (state == Helper.State.Auth)
            {
                if (!authSent)
                {
                    string? input = null;
                    input = Console.ReadLine();
                    if (input != null)
                    {
                        if (helper.IsValidCommand(input))
                        {
                            if (input.StartsWith("/auth"))
                            {
                                if (!AuthSendAndConfirm(input, UDPSocket, sendEndPoint, ref messageID, serverIpAddress))
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
                                Console.Error.WriteLine("ERR: /auth command is required. Use: /auth {Username} {Secret} {DisplayName}.");
                                continue;
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine("ERR: Error sending a message in non-open state.");
                            continue;
                        }
                    }
                }
                byte[] receivedMessage = new byte[1024];
                EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = UDPSocket.ReceiveFrom(receivedMessage, 0, receivedMessage.Length, SocketFlags.None, ref receiveEndPoint);
                sendEndPoint.Port = (ushort)((IPEndPoint)receiveEndPoint).Port;
                helper.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);

                if (receivedBytes > 0)
                {
                    ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                    if (!seenMessageIDs.Contains(receivedMsgID))
                    {
                        //adds the message ID to the set of seen IDs
                        seenMessageIDs.Add(receivedMsgID);

                        if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                        {
                            if (helper.PrintReceivedReply(receivedMessage, ref messageID))
                            {
                                state = Helper.State.Open;
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
                            helper.PrintReceivedErrorOrMessage(receivedMessage);
                            ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                            state = Helper.State.End;
                        }
                        else
                        {
                            state = Helper.State.Error;
                        }
                    }
                }
                else
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                    state = Helper.State.End;
                }
            }
            if (state == Helper.State.Open)
            {
                sendEvent.Reset();
                receiveEvent.Reset();
                confirmReceivedEvent.Reset();
                endOfInputEvent.Reset();
                receivedReplyEvent.Reset();

                Thread receiveThread = new Thread(() => ReceiveMessageUDP(UDPSocket, sendEndPoint, ref messageID, serverIpAddress));
                Thread sendThread = new Thread(() => SendMessageUDP(UDPSocket, sendEndPoint, ref messageID, serverIpAddress));
                sendThread.Start();
                receiveThread.Start();

                receiveThread.Join();
                sendThread.Join();

                if (sendBYE == true)
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                    sendBYE = false;
                    state = Helper.State.End;
                    break;
                }
                if (sendERR == true)
                {
                    string messageContent = string.Format("Incoming message from {0}:{1} failed to be parsed.", serverIpAddress, serverPort);
                    byte[] errorMessage = helper.ConstructMessage(Helper.MessageType.ERR, messageID, displayName, messageContent);
                    if (!(SendAndConfirm(errorMessage, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
                    {
                        Console.Error.WriteLine("ERR: ERR message wasn't received by the host.");
                        Environment.Exit(1);
                    }
                    sendERR = false;
                    state = Helper.State.Error;
                }
            }
            if (state == Helper.State.Error)
            {
                ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                state = Helper.State.End;
                break;
            }
            if (state == Helper.State.End)
            {
                break;
            }
        }
        UDPSocket.Close();
    }

    private void SendMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        while ((!receivedERR && !receievedBYE && !sendBYE && !sendERR))
        {
            receiveEvent.Set();
            confirmReceivedEvent.Set();
            sendEvent.WaitOne();
            if (state == Helper.State.End)
            {
                break;
            }
            if (helper.CheckKey())
            {
                string? input = Console.ReadLine();
                if (input != null)
                {

                    if (input.StartsWith("/join"))
                    {
                        string[] parts = input.Split(' ');
                        string channelId = parts[1];
                        byte[] joinMessage = helper.ConstructMessage(Helper.MessageType.JOIN, messageID, channelId, displayName);
                        confirmReceivedEvent.Reset();
                        if (!(SendAndConfirm(joinMessage, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
                        {
                            Console.Error.WriteLine("ERR: JOIN message wasn't received by the host.");
                            continue;
                        }
                        Thread.Sleep(300);
                        if (!receivedReplyEvent.WaitOne(5000))
                        {
                            Console.Error.WriteLine("ERR: Timeout waiting for REPLY to JOIN message.");
                            continue;
                        }
                    }
                    else if (input.StartsWith("/rename"))
                    {
                        string[] parts = input.Split(' ');
                        displayName = parts[1];
                    }
                    else if (input.StartsWith("/help"))
                    {
                        Program.PrintUserHelp();
                    }
                    else if (input.StartsWith("/auth"))
                    {
                        state = Helper.State.Error;
                        break;
                    }
                    else
                    {
                        if (input.Length == 0)
                        {
                            Console.Error.WriteLine("ERR: Enter non-empty input.");
                            continue;
                        }
                        string messageContent = input;
                        byte[] message = helper.ConstructMessage(Helper.MessageType.MSG, messageID, displayName, messageContent);
                        confirmReceivedEvent.Reset();
                        if (!(SendAndConfirm(message, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
                        {
                            Console.Error.WriteLine("ERR: MSG message wasn't received by the host.");
                            continue;
                        }
                        Thread.Sleep(300);
                    }
                }
                else
                {
                    endOfInputEvent.Set();
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                    state = Helper.State.End;
                    break;
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }

    private void ReceiveMessageUDP(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        while (true)
        {
            receiveEvent.WaitOne();
            confirmReceivedEvent.WaitOne();
            if (state == Helper.State.End || state == Helper.State.Error)
            {
                break;
            }
            if (endOfInputEvent.WaitOne(0))
            {
                break;
            }
            byte[] receivedMessage = new byte[1024];
            int receivedBytes = 0;
            EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);

            receivedBytes = UDPSocket.ReceiveFrom(receivedMessage, 0, receivedMessage.Length, SocketFlags.None, ref receiveEndPoint);
            if (receivedBytes > 0)
            {
                helper.SendConfirm(receivedMessage, UDPSocket, sendEndPoint);

                ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                if (!seenMessageIDs.Contains(receivedMsgID))
                {
                    //adds the message ID to the set of seen IDs
                    seenMessageIDs.Add(receivedMsgID);
                    if (receivedMessage[0] == (byte)Helper.MessageType.REPLY)
                    {
                        helper.PrintReceivedReply(receivedMessage, ref messageID);
                        receivedReplyEvent.Set();
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.MSG)
                    {
                        helper.PrintReceivedErrorOrMessage(receivedMessage);
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.ERR)
                    {
                        sendBYE = true;
                        receivedERR = true;
                        helper.PrintReceivedErrorOrMessage(receivedMessage);
                        break;
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.BYE)
                    {
                        receievedBYE = true;
                        state = Helper.State.End;
                        break;
                    }
                    else if (receivedMessage[0] == (byte)Helper.MessageType.CONFIRM)
                    {
                        ;
                    }
                    else
                    {
                        receivedERR = true;
                        sendERR = true;
                        break;
                    }
                }
            }
            sendEvent.Set();
        }
    }

    private void ByeSendAndConfirm(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        byte[] byeMessage = new byte[3];
        byeMessage[0] = (byte)Helper.MessageType.BYE;
        byeMessage[1] = (byte)(messageID >> 8);
        byeMessage[2] = (byte)(messageID);
        if (!(SendAndConfirm(byeMessage, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
        {
            Console.Error.WriteLine("ERR: BYE message wasn't received by the host.");
            Environment.Exit(1);
        }
    }

    private bool AuthSendAndConfirm(string input, Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        string[] parts = input.Split(' ');
        if (!helper.IsValidAuth(parts))
        {
            return false;
        }

        string username = parts[1];
        string secret = parts[2];
        displayName = parts[3];

        byte[] authMessage = helper.ConstructMessage(Helper.MessageType.AUTH, messageID, username, displayName, secret);

        if (!(SendAndConfirm(authMessage, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
        {
            Console.Error.WriteLine("ERR: AUTH message wasn't received by the host.");
            return false;
        }
        return true;
    }

    private bool SendAndConfirm(byte[] message, Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        int retryCount = 0;
        bool isConfirmed = false;

        while (retryCount < maxUDPRetr)
        {
            try
            {
                UDPSocket.SendTo(message, 0, message.Length, SocketFlags.None, sendEndPoint);
            }
            catch (Exception)
            {
                Console.Error.WriteLine("ERR: Error sending message to the host.");
            }
            try
            {
                byte[] confirmMessage = new byte[1024];
                UDPSocket.ReceiveTimeout = UDPConfTimeout;
                EndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int confirmBytes = UDPSocket.ReceiveFrom(confirmMessage, 0, confirmMessage.Length, SocketFlags.None, ref receiveEndPoint);

                ushort recMesID = (ushort)((confirmMessage[1] << 8) | confirmMessage[2]);
                if (confirmBytes > 0 && confirmMessage[0] == (byte)Helper.MessageType.CONFIRM && recMesID == messageID)
                {
                    isConfirmed = true;
                    confirmReceivedEvent.Set();
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
        if (!isConfirmed && retryCount == maxUDPRetr)
        {
            return false;
        }
        return true;
    }
}