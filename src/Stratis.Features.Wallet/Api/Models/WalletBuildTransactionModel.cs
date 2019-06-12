using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Features.Wallet.Api.Models
{
    public class WalletBuildTransactionModel
    {
        [JsonProperty(PropertyName = "fee")]
        public Money Fee { get; set; }

        [JsonProperty(PropertyName = "hex")]
        public string Hex { get; set; }

        [JsonProperty(PropertyName = "transactionId")]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
    }
}
