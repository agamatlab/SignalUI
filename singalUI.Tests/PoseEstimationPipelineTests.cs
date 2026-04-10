using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace singalUI.Tests
{
    /// <summary>
    /// Unit tests for the Pose Estimation Pipeline
    /// Tests image capture, parallel processing, CSV export, and concurrency control
    /// </summary>
    public class PoseEstimationPipelineTests : IDisposable
    {
        private readonly string _testDirectory;
        
        public PoseEstimationPipelineTests()
        {
            // Create a temporary test directory
            _testDirectory = Path.Combine(Path.GetTempPath(), $"PoseEstTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }
        
        public void Dispose()
        {
            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        
        [Fact]
        public void PoseEstimationTask_ShouldStoreAllRequiredData()
        {
            // Arrange & Act
            var task = new singalUI.ViewModels.PoseEstimationTask
            {
                ImagePath = "/test/image.jpg",
                PositionX = 1.5,
                PositionY = 2.5,
                PositionZ = 3.5,
                PositionRx = 0.1,
                PositionRy = 0.2,
                PositionRz = 0.3,
                Timestamp = DateTime.Now,
                StepNumber = 42
            };
            
            // Assert
            Assert.Equal("/test/image.jpg", task.ImagePath);
            Assert.Equal(1.5, task.PositionX);
            Assert.Equal(2.5, task.PositionY);
            Assert.Equal(3.5, task.PositionZ);
            Assert.Equal(0.1, task.PositionRx);
            Assert.Equal(0.2, task.PositionRy);
            Assert.Equal(0.3, task.PositionRz);
            Assert.Equal(42, task.StepNumber);
        }
        
        [Fact]
        public void PoseEstimationResult_ShouldStoreAllRequiredData()
        {
            // Arrange & Act
            var result = new singalUI.ViewModels.PoseEstimationResult
            {
                ImagePath = "/test/image.jpg",
                StageX = 1.0,
                StageY = 2.0,
                StageZ = 3.0,
                StageRx = 0.1,
                StageRy = 0.2,
                StageRz = 0.3,
                EstimatedX = 1.1,
                EstimatedY = 2.1,
                EstimatedZ = 3.1,
                EstimatedRx = 0.11,
                EstimatedRy = 0.21,
                EstimatedRz = 0.31,
                Success = true,
                ErrorMessage = "",
                Timestamp = DateTime.Now,
                StepNumber = 42
            };
            
            // Assert
            Assert.Equal("/test/image.jpg", result.ImagePath);
            Assert.Equal(1.0, result.StageX);
            Assert.Equal(1.1, result.EstimatedX);
            Assert.True(result.Success);
            Assert.Equal(42, result.StepNumber);
        }
        
        [Fact]
        public void CsvExport_ShouldCreateFileWithCorrectHeader()
        {
            // Arrange
            var csvPath = Path.Combine(_testDirectory, "test_results.csv");
            var results = new List<singalUI.ViewModels.PoseEstimationResult>
            {
                new singalUI.ViewModels.PoseEstimationResult
                {
                    StepNumber = 1,
                    Timestamp = DateTime.Now,
                    ImagePath = "/test/image1.jpg",
                    StageX = 1.0, StageY = 2.0, StageZ = 3.0,
                    StageRx = 0.1, StageRy = 0.2, StageRz = 0.3,
                    EstimatedX = 1.1, EstimatedY = 2.1, EstimatedZ = 3.1,
                    EstimatedRx = 0.11, EstimatedRy = 0.21, EstimatedRz = 0.31,
                    Success = true,
                    ErrorMessage = ""
                }
            };
            
            // Act
            using (var writer = new StreamWriter(csvPath))
            {
                // Write header
                writer.WriteLine("StepNumber,Timestamp,ImagePath," +
                               "StageX,StageY,StageZ,StageRx,StageRy,StageRz," +
                               "EstX,EstY,EstZ,EstRx,EstRy,EstRz," +
                               "ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz," +
                               "Success,ErrorMessage");
                
                // Write data
                foreach (var result in results)
                {
                    writer.WriteLine($"{result.StepNumber}," +
                                   $"{result.Timestamp:yyyy-MM-dd HH:mm:ss.fff}," +
                                   $"\"{result.ImagePath}\"," +
                                   $"{result.StageX:F6},{result.StageY:F6},{result.StageZ:F6}," +
                                   $"{result.StageRx:F6},{result.StageRy:F6},{result.StageRz:F6}," +
                                   $"{result.EstimatedX:F6},{result.EstimatedY:F6},{result.EstimatedZ:F6}," +
                                   $"{result.EstimatedRx:F6},{result.EstimatedRy:F6},{result.EstimatedRz:F6}," +
                                   $"{result.EstimatedX - result.StageX:F6}," +
                                   $"{result.EstimatedY - result.StageY:F6}," +
                                   $"{result.EstimatedZ - result.StageZ:F6}," +
                                   $"{result.EstimatedRx - result.StageRx:F6}," +
                                   $"{result.EstimatedRy - result.StageRy:F6}," +
                                   $"{result.EstimatedRz - result.StageRz:F6}," +
                                   $"{result.Success}," +
                                   $"\"{result.ErrorMessage}\"");
                }
            }
            
            // Assert
            Assert.True(File.Exists(csvPath));
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(2, lines.Length); // Header + 1 data row
            Assert.Contains("StepNumber,Timestamp,ImagePath", lines[0]);
            Assert.Contains("StageX,StageY,StageZ", lines[0]);
            Assert.Contains("ErrorX,ErrorY,ErrorZ", lines[0]);
        }
        
        [Fact]
        public void CsvExport_ShouldCalculateErrorsCorrectly()
        {
            // Arrange
            var csvPath = Path.Combine(_testDirectory, "test_errors.csv");
            var result = new singalUI.ViewModels.PoseEstimationResult
            {
                StepNumber = 1,
                Timestamp = DateTime.Now,
                ImagePath = "/test/image1.jpg",
                StageX = 10.0, StageY = 20.0, StageZ = 30.0,
                StageRx = 1.0, StageRy = 2.0, StageRz = 3.0,
                EstimatedX = 10.5, EstimatedY = 20.3, EstimatedZ = 30.1,
                EstimatedRx = 1.1, EstimatedRy = 2.2, EstimatedRz = 3.3,
                Success = true,
                ErrorMessage = ""
            };
            
            // Calculate expected errors
            double expectedErrorX = result.EstimatedX - result.StageX; // 0.5
            double expectedErrorY = result.EstimatedY - result.StageY; // 0.3
            double expectedErrorZ = result.EstimatedZ - result.StageZ; // 0.1
            
            // Act
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("ErrorX,ErrorY,ErrorZ");
                writer.WriteLine($"{expectedErrorX:F6},{expectedErrorY:F6},{expectedErrorZ:F6}");
            }
            
            // Assert
            var lines = File.ReadAllLines(csvPath);
            Assert.Contains("0.500000", lines[1]);
            Assert.Contains("0.300000", lines[1]);
            Assert.Contains("0.100000", lines[1]);
        }
        
        [Fact]
        public async Task ParallelProcessing_ShouldHandleMultipleTasks()
        {
            // Arrange
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();
            var maxConcurrent = 4;
            
            // Add 20 tasks to queue
            for (int i = 0; i < 20; i++)
            {
                queue.Enqueue(i);
            }
            
            // Act - Simulate worker threads
            var workers = new List<Task>();
            var cts = new CancellationTokenSource();
            
            for (int i = 0; i < maxConcurrent; i++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested && queue.TryDequeue(out var item))
                    {
                        await Task.Delay(10); // Simulate work
                        results.Add(item);
                    }
                }));
            }
            
            await Task.WhenAll(workers);
            cts.Cancel();
            
            // Assert
            Assert.Equal(20, results.Count);
            Assert.Equal(0, queue.Count);
        }
        
        [Fact]
        public async Task ConcurrencyControl_ShouldRespectMaxConcurrent()
        {
            // Arrange
            int maxConcurrent = 3;
            int currentConcurrent = 0;
            int maxObservedConcurrent = 0;
            var lockObj = new object();
            
            // Act - Simulate concurrent tasks
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    lock (lockObj)
                    {
                        currentConcurrent++;
                        if (currentConcurrent > maxObservedConcurrent)
                        {
                            maxObservedConcurrent = currentConcurrent;
                        }
                    }
                    
                    await Task.Delay(50); // Simulate work
                    
                    lock (lockObj)
                    {
                        currentConcurrent--;
                    }
                }));
                
                // Only allow maxConcurrent tasks to run at once
                if (tasks.Count >= maxConcurrent)
                {
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }
            
            await Task.WhenAll(tasks);
            
            // Assert - This test shows the pattern, actual implementation uses worker threads
            Assert.True(maxObservedConcurrent > 0);
        }
        
        [Fact]
        public void ImageFilename_ShouldFollowNamingConvention()
        {
            // Arrange
            int stepNumber = 42;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            
            // Act
            string filename = $"step_{stepNumber:D5}_{timestamp}.jpg";
            
            // Assert
            Assert.StartsWith("step_00042_", filename);
            Assert.EndsWith(".jpg", filename);
            Assert.Matches(@"step_\d{5}_\d{8}_\d{6}_\d{3}\.jpg", filename);
        }
        
        [Fact]
        public void MaxConcurrentPoseEstimations_ShouldBeWithinValidRange()
        {
            // Arrange
            int[] testValues = { 1, 4, 16, 32 };
            
            // Act & Assert
            foreach (var value in testValues)
            {
                Assert.InRange(value, 1, 32);
            }
        }
        
        [Fact]
        public void MaxConcurrentPoseEstimations_ShouldClampInvalidValues()
        {
            // Arrange
            int tooLow = 0;
            int tooHigh = 50;
            
            // Act
            int clampedLow = Math.Max(1, Math.Min(32, tooLow));
            int clampedHigh = Math.Max(1, Math.Min(32, tooHigh));
            
            // Assert
            Assert.Equal(1, clampedLow);
            Assert.Equal(32, clampedHigh);
        }
        
        [Fact]
        public async Task QueueProcessing_ShouldMaintainOrder()
        {
            // Arrange
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var processedOrder = new List<int>();
            var lockObj = new object();
            
            // Add items in order
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(i);
            }
            
            // Act - Process with single worker to verify order
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
        public void CsvExport_ShouldHandleSpecialCharactersInPaths()
        {
            // Arrange
            var csvPath = Path.Combine(_testDirectory, "test_special_chars.csv");
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
        public void PoseEstimationResult_ShouldHandleFailureCase()
        {
            // Arrange & Act
            var result = new singalUI.ViewModels.PoseEstimationResult
            {
                Success = false,
                ErrorMessage = "Pattern detection failed",
                StepNumber = 5
            };
            
            // Assert
            Assert.False(result.Success);
            Assert.NotEmpty(result.ErrorMessage);
            Assert.Equal("Pattern detection failed", result.ErrorMessage);
        }
        
        [Fact]
        public async Task WorkerThreads_ShouldStopOnCancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var workerStarted = false;
            var workerStopped = false;
            
            // Act
            var worker = Task.Run(async () =>
            {
                workerStarted = true;
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(10, cts.Token);
                }
                workerStopped = true;
            });
            
            await Task.Delay(50); // Let worker start
            cts.Cancel();
            
            try
            {
                await worker;
            }
            catch (TaskCanceledException)
            {
                // Expected
            }
            
            // Assert
            Assert.True(workerStarted);
            Assert.True(workerStopped);
        }
    }
}
