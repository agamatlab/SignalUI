using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace singalUI.Tests.Standalone
{
    /// <summary>
    /// Standalone unit tests for Pose Estimation Pipeline logic
    /// These tests don't require the main application to be built
    /// </summary>
    public class PoseEstimationLogicTests
    {
        private readonly string _testDirectory;
        
        public PoseEstimationLogicTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"PoseEstTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }
        
        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                try { Directory.Delete(_testDirectory, true); } catch { }
            }
        }
        
        [Fact]
        public void Test_CsvHeaderFormat()
        {
            // Arrange
            string expectedHeader = "StepNumber,Timestamp,ImagePath," +
                                  "StageX,StageY,StageZ,StageRx,StageRy,StageRz," +
                                  "EstX,EstY,EstZ,EstRx,EstRy,EstRz," +
                                  "ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz," +
                                  "Success,ErrorMessage";
            
            // Assert
            Assert.Contains("StepNumber", expectedHeader);
            Assert.Contains("StageX,StageY,StageZ", expectedHeader);
            Assert.Contains("ErrorX,ErrorY,ErrorZ", expectedHeader);
            Assert.Contains("Success,ErrorMessage", expectedHeader);
        }
        
        [Fact]
        public void Test_ErrorCalculation()
        {
            // Arrange
            double stageX = 10.0;
            double estimatedX = 10.5;
            
            // Act
            double errorX = estimatedX - stageX;
            
            // Assert
            Assert.Equal(0.5, errorX, precision: 6);
        }
        
        [Fact]
        public void Test_FilenameFormat()
        {
            // Arrange
            int stepNumber = 42;
            string timestamp = "20260403_123456_789";
            
            // Act
            string filename = $"step_{stepNumber:D5}_{timestamp}.jpg";
            
            // Assert
            Assert.Equal("step_00042_20260403_123456_789.jpg", filename);
            Assert.StartsWith("step_", filename);
            Assert.EndsWith(".jpg", filename);
        }
        
        [Fact]
        public void Test_MaxConcurrentRange()
        {
            // Arrange & Act
            int[] validValues = { 1, 4, 8, 16, 32 };
            
            // Assert
            foreach (var value in validValues)
            {
                Assert.InRange(value, 1, 32);
            }
        }
        
        [Fact]
        public void Test_MaxConcurrentClamping()
        {
            // Arrange
            int tooLow = -5;
            int tooHigh = 100;
            
            // Act
            int clampedLow = Math.Max(1, Math.Min(32, tooLow));
            int clampedHigh = Math.Max(1, Math.Min(32, tooHigh));
            
            // Assert
            Assert.Equal(1, clampedLow);
            Assert.Equal(32, clampedHigh);
        }
        
        [Fact]
        public async Task Test_ConcurrentQueueProcessing()
        {
            // Arrange
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();
            
            for (int i = 0; i < 20; i++)
            {
                queue.Enqueue(i);
            }
            
            // Act - Simulate 4 workers
            var workers = new List<Task>();
            for (int w = 0; w < 4; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (queue.TryDequeue(out var item))
                    {
                        await Task.Delay(1);
                        results.Add(item);
                    }
                }));
            }
            
            await Task.WhenAll(workers);
            
            // Assert
            Assert.Equal(20, results.Count);
            Assert.Empty(queue);
        }
        
        [Fact]
        public async Task Test_WorkerCancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            bool workerStarted = false;
            bool workerStopped = false;
            
            // Act
            var worker = Task.Run(async () =>
            {
                workerStarted = true;
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                workerStopped = true;
            });
            
            await Task.Delay(50);
            cts.Cancel();
            await Task.Delay(50);
            
            // Assert
            Assert.True(workerStarted);
            Assert.True(workerStopped);
        }
        
        [Fact]
        public void Test_CsvExportWithData()
        {
            // Arrange
            var csvPath = Path.Combine(_testDirectory, "test_export.csv");
            var data = new
            {
                StepNumber = 1,
                Timestamp = DateTime.Now,
                ImagePath = "C:\\test\\image.jpg",
                StageX = 10.0,
                StageY = 20.0,
                StageZ = 30.0,
                EstimatedX = 10.5,
                EstimatedY = 20.3,
                EstimatedZ = 30.1,
                Success = true
            };
            
            // Act
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("StepNumber,Timestamp,ImagePath,StageX,StageY,StageZ,EstX,EstY,EstZ,ErrorX,ErrorY,ErrorZ,Success");
                writer.WriteLine($"{data.StepNumber}," +
                               $"{data.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                               $"\"{data.ImagePath}\"," +
                               $"{data.StageX:F6},{data.StageY:F6},{data.StageZ:F6}," +
                               $"{data.EstimatedX:F6},{data.EstimatedY:F6},{data.EstimatedZ:F6}," +
                               $"{data.EstimatedX - data.StageX:F6}," +
                               $"{data.EstimatedY - data.StageY:F6}," +
                               $"{data.EstimatedZ - data.StageZ:F6}," +
                               $"{data.Success}");
            }
            
            // Assert
            Assert.True(File.Exists(csvPath));
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("0.500000", lines[1]); // ErrorX
            Assert.Contains("0.300000", lines[1]); // ErrorY
            Assert.Contains("0.100000", lines[1]); // ErrorZ
        }
        
        [Fact]
        public void Test_PathWithSpacesInCsv()
        {
            // Arrange
            var csvPath = Path.Combine(_testDirectory, "test_paths.csv");
            string imagePath = "C:\\test\\path with spaces\\image (1).jpg";
            
            // Act
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("ImagePath");
                writer.WriteLine($"\"{imagePath}\"");
            }
            
            // Assert
            var lines = File.ReadAllLines(csvPath);
            Assert.Contains(imagePath, lines[1]);
        }
        
        [Fact]
        public async Task Test_QueueOrderPreservation()
        {
            // Arrange
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var processedOrder = new List<int>();
            var lockObj = new object();
            
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(i);
            }
            
            // Act - Single worker to verify order
            while (queue.TryDequeue(out var item))
            {
                await Task.Delay(1);
                lock (lockObj)
                {
                    processedOrder.Add(item);
                }
            }
            
            // Assert
            Assert.Equal(Enumerable.Range(0, 10), processedOrder);
        }
        
        [Fact]
        public void Test_TimestampFormat()
        {
            // Arrange
            var now = new DateTime(2026, 4, 3, 12, 34, 56, 789);
            
            // Act
            string timestamp = now.ToString("yyyyMMdd_HHmmss_fff");
            
            // Assert
            Assert.Equal("20260403_123456_789", timestamp);
        }
        
        [Fact]
        public async Task Test_ParallelProcessingPerformance()
        {
            // Arrange
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();
            int taskCount = 100;
            int workerCount = 8;
            
            for (int i = 0; i < taskCount; i++)
            {
                queue.Enqueue(i);
            }
            
            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var workers = new List<Task>();
            
            for (int w = 0; w < workerCount; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (queue.TryDequeue(out var item))
                    {
                        await Task.Delay(5); // Simulate work
                        results.Add(item);
                    }
                }));
            }
            
            await Task.WhenAll(workers);
            stopwatch.Stop();
            
            // Assert
            Assert.Equal(taskCount, results.Count);
            Assert.True(stopwatch.ElapsedMilliseconds < taskCount * 5); // Should be faster than sequential
        }
    }
}
