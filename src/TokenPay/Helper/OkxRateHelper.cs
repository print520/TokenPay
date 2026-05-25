using Flurl;
using Flurl.Http;
using TokenPay.Domains;

namespace TokenPay.Helper
{
    /// <summary>从 OKX C2C 获取法币汇率</summary>
    public static class OkxRateHelper
    {
        private const string BaseUrl = "https://www.okx.com";
        private const string UserAgent = "TokenPay/1.0 Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/104.0.0.0 Safari/537.36";

        public static async Task<decimal?> FetchRateAsync(string baseCurrency, FiatCurrency quoteCurrency, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseCurrency)) return null;
            try
            {
                var result = await BaseUrl
                    .WithTimeout(5)
                    .WithHeaders(new { User_Agent = UserAgent })
                    .AppendPathSegment("/v3/c2c/otc-ticker/quotedPrice")
                    .SetQueryParams(new
                    {
                        side = "buy",
                        quoteCurrency = quoteCurrency.ToString(),
                        baseCurrency = baseCurrency.Trim(),
                    })
                    .GetJsonAsync<OkxQuotedPriceResponse>(cancellationToken: cancellationToken);
                if (result?.code == 0 && result.data?.Count > 0)
                {
                    var best = result.data.FirstOrDefault(x => x.bestOption);
                    if (best != null && best.price > 0)
                        return best.price;
                }
            }
            catch
            {
                // 由调用方记录日志或降级处理
            }
            return null;
        }
    }

    internal class OkxQuotedPriceDatum
    {
        public bool bestOption { get; set; }
        public decimal price { get; set; }
    }

    internal class OkxQuotedPriceResponse
    {
        public int code { get; set; }
        public List<OkxQuotedPriceDatum> data { get; set; } = [];
        public string? msg { get; set; }
        public string? error_message { get; set; }
    }
}
