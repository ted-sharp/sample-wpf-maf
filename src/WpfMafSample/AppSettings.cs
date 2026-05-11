namespace WpfMafSample;

public sealed class AppSettings
{
    public LlmSettings Llm { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

public sealed class LlmSettings
{
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "google/gemma-4-e2b";
    public float Temperature { get; set; } = 0.2f;
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class AgentSettings
{
    public string Name { get; set; } = "GuiAgent";
    public string Instructions { get; set; } = "";
}
