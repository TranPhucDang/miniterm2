using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;



namespace MiniTerm
{
    /// <summary>
    /// C# version of:
    /// https://blogs.msdn.microsoft.com/commandline/2018/08/02/windows-command-line-introducing-the-windows-pseudo-console-conpty/
    /// https://docs.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session
    ///
    /// System Requirements:
    /// As of September 2018, requires Windows 10 with the "Windows Insider Program" installed for Redstone 5.
    /// Also requires the Windows Insider Preview SDK: https://www.microsoft.com/en-us/software-download/windowsinsiderpreviewSDK
    /// </summary>
    /// <remarks>
    /// Basic design is:
    /// Terminal UI starts the PseudoConsole, and controls it using a pair of PseudoConsolePipes
    /// Terminal UI will run the Process (cmd.exe) and associate it with the PseudoConsole.
    /// </remarks>
    static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var terminal = new Terminal();
               /* StartServer(terminal);*/
                terminal.Run("cmd.exe");
                
            }
            catch (InvalidOperationException e)
            {
                Debug.Print("dadadadada");
                Console.Error.WriteLine(e.Message);
                throw;
            }
        }

        static void StartServer(Terminal _terminal)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 12345);
            listener.Start();
            Console.WriteLine("Server started. Waiting for a client...");

            using (TcpClient client = listener.AcceptTcpClient())
            using (NetworkStream stream = client.GetStream())
            {
                Console.WriteLine("Client connected.");

                // Start a thread to handle receiving messages from the client
                Thread receiveThread = new Thread(ReceiveMessages);
                /*receiveThread.Start(stream);*/

                // Start a loop to read input from the console and send it to the client
                
                using (var reader = new StreamReader(_terminal._consoleOutputReader))
                {
                    while (true)
                    {
                        /*var line = reader.ReadLine();
                        Debug.Print("Line read: " + line);*/
                        string input = reader.ReadLine();
                        byte[] data = Encoding.UTF8.GetBytes(input);
                        stream.Write(data, 0, data.Length);
                    }
                }
                /*while (true)
                {
                    string input = Console.ReadLine();
                    byte[] data = Encoding.UTF8.GetBytes(input);
                    stream.Write(data, 0, data.Length);
                }*/
            }
        }

        static void ReceiveMessages(object obj)
        {
            NetworkStream stream = (NetworkStream)obj;

            while (true)
            {
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Received from client: {receivedMessage}");
                }
            }
        }

    }
}
