using System;

namespace Stratis.Features.Wallet
{
    public static class FeeParser
    {
        public static FeeType Parse(string value)
        {
            bool isParsed = Enum.TryParse<FeeType>(value, true, out FeeType result);
            if (!isParsed)
            {
                throw new FormatException($"FeeType {value} is not a valid FeeType");
            }

            return result;
        }

        /// <summary>
        /// Map a fee type to the number of confirmations
        /// </summary>
        /// <param name="fee">The fee.</param>
        /// <returns>The number of confirmations</returns>
        /// <exception cref="WalletException">Invalid fee</exception>
        public static int ToConfirmations(this FeeType fee)
        {
            switch (fee)
            {
                case FeeType.Low:
                    return 50;

                case FeeType.Medium:
                    return 20;

                case FeeType.High:
                    return 5;
            }

            throw new WalletException("Invalid fee");
        }
    }
}
