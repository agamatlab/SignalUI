using System;
using Grpc.Net.Client;
using Stagecontrol;
using System.Threading;
using System.Threading.Tasks;

namespace singalUI.ViewModels;

public class GrpcStageService
{
    private readonly StageControl.StageControlClient _client;
    private readonly CancellationTokenSource _cts = new();

    public event Action<PositionData>? OnPosition;

    public GrpcStageService()
    {
        var channel = GrpcChannel.ForAddress("http://localhost:50051");
        _client = new StageControl.StageControlClient(channel);
    }

    public async Task StartAsync()
    {
        _ = Task.Run(async () =>
        {
            using var call = _client.StreamPositions(new PositionStreamRequest());

            while (await call.ResponseStream.MoveNext(_cts.Token))
            {
                var pos = call.ResponseStream.Current;
                OnPosition?.Invoke(pos);
            }
        });
    }

    public void Stop() => _cts.Cancel();

    // Move using discrete steps
    public async Task<MoveSingleResponse> MoveSingleAsync(string axisId, double stepSize, int numSteps, bool waitForCompletion = true, int timeoutSeconds = 30)
    {
        var request = new MoveSingleRequest
        {
            AxisId = axisId,
            StepSize = stepSize,
            NumSteps = numSteps,
            WaitForCompletion = waitForCompletion,
            TimeoutSeconds = timeoutSeconds
        };
        return await _client.MoveSingleAsync(request);
    }

    // Move to absolute position
    public async Task<MoveToAbsoluteResponse> MoveToAbsoluteAsync(string axisId, double targetPosition, bool waitForCompletion = true, int timeoutSeconds = 30)
    {
        var request = new MoveToAbsoluteRequest
        {
            AxisId = axisId,
            TargetPosition = targetPosition,
            WaitForCompletion = waitForCompletion,
            TimeoutSeconds = timeoutSeconds
        };
        return await _client.MoveToAbsoluteAsync(request);
    }

    // Move relative to current position
    public async Task<MoveRelativeResponse> MoveRelativeAsync(string axisId, double distance, bool waitForCompletion = true, int timeoutSeconds = 30)
    {
        var request = new MoveRelativeRequest
        {
            AxisId = axisId,
            Distance = distance,
            WaitForCompletion = waitForCompletion,
            TimeoutSeconds = timeoutSeconds
        };
        return await _client.MoveRelativeAsync(request);
    }

    // Stop all movement
    public async Task<StopResponse> StopMovementAsync()
    {
        var request = new StopRequest();
        return await _client.StopMovementAsync(request);
    }

    // Move multiple steps - server handles the loop and streams progress
    public async Task<MoveMultipleResponse> MoveMultipleAsync(
        string axisId,
        double stepSize,
        int numSteps,
        bool absoluteMode = false,
        Action<MoveProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MoveMultipleRequest
        {
            Sequential = true,  // Execute steps one at a time
            TimeoutSeconds = 300  // 5 minutes for full sequence
        };

        // For absolute mode: each step moves by stepSize (cumulative)
        // For relative mode: each step moves by stepSize (same each time)
        // The distance field in MoveOperation is always a relative distance
        for (int i = 0; i < numSteps; i++)
        {
            request.Moves.Add(new MoveOperation
            {
                AxisId = axisId,
                Distance = stepSize,  // Always relative distance
                StepSize = stepSize
            });
        }

        // Call the server and stream progress
        using var call = _client.MoveMultiple(request, cancellationToken: cancellationToken);

        var response = new MoveMultipleResponse
        {
            Success = true,
            Message = "MoveMultiple completed"
        };

        int completedSteps = 0;
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var progress = call.ResponseStream.Current;
            onProgress?.Invoke(progress);
            completedSteps = progress.CurrentStep;
        }

        response.TotalSteps = completedSteps;
        return response;
    }
}

// Helper class for MoveMultipleResponse (not in proto)
public class MoveMultipleResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalSteps { get; set; }
    public double ElapsedTime { get; set; }
}
