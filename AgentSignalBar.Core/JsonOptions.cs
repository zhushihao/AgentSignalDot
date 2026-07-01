using System.Text.Json;

namespace AgentSignalBar.Core;

public static class JsonOptions
{
    public static JsonSerializerOptions CreateIndented()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = true
        };
    }
}
