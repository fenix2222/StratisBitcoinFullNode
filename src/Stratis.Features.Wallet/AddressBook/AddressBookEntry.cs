using Newtonsoft.Json;

namespace Stratis.Features.Wallet
{
    /// <summary>
    /// Represents an entry in the address book.
    /// </summary>
    public class AddressBookEntry
    {
        /// <summary>
        /// A label uniquely identifying an entry.
        /// </summary>
        [JsonProperty(PropertyName = "label")]
        public string Label { get; set; }

        /// <summary>
        /// An address in base58.
        /// </summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }
    }
}