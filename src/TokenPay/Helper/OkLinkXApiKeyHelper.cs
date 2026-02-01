using System.Security.Cryptography;

namespace TokenPay.Helper
{
    /// <summary>
    /// OKLink 请求头 x-apikey 动态生成（与前端逆向逻辑一致）。
    /// 参考: https://www.cnblogs.com/sbhglqy/p/18424952
    /// 算法: x-apikey = Base64(encryptApiKey(rawKey) + "|" + encryptTime(timestampMs))
    /// </summary>
    public static class OkLinkXApiKeyHelper
    {
        /// <summary>时间戳偏移常数（逆向得到的定值）</summary>
        private const long TimeOffset = 1111111111111L;

        /// <summary>
        /// 若配置值为原始 API Key（UUID 格式，如 a2c903cc-b31e-4547-9299-b6d07b7631ab），则按算法计算 x-apikey；
        /// 否则视为已计算好的 x-apikey 原样返回（兼容旧配置）。
        /// </summary>
        public static string GetXApiKeyForRequest(string configValue)
        {
            if (string.IsNullOrWhiteSpace(configValue)) return configValue;
            if (!IsRawApiKey(configValue)) return configValue;
            return Compute(configValue);
        }

        /// <summary>是否为原始 API Key（UUID 形态，含 4 个连字符、总长 36）</summary>
        public static bool IsRawApiKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var s = value.Trim();
            if (s.Length != 36) return false;
            var hyphens = 0;
            foreach (var c in s)
            {
                if (c == '-') hyphens++;
            }
            return hyphens == 4;
        }

        /// <summary>根据原始 API Key 和当前时间生成 x-apikey</summary>
        public static string Compute(string rawApiKey)
        {
            var tMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Compute(rawApiKey, tMs);
        }

        /// <summary>根据原始 API Key 与指定时间戳(ms)生成 x-apikey</summary>
        public static string Compute(string rawApiKey, long timestampMs)
        {
            var encApi = EncryptApiKey(rawApiKey);
            var encTime = EncryptTime(timestampMs);
            return Comb(encApi, encTime);
        }

        /// <summary>encryptApiKey: 前 8 个字符移到末尾</summary>
        private static string EncryptApiKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (key.Length <= 8) return key;
            return key[8..] + key[..8];
        }

        /// <summary>encryptTime: (timestampMs + offset) 转字符串再拼 3 位随机数</summary>
        private static string EncryptTime(long timestampMs)
        {
            var sum = (timestampMs + TimeOffset).ToString();
            var r1 = RandomNumberGenerator.GetInt32(0, 10);
            var r2 = RandomNumberGenerator.GetInt32(0, 10);
            var r3 = RandomNumberGenerator.GetInt32(0, 10);
            return sum + r1 + r2 + r3;
        }

        /// <summary>comb: Base64(encApi + "|" + encTime)</summary>
        private static string Comb(string encApi, string encTime)
        {
            var combined = encApi + "|" + encTime;
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(combined));
        }
    }
}
