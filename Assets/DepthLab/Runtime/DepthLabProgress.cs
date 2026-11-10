using System;
using UnityEngine;

namespace DepthLab
{
    [Serializable]
    public class ChapterRecord
    {
        public bool completed;
        public int stars;
        public float seconds;
        public int mistakes;
        public int hints;
        public int retries;
        public int attempts;
    }

    public static class DepthLabProgress
    {
        public static readonly string[] Titles = {
            "立体视差之室", "运动视差森林", "视觉错觉回廊", "光影迷宫"
        };
        public static readonly string[] Translations = {
            "立体视差之室", "运动视差森林",
            "视觉错觉回廊", "光影迷宫"
        };
        public static readonly string[] Concepts = {
            "双眼视差", "运动视差", "大小与长度错觉", "光线与材质"
        };
        public static readonly string[] Descriptions = {
            "1. 抓取三种彩色实体，放入不同深度的对应线框目标；线框非实体、无碰撞。\n2. 全部匹配后，步行穿过已开启的出口。\n双眼视差帮助判断深度；桌面预览不呈现双眼立体视觉。",
            "1. 左右观察，发现隐藏路径。\n2. 依次选择四个安全平台。\n3. 步行穿过已开启的出口。",
            "1. 比较立方体大小，阅读解释并继续。\n2. 比较直线段长度，阅读解释并继续。\n3. 步行穿过已开启的出口。",
            "1. 比较金属与粗糙表面，将光源设为 +25°。\n2. 转动镜面，让接收器持续亮绿灯 1.5 秒。\n3. 步行穿过已开启的出口。"
        };
        public static readonly string[] Times = { "3–5 分钟", "3–5 分钟", "2–4 分钟", "3–6 分钟" };
        private const string Prefix = "DepthLab.Progress.";

        public static ChapterRecord Read(int index)
        {
            string value = PlayerPrefs.GetString(Prefix + index, "");
            if (string.IsNullOrEmpty(value)) return new ChapterRecord();
            try { return JsonUtility.FromJson<ChapterRecord>(value) ?? new ChapterRecord(); }
            catch (ArgumentException) { return new ChapterRecord(); }
        }

        public static bool Unlocked(int index) => index == 0 || (index > 0 && index < 4 && Read(index - 1).completed);
        public static int CalculateStars(float seconds, int mistakes, int hints, int retries,
            float threeSeconds, float twoSeconds, int threeMistakes, int twoMistakes)
        {
            int penalty = mistakes + hints + retries;
            if (seconds <= threeSeconds && penalty <= threeMistakes) return 3;
            return seconds <= twoSeconds && penalty <= twoMistakes ? 2 : 1;
        }

        public static void BeginAttempt(int index)
        {
            ChapterRecord record = Read(index);
            record.attempts++;
            Write(index, record);
        }

        public static void Complete(int index, ChapterRecord run)
        {
            ChapterRecord old = Read(index);
            run.completed = true;
            run.attempts = old.attempts;
            if (!old.completed || run.stars > old.stars || (run.stars == old.stars && run.seconds < old.seconds))
                Write(index, run);
        }

        private static void Write(int index, ChapterRecord record)
        {
            PlayerPrefs.SetString(Prefix + index, JsonUtility.ToJson(record));
            PlayerPrefs.Save();
        }

        public static void Reset()
        {
            for (int i = 0; i < 4; i++) PlayerPrefs.DeleteKey(Prefix + i);
            PlayerPrefs.Save();
        }
        public static string TimeLabel(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
        public static string Summary(int index)
        {
            ChapterRecord r = Read(index);
            if (!Unlocked(index)) return "未解锁  /  请先完成上一章";
            return r.completed ? $"{new string('★', r.stars)}   最佳成绩 {TimeLabel(r.seconds)}   /   错误 {r.mistakes} 次" : "已解锁  /  尚未探索";
        }
    }
}
