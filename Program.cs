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
using System.ComponentModel.Design;
using System.Runtime.Intrinsics.Arm;

class Program
{
    public static void PrintUserHelp()
    {
        Console.WriteLine(@"Supported local commands:
    /auth {Username} {Secret} {DisplayName}   Sends AUTH message with the provided data to the server, 
                                              sets the DisplayName.
    
    /join {ChannelID}                         Sends JOIN message with channel name from the command to the server.
    
    /rename {DisplayName}                     Locally changes the display name of the user.
    
    /help                                     Prints the local commands help message.");
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"IPK Project 1: Client for a chat server using IPK24-CHAT protocol

Description:
    This program is designed to act as a client application for communicating with a remote server 
    using the IPK24-CHAT protocol.The protocol has two variants, the first is based on the TCP protocol, 
    while the second is built on the UDP protocol.

Usage:
    ipk24chat-client [-t <transport_protocol>] [-s <IP_or_hostname>] [-p <server_port>] [-d <UDPtimeout>] 
    [-r <UDPretransmissions>]
    
    ipk24chat-client [-h]

Command Line Interface Arguments:
    -t <tcp | udp>      Transport protocol used for connection, 'tcp' or 'udp'. Must be specified by the user.
    -s <IP or hostname> IP address or hostname of the server. Must be specified by the user.
    -p <uint16>         Server port number. Dafault is 4567.
    -d <uint16>         UDP confirmation timeout in milliseconds. Default is 250.
    -r <uint8>          Maximum number of UDP retransmissions. Default is 3.
    -h                  Prints this help message.
");
        PrintUserHelp();
    }

    static void Main(string[] args)
    {
        string? transportProtocol = null;
        string? hostnameOrIpAddress = null;
        ushort serverPort = 4567;
        ushort UDPConfTimeout = 250;
        byte maxUDPRetr = 3;

        IPAddress? serverIpAddress = null;

        if (args.Length == 1 && args[0] == "-h")
        {
            PrintHelp();
            Environment.Exit(0);
        }

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
                    hostnameOrIpAddress = args[i + 1];
                }
                else if (args[i] == "-p" || args[i] == "--protocol")
                {
                    if (ushort.TryParse(args[i + 1], out ushort parsedValue))
                    {
                        serverPort = parsedValue;
                    }
                    else
                    {
                        Console.Error.WriteLine("ERR: Invalid value for -p. Please provide a valid UInt16 value.");
                        Environment.Exit(1);
                    }
                }
                else if (args[i] == "-d")
                {
                    if (ushort.TryParse(args[i + 1], out ushort parsedValue))
                    {
                        UDPConfTimeout = parsedValue;
                    }
                    else
                    {
                        Console.Error.WriteLine("ERR: Invalid value for -d. Please provide a valid UInt16 value.");
                        Environment.Exit(1);
                    }
                }
                else if (args[i] == "-r")
                {
                    if (byte.TryParse(args[i + 1], out byte parsedValue))
                    {
                        maxUDPRetr = parsedValue;
                    }
                    else
                    {
                        Console.Error.WriteLine("ERR: Invalid value for -r. Please provide a valid UInt8 value.");
                        Environment.Exit(1);
                    }

                }
                else if (args[i] == "-h")
                {
                    Console.Error.WriteLine("ERR: Invalid program parameters.");
                    Environment.Exit(1);
                }
            }
        }

        if (transportProtocol == null || hostnameOrIpAddress == null)
        {
            Console.Error.WriteLine("ERR: Invalid program parameters, transport protocol and server IP address or hostname can't be null.");
            Environment.Exit(1);
        }

        if (!IPAddress.TryParse(hostnameOrIpAddress, out serverIpAddress))
        {
            IPAddress[] address = Dns.GetHostAddresses(hostnameOrIpAddress);
            serverIpAddress = address[0];
        }


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