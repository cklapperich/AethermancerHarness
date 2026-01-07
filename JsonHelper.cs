using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Shared JSON utilities for the harness.
    /// </summary>
    public static class JsonHelper
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Serialize(object obj)
        {
            return JsonConvert.SerializeObject(obj, Settings);
        }

        public static JObject Parse(string json)
        {
            return JObject.Parse(json);
        }

        public static T Value<T>(JObject obj, string key, T defaultValue = default)
        {
            var token = obj[key];
            if (token == null) return defaultValue;
            return token.Value<T>();
        }
    }
}
