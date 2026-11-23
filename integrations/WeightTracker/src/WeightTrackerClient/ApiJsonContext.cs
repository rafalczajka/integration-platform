using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integrations.WeightTracker.WeightTrackerClient;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WeightsSummaryResponse))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
