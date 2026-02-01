using Flurl;
using Flurl.Http;
using FreeSql;
using System.Numerics;
using System.Threading.Channels;
using TokenPay.Domains;
using TokenPay.Extensions;
using TokenPay.Helper;
using TokenPay.Models.EthModel;

namespace TokenPay.BgServices
{
    /// <summary>节点扫描得到的 BEP20 转账项，用于与待支付订单匹配</summary>
    internal record NodeScanTransferItem(string Hash, string From, string To, decimal RealAmount, DateTime DateTime, long Confirmations, string ContractAddress, long BlockNumber);

    public class OrderCheckEVMERC20Service : BaseScheduledService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly List<EVMChain> _chains;
        private readonly Channel<TokenOrders> _channel;
        private readonly IFreeSql freeSql;
        private bool UseDynamicAddress => _configuration.GetValue("UseDynamicAddress", true);
        private bool UseDynamicAddressAmountMove => _configuration.GetValue("DynamicAddressConfig:AmountMove", false);
        public OrderCheckEVMERC20Service(ILogger<OrderCheckEVMERC20Service> logger,
            IConfiguration configuration,
            IHostEnvironment env,
            List<EVMChain> Chains,
            Channel<TokenOrders> channel,
            IFreeSql freeSql) : base("EVM代币订单检测", TimeSpan.FromSeconds(15), logger)
        {
            this._configuration = configuration;
            this._env = env;
            _chains = Chains;
            this._channel = channel;
            this.freeSql = freeSql;
        }

