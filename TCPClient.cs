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
    private readonly IPAddress serverIpAddress;
    private readonly ushort serverPort;
    private readonly ushort UDPConfTimeout;
    private readonly byte maxUDPRetr;

    Helper.State state = Helper.State.Start;
    bool sendBYE = false;
    bool sendERR = false;
    string displayName = "";
    bool receivedERR = false;
    bool receievedBYE = false;
    private ManualResetEvent sendEvent = new ManualResetEvent(false);
    private ManualResetEvent receiveEvent = new ManualResetEvent(false);
    private ManualResetEvent endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent receivedReplyEvent = new ManualResetEvent(false);

    Helper helper = new Helper();

    public TCPClient(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this.serverIpAddress = serverIpAddress;
        this.serverPort = serverPort;
        this.UDPConfTimeout = UDPConfTimeout;
        this.maxUDPRetr = maxUDPRetr;
    }


    public void Connect()
    {
        Socket? TCPSocket = null;
        NetworkStream? stream = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;
        try
        {
            TCPSocket = new Socket(serverIpAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint TCPEndPoint = new IPEndPoint(serverIpAddress, serverPort);
            TCPSocket.Connect(TCPEndPoint);
            stream = new NetworkStream(TCPSocket);

            writer = new StreamWriter(stream);
            reader = new StreamReader(stream);

            bool authSent = false;
            helper.SetupCtrlCHandlerTCP(writer);
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
                                string[] parts = input.Split(' ');
                                if (!helper.IsValidAuth(parts))
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
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        state = Helper.State.End;

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
                                    string[] parts = input.Split(' ');
                                    if (!helper.IsValidAuth(parts))
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
                        else
                        {
                            writer.Write("BYE\r\n");
                            writer.Flush();
                            state = Helper.State.End;
                            break;
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
                                state = Helper.State.Open;
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
                            state = Helper.State.End;
                        }
                        else
                        {
                            state = Helper.State.Error;
                        }
                    }
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        state = Helper.State.End;
                    }
                }
                if (state == Helper.State.Open)
                {
                    sendEvent.Reset();
                    receiveEvent.Reset();
                    receivedReplyEvent.Reset();
                    endOfInputEvent.Reset();

                    Thread sendThread = new Thread(() => SendMessageTCP(writer));
                    Thread receiveThread = new Thread(() => ReceiveMessageTCP(reader));
                    sendThread.Start();
                    receiveThread.Start();

                    sendThread.Join();
                    receiveThread.Join();

                    if (sendBYE == true)
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        sendBYE = false;
                        state = Helper.State.End;
                        break;
                    }
                    if (sendERR == true)
                    {
                        string message = string.Format("ERR FROM {0} IS Incoming message from {1}:{2} failed to be parsed.\r\n", displayName, serverIpAddress, serverPort);
                        writer.Write(message);
                        writer.Flush();
                        sendERR = false;
                        state = Helper.State.Error;
                    }
                }
                if (state == Helper.State.Error)
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    endOfInputEvent.Set();
                    state = Helper.State.End;
                    break;
                }
                if (state == Helper.State.End)
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
            receiveEvent.WaitOne();
            iif(state == Helper.State.End || state == Helper.State.Error || endOfInputEvent.WaitOne(0))
            {
                sendEvent.Set();
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
                    receivedReplyEvent.Set();
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
                    sendBYE = true;
                    receivedERR = true;
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.IndexOf(parts, "IS");
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
                    sendEvent.Set();
                    break;
                }
                else if (receivedMessage.StartsWith("BYE"))
                {
                    receievedBYE = true;
                    state = Helper.State.End;
                    sendEvent.Set();
                    break;
                }
                else
                {
                    receivedERR = true;
                    sendERR = true;
                    sendEvent.Set();
                    break;
                }
            }
            sendEvent.Set();
        }
    }
    private void SendMessageTCP(StreamWriter writer)
    {
        while ((!receivedERR && !receievedBYE && !sendBYE && !sendERR))
        {
            receiveEvent.Set();
            sendEvent.WaitOne();
            if (state == Helper.State.End)
            {
                receiveEvent.Set();
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
                        string message = string.Format("JOIN {0} AS {1}\r\n", channelId.Trim(), displayName.Trim());
                        writer.Write(message);
                        writer.Flush();
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
                        receiveEvent.Set();
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
                        Thread.Sleep(300);
                    }
                }
                else
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    state = Helper.State.End;
                    endOfInputEvent.Set();
                    receiveEvent.Set();
                    //Thread.Sleep(300);                   	
                    //break;
                    Environment.Exit(0);
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }
}