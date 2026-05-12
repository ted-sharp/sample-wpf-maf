using SampleWpfMaf.Core.Settings;

namespace SampleWpfMaf;

public sealed class AppSettings
{
    public LlmSettings Llm { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}
