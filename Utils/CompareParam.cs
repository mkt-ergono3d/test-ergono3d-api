using System;
using System.Text.Json;

namespace InsoleApi.Utils
{
    // 左右脚子对象
    public class InsoleSide
    {
        public double arch_height { get; set; }
        public double arch_length { get; set; }
        public double lateral_heel_skive { get; set; }
        public double medial_heel_skive { get; set; }
        public double rearfoot_varus_post { get; set; }
        public double rearfoot_valgus_post { get; set; }
        public double forefoot_varus_post { get; set; }
        public double forefoot_valgus_post { get; set; }
        public double heel_cup_depth { get; set; }
        public double heel_lift { get; set; }
        public double medial_flange_height { get; set; }
        public double lateral_flange_height { get; set; }
    }

    // 顶层 Insole 对象
    public class Insole
    {
        public double length { get; set; }
        public double width { get; set; }
        public double thickness { get; set; }
        public double midsole { get; set; }
        public InsoleSide left { get; set; }
        public InsoleSide right { get; set; }
    }

    // 包装 JSON 顶层 patient_id
    public class PatientInsole
    {
        public string patient_id { get; set; }
        public Insole insole { get; set; }
    }

    // 核心对比类
    public class CompareParam
    {
        /// <summary>
        /// 输入两个 JSON 字符串，返回是否只改了 length/thickness/midsole
        /// </summary>
        public static bool IsOnlyBaseChanged(string oldJson, string newJson)
        {
            // 解析 JSON
            var oldWrapper = JsonSerializer.Deserialize<PatientInsole>(oldJson);
            var newWrapper = JsonSerializer.Deserialize<PatientInsole>(newJson);

            if (oldWrapper == null || newWrapper == null)
                throw new ArgumentException("JSON 无法解析为 PatientInsole 对象");

            var oldObj = oldWrapper.insole;
            var newObj = newWrapper.insole;

            if (oldObj == null || newObj == null)
                throw new ArgumentException("insole 字段为空");

            // 1️⃣ 非允许字段检查
            if (oldObj.width != newObj.width)
                return false;

            if (!SidesAreEqual(oldObj.left, newObj.left))
                return false;

            if (!SidesAreEqual(oldObj.right, newObj.right))
                return false;

            // 2️⃣ 允许字段变化检查
            return oldObj.length != newObj.length ||
                   oldObj.thickness != newObj.thickness ||
                   oldObj.midsole != newObj.midsole;
        }

        // 比较 left/right 内部字段（除 length/thickness/midsole）
        private static bool SidesAreEqual(InsoleSide a, InsoleSide b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            return a.arch_height == b.arch_height &&
                   a.arch_length == b.arch_length &&
                   a.lateral_heel_skive == b.lateral_heel_skive &&
                   a.medial_heel_skive == b.medial_heel_skive &&
                   a.rearfoot_varus_post == b.rearfoot_varus_post &&
                   a.rearfoot_valgus_post == b.rearfoot_valgus_post &&
                   a.forefoot_varus_post == b.forefoot_varus_post &&
                   a.forefoot_valgus_post == b.forefoot_valgus_post &&
                   a.heel_cup_depth == b.heel_cup_depth &&
                   a.heel_lift == b.heel_lift &&
                   a.medial_flange_height == b.medial_flange_height &&
                   a.lateral_flange_height == b.lateral_flange_height;
        }
    }
}
