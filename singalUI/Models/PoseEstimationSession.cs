using System;
using System.Collections.Generic;

namespace singalUI.Models
{
    /// <summary>
    /// Represents a single pose estimation result with associated image
    /// </summary>
    public class PoseEstimationResult
    {
        public int StepNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string ImagePath { get; set; } = "";
        
        // Stage (commanded) positions
        public double StageX { get; set; }
        public double StageY { get; set; }
        public double StageZ { get; set; }
        public double StageRx { get; set; }
        public double StageRy { get; set; }
        public double StageRz { get; set; }
        
        // Estimated positions from DLL
        public double EstimatedX { get; set; }
        public double EstimatedY { get; set; }
        public double EstimatedZ { get; set; }
        public double EstimatedRx { get; set; }
        public double EstimatedRy { get; set; }
        public double EstimatedRz { get; set; }
        
        // Calculated errors
        public double ErrorX { get; set; }
        public double ErrorY { get; set; }
        public double ErrorZ { get; set; }
        public double ErrorRx { get; set; }
        public double ErrorRy { get; set; }
        public double ErrorRz { get; set; }
        
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
    
    /// <summary>
    /// Represents a complete pose estimation session
    /// </summary>
    public class PoseEstimationSession
    {
        public string SessionName { get; set; } = "";
        public string CsvPath { get; set; } = "";
        public DateTime SessionDate { get; set; }
        public int TotalResults { get; set; }
        public int SuccessfulResults { get; set; }
        public int FailedResults { get; set; }
        public List<PoseEstimationResult> Results { get; set; } = new();
        
        /// <summary>
        /// Load session from CSV file
        /// </summary>
        public static PoseEstimationSession LoadFromCsv(string csvPath)
        {
            var session = new PoseEstimationSession
            {
                CsvPath = csvPath,
                SessionName = System.IO.Path.GetFileNameWithoutExtension(csvPath),
                SessionDate = System.IO.File.GetLastWriteTime(csvPath)
            };
            
            var lines = System.IO.File.ReadAllLines(csvPath);
            
            // Skip header
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                
                if (parts.Length < 22) continue;
                
                try
                {
                    var result = new PoseEstimationResult
                    {
                        StepNumber = int.Parse(parts[0]),
                        Timestamp = DateTime.Parse(parts[1]),
                        ImagePath = parts[2].Trim('"'),
                        StageX = double.Parse(parts[3]),
                        StageY = double.Parse(parts[4]),
                        StageZ = double.Parse(parts[5]),
                        StageRx = double.Parse(parts[6]),
                        StageRy = double.Parse(parts[7]),
                        StageRz = double.Parse(parts[8]),
                        EstimatedX = double.Parse(parts[9]),
                        EstimatedY = double.Parse(parts[10]),
                        EstimatedZ = double.Parse(parts[11]),
                        EstimatedRx = double.Parse(parts[12]),
                        EstimatedRy = double.Parse(parts[13]),
                        EstimatedRz = double.Parse(parts[14]),
                        ErrorX = double.Parse(parts[15]),
                        ErrorY = double.Parse(parts[16]),
                        ErrorZ = double.Parse(parts[17]),
                        ErrorRx = double.Parse(parts[18]),
                        ErrorRy = double.Parse(parts[19]),
                        ErrorRz = double.Parse(parts[20]),
                        Success = parts[21].Trim() == "True",
                        ErrorMessage = parts.Length > 22 ? parts[22].Trim('"') : ""
                    };
                    
                    session.Results.Add(result);
                    session.TotalResults++;
                    
                    if (result.Success)
                        session.SuccessfulResults++;
                    else
                        session.FailedResults++;
                }
                catch
                {
                    // Skip invalid lines
                }
            }
            
            return session;
        }
    }
}
