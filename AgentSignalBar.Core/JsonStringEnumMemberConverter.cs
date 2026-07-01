using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSignalBar.Core;

public sealed class JsonStringEnumMemberConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (typeof(TEnum) == typeof(AgentSignal) && AgentSignalParser.Normalize(raw) is { } signal)
        {
            return (TEnum)(object)signal;
        }

        if (typeof(TEnum) == typeof(DisplayState) && raw is not null)
        {
            var normalized = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            foreach (var value in Enum.GetValues<DisplayState>())
            {
                if (AgentSignalParser.ToRawValue(value) == normalized)
                {
                    return (TEnum)(object)value;
                }
            }
        }

        throw new JsonException($"Unknown {typeof(TEnum).Name} value: {raw}");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (value is AgentSignal signal)
        {
            writer.WriteStringValue(AgentSignalParser.ToRawValue(signal));
            return;
        }

        if (value is DisplayState state)
        {
            writer.WriteStringValue(AgentSignalParser.ToRawValue(state));
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
