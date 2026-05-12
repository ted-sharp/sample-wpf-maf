using WpfMafSample.Core.Settings;

namespace WpfMafSample;

public sealed class AppSettings
{
    public LlmSettings Llm { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}
