using System;
using System.IO.Ports;
using System.Threading;

namespace SigmakokiDiagnostic
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Sigmakoki HSC-103 Diagnostic Tool ===");
            Console.WriteLine();
            
            // Configure serial port
            string portName = "COM3"; // Change this to your port
            Console.Write($"Enter COM port (default: {portName}): ");
            string input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input)) portName = input;
            
            SerialPort port = new SerialPort();
            port.PortName = portName;
            port.BaudRate = 38400;
            port.Parity = Parity.None;
            port.DataBits = 8;
            port.StopBits = StopBits.One;
            port.ReadTimeout = 5000;
            port.WriteTimeout = 5000;
            port.RtsEnable = true;
            port.NewLine = "\r\n";
            
            try
            {
                Console.WriteLine($"Connecting to {portName}...");
                port.Open();
                Console.WriteLine("Connected!");
                Console.WriteLine();
                
                while (true)
                {
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  Q  - Query position");
                    Console.WriteLine("  !  - Query status");
                    Console.WriteLine("  M1 - Move axis 1 (X) +1000 steps");
                    Console.WriteLine("  M2 - Move axis 2 (Y) +1000 steps");
                    Console.WriteLine("  M3 - Move axis 3 (Z) +1000 steps");
                    Console.WriteLine("  H  - Home all axes");
                    Console.WriteLine("  S  - Stop all movement");
                    Console.WriteLine("  X  - Exit");
                    Console.Write("\nEnter command: ");
                    
                    string cmd = Console.ReadLine()?.ToUpper();
                    
                    if (cmd == "X") break;
                    
                    string command = cmd switch
                    {
                        "Q" => "Q:",
                        "!" => "!:",
                        "M1" => "M:+1000",
                        "M2" => "M:,+1000",
                        "M3" => "M:,,+1000",
                        "H" => "H:",
                        "S" => "L:",
                        _ => null
                    };
                    
                    if (command == null)
                    {
                        Console.WriteLine("Invalid command!");
                        continue;
                    }
                    
                    try
                    {
                        Console.WriteLine($"Sending: {command}");
                        port.WriteLine(command);
                        
                        string response = port.ReadLine();
                        Console.WriteLine($"Response: {response}");
                        
                        if (cmd.StartsWith("M"))
                        {
                            Console.WriteLine("Waiting for movement to complete...");
                            Thread.Sleep(500);
                            
                            // Check status
                            port.WriteLine("!:");
                            string status = port.ReadLine();
                            Console.WriteLine($"Status: {status}");
                            
                            // Check position
                            port.WriteLine("Q:");
                            string position = port.ReadLine();
                            Console.WriteLine($"Position: {position}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                    
                    Console.WriteLine();
                }
                
                port.Close();
                Console.WriteLine("Disconnected.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
