namespace Daeanne.Shared.Requests;

public class PatchSmsStatusRequest
{
    public string  Status { get; set; } = string.Empty;
    public string? Error  { get; set; }
}
