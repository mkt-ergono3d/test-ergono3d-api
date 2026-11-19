using InsoleApi.Controllers;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace InsoleApi.Utils
{
    class AI
    {
        private static readonly string apiKey = Environment.GetEnvironmentVariable("INSOLE_AI_API_KEY") ?? throw new InvalidOperationException("Set INSOLE_AI_API_KEY for this test environment.");
        private static readonly string apiUrl = "https://api.anthropic.com/v1/messages";

        //static async Task Main(string[] args)
        //{
        //    var userInput = @"{
        //    ""daily_routine"": ""Daily Life"",
        //    ""sub_category"": ""Walking"",
        //    ""arch_type"": ""Normal Arch"",
        //    ""wear_pattern"": ""Lateral"",
        //    ""discomfort_area"": [""Heel""],
        //    ""Usage_scenario"": [""Standing"", ""Walking""],
        //    ""priority"": ""Comfort"",
        //    ""description"": """",
        //    ""length"": 23
        //}";

        //    var result = await GetInsoleRecommendation(userInput);
        //    Console.WriteLine("Claude 返回：\n");
        //    Console.WriteLine(result);
        //}

        public static async Task<string> GetInsoleRecommendation(ToolController.Step1 step1, ToolController.Step2 step2)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);


                var userInput = $@"{{
                    ""daily_routine"": ""{step2.Daily_routine}"",
                    ""sub_category"": ""{step2.Sub_category}"",
                    ""arch_type"": ""{step2.Arch_type}"",
                    ""wear_pattern"": ""{step2.Wear_pattern}"",
                    ""discomfort_area"": {JsonSerializer.Serialize(step2.Discomfort_area)},
                    ""Usage_scenario"": {JsonSerializer.Serialize(step2.Usage_scenario)},
                    ""priority"": ""{step2.Priority}"",
                    ""shoe_type"": ""{step2.Shoe_type}"",
                    ""description"": ""{step2.Personal_description}"",
                    ""length"": {step1.Size_}
                }}";

                var requestBody = new
                {
                    model = "claude-sonnet-5",
                    cache_control = new { type = "ephemeral" },
                    max_tokens = 2000,
                    system = "You are world-class sports medicine physician with expertise in running biomechanics and custom orthotic design to determine the insole parameters. Parameters you pick should put comfort as priority.  Please Pick minimum. Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%) \n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios for post.\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please only pick the smallest needed parameter (1 or 2)\n",
                    messages = new object[]
                    {
                        new {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = "<examples>\n<example>\n<example_description>\nParameter Definitions\nBasic Parameters\n\npatient_id: Patient identifier\nlength: Insole length (insole size)\nwidth: Insole width (0 = use default)\nthickness: Insole thickness (millimeters)\nmidsole: Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%)\n\nCorrection Parameters (Left/Right foot separately)\nArch Support:\n\narch_height: Height of arch support dome (millimeters) - Example: 6mm creates moderate arch support\narch_length: Length of arch support as ratio of total insole length start from midfoot arch point - Example: 0.25 = 25% of insole length\n\nPost Corrections (Wedging):\n\nrearfoot_varus_post: Medial heel elevation/wedge (raises inner rearfoot) - millimeters\nrearfoot_valgus_post: Lateral heel elevation/wedge (raises outer rearfoot) - millimeters\nforefoot_varus_post: Medial forefoot elevation/wedge (raises inner forefoot) - millimeters\nforefoot_valgus_post: Lateral forefoot elevation/wedge (raises outer forefoot) - Example: 2mm lateral forefoot elevation\n\nHeel Modifications:\n\nmedial_heel_skive: Medial heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nlateral_heel_skive: Lateral heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nheel_cup_depth: Depth of heel cup/cradle (millimeters) - Example: 6mm deep heel cradle\n\n\nFlange Extensions:\n\nmedial_flange_height: Height of medial (inner) side wall extension - millimeters\nlateral_flange_height: Height of lateral (outer) side wall extension - Example: 3mm outer wall height\n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please pick the smallest needed parameter\n\nOnly give me the json output, directly start with {\n</example_description>\n<USER_INPUT>\n{\n  \"category\": \"Sports\",\n  \"sub_category\": \"Running\",\n  \"arch_type\": \"High Arch\",\n  \"wear_pattern\": \"Lateral\",\n  \"discomfort_area\": [\n    \"Heel\",\n    \"Ball of foot\"\n  ],\n  \"activity_scenario\": [\n    \"Long-distance running\",\n    \"Sprint training\"\n  ],\n  \"priority\": \"Comfort\",\n  \"description\": \"Experiencing sharp pain in right heel after 5km runs, and numbness in toes during sprints. Looking for insole recommendations to alleviate these issues and improve overall running performance.\"\n}\n</USER_INPUT>\n<SPECIALIST_TYPE>\northopedic surgeon specializing in foot and ankle biomechanics\n</SPECIALIST_TYPE>\n<ideal_output>\n{\n  \"patient_id\": \"P002\",\n  \"insole\": {\n    \"length\": 44,\n    \"width\": 2,\n    \"thickness\": 3,\n     \"midsole\": 0.15\n    \"left\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 3,\n      \"medial_heel_skive\": 0,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 3\n    },\n    \"right\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 3,\n      \"medial_heel_skive\": 0,\n      \"post_length_percent\": 0.3,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 3\n    }\n  }\n}\n\n</ideal_output>\n</example>\n</examples>\n\n" },
                                new { type = "text", text = userInput }
                            }
                        }
                    }
                };

                try
                {
                    var json = JsonSerializer.Serialize(requestBody);

                    //Console.WriteLine(json);

                    var content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await client.PostAsync(apiUrl, content);

                    var responseString = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        //Console.WriteLine($"HTTP {(int)response.StatusCode}");
                        //Console.WriteLine(responseString);

                        throw new Exception(responseString);
                    }

                    return Extract(responseString);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("========== AI ERROR ==========");
                    Console.WriteLine(ex);
                    Console.WriteLine("==============================");

                    throw;
                }
            }
        }

        public static async Task<string> GetInsoleRecommendation(ToolController.Step1 step1, ToolController.Step3 step3)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);


                var userInput = $@"{{
                    ""daily_routine"": ""{step3.Daily_routine}"",
                    ""sub_category"": ""{step3.Sub_category}"",
                    ""foot_arch_assessment"": ""{step3.Foot_arch_assessment}"",
                    ""loading_wear_pattern"": ""{step3.Loading_wear_pattern}"",
                    ""concern_area"": {JsonSerializer.Serialize(step3.Concern_area)},
                    ""usage_scenario"": {JsonSerializer.Serialize(step3.Usage_scenario)},
                    ""prescription_goal"": ""{step3.Prescription_goal}"",
                    ""shoe_type"": ""{step3.Shoe_type}"",
                    ""description"": ""{step3.Personal_description}"",
                    ""length"": {step1.Size_}
                }}";

                var requestBody = new
                {
                    model = "claude-sonnet-5",
                    cache_control = new { type = "ephemeral" },
                    max_tokens = 2000,
                    system = "You are world-class sports medicine physician with expertise in running biomechanics and custom orthotic design to determine the insole parameters. Parameters you pick should put comfort as priority.  Please Pick minimum. Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%) \n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios for post.\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please only pick the smallest needed parameter (1 or 2)\n",
                    messages = new object[]
                    {
                        new {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = "<examples>\n<example>\n<example_description>\nParameter Definitions\nBasic Parameters\n\npatient_id: Patient identifier\nlength: Insole length (insole size)\nwidth: Insole width (0 = use default)\nthickness: Insole thickness (millimeters)\nmidsole: Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%)\n\nCorrection Parameters (Left/Right foot separately)\nArch Support:\n\narch_height: Height of arch support dome (millimeters) - Example: 6mm creates moderate arch support\narch_length: Length of arch support as ratio of total insole length start from midfoot arch point - Example: 0.25 = 25% of insole length\n\nPost Corrections (Wedging):\n\nrearfoot_varus_post: Medial heel elevation/wedge (raises inner rearfoot) - millimeters\nrearfoot_valgus_post: Lateral heel elevation/wedge (raises outer rearfoot) - millimeters\nforefoot_varus_post: Medial forefoot elevation/wedge (raises inner forefoot) - millimeters\nforefoot_valgus_post: Lateral forefoot elevation/wedge (raises outer forefoot) - Example: 2mm lateral forefoot elevation\n\nHeel Modifications:\n\nmedial_heel_skive: Medial heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nlateral_heel_skive: Lateral heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nheel_cup_depth: Depth of heel cup/cradle (millimeters) - Example: 6mm deep heel cradle\n\n\nFlange Extensions:\n\nmedial_flange_height: Height of medial (inner) side wall extension - millimeters\nlateral_flange_height: Height of lateral (outer) side wall extension - Example: 3mm outer wall height\n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please pick the smallest needed parameter\n\nOnly give me the json output, directly start with {\n</example_description>\n<USER_INPUT>\n{\n  \"category\": \"Sports\",\n  \"sub_category\": \"Running\",\n  \"arch_type\": \"High Arch\",\n  \"wear_pattern\": \"Lateral\",\n  \"discomfort_area\": [\n    \"Heel\",\n    \"Ball of foot\"\n  ],\n  \"activity_scenario\": [\n    \"Long-distance running\",\n    \"Sprint training\"\n  ],\n  \"priority\": \"Comfort\",\n  \"description\": \"Experiencing sharp pain in right heel after 5km runs, and numbness in toes during sprints. Looking for insole recommendations to alleviate these issues and improve overall running performance.\"\n}\n</USER_INPUT>\n<SPECIALIST_TYPE>\northopedic surgeon specializing in foot and ankle biomechanics\n</SPECIALIST_TYPE>\n<ideal_output>\n{\n  \"patient_id\": \"P002\",\n  \"insole\": {\n    \"length\": 44,\n    \"width\": 2,\n    \"thickness\": 3,\n     \"midsole\": 0.15\n    \"left\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 3,\n      \"medial_heel_skive\": 0,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 3\n    },\n    \"right\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 3,\n      \"medial_heel_skive\": 0,\n      \"post_length_percent\": 0.3,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 3\n    }\n  }\n}\n\n</ideal_output>\n</example>\n</examples>\n\n" },
                                new { type = "text", text = userInput }
                            }
                        }
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Status Code{(int)response.StatusCode}-{response.StatusCode}");
                //Console.WriteLine(responseString);
                return Extract(responseString);
            }
        }


        public static async Task<string> AutoUpdateParam(string oldParams, string advice)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                //""""""


                var userInput = $@"";

                var requestBody = new
                {
                    model = "claude-sonnet-5",
                    cache_control = new { type = "ephemeral" },
                    max_tokens = 2000,
                    system = "You are world-class sports medicine physician with expertise in running biomechanics and custom orthotic design to determine the insole parameters. Parameters you pick should put comfort as priority.  Please Pick minimum. Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%) \n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios for post.\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please only pick the smallest needed parameter (1 or 2)\n",
                    messages = new object[]
                    {
                        new {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = $"{oldParams}\nFeedback on the above parameter is {advice} Please update the parameters and return the json structured output only", }
                            }
                        }
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Status Code{(int)response.StatusCode}-{response.StatusCode}");
                //Console.WriteLine(responseString);
                return Extract(responseString);
            }
        }

        public static async Task<string> GetInsoleRecommendationFromImage(ToolController.Step1 step1, string imageBase64, string mediaType)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var userTextPrompt = $@"This is a podoscopy (plantar pressure scan) image.
