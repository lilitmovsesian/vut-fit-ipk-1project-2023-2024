using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


public class Client
{
    private readonly IPAddress serverIpAddress;
    private readonly ushort serverPort;
    private readonly ushort UDPConfTimeout;
    private readonly byte maxUDPRetr;


    public Client(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this.serverIpAddress = serverIpAddress;
        this.serverPort = serverPort;
        this.UDPConfTimeout = UDPConfTimeout;
        this.maxUDPRetr = maxUDPRetr;
    }

    public void ConnectUDP()
    {
        UDPClient udpClient = new UDPClient(serverIpAddress, serverPort, UDPConfTimeout, maxUDPRetr);
        udpClient.Connect();
    }

    public void ConnectTCP()
    {
        TCPClient tcpClient = new TCPClient(serverIpAddress, serverPort, UDPConfTimeout, maxUDPRetr);
        tcpClient.Connect();
    }
}