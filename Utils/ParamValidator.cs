using System.Text.Json.Nodes;

namespace InsoleApi.Utils
{
    // Clamps/snaps AI-generated insole parameters to the same legal ranges the
    // editing UI already enforces (see <Input1 min max> in design.vue / design-2.vue / step-3.vue
    // and the Select2/T()/MidsoleMap() midsole enum), so all three generation
    // methods (questionnaire Step2, questionnaire Step3, podoscopy image) are
    // guaranteed to produce the same valid output regardless of how reliably
    // the model itself follows the prompt's stated rules.
    static class ParamValidator
    {
        private static readonly double[] MidsoleValues = { 0, 0.05, 0.15, 0.25 };

        private static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));

        private static double SnapMidsole(double value)
        {
            double closest = MidsoleValues[0];
            double bestDiff = Math.Abs(value - closest);
            foreach (double candidate in MidsoleValues)
            {
                double diff = Math.Abs(value - candidate);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    closest = candidate;
                }
            }
            return closest;
        }

        private static void ClampField(JsonObject obj, string key, double min, double max)
        {
            if (obj[key] is JsonValue v && v.TryGetValue(out double d))
                obj[key] = Clamp(d, min, max);
        }

        private static void ClampFieldOrDefault(JsonObject obj, string key, double min, double max, double defaultValue)
        {
            if (obj[key] is JsonValue v && v.TryGetValue(out double d))
            {
                obj[key] = Clamp(d, min, max);
                return;
            }

            obj[key] = defaultValue;
        }

        public static string Normalize(string json)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch
            {
                return json;
            }

            if (root is not JsonObject rootObj) return json;
            if (rootObj["insole"] is not JsonObject insole) return json;

            if (insole["midsole"] is JsonValue midsoleValue && midsoleValue.TryGetValue(out double midsole))
                insole["midsole"] = SnapMidsole(midsole);

            foreach (string side in new[] { "left", "right" })
            {
                if (insole[side] is not JsonObject foot) continue;
                ClampField(foot, "arch_height", 0, 10);
                ClampField(foot, "arch_length", 0, 0.5);
                ClampField(foot, "medial_flange_height", 0, 2);
                ClampField(foot, "lateral_flange_height", 0, 2);
                ClampField(foot, "heel_cup_depth", 0, 10);
                ClampFieldOrDefault(foot, "heel_lift", 0, 4, 0);
                ClampField(foot, "rearfoot_varus_post", 0, 4);
                ClampField(foot, "rearfoot_valgus_post", 0, 4);
                ClampField(foot, "forefoot_varus_post", 0, 4);
                ClampField(foot, "forefoot_valgus_post", 0, 4);
                ClampField(foot, "medial_heel_skive", 0, 2);
                ClampField(foot, "lateral_heel_skive", 0, 2);
            }

            return rootObj.ToJsonString();
        }
    }
}
