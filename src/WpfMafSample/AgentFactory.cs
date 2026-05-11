using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace WpfMafSample;

internal static class AgentFactory
{
    public static IChatClient CreateChatClient(LlmSettings settings)
    {
        var apiKey = String.IsNullOrWhiteSpace(settings.ApiKey) ? "lm-studio" : settings.ApiKey;

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(settings.Endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };

        var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

        return openAIClient
            .GetChatClient(settings.Model)
            .AsIChatClient();
    }
}
