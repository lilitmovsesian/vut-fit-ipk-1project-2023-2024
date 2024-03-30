using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Channels;


public class TCPClient
{
    /* Declares instantion fields. */
    private readonly IPAddress _serverIpAddress;
    private readonly ushort _serverPort;
    
    /* Current state. */
    private Helper.State _state = Helper.State.Start;
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
    private ManualResetEvent _endOfInputEvent = new ManualResetEvent(false);
    private ManualResetEvent _receivedReplyEvent = new ManualResetEvent(false);
    private ManualResetEvent _ctrlCEvent = new ManualResetEvent(false);
    private ManualResetEvent _threadsTerminatedEvent = new ManualResetEvent(false);

    /* Instantiation of an object. */
    private Helper _helper = new Helper();

    /* Constructor method. */
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
            /* Creates a TCP socket and connects. */
            TCPSocket = new Socket(_serverIpAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint TCPEndPoint = new IPEndPoint(_serverIpAddress, _serverPort);
            TCPSocket.Connect(TCPEndPoint);

            /* Creates a stream, writer and reader. */
            stream = new NetworkStream(TCPSocket);
            writer = new StreamWriter(stream);
            reader = new StreamReader(stream);
            bool authSent = false;
            /* Invokes a method for cancel events handling. */
            SetupCtrlCHandlerTCP(writer);

            /* Main loop for client operation. */
            while (true)
            {
                /* If the current state is Start state, the stdin is read and all posible user 
                * inputs are handled.*/
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
                    /*If the input is null (end of input), bye is sent and state is changed to End.*/
                    else
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
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
                        }
                    }
                    /* Gets a reply message for the AUTH. */
                    string? receivedMessage = reader.ReadLine();
                    if (!string.IsNullOrEmpty(receivedMessage))
                    {
                        if (receivedMessage.ToUpper().StartsWith("REPLY"))
                        {
                            /* Gets the reason from the received message. */
                            string[] parts = receivedMessage.Split(' ');
                            int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                            string? reason = null;
                            if (isIndex + 1 < parts.Length)
                            {
                                reason = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                            }
                            /* If reply is positive, the current state is Open state. */
                            if (parts[1] == "OK")
                            {
                                Console.Error.WriteLine("Success: " + reason);
                                _state = Helper.State.Open;
                                continue;
                            }
                            /* Otherwise, the state remains and authSent flag is reset. */
                            else if (parts[1] == "NOK")
                            {
                                Console.Error.WriteLine("Failure: " + reason);
                                authSent = false;
                                continue;
                            }
                        }
                        /* If an error is received, BYE is sent and current state is changed to End state. */
                        else if (receivedMessage.ToUpper().StartsWith("ERR"))
                        {
                            string[] parts = receivedMessage.Split(' ');
                            int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
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
                        /* If any other message types are received, the state is changed to Error. */
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
                    /* Resets ManualResetEvents required for the synchronization. */
                    _sendEvent.Reset();
                    _receiveEvent.Reset();
                    _receivedReplyEvent.Reset();
                    _endOfInputEvent.Reset();
                    TCPSocket.ReceiveTimeout = 250;

                    /* The threads creation. */
                    Thread sendThread = new Thread(() => SendMessageTCP(writer));
                    Thread receiveThread = new Thread(() => ReceiveMessageTCP(reader));
                    sendThread.Start();
                    receiveThread.Start();

                    /* The threads termination and corresponding semaphore setting. */
                    sendThread.Join();
                    receiveThread.Join();
                    _threadsTerminatedEvent.Set();

                    /* If this flag is set inside the Open state threads, BYE is sent and current state is changed to End state. */
                    if (_sendBYE == true)
                    {
                        writer.Write("BYE\r\n");
                        writer.Flush();
                        _sendBYE = false;
                        _state = Helper.State.End;
                    }
                    /* If this flag is set inside the Open state threads, ERR is sent and the current state is set to Error state. */
                    if (_sendERR == true)
                    {
                        string message = string.Format("ERR FROM {0} IS Incoming message from {1}:{2} failed to be parsed.\r\n", _displayName, _serverIpAddress, _serverPort);
                        writer.Write(message);
                        writer.Flush();
                        _sendERR = false;
                        _state = Helper.State.Error;
                    }
                }
                /* In the Error state the BYE is sent and current state is changed to End state. */
                if (_state == Helper.State.Error)
                {
                    writer.Write("BYE\r\n");
                    writer.Flush();
                    _state = Helper.State.End;
                }
                /* Exit from the main loop, the socket, stream, writer and reader are closed and the client is terminated. */
                if (_state == Helper.State.End)
                {
                    TCPSocket.Close();
                    stream.Close();
                    writer.Close();
                    reader.Close();
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

    /* A method for a thread for message receiving. */
    private void ReceiveMessageTCP(StreamReader reader)
    {
        /* A loop for message receiving while the semaphore for cancel events is not set, the state is not End
         * and the semaphore for the end of input is not set. */
        while (_state != Helper.State.End && !_endOfInputEvent.WaitOne(0) && !_ctrlCEvent.WaitOne(0))
        {
            /* Waits for a semaphore to be set. */
            _receiveEvent.WaitOne();
            string? receivedMessage = null;

            /* Try-Catch to receive a message or continue. */
            try
            {
                receivedMessage = reader.ReadLine();
            }
            catch (Exception)
            {
                _sendEvent.Set();
                continue;
            }
            /* Handles all possible received messages. */
            if (!string.IsNullOrEmpty(receivedMessage))
            {
                /* If a reply to JOIN is received, the output is printed and a semaphore is set. */
                if (receivedMessage.ToUpper().StartsWith("REPLY"))
                {
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                    /* Gets the reason. */
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
                /* Prints an output based on the received message. */
                else if (receivedMessage.ToUpper().StartsWith("MSG"))
                {
                    string[] parts = receivedMessage.Split(' ');
                    int isIndex = Array.FindIndex(parts, part => part.Equals("is", StringComparison.OrdinalIgnoreCase));
                    /* Gets the message content and the display name. */
                    string receivedDisplayName = parts[2];
                    string? messageContent = null;
                    if (isIndex + 1 < parts.Length)
                    {
                        messageContent = string.Join(" ", parts, isIndex + 1, parts.Length - isIndex - 1);
                    }
                    Console.WriteLine(receivedDisplayName + ": " + messageContent);

                }
                /* If the ERR is received, the flags are set, output is printed and thread is terminated with
                 * the setting od a semaphore required for sending thread. */
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
                    break;
                }
                /* If the BYE is received, the flag is set, state is changed, output is printed and thread is 
                 * terminated with the setting od a semaphore required for sending thread. */
                else if (receivedMessage.ToUpper().StartsWith("BYE"))
                {
                    _receievedBYE = true;
                    _state = Helper.State.End;
                    _sendEvent.Set();
                    break;
                }
                /* If anything else is received, the flags and semaphores are set and the thread is terminated. */
                else
                {
                    _receivedERR = true;
                    _sendERR = true;
                    _sendEvent.Set();
                    break;
                }
            }
            _sendEvent.Set();
        }
    }

    /* A method for a thread for message sending. */
    private void SendMessageTCP(StreamWriter writer)
    {
        /* A loop for message sending while the flags and the semaphore for cancel events is not set. */
       while (!_receivedERR && !_receievedBYE && !_sendBYE && !_sendERR && !_ctrlCEvent.WaitOne(0))
       {
           /* Sets the required semaphore for a receiving thread, waits for a semaphore for sending. */
           _receiveEvent.Set();
           _sendEvent.WaitOne();
           /* Terminate if the state has changed. */
           if (_state == Helper.State.End)
           {
               _receiveEvent.Set();
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
                       string channelId = parts[1];
                       string message = string.Format("JOIN {0} AS {1}\r\n", channelId.Trim(), _displayName.Trim());
                       writer.Write(message);
                       writer.Flush();
                       /* A short thread sleep to send every message in a different packet. */
                       Thread.Sleep(100);
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
                       /* Sends a MSG. */
                       string messageContent = input;
                       string message = string.Format("MSG FROM {0} IS {1}\r\n", _displayName.Trim(), messageContent);
                       writer.Write(message);
                       writer.Flush();
                       /* A short thread sleep to send every message in a different packet. */
                       Thread.Sleep(100);
                   }
               }
                /* If the input is null, the BYE is sent, the state is changed, the end of input semaphore is set and 
                 * all semaphores required for receive thread are set.*/
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
               /* Avoids blocking of program in several cases that I tested. */
               Thread.Sleep(100);
           }
       }
   }

   /* Handles Ctrl+C and Ctrl+D event. If cancel key is pressed, a semaphore _ctrlCEvent is set.
       Bye message is sent. If the current state is Open state, the handler waits for both 
       threads to terminate and exits the program. */
        private void SetupCtrlCHandlerTCP(StreamWriter writer)
    {
        _ctrlCEvent.Reset();
        _threadsTerminatedEvent.Reset();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            _ctrlCEvent.Set();
            writer.Write("BYE\r\n");
            writer.Flush();
            if (_state == Helper.State.Open)
            {
                _threadsTerminatedEvent.WaitOne();
            }
            Environment.Exit(0);
        };
    }
}