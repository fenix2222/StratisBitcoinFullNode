using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Features.Wallet
{
    /// <summary>
    /// Represents an address book.
    /// </summary>
    public class AddressBook
    {
        /// <summary>
        /// Initializes a new instance of the wallet.
        /// </summary>
        public AddressBook()
        {
            this.Addresses = new List<AddressBookEntry>();
        }

        /// <summary>
        /// The list of addresses in the address book.
        /// </summary>
        [JsonProperty(PropertyName = "addresses")]
        public ICollection<AddressBookEntry> Addresses { get; set; }
    }
}