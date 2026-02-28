namespace Lilia.Import.Models;

public class MathpixOptions
{
    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.mathpix.com";
    public int PollIntervalMs { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxFileSizeMb { get; set; } = 50;
}
