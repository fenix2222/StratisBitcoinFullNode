﻿using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;
using TracerAttributes;

namespace Stratis.Features.Wallet
{
    /// <summary>
    /// A wallet.
    /// </summary>
    public class Wallet : IWallet
    {
        /// <summary>Default pattern for accounts in the wallet. The first account will be called 'account 0', then 'account 1' and so on.</summary>
        public const string AccountNamePattern = "account {0}";

        /// <summary>Account numbers greater or equal to this number are reserved for special purpose account indexes.</summary>
        public const int SpecialPurposeAccountIndexesStart = 100_000_000;

        /// <summary>
        /// Initializes a new instance of the wallet.
        /// </summary>
        public Wallet()
        {
            this.AccountsRoot = new List<AccountRoot>();
        }

        /// <summary>
        /// The name of this wallet.
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Flag indicating if it is a watch only wallet.
        /// </summary>
        [JsonProperty(PropertyName = "isExtPubKeyWallet")]
        public bool IsExtPubKeyWallet { get; set; }

        /// <summary>
        /// The seed for this wallet, password encrypted.
        /// </summary>
        [JsonProperty(PropertyName = "encryptedSeed", NullValueHandling = NullValueHandling.Ignore)]
        public string EncryptedSeed { get; set; }

        /// <summary>
        /// The chain code.
        /// </summary>
        [JsonProperty(PropertyName = "chainCode", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ByteArrayConverter))]
        public byte[] ChainCode { get; set; }

        /// <summary>
        /// Gets or sets the merkle path.
        /// </summary>
        [JsonProperty(PropertyName = "blockLocator", ItemConverterType = typeof(UInt256JsonConverter))]
        public ICollection<uint256> BlockLocator { get; set; }

        /// <summary>
        /// The network this wallet is for.
        /// </summary>
        [JsonProperty(PropertyName = "network")]
        [JsonConverter(typeof(NetworkConverter))]
        public Network Network { get; set; }

        /// <summary>
        /// The time this wallet was created.
        /// </summary>
        [JsonProperty(PropertyName = "creationTime")]
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset CreationTime { get; set; }

        /// <summary>
        /// The root of the accounts tree.
        /// </summary>
        [JsonProperty(PropertyName = "accountsRoot")]
        public ICollection<AccountRoot> AccountsRoot { get; set; }

        /// <summary>
        /// Gets the accounts in the wallet.
        /// </summary>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>The accounts in the wallet.</returns>
        public IEnumerable<HdAccount> GetAccounts(Func<HdAccount, bool> accountFilter = null)
        {
            return this.AccountsRoot.SelectMany(a => a.Accounts).Where(accountFilter ?? AccountFilters.NormalAccounts);
        }

        /// <summary>
        /// Gets an account from the wallet's accounts.
        /// </summary>
        /// <param name="accountName">The name of the account to retrieve.</param>
        /// <returns>The requested account or <c>null</c> if the account does not exist.</returns>
        public HdAccount GetAccount(string accountName)
        {
            return this.AccountsRoot.SingleOrDefault()?.GetAccountByName(accountName);
        }

        /// <summary>
        /// Update the last block synced height and hash in the wallet.
        /// </summary>
        /// <param name="block">The block whose details are used to update the wallet.</param>
        public void SetLastBlockDetails(ChainedHeader block)
        {
            AccountRoot accountRoot = this.AccountsRoot.SingleOrDefault();

            if (accountRoot == null) return;

            accountRoot.LastBlockSyncedHeight = block.Height;
            accountRoot.LastBlockSyncedHash = block.HashBlock;
        }

        /// <summary>
        /// Gets all the transactions in the wallet.
        /// </summary>
        /// <returns>A list of all the transactions in the wallet.</returns>
        public IEnumerable<TransactionData> GetAllTransactions()
        {
            List<HdAccount> accounts = this.GetAccounts().ToList();

            foreach (TransactionData txData in accounts.SelectMany(x => x.ExternalAddresses).SelectMany(x => x.Transactions))
            {
                yield return txData;
            }

            foreach (TransactionData txData in accounts.SelectMany(x => x.InternalAddresses).SelectMany(x => x.Transactions))
            {
                yield return txData;
            }
        }

