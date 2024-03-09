using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Reflection.PortableExecutable;
using System.Threading.Channels;
using System.Globalization;
using static System.Net.WebRequestMethods;


public class Client
{

    private readonly IPAddress serverIpAddress;
    private readonly ushort serverPort;
    private readonly ushort UDPConfTimeout;
    private readonly byte maxUDPRetr;

    private enum MessageType : byte
    {
        CONFIRM = 0x00,
        REPLY = 0x01,
        AUTH = 0x02,
        JOIN = 0x03,
        MSG = 0x04,
        ERR = 0xFE,
        BYE = 0xFF
    }

    private enum State
    {
        Start,
        Auth,
        Open,
        Error,
        End
    };

    private HashSet<ushort> seenMessageIDs = new HashSet<ushort>();
    State state = State.Start;
    bool TCPSendBYE = false;
    bool TCPSendERR = false;
    string? displayName = null;
    bool TCPReceivedError = false;
    bool TCPReceievedBye = false;
    private ushort dynamicPort = 0;
    private readonly object socketLock = new object();

    public Client(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this.serverIpAddress = serverIpAddress;
        this.serverPort = serverPort;
        this.UDPConfTimeout = UDPConfTimeout;
        this.maxUDPRetr = maxUDPRetr;
    }

    public void ConnectUDP()
    {
        Socket UDPSocket = new Socket(serverIpAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint sendEndPoint = new IPEndPoint(serverIpAddress, serverPort);

        bool authSent = false;
        ushort messageID = 0;
        while (true)
        {

            if (state == State.Start)
            {
                string? input = null;
                input = Console.ReadLine();
                if (input != null)
                {
                    if (IsValidCommand(input))
                    {
                        if (input.StartsWith("/auth"))
                        {
                            if (!AuthSendAndConfirm(input, UDPSocket, sendEndPoint, ref messageID, serverIpAddress))
                            {
                                continue;
                            }
                            authSent = true;
                            state = State.Auth;
                            //sendEndPoint.Port = dynamicPort;
                        }
                        else if (input.StartsWith("/help"))
                        {
                            printUserHelp();
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
            if (state == State.Auth)
            {
                if (!authSent)
                {
                    string? input = null;
                    input = Console.ReadLine();
                    if (input != null)
                    {
                        if (IsValidCommand(input))
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
                                printUserHelp();
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

                SendConfirm(receivedMessage, UDPSocket, sendEndPoint);
                if (receivedBytes > 0)
                {
                    ushort receivedMsgID = (ushort)((receivedMessage[1] << 8) | receivedMessage[2]);

                    if (!seenMessageIDs.Contains(receivedMsgID))
                    {
                        //adds the message ID to the set of seen IDs
                        seenMessageIDs.Add(receivedMsgID);

                        if (receivedMessage[0] == (byte)MessageType.REPLY)
                        {
                            if (PrintReceivedReply(receivedMessage, ref messageID))
                            {
                                state = State.Open;
                                continue;
                            }
                            else
                            {
                                authSent = false;
                                continue;
                            }
                        }

                        else if (receivedMessage[0] == (byte)MessageType.ERR)
                        {
                            PrintReceivedError(receivedMessage);
                            ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                            state = State.End;
                        }
                        else
                        {
                            state = State.Error;
                        }
                    }
                }
                else
                {
                    ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                    state = State.End;
                }
            }
            if (state == State.Open)
            {
                Thread receiveThread = new Thread(() => ReceiveMessageUDP(UDPSocket, sendEndPoint, ref messageID, serverIpAddress));
                Thread sendThread = new Thread(() => SendMessageUDP(UDPSocket, sendEndPoint, ref messageID, serverIpAddress));
                sendThread.Start();
                receiveThread.Start();

            }
            if (state == State.Error)
            {
                ByeSendAndConfirm(UDPSocket, sendEndPoint, ref messageID, serverIpAddress);
                state = State.End;
                break;
            }
            if (state == State.End)
            {
                break;
            }
        }
        UDPSocket.Close();
    }

    private void SendMessageUDP(Socket uDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        lock (socketLock)
        {
        }
    }

    private void ReceiveMessageUDP(Socket uDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        lock (socketLock)
        {
        }
    }

    //TODO ctrl c handling x2
    //TODO UDP connection wireshark



    private bool PrintReceivedReply(byte[] replyMessage, ref ushort messageID)
    {
        bool success = false;
        ushort replyRefID = (ushort)((replyMessage[4] << 8) | replyMessage[5]);

        if (replyRefID == (messageID - 1)) //before the last incremenentation of messageID there was an ID of recieved auth message)
        {
            int reasonStartIndex = 6;
            int reasonEndIndex = Array.IndexOf(replyMessage, (byte)0, reasonStartIndex);
            string reason = Encoding.UTF8.GetString(replyMessage, reasonStartIndex, reasonEndIndex - reasonStartIndex);

            string? result;
            if (replyMessage[3] == (byte)1)
            {
                result = "Success";
                success = true;
            }
            else
            {
                result = "Failure";
            }
            Console.Error.WriteLine(result + ": " + reason);
        }
        else
        {
            Console.Error.WriteLine("ERR: Error receiving REPLY message.");
        }
        return success;
    }

    private void PrintReceivedError(byte[] errorMessage)
    {
        int disNameStartIndex = 3;
        int disNameEndIndex = Array.IndexOf(errorMessage, (byte)0, disNameStartIndex);
        string receivedDisplayName = Encoding.UTF8.GetString(errorMessage, disNameStartIndex, disNameEndIndex - disNameStartIndex);

        int mesContentStartIndex = disNameEndIndex + 1;
        int mesContentEndIndex = Array.IndexOf(errorMessage, (byte)0, mesContentStartIndex);
        string messageContent = Encoding.UTF8.GetString(errorMessage, mesContentStartIndex, mesContentEndIndex - mesContentStartIndex);

        Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
    }

    private void ByeSendAndConfirm(Socket UDPSocket, IPEndPoint sendEndPoint, ref ushort messageID, IPAddress serverIpAddress)
    {
        byte[] byeMessage = new byte[3];
        byeMessage[0] = (byte)MessageType.BYE;
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
        if (!IsValidAuth(parts))
        {
            return false;
        }

        string username = parts[1];
        string secret = parts[2];
        displayName = parts[3];

        byte[] authMessage = ConstructMessage(MessageType.AUTH, messageID, username, displayName, secret);

        if (!(SendAndConfirm(authMessage, UDPSocket, sendEndPoint, ref messageID, serverIpAddress)))
        {
            Console.Error.WriteLine("ERR: AUTH message wasn't received by the host.");
            //Environment.Exit(1);
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
                if (confirmBytes > 0 && confirmMessage[0] == (byte)MessageType.CONFIRM && recMesID == messageID)
                {
                    //if (message[0] == (byte)MessageType.AUTH)
                    //{
                    //    dynamicPort = (ushort)((IPEndPoint)receiveEndPoint).Port;
                    //}
                    isConfirmed = true;
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

    private void SendConfirm(byte[] replyMessage, Socket UDPSocket, IPEndPoint sendEndPoint)
    {
        byte[] clientConfirm = new byte[3];
        clientConfirm[0] = (byte)MessageType.CONFIRM;
        clientConfirm[1] = replyMessage[1];
        clientConfirm[2] = replyMessage[2];
        UDPSocket.SendTo(clientConfirm, 0, clientConfirm.Length, SocketFlags.None, sendEndPoint);
    }

    private bool IsValidAuth(string[] parts)
    {
        if (parts.Length < 4)
        {
            Console.Error.WriteLine("ERR: Invalid /auth command format. Use: /auth {Username} {Secret} {DisplayName}.");
            return false;
        }
        if (!(Regex.IsMatch(parts[1], "^[A-Za-z0-9-]{1,20}$")))
        {
            Console.Error.WriteLine("ERR: Invalid Username format. Use only A-z0-9- up to 20 characters.");
            return false;
        }
        if (!(Regex.IsMatch(parts[2], "^[A-Za-z0-9-]{1,128}$")))
        {
            Console.Error.WriteLine("ERR: Invalid Secret format. Use only A-z0-9- up to 128 characters.");
            return false;
        }
        if (parts[3].Length > 20)
        {
            Console.Error.WriteLine("ERR: Use maximum 20 characters for the Display Name.");
            return false;
        }
        return true;
    }

    private byte[] ConstructMessage(MessageType messageType, ushort messageID, params string[] fields)
    {
        string temp = ((char)messageType).ToString() + ((char)(messageID >> 8)).ToString() + ((char)messageID).ToString() + (string.Join("\0", fields) + "\0");
        byte[] message = Encoding.ASCII.GetBytes(temp);
        return message;
    }

    public void ConnectTCP()
    {
        //TcpClient client = null;
        Socket TCPSocket = null;
        NetworkStream stream = null;
        StreamWriter writer = null;
        StreamReader reader = null;
        try
        {
            TCPSocket = new Socket(serverIpAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint TCPEndPoint = new IPEndPoint(serverIpAddress, serverPort);
            TCPSocket.Connect(TCPEndPoint);
            stream = new NetworkStream(TCPSocket);

            writer = new StreamWriter(stream);
            reader = new StreamReader(stream);

            bool authSent = false;
            while (true)
            {
                if (state == State.Start)
                {
                    string? input = null;
                    input = Console.ReadLine();

                    if (input != null)
                    {
                        if (IsValidCommand(input))
                        {
                            if (input.StartsWith("/auth"))
                            {
                                string[] parts = input.Split(' ');
                                if (!IsValidAuth(parts))
                                {
                                    continue;
                                }
                                string username = parts[1];
                                string secret = parts[2];
                                displayName = parts[3];
                                string message = string.Format("AUTH {0} AS {1} USING {2}\r\n", username.Trim(), displayName.Trim(), secret.Trim());
                                writer.Write(message);
                                writer.Flush();
                                authSent = true;
                                state = State.Auth;
                            }
                            else if (input.StartsWith("/help"))
                            {
                                printUserHelp();
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
                if (state == State.Auth)
                {
                    if (!authSent)
                    {
                        string? input = null;
                        input = Console.ReadLine();
                        if (input != null)
                        {
                            if (IsValidCommand(input))
                            {
                                if (input.StartsWith("/auth"))
                                {
                                    string[] parts = input.Split(' ');
                                    if (!IsValidAuth(parts))
                                    {
                                        continue;
                                    }
                                    string username = parts[1];
                                    string secret = parts[2];
                                    displayName = parts[3];
                                    string message = string.Format("AUTH {0} AS {1} USING {2}\r\n", username.Trim(), displayName.Trim(), secret.Trim());
                                    writer.Write(message);
                                    writer.Flush();
                                    authSent = true;
                                }
                                else if (input.StartsWith("/help"))
                                {
                                    printUserHelp();
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
                    string? receivedMessage = reader.ReadLine();
                    if (!string.IsNullOrEmpty(receivedMessage))
                    {
                        if (receivedMessage.StartsWith("REPLY"))
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
                                state = State.Open;
                                continue;
                            }
                            else if (parts[1] == "NOK")
                            {
                                Console.Error.WriteLine("Failure: " + reason);
                                authSent = false;
                                continue;
                            }
                        }
                        else if (receivedMessage.StartsWith("ERR"))
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
                            state = State.End;
                        }
                        else
                        {
                            state = State.Error;
                        }
                    }
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        state = State.End;
                    }
                }
                if (state == State.Open)
                {
                    Thread sendThread = new Thread(() => SendMessageTCP(writer));
                    Thread receiveThread = new Thread(() => ReceiveMessageTCP(reader));
                    sendThread.Start();
                    receiveThread.Start();

                    sendThread.Join();
                    receiveThread.Join();

                    if (TCPSendBYE == true)
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        TCPSendBYE = false;
                        state = State.End;
                        break;
                    }
                    if (TCPSendERR == true)
                    {
                        string message = string.Format("ERR FROM {0} IS Incoming message from {1}:{2} failed to be parsed.\r\n", displayName, serverIpAddress, serverPort);
                        writer.Write(message);
                        writer.Flush();
                        TCPSendERR = false;
                        state = State.Error;
                    }
                }
                if (state == State.Error)
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    state = State.End;
                    break;
                }
                if (state == State.End)
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
        while (true)
        {
            if (state == State.End)
            {
                break;
            }
            string? receivedMessage = reader.ReadLine();
            if (!string.IsNullOrEmpty(receivedMessage))
            {
                if (receivedMessage.StartsWith("REPLY"))
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
                    }
                    else if (parts[1] == "NOK")
                    {
                        Console.Error.WriteLine("Failure: " + reason);
                    }
                }
                else if (receivedMessage.StartsWith("MSG"))
                {
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.IndexOf(parts, "IS");
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.WriteLine(receivedDisplayName + ": " + messageContent);

                }
                else if (receivedMessage.StartsWith("ERR"))
                {
                    TCPSendBYE = true;
                    TCPReceivedError = true;
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.IndexOf(parts, "IS");
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
                    break;
                }
                else if (receivedMessage.StartsWith("BYE"))
                {
                    TCPReceievedBye = true;
                    state = State.End;
                    break;
                }
                else
                {
                    TCPReceivedError = true;
                    TCPSendERR = true;
                    break;
                }
            }
        }
    }

    private bool CheckKey()
    {
        if (!Console.IsInputRedirected)
        {
            if (Console.KeyAvailable)
            {
                return true;
            }
            return false;
        }
        return true;
    }
    private void SendMessageTCP(StreamWriter writer)
    {
        while ((!TCPReceivedError && !TCPReceievedBye && !TCPSendBYE && !TCPSendERR))
        {

            if (CheckKey())
            {
                string? input = Console.ReadLine();
                if (input != null)
                {
                    if (input.StartsWith("/join"))
                    {
                        string[] parts = input.Split(' ');
                        string channelId = parts[1];
                        string message = string.Format("JOIN {0} AS {1}\r\n", channelId.Trim(), displayName.Trim());
                        writer.Write(message);
                        writer.Flush();
                    }
                    else if (input.StartsWith("/rename"))
                    {
                        string[] parts = input.Split(' ');
                        displayName = parts[1];

                    }
                    else if (input.StartsWith("/help"))
                    {
                        printUserHelp();
                    }
                    else if (input.StartsWith("/auth"))
                    {
                        state = State.Error;
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
                        string message = string.Format("MSG FROM {0} IS {1}\r\n", displayName.Trim(), messageContent);
                        writer.Write(message);
                        writer.Flush();
                    }
                }
                else
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    state = State.End;
                    break;
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }
    //TODO vychodit iz progy posle pustoy linii
    //TODO posmotret v wireshark otpravlyaetsa li bye
    //TODO PROBLEM S UKONCENIM
    //READLINE REQUIRED

    private void printUserHelp()
    {
        Console.WriteLine("Supported local commands:");
        Console.WriteLine("/auth {Username} {Secret} {DisplayName} - Sends AUTH message with the data provided from the command to the server, locally sets the DisplayName");
        Console.WriteLine("/join {ChannelID} - Sends JOIN message with channel name from the command to the server");
        Console.WriteLine("/rename {DisplayName} - Locally changes the display name of the user");
        Console.WriteLine("/help - Prints this message");
    }
    private bool IsValidCommand(string input)
    {
        string[] parts = input.Split(' ');
        string commandPattern = @"^\/[A-Za-z0-9\-_]+$";
        return Regex.IsMatch(parts[0], commandPattern);
    }
}

//TODO normal structure of program
class Program
{
    static void PrintHelp()
    {
        //TODO print help
        ;
    }

    static void Main(string[] args)
    {
        string? transportProtocol = null;
        string? hostnameStr = null;
        ushort serverPort = 4567;
        ushort UDPConfTimeout = 250;
        byte maxUDPRetr = 3;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-"))
            {

                if (args[i] == "-t")
                {
                    transportProtocol = args[i + 1];
                }
                else if (args[i] == "-s")
                {
                    hostnameStr = args[i + 1];
                }
                else if (args[i] == "-p" || args[i] == "--protocol")
                {
                    serverPort = ushort.Parse(args[i + 1]);
                }
                else if (args[i] == "-d")
                {
                    UDPConfTimeout = ushort.Parse(args[i + 1]);
                }
                else if (args[i] == "-r")
                {
                    maxUDPRetr = byte.Parse(args[i + 1]);
                }
                else if (args[i] == "-h")
                {
                    PrintHelp();
                }
            }
        }

        if (transportProtocol == null || hostnameStr == null)
        {
            Console.Error.WriteLine("ERR: Invalid program parameters, transport protocol and server IP address can't be null.");
            Environment.Exit(1);
        }

        //IPAddress[] address = Dns.GetHostAddresses(hostnameStr);
        IPAddress serverIpAddress = IPAddress.Parse(hostnameStr);

        Client client = new Client(serverIpAddress, serverPort, UDPConfTimeout, maxUDPRetr);

        if (transportProtocol == "udp")
        {
            client.ConnectUDP();
        }
        else if (transportProtocol == "tcp")
        {
            client.ConnectTCP();
        }
        else
        {
            Console.Error.WriteLine("ERR: Invalid transport protocol. Use 'udp' or 'tcp'.");
            Environment.Exit(1);
        }
    }
}