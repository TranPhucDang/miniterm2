using Microsoft.Win32.SafeHandles;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static MiniTerm.Native.ConsoleApi;


namespace MiniTerm
{


    public class CustomFileStream : FileStream
    {
        public CustomFileStream (SafeFileHandle handle, FileAccess access) : base(handle, access)
        {
        }
        public CustomFileStream(string path, FileMode mode) : base(path, mode)
        {
        }

        public CustomFileStream(string path, FileMode mode, FileAccess access) : base(path, mode, access)
        {
        }

        public CustomFileStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
        {
        }

        // Override the CopyTo method
        public void customCopyTo(Stream destination)
        {
            customInternalCopyTo(destination, 81920);
        }

        private void customInternalCopyTo(Stream destination, int bufferSize)
        {
            byte[] array = new byte[bufferSize];
            int count;
            while ((count = Read(array, 0, array.Length)) != 0)
            {
                destination.Write(array, 0, count);
                string output1 = Encoding.ASCII.GetString(array, 0, array.Length);
                Debug.Print("Line read: "+output1);
            }
        }
    }
    /// <summary>
    /// The UI of the terminal. It's just a normal console window, but we're managing the input/output.
    /// In a "real" project this could be some other UI.
    /// </summary>
    internal sealed class Terminal
    {
        private const string ExitCommand = "exit\r";
        private const string CtrlC_Command = "\x3";

        public SafeFileHandle _consoleInputPipeWriteHandle;
        public StreamWriter _consoleInputWriter;
        public SafeFileHandle _consoleutPipeWriteHandle;
        public FileStream _consoleOutputReader;

        public Terminal()
        {
            EnableVirtualTerminalSequenceProcessing();
        }

        /// <summary>
        /// Newer versions of the windows console support interpreting virtual terminal sequences, we just have to opt-in
        /// </summary>
        private static void EnableVirtualTerminalSequenceProcessing()
        {
            var hStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (!GetConsoleMode(hStdOut, out uint outConsoleMode))
            {
                throw new InvalidOperationException("Could not get console mode");
            }

            outConsoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
            if (!SetConsoleMode(hStdOut, outConsoleMode))
            {
                throw new InvalidOperationException("Could not enable virtual terminal processing");
            }
        }

        /// <summary>
        /// Start the pseudoconsole and run the process as shown in 
        /// https://docs.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session#creating-the-pseudoconsole
        /// </summary>
        /// <param name="command">the command to run, e.g. cmd.exe</param>
        public void Run(string command)
        {
            using (var inputPipe = new PseudoConsolePipe())
            using (var outputPipe = new PseudoConsolePipe())
            using (var pseudoConsole = PseudoConsole.Create(inputPipe.ReadSide, outputPipe.WriteSide, (short)Console.WindowWidth, (short)Console.WindowHeight))
            using (var process = ProcessFactory.Start(command, PseudoConsole.PseudoConsoleThreadAttribute, pseudoConsole.Handle))
            {
                // copy all pseudoconsole output to stdout
                Task.Run(() => CopyPipeToOutput(outputPipe.ReadSide));
                // create new get text
                /*Task.Run(() => GetCopyPipeToOutput(inputPipe.ReadSide, outputPipe.ReadSide));*/
                // prompt for stdin input and send the result to the pseudoconsole
                Task.Run(() => CopyInputToPipe(inputPipe.WriteSide));
                // free resources in case the console is ungracefully closed (e.g. by the 'x' in the window titlebar)
                OnClose(() => DisposeResources(process, pseudoConsole, outputPipe, inputPipe));

                WaitForExit(process).WaitOne(Timeout.Infinite);
            }
        }

        /// <summary>
        /// Reads terminal input and copies it to the PseudoConsole
        /// </summary>
        /// <param name="inputWriteSide">the "write" side of the pseudo console input pipe</param>
        private static void CopyInputToPipe(SafeFileHandle inputWriteSide)
        {
            using (var writer = new StreamWriter(new FileStream(inputWriteSide, FileAccess.Write)))
            {
                ForwardCtrlC(writer);
                /*ForwardEnter(writer);*/
                writer.AutoFlush = true;
                /*writer.WriteLine(@"cd \");*/

                while (true)
                {
                    // send input character-by-character to the pipe
                    char key = Console.ReadKey(intercept: true).KeyChar;
                    writer.Write(key);
                    /*Debug.Print(key.ToString());*/
                }
            }
        }

        /// <summary>
        /// Don't let ctrl-c kill the terminal, it should be sent to the process in the terminal.
        /// </summary>
        private static void ForwardCtrlC(StreamWriter writer)
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Debug.WriteLine("send control+C");
                writer.Write(CtrlC_Command);
            };

        }

        private static void ForwardEnter(StreamWriter writer) {
            if (Console.ReadKey().Key == ConsoleKey.Enter)
            {
                Console.WriteLine("User pressed \"Enter\"");
            }
        }


        /*private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                Console.Write("Press Enter to continue!")
            }
        }*/

        /// <summary>
        /// Reads PseudoConsole output and copies it to the terminal's standard out.
        /// </summary>
        /// <param name="outputReadSide">the "read" side of the pseudo console output pipe</param>
        private void CopyPipeToOutput(SafeFileHandle outputReadSide)
        {

            /*using (var terminalOutput = Console.OpenStandardOutput())
            using (var pseudoConsoleOutput = new CustomFileStream(outputReadSide, FileAccess.Read))
            {
                pseudoConsoleOutput.customCopyTo(terminalOutput);
            }*/

            using (var terminalOutput = Console.OpenStandardOutput())
            using (var pseudoConsoleOutput = new FileStream(outputReadSide, FileAccess.Read))
            {
                pseudoConsoleOutput.CopyTo(terminalOutput);
            }
            /*using (var terminalOutput = Console.OpenStandardOutput())
            using (var pseudoConsoleOutput = new FileStream(outputReadSide, FileAccess.Read))
            {
                using (var reader = new StreamReader(pseudoConsoleOutput))
                {
                    
                    while (true)
                    {
                        var line = reader.ReadLine();
                        Debug.Print("Line read: " + line);
                        Console.WriteLine(line);
                    }
                    
                }


            }*/

        }

        private static void GetCopyPipeToOutput(SafeFileHandle outputReadSide, SafeFileHandle inputWriteSide)
        {
            using (var fs = new FileStream(inputWriteSide, FileAccess.Read))
            using (var reader = new StreamReader(fs))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    Debug.Print("Line read: " + line);
                }
            }
        }

        /// <summary>
        /// Get an AutoResetEvent that signals when the process exits
        /// </summary>
        private static AutoResetEvent WaitForExit(Process process) =>
            new AutoResetEvent(false)
            {
                SafeWaitHandle = new SafeWaitHandle(process.ProcessInfo.hProcess, ownsHandle: false)
            };

        /// <summary>
        /// Set a callback for when the terminal is closed (e.g. via the "X" window decoration button).
        /// Intended for resource cleanup logic.
        /// </summary>
        private static void OnClose(Action handler)
        {
            SetConsoleCtrlHandler(eventType =>
            {
                if(eventType == CtrlTypes.CTRL_CLOSE_EVENT)
                {
                    handler();
                }
                return false;
            }, true);
        }

        private void DisposeResources(params IDisposable[] disposables)
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