        /// <summary>
        /// Gets all the pub keys contained in this wallet.
        /// </summary>
        /// <returns>A list of all the public keys contained in the wallet.</returns>
        public IEnumerable<Script> GetAllPubKeys()
        {
            List<HdAccount> accounts = this.GetAccounts().ToList();

            foreach (Script script in accounts.SelectMany(x => x.ExternalAddresses).Select(x => x.ScriptPubKey))
            {
                yield return script;
            }

            foreach (Script script in accounts.SelectMany(x => x.InternalAddresses).Select(x => x.ScriptPubKey))
            {
                yield return script;
            }
        }

        /// <summary>
        /// Gets all the addresses contained in this wallet.
        /// </summary>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>A list of all the addresses contained in this wallet.</returns>
        public IEnumerable<HdAddress> GetAllAddresses(Func<HdAccount, bool> accountFilter = null)
        {
            IEnumerable<HdAccount> accounts = this.GetAccounts(accountFilter);

            var allAddresses = new List<HdAddress>();
            foreach (HdAccount account in accounts)
            {
                allAddresses.AddRange(account.GetCombinedAddresses());
            }
            return allAddresses;
        }

        /// <summary>
        /// Adds an account to the current wallet.
        /// </summary>
        /// <remarks>
        /// The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains at least one transaction.
        /// </remarks>
        /// <seealso cref="https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki"/>
        /// <param name="password">The password used to decrypt the wallet's <see cref="EncryptedSeed"/>.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <param name="accountIndex">The index at which an account will be created. If left null, a new account will be created after the last used one.</param>
        /// <param name="accountName">The name of the account to be created. If left null, an account will be created according to the <see cref="Wallet.AccountNamePattern"/>.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount AddNewAccount(string password, DateTimeOffset accountCreationTime, int? accountIndex = null, string accountName = null)
        {
            Guard.NotEmpty(password, nameof(password));

            AccountRoot accountRoot = this.AccountsRoot.Single();
            return accountRoot.AddNewAccount(password, this.EncryptedSeed, this.ChainCode, this.Network, accountCreationTime, accountIndex, accountName);
        }

        /// <summary>
        /// Adds an account to the current wallet.
        /// </summary>
        /// <remarks>
        /// The name given to the account is of the form "account (i)" by default, where (i) is an incremental index starting at 0.
        /// According to BIP44, an account at index (i) can only be created when the account at index (i - 1) contains at least one transaction.
        /// </remarks>
        /// <seealso cref="https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki"/>
        /// <param name="extPubKey">The extended public key for the wallet<see cref="EncryptedSeed"/>.</param>
        /// <param name="accountIndex">Zero-based index of the account to add.</param>
        /// <param name="accountCreationTime">Creation time of the account to be created.</param>
        /// <returns>A new HD account.</returns>
        public HdAccount AddNewAccount(ExtPubKey extPubKey, int accountIndex, DateTimeOffset accountCreationTime)
        {
            AccountRoot accountRoot = this.AccountsRoot.Single();
            return accountRoot.AddNewAccount(extPubKey, accountIndex, this.Network, accountCreationTime);
        }

