using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/*Helper class for the utility functions.*/
public class Helper
{
    /*Enum defining message types.*/
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

    /*Enum defining states.*/
    public enum State
    {
        Start,
        Auth,
        Open,
        Error,
        End
    };

    /* Validates the /auth command format. The display name, secret and username is validated using regexes.*/
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

    /* A function which i use in both protocols in waiting for input by the send message thread.
     * In case of non redirected standard input, the program can be blocked waiting for input
     * in some cases which i tested.*/
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


    /*Checks if a command is valid comparing it with a regex pattern, returns boolean.*/
    public bool IsValidCommand(string input)
    {
        string[] parts = input.Split(' ');
        string commandPattern = @"^\/[A-Za-z0-9\-_]+$";
        return Regex.IsMatch(parts[0], commandPattern);
    }
}