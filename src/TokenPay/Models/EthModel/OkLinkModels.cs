using Newtonsoft.Json;

namespace TokenPay.Models.EthModel
{
    /// <summary>OKLink 地址代币转账接口响应</summary>
    public class OkLinkTransfersResponse
    {
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("msg")] public string? Msg { get; set; }
        [JsonProperty("detailMsg")] public string? DetailMsg { get; set; }
        [JsonProperty("data")] public OkLinkTransfersData? Data { get; set; }
    }

    public class OkLinkTransfersData
    {
        [JsonProperty("total")] public int Total { get; set; }
        [JsonProperty("hits")] public List<OkLinkTransferHit>? Hits { get; set; }
    }

    /// <summary>OKLink 单条转账记录；realValue &gt; 0 表示该地址收款</summary>
    public class OkLinkTransferHit
    {
        [JsonProperty("txhash")] public string TxHash { get; set; } = "";
        [JsonProperty("blockHeight")] public long BlockHeight { get; set; }
        [JsonProperty("blocktime")] public long Blocktime { get; set; }
        [JsonProperty("from")] public string From { get; set; } = "";
        [JsonProperty("to")] public string To { get; set; } = "";
        [JsonProperty("tokenContractAddress")] public string TokenContractAddress { get; set; } = "";
        [JsonProperty("symbol")] public string Symbol { get; set; } = "";
        [JsonProperty("value")] public decimal Value { get; set; }
        [JsonProperty("realValue")] public decimal RealValue { get; set; }
    }
}
