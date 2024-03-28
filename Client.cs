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
    private readonly IPAddress _serverIpAddress;
    private readonly ushort _serverPort;
    private readonly ushort _UDPConfTimeout;
    private readonly byte _maxUDPRetr;


    public Client(IPAddress serverIpAddress, ushort serverPort, ushort UDPConfTimeout, byte maxUDPRetr)
    {
        this._serverIpAddress = serverIpAddress;
        this._serverPort = serverPort;
        this._UDPConfTimeout = UDPConfTimeout;
        this._maxUDPRetr = maxUDPRetr;
    }

    public void ConnectUDP()
    {
        UDPClient udpClient = new UDPClient(_serverIpAddress, _serverPort, _UDPConfTimeout, _maxUDPRetr);
        udpClient.Connect();
    }

    public void ConnectTCP()
    {
        TCPClient tcpClient = new TCPClient(_serverIpAddress, _serverPort, _UDPConfTimeout, _maxUDPRetr);
        tcpClient.Connect();
    }
}