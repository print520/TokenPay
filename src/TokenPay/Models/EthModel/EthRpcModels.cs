using Newtonsoft.Json;

namespace TokenPay.Models.EthModel
{
    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("method")] public string Method { get; set; } = null!;
        [JsonProperty("params")] public object?[]? Params { get; set; }
    }

    public class JsonRpcResponse<T>
    {
        [JsonProperty("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("result")] public T? Result { get; set; }
        [JsonProperty("error")] public JsonRpcError? Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; } = "";
    }

    public class EthLogEntry
    {
        [JsonProperty("address")] public string Address { get; set; } = "";
        [JsonProperty("topics")] public string[] Topics { get; set; } = [];
        [JsonProperty("data")] public string Data { get; set; } = "";
        [JsonProperty("blockNumber")] public string BlockNumber { get; set; } = "";
        [JsonProperty("transactionHash")] public string TransactionHash { get; set; } = "";
    }

    public class EthBlock
    {
        [JsonProperty("number")] public string Number { get; set; } = "";
        [JsonProperty("timestamp")] public string Timestamp { get; set; } = "";
    }
}
