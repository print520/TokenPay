using Flurl;
using Flurl.Http;
using TokenPay.Models;

namespace TokenPay.Helper
{
    /// <summary>
    /// 从 DexScreener 获取代币 priceUsd，用于猫头鹰等币种：订单总额(法币) ÷ (priceUsd × 法币兑USD) = 应付代币数量
    /// </summary>
    public static class DexScreenerHelper
    {
        private const string BaseUrl = "https://api.dexscreener.com/latest/dex/tokens";

        /// <summary>
        /// 获取代币当前 priceUsd（取第一个交易对的 priceUsd）
        /// </summary>
        public static async Task<decimal?> GetPriceUsdAsync(string contractAddress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(contractAddress)) return null;
            try
            {
                var resp = await BaseUrl
                    .AppendPathSegment(contractAddress.Trim())
                    .WithTimeout(10)
                    .GetJsonAsync<DexScreenerResponse>();
                var pair = resp?.Pairs?.FirstOrDefault();
                if (pair == null || string.IsNullOrEmpty(pair.PriceUsd)) return null;
                return decimal.TryParse(pair.PriceUsd, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price) ? price : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
