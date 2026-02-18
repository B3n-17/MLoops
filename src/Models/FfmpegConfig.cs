namespace MLoops.Models;

public enum FfmpegSource
{
    NotFound,
    CommandLineArgument,
    AppLocalBundle,
    SystemPath
}

public class FfmpegConfig
{
    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
    public string? FfplayPath { get; set; }
    public string? Version { get; set; }
    public bool IsAvailable { get; set; }
    public FfmpegSource Source { get; set; }
}
