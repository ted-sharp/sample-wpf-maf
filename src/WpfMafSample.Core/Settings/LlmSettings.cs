namespace WpfMafSample.Core.Settings;

public sealed class LlmSettings
{
    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "google/gemma-4-e2b";
    public float Temperature { get; set; } = 0.2f;
    public int TimeoutSeconds { get; set; } = 120;
}
