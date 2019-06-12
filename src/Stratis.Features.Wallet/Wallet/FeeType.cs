namespace Stratis.Features.Wallet
{
    /// <summary>
    /// An indicator of how fast a transaction will be accepted in a block.
    /// </summary>
    public enum FeeType
    {
        /// <summary>
        /// Slow.
        /// </summary>
        Low = 0,

        /// <summary>
        /// Avarage.
        /// </summary>
        Medium = 1,

        /// <summary>
        /// Fast.
        /// </summary>
        High = 105
    }
}
