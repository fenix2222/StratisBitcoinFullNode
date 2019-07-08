using System;

namespace Stratis.Features.Wallet
{
    /// <summary>
    /// Helper class that expose default account filters
    /// </summary>
    public static class AccountFilters
    {
        /// <summary>Filter for identifying normal wallet accounts.</summary>
        public static Func<HdAccount, bool> NormalAccounts = a => a.Index < Wallet.SpecialPurposeAccountIndexesStart;
    }
}
