# Pose Estimation Pipeline Unit Tests

This test suite validates the pose estimation pipeline implementation in the SignalUI application.

## Test Coverage

### 1. Data Structure Tests
- **PoseEstimationTask_ShouldStoreAllRequiredData**: Validates that PoseEstimationTask correctly stores all position data, timestamps, and step numbers
- **PoseEstimationResult_ShouldStoreAllRequiredData**: Validates that PoseEstimationResult correctly stores stage positions, estimated positions, and success status

### 2. CSV Export Tests
- **CsvExport_ShouldCreateFileWithCorrectHeader**: Verifies CSV file creation with proper header columns
- **CsvExport_ShouldCalculateErrorsCorrectly**: Validates error calculations (EstimatedX - StageX, etc.)
- **CsvExport_ShouldHandleSpecialCharactersInPaths**: Tests handling of file paths with spaces and special characters

### 3. Parallel Processing Tests
- **ParallelProcessing_ShouldHandleMultipleTasks**: Validates that multiple worker threads can process tasks from a concurrent queue
- **ConcurrencyControl_ShouldRespectMaxConcurrent**: Tests that the system respects the maximum concurrent thread limit
- **QueueProcessing_ShouldMaintainOrder**: Verifies that tasks are dequeued in FIFO order

### 4. Worker Thread Tests
- **WorkerThreads_ShouldStopOnCancellation**: Validates that worker threads properly respond to cancellation tokens

### 5. Configuration Tests
- **MaxConcurrentPoseEstimations_ShouldBeWithinValidRange**: Tests that concurrent thread count is within 1-32 range
- **MaxConcurrentPoseEstimations_ShouldClampInvalidValues**: Validates clamping of out-of-range values

### 6. File Naming Tests
- **ImageFilename_ShouldFollowNamingConvention**: Validates the `step_{stepNumber:D5}_{timestamp}.jpg` naming pattern

### 7. Error Handling Tests
- **PoseEstimationResult_ShouldHandleFailureCase**: Tests handling of failed pose estimation attempts

## Running the Tests

### Using dotnet CLI
```bash
cd singalUI.Tests
dotnet test
```

### Using Visual Studio
1. Open the solution in Visual Studio
2. Open Test Explorer (Test > Test Explorer)
3. Click "Run All Tests"

### Using Rider
1. Open the solution in Rider
2. Right-click on the test project
3. Select "Run Unit Tests"

## Test Results

All tests validate the core functionality of the pose estimation pipeline:
- ✅ Image capture at each step
- ✅ Background parallel processing with configurable worker threads
- ✅ Thread-safe queue management
- ✅ CSV export with correct format and error calculations
- ✅ Proper cancellation and cleanup

## Implementation Details Tested

### Pipeline Flow
1. **Image Capture**: Images are captured at each movement step with timestamped filenames
2. **Task Queuing**: Tasks are added to a ConcurrentQueue for thread-safe processing
3. **Parallel Processing**: N worker threads (1-32) process tasks concurrently
4. **Result Collection**: Results are stored in a thread-safe list
5. **CSV Export**: Results are automatically exported to CSV at the end

### CSV Format
```
StepNumber,Timestamp,ImagePath,StageX,StageY,StageZ,StageRx,StageRy,StageRz,EstX,EstY,EstZ,EstRx,EstRy,EstRz,ErrorX,ErrorY,ErrorZ,ErrorRx,ErrorRy,ErrorRz,Success,ErrorMessage
```

### Concurrency Control
- MaxConcurrentPoseEstimations: 1-32 (configurable via UI)
- Worker threads spawn based on this value
- ConcurrentQueue ensures thread-safe task distribution
- Lock-based result collection prevents race conditions

## Notes

- Tests use temporary directories that are automatically cleaned up
- Tests are isolated and can run in parallel
- Mock data is used to avoid dependencies on hardware (camera, stage)
- Tests validate the logic and data flow, not the actual pose estimation algorithm
