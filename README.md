# IPK24-Chat_client

## Author: Movsesian Lilit
## Login: xmovse00

### Description
This program serves as a client application for communicating with a remote server using the IPK24-CHAT protocol. The protocol offers two variants: one based on the TCP protocol and the other built on the UDP protocol. The client application supports various arguments to configure the connection, users can customize the transport protocol (TCP or UDP), server IP address or hostname, server port, UDP connection timeout, and maximum UDP retransmissions.

### Usage 
The client can be compiled by `make` command, the executable is named `ipk24chat-client`. The client application can be run as following:

    ./ipk24chat-client [-t <transport_protocol>] [-s <IP_or_hostname>] [-p <server_port>] [-d <UDPtimeout>] [-r <UDPretransmissions>]
    ./ipk24chat-client [-h]

    Command Line Interface Arguments:
    -t <tcp | udp>      Transport protocol used for connection, 'tcp' or 'udp'. Must be specified by the user.
    -s <IP or hostname> IP address or hostname of the server. Must be specified by the user.
    -p <uint16>         Server port number. Dafault is 4567.
    -d <uint16>         UDP confirmation timeout in milliseconds. Default is 250.
    -r <uint8>          Maximum number of UDP retransmissions. Default is 3.
    -h                  Prints the CLI.

    Supported local commands:
    /auth {Username} {Secret} {DisplayName}   Sends AUTH message with the provided data to the server, sets the DisplayName.
    
    /join {ChannelID}                         Sends JOIN message with channel name from the command to the server.
    
    /rename {DisplayName}                     Locally changes the display name of the user.
    
    /help                                     Prints the local commands help message.

### Implementation

#### Content structuring
The program architecture is organized across 5 files and 5 classes: `Program.cs`, `UDPClient.cs`, `TCPClient.cs`, `Helper.cs`, `HelperUDP.cs`.

##### Program.cs
Within this class, a `Main` method parses command-line arguments to determine the transport protocol, server IP address or hostname, server port, UDP connection timeout, and maximum UDP retransmissions. It also handles errors related to invalid arguments by displaying appropriate error messages. The hostnames is resolved to IP addresses using DNS lookup. This method creates an instance of `UDPClient` or `TCPclient` class based on the specified transport protocol and ivoke a `Connect` method of the corresponding class. 

##### UDPClient.cs
Both `UDPClient` and `TCPclient` contain public constructor and `Connect` methods, invoked from the `Program.cs`. `UDPClient` contains 8 private ManualResetEvent variables and 4 private boolean flags, which are used to ensure the correct synchronization. The `UDPclient` operates within an infinite loop, emulating Finite State Machine states through `if` statements. The logic of `Start`, `Auth`, `Error` and `End` states is serial, while the `Open` state employs parallel processing through `System.Threading`. Threads for sending and receiving of messages are implemented in two separated methods. `Ctrl+C` and `Ctrl+D` events are handled using `Console.CancelKeyPress` event. When `Ctrl+C` or `Ctrl+D` are pressed in the console window, the event handler is invoked, the ManualResetEvent `ctrlCEvent` is Set, the ManualResetEvent `threadsTerminatedEvent`, which is set after the threads are gracefully finished, is awaited, the `BYE` message is sent and the program exits. As the sending of messages is relatively complex in UDP protocol, the helper methods `ByeSendAndConfirm`, `AuthSendAndConfirm` and `SendAndConfirm` were implemented. Threads termination is properly synchronized in different possible cases of termination, such as the end of user input, `BYE` or `ERR` received from the server etc. The possible packet delay/duplication is managed using the HashSet `seenMessageIDs`, where a set of already seen message IDs is contained. If the message was not previously seen, it is added to the set and processed normally, otherwise only the confirmation is sent. The display name is set as a class private variable and can be changed by user.

##### TCPClient.cs
The main logic of `TCPclient` is very similar to `UDPClient` described above. `TCPClient` contains 7 private ManualResetEvent variables and 4 private boolean flags, the only difference is the absence of 2 semaphores for the confirmation reception. Similar to `UDPClient`, the `TCPclient` is implemented as an infinite loop, emulating Finite State Machine states through `if` statements. Similar to the `UDPClient`, the logic of `Start`, `Auth`, `Error` and `End` states is serial, while the `Open` state uses parallel processing through threads. The `Ctrl+C` and `Ctrl+D` events are also implemented with use of 2 semaphores which ensure gracefull termination of threads before the `BYE` message is sent and the program exits. Threads termination is properly synchronized in different possible cases of termination, such as the end of user input, `BYE` or `ERR` received from the server etc. The display name is set as a class private variable and can be changed by user.

##### Helper.cs
This class contains implementation of public helper methods for both `UDPClient` and `TCPClient`, which create an instantion of the `Helper` class and call its methods. The `Helper` class includes methods for validating `AUTH` command parameters and command validation.

##### HelperUDP.cs
This class contains implementation of public helper methods for `UDPClient`, such as a method for construction of UDP byte arrays, a method for confirmation sending and methods for printing received packets to the standard output.

#### Noteworthy Source Code Sections
One interesting aspect of the source code lies in the synchronization mechanism implemented within the `UDPClient`. As the sending of messages is relatively complex in UDP protocol, the process of sending, confirming reception, and potentially retransmitting messages is handled outside the two main threads. Within the sending thread's `while` loop, a ManualResetEvent `confirmReceivedEvent` is consistently set, and at the beginning of receiving threads this semaphore is always awaited. When a user input should be sent, the `confirmReceivedEvent` is reset, and sending thread waits for a ManualResetEvent `confirmWaitingEvent`, which is signalled when receive thread is completely paused. This prevents any potential confirmation from being received in the receiving thread. Following this synchronization, the message can then be sent, potentially retransmitted, and the confirmation received within the `SendAndConfirm` method.

#### Features beyond the assignment
No extra features were implemented in this project.

### Testing
The client was tested on the provided reference Linux `IPK24` VM against the reference server running on the VM. Testing was made by the `test.sh` script which is located in the folder `Tests`. `Test.sh` script compares the expected output with the reference output for the edge cases of program CLI arguments and for several simple client input cases with the clear expected behaviour. Such inputs are for example sending a `JOIN` or `MSG` messages in non-open state or `AUTH` message with a wrong secret. The edge cases include missing required arguments (transport protocol or server hostname/server IP address), invalid transport protocol, help argument used with other arguments, server port or UDP confirmation timeout out of the range of `UInt16`, maximum number of UDP retransmissions out of the range of `UInt8`. 
Moreover, additional input cases, which are located in the `Tests/TestsStdin` folder, were tested. Since the behavior with more complex input data may change due to the messages received from the server and creating a test script containing several of these input tests would overload the server, I have tested it using individual `test.sh` scripts without output comparison and manually. The screenshots of running each of these test cases alongside Wireshark captures screenshots and text output data (not taken at the same time as the screenshots) are located in the same folder. The text output of tests includes both standard error and standard output redirected.

### References
[1]: Dolejška, Daniel. "IPK-Projects-2024/Project 1 at master." Available at: git.fit.vutbr.cz/NESFIT/IPK-Projects-2024/src/branch/master.

[2]: Kurose J.F., Ross K.W. "Computer Networking, A Top-Down Approach Featuring the Internet" (8th edition). Addison-Wesley, 2021.