        /// <summary>
        /// Gets the first account that contains no transaction.
        /// </summary>
        /// <returns>An unused account.</returns>
        public HdAccount GetFirstUnusedAccount()
        {
            // Get the accounts root for this type of coin.
            AccountRoot accountsRoot = this.AccountsRoot.Single();

            if (accountsRoot.Accounts.Any())
            {
                // Get an unused account.
                HdAccount firstUnusedAccount = accountsRoot.GetFirstUnusedAccount();
                if (firstUnusedAccount != null)
                {
                    return firstUnusedAccount;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether the wallet contains the specified address.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <returns>A value indicating whether the wallet contains the specified address.</returns>
        public bool ContainsAddress(HdAddress address)
        {
            if (!this.AccountsRoot.Any(r => r.Accounts.Any(
                a => a.ExternalAddresses.Any(i => i.Address == address.Address) ||
                     a.InternalAddresses.Any(i => i.Address == address.Address))))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the extended private key for the given address.
        /// </summary>
        /// <param name="password">The password used to encrypt/decrypt sensitive info.</param>
        /// <param name="address">The address to get the private key for.</param>
        /// <returns>The extended private key.</returns>
        [NoTrace]
        public ISecret GetExtendedPrivateKeyForAddress(string password, HdAddress address)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotNull(address, nameof(address));

            // Check if the wallet contains the address.
            if (!this.ContainsAddress(address))
            {
                throw new WalletException("Address not found on wallet.");
            }

            // get extended private key
            Key privateKey = HdOperations.DecryptSeed(this.EncryptedSeed, password, this.Network);
            return HdOperations.GetExtendedPrivateKey(privateKey, this.ChainCode, address.HdPath, this.Network);
        }

        /// <summary>
        /// Lists all spendable transactions from all accounts in the wallet.
        /// </summary>
        /// <param name="currentChainHeight">Height of the current chain, used in calculating the number of confirmations.</param>
        /// <param name="confirmations">The number of confirmations required to consider a transaction spendable.</param>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>A collection of spendable outputs.</returns>
        public IEnumerable<UnspentOutputReference> GetAllSpendableTransactions(int currentChainHeight, int confirmations = 0, Func<HdAccount, bool> accountFilter = null)
        {
            IEnumerable<HdAccount> accounts = this.GetAccounts(accountFilter);

            return accounts.SelectMany(x => x.GetSpendableTransactions(currentChainHeight, this.Network.Consensus.CoinbaseMaturity, confirmations));
        }

        /// <summary>
        /// Calculates the fee paid by the user on a transaction sent.
        /// </summary>
        /// <param name="transactionId">The transaction id to look for.</param>
        /// <returns>The fee paid.</returns>
        public Money GetSentTransactionFee(uint256 transactionId)
        {
            List<TransactionData> allTransactions = this.GetAllTransactions().ToList();

            // Get a list of all the inputs spent in this transaction.
            List<TransactionData> inputsSpentInTransaction = allTransactions.Where(t => t.SpendingDetails?.TransactionId == transactionId).ToList();

            if (!inputsSpentInTransaction.Any())
            {
                throw new WalletException("Not a sent transaction");
            }

            // Get the details of the spending transaction, which can be found on any input spent.
            SpendingDetails spendingTransaction = inputsSpentInTransaction.Select(s => s.SpendingDetails).First();

            // The change is the output paid into one of our addresses. We make sure to exclude the output received to one of
            // our addresses if this transaction is self-sent.
            IEnumerable<TransactionData> changeOutput = allTransactions.Where(t => t.Id == transactionId && spendingTransaction.Payments.All(p => p.OutputIndex != t.Index)).ToList();

            Money inputsAmount = new Money(inputsSpentInTransaction.Sum(i => i.Amount));
            Money outputsAmount = new Money(spendingTransaction.Payments.Sum(p => p.Amount) + changeOutput.Sum(c => c.Amount));

            return inputsAmount - outputsAmount;
        }

        /// <summary>
        /// Finds the HD addresses for the address.
        /// </summary>
        /// <remarks>
        /// Returns an HDAddress.
        /// </remarks>
        /// <param name="address">An address.</param>
        /// <param name="accountFilter">An optional filter for filtering the accounts being returned.</param>
        /// <returns>HD Address</returns>
        public HdAddress GetAddress(string address, Func<HdAccount, bool> accountFilter = null)
        {
            Guard.NotNull(address, nameof(address));
            return this.GetAllAddresses(accountFilter).SingleOrDefault(a => a.Address == address);
        }
    }
}