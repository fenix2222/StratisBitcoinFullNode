﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>
    /// Checks if <see cref="Block"/> time stamp is ahead of current consensus and not more then two hours in the future.
    /// </summary>
    [HeaderValidationRule(CanSkipValidation = true)]
    public class HeaderTimeChecksRule : ConsensusRule
    {
        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.TimeTooOld">Thrown if block's timestamp is too early.</exception>
        /// <exception cref="ConsensusErrors.TimeTooNew">Thrown if block's timestamp too far in the future.</exception>
        public override Task RunAsync(RuleContext context)
        {
            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeader;

            // Check timestamp against prev.
            if (chainedHeader.Header.BlockTime <= chainedHeader.Previous.GetMedianTimePast())
            {
                this.Logger.LogTrace("(-)[TIME_TOO_OLD]");
                ConsensusErrors.TimeTooOld.Throw();
            }

            // Check timestamp.
            if (chainedHeader.Header.BlockTime > (context.Time + TimeSpan.FromHours(2)))
            {
                this.Logger.LogTrace("(-)[TIME_TOO_NEW]");
                ConsensusErrors.TimeTooNew.Throw();
            }

            return Task.CompletedTask;
        }
    }
}