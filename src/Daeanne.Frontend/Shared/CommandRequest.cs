namespace DaeanneFrontend.Shared;

public record CommandRequest(string Prompt, string TaskType = "Generic");
