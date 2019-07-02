using System;
using System.IO;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.FederatedSidechains.AdminDashboard.Settings;

namespace Stratis.FederatedSidechains.AdminDashboard.Services
{
    public interface IMiningKeyReader
    {
        string GetKey();
    }

    public class MiningKeyReader : IMiningKeyReader
    {
        private readonly DefaultEndpointsSettings settings;
        private readonly ILogger<MiningKeyReader> logger;

        public MiningKeyReader(DefaultEndpointsSettings settings, ILoggerFactory loggerFactory)
        {
            this.settings = settings;
            this.logger = loggerFactory.CreateLogger<MiningKeyReader>();
        }

        public string GetKey()
        {
            string miningKeyFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StratisNode",
                "cirrus",
                this.settings.IsMainnet ? "CirrusMain" : "CirrusTest",
                "federationKey.dat");
            if (!File.Exists(miningKeyFile))
            {
                this.logger.LogError("File {file} does not exist", miningKeyFile);
                return null;
            }

            try
            {
                using (FileStream readStream = File.OpenRead(miningKeyFile))
                {
                    var privateKey = new Key();
                    var stream = new BitcoinStream(readStream, false);
                    stream.ReadWrite(ref privateKey);
                    return Encoders.Hex.EncodeData(privateKey.PubKey.ToBytes());
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to parse private key from {miningKeyFile}", miningKeyFile);
                return null;
            }
        }
    }
}