The patient's EU shoe size is {step1.Size_} — use this as the definitive reference for insole length, do NOT estimate size from the image.
Analyze the image for: arch type (flat/normal/high based on contact area pattern), heel vs forefoot pressure balance, medial vs lateral loading distribution, and any notable pressure concentrations.
Patient info: gender {step1.Usertype_}, age {(string.IsNullOrEmpty(step1.Age_) ? "unknown" : step1.Age_)}, weight {(string.IsNullOrEmpty(step1.Weight_) ? "unknown" : step1.Weight_)} kg.
Generate optimal custom insole parameters based on what you observe in the image, strictly within the allowed value ranges given in the system rules above — in particular, midsole MUST be exactly 0, 0.05, 0.15, or 0.25, never any other number. Only output the JSON, starting with {{.";

                var requestBody = new
                {
                    model = "claude-sonnet-5",
                    max_tokens = 2000,
                    system = "You are world-class sports medicine physician with expertise in running biomechanics and custom orthotic design to determine the insole parameters. Parameters you pick should put comfort as priority.  Please Pick minimum. Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%) \n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios for post.\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please only pick the smallest needed parameter (1 or 2)\n\nAllowed value ranges — never output a value outside these, for any generation method:\nmidsole: MUST be exactly one of 0, 0.05, 0.15, or 0.25 — no other number is valid, even if it seems reasonable\narch_height: 0 to 10 millimeters\narch_length: 0 to 0.5 (ratio)\nmedial_flange_height: 0 to 2 millimeters\nlateral_flange_height: 0 to 2 millimeters\nheel_cup_depth: 0 to 10 millimeters\nrearfoot_varus_post: 0 to 4 millimeters\nrearfoot_valgus_post: 0 to 4 millimeters\nforefoot_varus_post: 0 to 4 millimeters\nforefoot_valgus_post: 0 to 4 millimeters\nmedial_heel_skive: 0 to 2 millimeters\nlateral_heel_skive: 0 to 2 millimeters\n",
                    messages = new object[]
                    {
                        new {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = "<examples>\n<example>\n<example_description>\nParameter Definitions\nBasic Parameters\n\npatient_id: Patient identifier\nlength: Insole length (insole size)\nwidth: Insole width (0 = use default)\nthickness: Insole thickness (millimeters)\nmidsole: Midsole fabric indentation/recess from insole bottom (measured as ratio/percentage 0.05 - 20%，0.15 - 30%， 0.25 - 40%)\n\nCorrection Parameters (Left/Right foot separately)\nArch Support:\n\narch_height: Height of arch support dome (millimeters) - Example: 6mm creates moderate arch support\narch_length: Length of arch support as ratio of total insole length start from midfoot arch point - Example: 0.25 = 25% of insole length\n\nPost Corrections (Wedging):\n\nrearfoot_varus_post: Medial heel elevation/wedge (raises inner rearfoot) - millimeters\nrearfoot_valgus_post: Lateral heel elevation/wedge (raises outer rearfoot) - millimeters\nforefoot_varus_post: Medial forefoot elevation/wedge (raises inner forefoot) - millimeters\nforefoot_valgus_post: Lateral forefoot elevation/wedge (raises outer forefoot) - Example: 2mm lateral forefoot elevation\n\nHeel Modifications:\n\nmedial_heel_skive: Medial heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nlateral_heel_skive: Lateral heel skive is a slope on the superior medial heel of an orthotic frame  - millimeters\nheel_cup_depth: Depth of heel cup/cradle (millimeters) - Example: 6mm deep heel cradle\n\n\nFlange Extensions:\n\nmedial_flange_height: Height of medial (inner) side wall extension - millimeters\nlateral_flange_height: Height of lateral (outer) side wall extension - Example: 3mm outer wall height\n\nKey Rules:\n\nAll measurements in millimeters unless specified as ratios\n0 values indicate no modification applied\nVarus post = medial elevation, Valgus post = lateral elevation\nHeel skive is an angled modification to an orthotic, or shoe insert, that creates a flat, sloped surface within the heel cup to control foot movement\nFlanges provide lateral stability and containment\nFor flanges and skives please pick the smallest needed parameter\n\nOnly give me the json output, directly start with {\n</example_description>\n<USER_INPUT>\n{\n  \"category\": \"Sports\",\n  \"sub_category\": \"Running\",\n  \"arch_type\": \"High Arch\",\n  \"wear_pattern\": \"Lateral\",\n  \"discomfort_area\": [\n    \"Heel\",\n    \"Ball of foot\"\n  ],\n  \"activity_scenario\": [\n    \"Long-distance running\",\n    \"Sprint training\"\n  ],\n  \"priority\": \"Comfort\",\n  \"description\": \"Experiencing sharp pain in right heel after 5km runs, and numbness in toes during sprints. Looking for insole recommendations to alleviate these issues and improve overall running performance.\"\n}\n</USER_INPUT>\n<SPECIALIST_TYPE>\northopedic surgeon specializing in foot and ankle biomechanics\n</SPECIALIST_TYPE>\n<ideal_output>\n{\n  \"patient_id\": \"P002\",\n  \"insole\": {\n    \"length\": 44,\n    \"width\": 2,\n    \"thickness\": 3,\n     \"midsole\": 0.15,\n    \"left\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 2,\n      \"medial_heel_skive\": 0,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 2\n    },\n    \"right\": {\n      \"arch_height\": 6,\n      \"arch_length\": 0.25,\n      \"lateral_heel_skive\": 2,\n      \"medial_heel_skive\": 0,\n      \"rearfoot_varus_post\": 0,\n      \"rearfoot_valgus_post\": 0,\n      \"forefoot_varus_post\": 0,\n      \"forefoot_valgus_post\": 2,\n      \"heel_cup_depth\": 6,\n      \"medial_flange_height\": 0,\n      \"lateral_flange_height\": 2\n    }\n  }\n}\n\n</ideal_output>\n</example>\n</examples>\n\n" },
                                new {
                                    type = "image",
                                    source = new {
                                        type = "base64",
                                        media_type = mediaType,
                                        data = imageBase64
                                    }
                                },
                                new { type = "text", text = userTextPrompt }
                            }
                        }
                    }
                };

                try
                {
                    var json = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(apiUrl, content);

                    var responseString = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new Exception(responseString);

                    return Extract(responseString);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("========== AI VISION ERROR ==========");
                    Console.WriteLine(ex);
                    Console.WriteLine("=====================================");
                    throw;
                }
            }
        }

        static private string Extract(string json)
        {
            var result = new List<string>();

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("content", out JsonElement contentElement))
                {
                    foreach (var item in contentElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out JsonElement textElement))
                        {
                            result.Add(textElement.GetString()!);
                        }
                    }
                }
            }
            var parameters = JsonNode.Parse(result[0].Replace("```json\n", "").Replace("\n```", ""));
            if (parameters?["insole"] is JsonObject insole)
            {
                // Apply only to AI output; users can still edit midsole afterwards.
                insole["midsole"] = 0.25;
            }
            return parameters!.ToJsonString();
        }

    }

}
