# Issues on current wallet implementation

### Stratis.Bitcoin.Features.Wallet

##### SpecialPurposeAccountIndexesStart

`Wallet.SpecialPurposeAccountIndexesStart` is used to set a limit to "normal" accounts, reserving
indexes with higher value for special purpose.

The problem is that there isn't a reliable way to reserve an index for special purpose, theoretically
any feature can reuse a special index used by another feature. 

e.g. ColdStaking defines
`const int ColdWalletAccountIndex = Wallet.Wallet.SpecialPurposeAccountIndexesStart + 0;`

If another feature try to reuse the same index, coldstaking would not be compatible with such feature without having a clear evidence of the problem.

A proposed solution to this, is implement a method to register special purpose account indexes for this need and if multiple features try to use the same index, an exception should be thrown during the feature initialization.