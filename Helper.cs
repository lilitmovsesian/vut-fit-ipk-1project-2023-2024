using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


public class Helper
{
    public enum MessageType : byte
    {
        CONFIRM = 0x00,
        REPLY = 0x01,
        AUTH = 0x02,
        JOIN = 0x03,
        MSG = 0x04,
        ERR = 0xFE,
        BYE = 0xFF
    }

    public enum State
    {
        Start,
        Auth,
        Open,
        Error,
        End
    };

    public bool PrintReceivedReply(byte[] replyMessage, ref ushort messageID)
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

    public void PrintReceivedErrorOrMessage(byte[] receivedMessage)
    {
        int disNameStartIndex = 3;
        int disNameEndIndex = Array.IndexOf(receivedMessage, (byte)0, disNameStartIndex);
        string receivedDisplayName = Encoding.UTF8.GetString(receivedMessage, disNameStartIndex, disNameEndIndex - disNameStartIndex);

        int mesContentStartIndex = disNameEndIndex + 1;
        int mesContentEndIndex = Array.IndexOf(receivedMessage, (byte)0, mesContentStartIndex);
        string messageContent = Encoding.UTF8.GetString(receivedMessage, mesContentStartIndex, mesContentEndIndex - mesContentStartIndex);

        if (receivedMessage[0] == (byte)MessageType.ERR)
        {
            Console.Error.WriteLine("ERR FROM " + receivedDisplayName + ": " + messageContent);
        }
        else if (receivedMessage[0] == (byte)MessageType.MSG)
        {
            Console.WriteLine(receivedDisplayName + ": " + messageContent);
        }
    }

    public void SendConfirm(byte[] replyMessage, Socket UDPSocket, IPEndPoint sendEndPoint)
    {
        byte[] clientConfirm = new byte[3];
        clientConfirm[0] = (byte)MessageType.CONFIRM;
        clientConfirm[1] = replyMessage[1];
        clientConfirm[2] = replyMessage[2];
        UDPSocket.SendTo(clientConfirm, 0, clientConfirm.Length, SocketFlags.None, sendEndPoint);
    }

    public bool IsValidAuth(string[] parts)
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

    public byte[] ConstructMessage(MessageType messageType, ushort messageID, params string[] fields)
    {
        string temp = ((char)messageType).ToString() + ((char)(messageID >> 8)).ToString() + ((char)messageID).ToString() + (string.Join("\0", fields) + "\0");
        byte[] message = Encoding.ASCII.GetBytes(temp);
        return message;
    }

    public bool CheckKey()
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

    public void SetupCtrlCHandlerTCP(StreamWriter writer)
    {

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            writer.Write("BYE\r\n");
            writer.Flush();
            Environment.Exit(0);
        };
    }

    public bool IsValidCommand(string input)
    {
        string[] parts = input.Split(' ');
        string commandPattern = @"^\/[A-Za-z0-9\-_]+$";
        return Regex.IsMatch(parts[0], commandPattern);
    }
}