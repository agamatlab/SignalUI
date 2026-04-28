using System;
using mv.impact.acquire;
using mv.impact.acquire.GenICam;

namespace ExposureTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Matrix Vision Camera Exposure Test ===\n");

            Device? device = null;
            
            try
            {
                // Update device list
                Console.WriteLine("Scanning for cameras...");
                DeviceManager.updateDeviceList();
                
                int deviceCount = DeviceManager.deviceCount;
                Console.WriteLine($"Found {deviceCount} device(s)\n");
                
                if (deviceCount == 0)
                {
                    Console.WriteLine("ERROR: No cameras found!");
                    Console.WriteLine("Please check:");
                    Console.WriteLine("  - Camera is powered on");
                    Console.WriteLine("  - Camera is connected to network");
                    Console.WriteLine("  - mvIMPACT Acquire drivers are installed");
                    return;
                }

                // List all devices
                for (int i = 0; i < deviceCount; i++)
                {
                    var dev = DeviceManager.getDevice(i);
                    Console.WriteLine($"Device {i}: {dev.family.read()} - {dev.serial.read()}");
                }

                // Open first device
                Console.WriteLine($"\nOpening device 0...");
                device = DeviceManager.getDevice(0);
                
                try
                {
                    device.open();
                    Console.WriteLine("Device opened successfully!\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Failed to open device: {ex.Message}");
                    return;
                }

                // Set GenICam interface layout
                try
                {
                    device.interfaceLayout.write(TDeviceInterfaceLayout.dilGenICam);
                    Console.WriteLine("Interface layout set to GenICam\n");
                }
                catch
                {
                    Console.WriteLine("Warning: Could not set GenICam interface layout (continuing anyway)\n");
                }

                // Test exposure control
                Console.WriteLine("=== Testing Exposure Control ===\n");
                
                var acqCtrl = new AcquisitionControl(device);
                
                // Check if exposure nodes are valid
                Console.WriteLine("Checking GenICam nodes:");
                Console.WriteLine($"  exposureAuto.isValid: {acqCtrl.exposureAuto.isValid}");
                Console.WriteLine($"  exposureTime.isValid: {acqCtrl.exposureTime.isValid}");
                
                if (!acqCtrl.exposureTime.isValid)
                {
                    Console.WriteLine("\nERROR: exposureTime node is not valid!");
                    Console.WriteLine("This camera may not support GenICam exposure control.");
                    return;
                }
                
                // Get current exposure
                double currentExposure = acqCtrl.exposureTime.read();
                Console.WriteLine($"\nCurrent exposure: {currentExposure:F1} µs");
                
                // Check min/max limits
                if (acqCtrl.exposureTime.hasMinValue)
                {
                    double minExp = acqCtrl.exposureTime.minValue;
                    Console.WriteLine($"Min exposure: {minExp:F1} µs");
                }
                
                if (acqCtrl.exposureTime.hasMaxValue)
                {
                    double maxExp = acqCtrl.exposureTime.maxValue;
                    Console.WriteLine($"Max exposure: {maxExp:F1} µs");
                }
                
                // Turn off auto exposure
                if (acqCtrl.exposureAuto.isValid)
                {
                    Console.WriteLine("\nTurning off ExposureAuto...");
                    try
                    {
                        acqCtrl.exposureAuto.write(0); // 0 = Off
                        Console.WriteLine("ExposureAuto set to Off");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not set ExposureAuto: {ex.Message}");
                    }
                }
                
                // Test different exposure values
                double[] testExposures = { 1000.0, 5000.0, 10000.0, 20000.0, 50000.0 };
                
                Console.WriteLine("\n=== Testing Exposure Values ===\n");
                
                foreach (double testExp in testExposures)
                {
                    Console.WriteLine($"Setting exposure to {testExp:F1} µs...");
                    
                    try
                    {
                        acqCtrl.exposureTime.write(testExp);
                        double appliedExp = acqCtrl.exposureTime.read();
                        
                        bool success = Math.Abs(appliedExp - testExp) < 1.0;
                        string status = success ? "✓ SUCCESS" : "⚠ CLAMPED";
                        
                        Console.WriteLine($"  Applied: {appliedExp:F1} µs [{status}]");
                        
                        if (!success)
                        {
                            Console.WriteLine($"  Note: Camera adjusted value (may be within valid range)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                    }
                    
                    Console.WriteLine();
                }
                
                // Test gain control
                Console.WriteLine("=== Testing Gain Control ===\n");
                
                var analogCtrl = new AnalogControl(device);
                
                Console.WriteLine("Checking gain nodes:");
                Console.WriteLine($"  gainAuto.isValid: {analogCtrl.gainAuto.isValid}");
                Console.WriteLine($"  gain.isValid: {analogCtrl.gain.isValid}");
                
                if (analogCtrl.gain.isValid)
                {
                    double currentGain = analogCtrl.gain.read();
                    Console.WriteLine($"\nCurrent gain: {currentGain:F2} dB");
                    
                    if (analogCtrl.gain.hasMinValue)
                    {
                        double minGain = analogCtrl.gain.minValue;
                        Console.WriteLine($"Min gain: {minGain:F2} dB");
                    }
                    
                    if (analogCtrl.gain.hasMaxValue)
                    {
                        double maxGain = analogCtrl.gain.maxValue;
                        Console.WriteLine($"Max gain: {maxGain:F2} dB");
                    }
                    
                    // Turn off auto gain
                    if (analogCtrl.gainAuto.isValid)
                    {
                        Console.WriteLine("\nTurning off GainAuto...");
                        try
                        {
                            analogCtrl.gainAuto.write(0); // 0 = Off
                            Console.WriteLine("GainAuto set to Off");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not set GainAuto: {ex.Message}");
                        }
                    }
                    
                    // Test gain value
                    Console.WriteLine("\nSetting gain to 12.0 dB...");
                    try
                    {
                        analogCtrl.gain.write(12.0);
                        double appliedGain = analogCtrl.gain.read();
                        Console.WriteLine($"Applied gain: {appliedGain:F2} dB");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("\nGain control not available on this camera");
                }
                
                Console.WriteLine("\n=== Test Complete ===");
                Console.WriteLine("\nSUMMARY:");
                Console.WriteLine("  ✓ Camera connected successfully");
                Console.WriteLine("  ✓ GenICam interface accessible");
                Console.WriteLine($"  {(acqCtrl.exposureTime.isValid ? "✓" : "✗")} Exposure control available");
                Console.WriteLine($"  {(analogCtrl.gain.isValid ? "✓" : "✗")} Gain control available");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            }
            finally
            {
                // Clean up
                if (device != null)
                {
                    try
                    {
                        device.close();
                        Console.WriteLine("\nDevice closed.");
                    }
                    catch { }
                }
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
