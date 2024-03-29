using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Helper;
using static System.Runtime.InteropServices.JavaScript.JSType;

/*Helper class for the utility functions for UDP.*/
public class HelperUDP
{
    /* Prints received reply message from the byte array containing the received reply message. */
    public bool PrintReceivedReply(byte[] replyMessage, ref ushort messageID)
    {
        bool success = false;
        ushort replyRefID = (ushort)((replyMessage[4] << 8) | replyMessage[5]);

        /* Before the last incremenentation of messageID there was an ID of recieved auth message.*/
        if (replyRefID == (messageID - 1)) 
        {
            /* Extracts reason from the reply message. */
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

    /* Prints received ERR or MSG message from the byte array containing the received message. */
    public void PrintReceivedErrorOrMessage(byte[] receivedMessage)
    {
        /* Extracts display name from the message. */
        int disNameStartIndex = 3;
        int disNameEndIndex = Array.IndexOf(receivedMessage, (byte)0, disNameStartIndex);
        string receivedDisplayName = Encoding.UTF8.GetString(receivedMessage, disNameStartIndex, disNameEndIndex - disNameStartIndex);

        /* Extracts message content from the message. */
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

    /* Sends a confirmation with the recieved ID. */
    public void SendConfirm(byte[] receivedMessage, Socket UDPSocket, IPEndPoint sendEndPoint)
    {
        byte[] clientConfirm = new byte[3];
        clientConfirm[0] = (byte)MessageType.CONFIRM;
        clientConfirm[1] = receivedMessage[1];
        clientConfirm[2] = receivedMessage[2];
        UDPSocket.SendTo(clientConfirm, 0, clientConfirm.Length, SocketFlags.None, sendEndPoint);
    }

    /* Constructs byte arrays for several types of messages with use of params string[]. */
    public byte[] ConstructMessage(MessageType messageType, ushort messageID, params string[] fields)
    {
        string temp = ((char)messageType).ToString() + ((char)(messageID >> 8)).ToString() + ((char)messageID).ToString() + (string.Join("\0", fields) + "\0");
        byte[] message = Encoding.ASCII.GetBytes(temp);
        return message;
    }
}