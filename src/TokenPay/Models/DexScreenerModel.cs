using Newtonsoft.Json;

namespace TokenPay.Models
{
    /// <summary>
    /// DexScreener API: https://api.dexscreener.com/latest/dex/tokens/{contractAddress}
    /// 用于获取代币 priceUsd，计算猫头鹰等币种应付数量：订单总额(法币) ÷ (priceUsd × 法币兑USD)
    /// </summary>
    public class DexScreenerResponse
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; } = "";

        [JsonProperty("pairs")]
        public DexScreenerPair[] Pairs { get; set; } = [];
    }

    public class DexScreenerPair
    {
        [JsonProperty("chainId")]
        public string ChainId { get; set; } = "";

        [JsonProperty("priceUsd")]
        public string PriceUsd { get; set; } = "";

        [JsonProperty("baseToken")]
        public DexScreenerToken BaseToken { get; set; } = new();
    }

    public class DexScreenerToken
    {
        [JsonProperty("address")]
        public string Address { get; set; } = "";

        [JsonProperty("symbol")]
        public string Symbol { get; set; } = "";
    }
}
