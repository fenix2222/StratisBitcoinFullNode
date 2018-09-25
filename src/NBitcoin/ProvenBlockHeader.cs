﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace NBitcoin
{
    /// <summary>
    /// <para>
    /// Extension to an existing <see cref="BlockHeader"/> which is used in PoS to prevent attacker from constructing
    /// a fake chain of headers that has more work than the valid chain and attacking a node.
    /// </para>
    /// <para>
    /// Proven header prevents such an attack by including additional information that can be validated and confirmed whether
    /// the header is fake or real.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Additional information included into proven header:
    /// </para>
    /// <para>
    /// Block header signature (<see cref="Signature"/>), which is signed with the private key which corresponds to
    /// coinstake's second output's public key.
    /// </para>
    /// <para>
    /// Coinstake transaction (<see cref="Coinstake"/>).
    /// </para>
    /// <para>
    /// Merkle proof (<see cref="MerkleProof"/>) that proves the coinstake tx is included in a block that is being represented by the provided header.
    /// </para>
    /// </remarks>
    public class ProvenBlockHeader : PosBlockHeader
    {
        /// <inheritdoc />
        public override int CurrentVersion => 8;

        /// <summary>
        /// Coinstake transaction.
        /// </summary>
        private Transaction coinstake;

        /// <summary>
        /// Gets coinstake transaction.
        /// </summary>
        public Transaction Coinstake => this.coinstake;

        /// <summary>
        /// Merkle proof that proves the coinstake tx is included in a block that is being represented by the provided header.
        /// </summary>
        private PartialMerkleTree merkleProof;

        /// <summary>
        /// Gets merkle proof that proves the coinstake tx is included in a block that is being represented by the provided header.
        /// </summary>
        public PartialMerkleTree MerkleProof => this.merkleProof;

        /// <summary>
        /// Block header signature which is signed with the private key which corresponds to
        /// coinstake's second output's public key.
        /// </summary>
        private BlockSignature signature;

        /// <summary>
        /// Gets block header signature which is signed with the private key which corresponds to
        /// coinstake's second output's public key.
        /// </summary>
        public BlockSignature Signature => this.signature;

        /// <summary>The size of the proven block header in bytes, the header must be serialized for this property to be set.</summary>
        public long? HeaderSize { get; protected set; }

        public ProvenBlockHeader()
        {
        }

        public ProvenBlockHeader(PosBlock block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));

            // Copy block header properties.
            this.HashPrevBlock = block.Header.HashPrevBlock;
            this.HashMerkleRoot = block.Header.HashMerkleRoot;
            this.Time = block.Header.Time;
            this.Bits = block.Header.Bits;
            this.Nonce = block.Header.Nonce;
            this.Version = block.Header.Version;

            // Set additional properties.
            this.HeaderSize = null;
            this.signature = block.BlockSignature;
            this.coinstake = block.Transactions[1];

            this.merkleProof = new MerkleBlock(block, new[] { this.coinstake.GetHash() }).PartialMerkleTree;
        }

        /// <inheritdoc />
        public override void ReadWrite(BitcoinStream stream)
        {
            base.ReadWrite(stream);
            stream.ReadWrite(ref this.merkleProof);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.coinstake);

            this.HeaderSize = stream.Serializing ? stream.Counter.WrittenBytes : stream.Counter.ReadBytes;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this.GetHash().ToString();
        }
    }
}