        protected override async Task ExecuteAsync(DateTime RunTime, CancellationToken stoppingToken)
        {
            var _repository = freeSql.GetRepository<TokenOrders>();
            foreach (var chain in _chains)
            {
                if (chain == null || !chain.Enable || chain.ERC20 == null) continue;
                foreach (var erc20 in chain.ERC20)
                {
                    var Currency = $"EVM_{chain.ChainNameEN}_{erc20.Name}_{chain.ERC20Name}";
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(chain.RpcUrl))
                            await ERC20ByNode(_repository, Currency, chain, erc20);
                        else
                            await ERC20(_repository, Currency, chain, erc20);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{Currency}查询交易记录出错", Currency);
                    }
                }

            }

        }

        private const string TransferTopic0 = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";
        private const int NodeChunkBlocks = 300;
        private const int NodeTotalBlocks = 2000;

        /// <summary>
        /// BEP20 支付监控：用节点 RPC 分片查 Transfer，匹配待支付订单（猫头鹰等配置了 RpcUrl 的币种走此逻辑）
        /// </summary>
        private async Task ERC20ByNode(IBaseRepository<TokenOrders> _repository, string Currency, EVMChain chain, EVMErc20 erc20)
        {
            var addresses = await _repository
                .Where(x => x.Status == OrderStatus.Pending)
                .Where(x => x.Currency == Currency)
                .Distinct()
                .ToListAsync(x => x.ToAddress);

            if (addresses.Count == 0) return;

            var rpc = chain.RpcUrl!.TrimEnd('/');
            var decimals = erc20.Decimals > 0 ? erc20.Decimals : 18;

            var blockHex = await RpcCall<string>(rpc, "eth_blockNumber", null);
            if (string.IsNullOrEmpty(blockHex)) return;
            var currentBlock = HexToLong(blockHex);

            foreach (var address in addresses)
            {
                var orders = await _repository
                    .Where(x => x.Status == OrderStatus.Pending)
                    .Where(x => x.Currency == Currency)
                    .Where(x => x.ToAddress == address)
                    .OrderBy(x => x.CreateTime)
                    .ToListAsync();
                if (orders.Count == 0) continue;

                var toTopic = "0x000000000000000000000000" + address.Replace("0x", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
                var allLogs = new List<EthLogEntry>();

                for (var fromBlock = Math.Max(0, currentBlock - NodeTotalBlocks); fromBlock < currentBlock; fromBlock += NodeChunkBlocks)
                {
                    var toBlock = Math.Min(fromBlock + NodeChunkBlocks - 1, currentBlock);
                    var getLogsFilter = new { address = erc20.ContractAddress, fromBlock = $"0x{fromBlock:x}", toBlock = $"0x{toBlock:x}", topics = new object?[] { TransferTopic0, null, toTopic } };
                    var logs = await RpcCall<EthLogEntry[]>(rpc, "eth_getLogs", new object[] { getLogsFilter });
                    if (logs != null && logs.Length > 0)
                        allLogs.AddRange(logs);
                }

                if (allLogs.Count == 0) continue;

                var blockNumbers = allLogs.Select(l => l.BlockNumber).Distinct().ToList();
                var blockTimes = await GetBlockTimestamps(rpc, blockNumbers);

                var items = new List<NodeScanTransferItem>();
                foreach (var log in allLogs)
                {
                    if (log.Topics.Length < 3 || string.IsNullOrEmpty(log.Data)) continue;
                    var from = "0x" + log.Topics[1].AsSpan(^40).ToString();
                    var to = "0x" + log.Topics[2].AsSpan(^40).ToString();
                    var valueWei = HexToBigInteger(log.Data);
                    var realAmount = (decimal)(valueWei / (BigInteger)Math.Pow(10, decimals));
                    var blockNum = HexToLong(log.BlockNumber);
                    var confirmations = currentBlock - blockNum;
                    var time = blockTimes.TryGetValue(log.BlockNumber, out var ts) ? ts : DateTime.UtcNow;
                    items.Add(new NodeScanTransferItem(log.TransactionHash, from, to, realAmount, time, confirmations, log.Address, blockNum));
                }

                foreach (var item in items.OrderByDescending(x => x.BlockNumber).ToList())
                {
                    if (orders.Count == 0) break;
                    if (await _repository.Select.AnyAsync(x => x.BlockTransactionId == item.Hash)) continue;
                    if (item.ContractAddress.Replace("0x", "", StringComparison.OrdinalIgnoreCase) != erc20.ContractAddress.Replace("0x", "", StringComparison.OrdinalIgnoreCase) || item.Confirmations < chain.Confirmations)
                        continue;

                    var order = orders.Where(x => x.Amount == item.RealAmount && x.ToAddress.Equals(item.To, StringComparison.OrdinalIgnoreCase) && x.CreateTime < item.DateTime)
                        .OrderByDescending(x => x.CreateTime).FirstOrDefault();
                recheck:
                    if (order != null)
                    {
                        order.FromAddress = item.From;
                        order.BlockTransactionId = item.Hash;
                        order.Status = OrderStatus.Paid;
                        order.PayTime = item.DateTime;
                        order.PayAmount = item.RealAmount;
                        await _repository.UpdateAsync(order);
                        orders.Remove(order);
                        await SendAdminMessage(order);
                    }
                    else if (UseDynamicAddress && UseDynamicAddressAmountMove)
                    {
                        var move = _configuration.GetSection($"DynamicAddressConfig:{erc20.Name}").Get<decimal[]>() ?? [];
                        if (move.Length == 2)
                        {
                            order = orders.Where(x => item.RealAmount >= x.Amount - move[0] && item.RealAmount <= x.Amount + move[1])
                                .Where(x => x.ToAddress.Equals(item.To, StringComparison.OrdinalIgnoreCase) && x.CreateTime < item.DateTime)
                                .OrderByDescending(x => x.CreateTime).FirstOrDefault();
                            if (order != null) { order.IsDynamicAmount = true; goto recheck; }
                        }
                    }
                }
            }
        }

        private static async Task<Dictionary<string, DateTime>> GetBlockTimestamps(string rpc, List<string> blockNumbers)
        {
            var dict = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var bn in blockNumbers)
            {
                var block = await RpcCall<EthBlock>(rpc, "eth_getBlockByNumber", new object[] { bn, false });
                if (block?.Timestamp != null && long.TryParse(block.Timestamp.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var ts))
                    dict[bn] = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
            }
            return dict;
        }

        private static async Task<T?> RpcCall<T>(string rpcUrl, string method, object?[]? parameters)
        {
            try
            {
                var req = new JsonRpcRequest { Id = 1, Method = method, Params = parameters };
                var resp = await rpcUrl.WithTimeout(15).PostJsonAsync(req).ReceiveJson<JsonRpcResponse<T>>();
                return resp.Error != null ? default : resp.Result;
            }
            catch { return default; }
        }

        private static long HexToLong(string hex)
        {
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            return long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        }

        private static BigInteger HexToBigInteger(string hex)
        {
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            return BigInteger.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        }

        /// <summary>
        /// 查询交易记录（区块浏览器 API，未配置 RpcUrl 时使用）
        /// </summary>
        private async Task ERC20(IBaseRepository<TokenOrders> _repository, string Currency, EVMChain chain, EVMErc20 erc20)
        {
            var Address = await _repository
                .Where(x => x.Status == OrderStatus.Pending)
                .Where(x => x.Currency == Currency)
                .Distinct()
                .ToListAsync(x => x.ToAddress);

            var BaseUrl = chain.ApiHost ?? "https://api.etherscan.io/v2/";

            foreach (var address in Address)
            {
                //查询此地址待支付订单
                var orders = await _repository
                    .Where(x => x.Status == OrderStatus.Pending)
                    .Where(x => x.Currency == Currency)
                    .Where(x => x.ToAddress == address)
                    .OrderBy(x => x.CreateTime)
                    .ToListAsync();
                if (!orders.Any())
                {
                    continue;
                }
                var query = new Dictionary<string, object>
                {
                    { "chainid", chain.ChainId },
                    { "module", "account" },
                    { "action", "tokentx" },
                    { "contractaddress", erc20.ContractAddress },
                    { "address", address },
                    { "page", 1 },
                    { "offset", 100 },
                    { "sort", "desc" }
                };
                if (_env.IsProduction())
                    query.Add("apikey", chain.ApiKey);

                var req = BaseUrl
                    .AppendPathSegment($"api")
                    .SetQueryParams(query)
                    .WithTimeout(15);
                var result = await req
                    .GetJsonAsync<BaseResponseList<ERC20Transaction>>();

                if (result.Status == "1" && result.Result?.Count > 0)
                {
                    foreach (var item in result.Result)
                    {
                        //没有需要匹配的订单了
                        if (!orders.Any())
                        {
                            break;
                        }
                        //此交易已被其他订单使用
                        if (await _repository.Select.AnyAsync(x => x.BlockTransactionId == item.Hash))
                        {
                            continue;
                        }
                        //合约地址 确认数
                        if (item.ContractAddress.ToLower() != erc20.ContractAddress.ToLower() || item.Confirmations < chain.Confirmations)
                        {
                            continue;
                        }
                        var order = orders.Where(x => x.Amount == item.RealAmount && x.ToAddress.ToLower() == item.To.ToLower() && x.CreateTime < item.DateTime)
                            .OrderByDescending(x => x.CreateTime)//优先付最后一单
                            .FirstOrDefault();
                    recheck:
                        if (order != null)
                        {
                            order.FromAddress = item.From;
                            order.BlockTransactionId = item.Hash;
                            order.Status = OrderStatus.Paid;
                            order.PayTime = item.DateTime;
                            order.PayAmount = item.RealAmount;
                            await _repository.UpdateAsync(order);
                            orders.Remove(order);
                            await SendAdminMessage(order);
                        }
                        else
                        {
                            if (UseDynamicAddress && UseDynamicAddressAmountMove)
                            {
                                //允许非准确金额支付
                                var Move = _configuration.GetSection($"DynamicAddressConfig:{erc20.Name}").Get<decimal[]>() ?? [];
                                if (Move.Length == 2)
                                {
                                    var Down = Move[0]; //上浮金额
                                    var Up = Move[1]; //下浮金额
                                    order = orders.Where(x => item.RealAmount >= x.Amount - Down && item.RealAmount <= x.Amount + Up)
                                        .Where(x => x.ToAddress.ToLower() == item.To.ToLower() && x.CreateTime < item.DateTime)
                                       .OrderByDescending(x => x.CreateTime)//优先付最后一单
                                       .FirstOrDefault();
                                    if (order != null)
                                    {
                                        order.IsDynamicAmount = true;
                                        goto recheck;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        private async Task SendAdminMessage(TokenOrders order)
        {
            await _channel.Writer.WriteAsync(order);
        }
    }
}
