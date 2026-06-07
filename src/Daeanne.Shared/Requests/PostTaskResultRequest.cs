namespace Daeanne.Shared.Requests;

public class PostTaskResultRequest
{
    /// <summary>"succeeded", "partial", or "failed"</summary>
    public string Status { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
}
