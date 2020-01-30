using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Libplanet.Blocks;
using Libplanet.Store;
using Libplanet.Tx;
using LiteDB;
using FileMode = LiteDB.FileMode;

namespace Libplanet.Explorer.Store
{
    // It assumes running Explorer as online-mode.
    public class RichStore : DefaultStore
    {
        private const string TxRefCollectionName = "block_ref";
        private const string SignerRefCollectionName = "signer_ref";
        private const string UpdatedAddressRefCollectionName = "updated_address_ref";

        private readonly MemoryStream _memoryStream;
        private readonly LiteDatabase _db;

        /// <inheritdoc cref="DefaultStore"/>
        public RichStore(
            string path,
            bool compress = false,
            bool journal = true,
            int indexCacheSize = 50000,
            int blockCacheSize = 512,
            int txCacheSize = 1024,
            int statesCacheSize = 10000,
            bool flush = true,
            bool readOnly = false)
            : base(
                path,
                compress,
                journal,
                indexCacheSize,
                blockCacheSize,
                txCacheSize,
                statesCacheSize,
                flush,
                readOnly)
        {
            if (path is null)
            {
                _memoryStream = new MemoryStream();
                _db = new LiteDatabase(_memoryStream);
            }
            else
            {
                var connectionString = new ConnectionString
                {
                    Filename = Path.Combine(path, "ext.ldb"),
                    Journal = journal,
                    CacheSize = indexCacheSize,
                    Flush = flush,
                };

                if (readOnly)
                {
                    connectionString.Mode = FileMode.ReadOnly;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                         Type.GetType("Mono.Runtime") is null)
                {
                    // macOS + .NETCore doesn't support shared lock.
                    connectionString.Mode = FileMode.Exclusive;
                }

                _db = new LiteDatabase(connectionString);
            }
        }

        public override void PutBlock<T>(Block<T> block)
        {
            base.PutBlock(block);
            foreach (var tx in block.Transactions)
            {
                StoreTxReferences(tx.Id, block.Hash, block.Index);
            }
        }

        public override void PutTransaction<T>(Transaction<T> tx)
        {
            base.PutTransaction(tx);
            StoreSignerReferences(tx.Id, tx.Nonce, tx.Signer);
            foreach (var updatedAddress in tx.UpdatedAddresses)
            {
                StoreUpdatedAddressReferences(tx.Id, tx.Nonce, updatedAddress);
            }
        }

        public void StoreTxReferences(TxId txId, HashDigest<SHA256> blockHash, long blockIndex)
        {
            var collection = TxRefCollection();
            collection.Upsert(
                new TxRefDoc
                {
                    TxId = txId, BlockHash = blockHash, BlockIndex = blockIndex,
                });
            collection.EnsureIndex(nameof(TxRefDoc.TxId));
            collection.EnsureIndex(nameof(TxRefDoc.BlockIndex));
        }

        public IEnumerable<ValueTuple<TxId, HashDigest<SHA256>>> IterateTxReferences(
            TxId? txId = null,
            bool desc = false,
            int offset = 0,
            int limit = int.MaxValue)
        {
            var collection = TxRefCollection();
            var order = desc ? Query.Descending : Query.Ascending;
            var query = Query.All(nameof(TxRefDoc.BlockIndex), order);

            if (!(txId is null))
            {
                query = Query.And(
                    query,
                    Query.EQ(nameof(TxRefDoc.TxId), txId?.ToByteArray())
                );
            }

            return collection.Find(query, offset, limit).Select(doc => (doc.TxId, doc.BlockHash));
        }

        public void StoreSignerReferences(TxId txId, long txNonce, Address signer)
        {
            var collection = SignerRefCollection();
            collection.Upsert(new AddressRefDoc
            {
                Address = signer, TxNonce = txNonce, TxId = txId,
            });
            collection.EnsureIndex(nameof(AddressRefDoc.AddressString));
            collection.EnsureIndex(nameof(AddressRefDoc.TxNonce));
        }

        public IEnumerable<TxId> IterateSignerReferences(
            Address signer,
            bool desc,
            int offset = 0,
            int limit = int.MaxValue)
        {
            var collection = SignerRefCollection();
            var order = desc ? Query.Descending : Query.Ascending;
            var addressString = signer.ToHex().ToLowerInvariant();
            var query = Query.And(
                Query.All(nameof(AddressRefDoc.TxNonce), order),
                Query.EQ(nameof(AddressRefDoc.AddressString), addressString)
            );
            return collection.Find(query, offset, limit).Select(doc => doc.TxId);
        }

        public void StoreUpdatedAddressReferences(
            TxId txId,
            long txNonce,
            Address updatedAddress)
        {
            var collection = UpdatedAddressRefCollection();
            collection.Upsert(new AddressRefDoc
            {
                Address = updatedAddress, TxNonce = txNonce, TxId = txId,
            });
            collection.EnsureIndex(nameof(AddressRefDoc.AddressString));
            collection.EnsureIndex(nameof(AddressRefDoc.TxNonce));
        }

        public IEnumerable<TxId> IterateUpdatedAddressReferences(
            Address updatedAddress,
            bool desc,
            int offset = 0,
            int limit = int.MaxValue)
        {
            var collection = UpdatedAddressRefCollection();
            var order = desc ? Query.Descending : Query.Ascending;
            var addressString = updatedAddress.ToHex().ToLowerInvariant();
            var query = Query.And(
                Query.All(nameof(AddressRefDoc.TxNonce), order),
                Query.EQ(nameof(AddressRefDoc.AddressString), addressString)
            );
            return collection.Find(query, offset, limit).Select(doc => doc.TxId);
        }

        private LiteCollection<TxRefDoc> TxRefCollection() =>
            _db.GetCollection<TxRefDoc>(TxRefCollectionName);

        private LiteCollection<AddressRefDoc> SignerRefCollection() =>
            _db.GetCollection<AddressRefDoc>(SignerRefCollectionName);

        private LiteCollection<AddressRefDoc> UpdatedAddressRefCollection() =>
            _db.GetCollection<AddressRefDoc>(UpdatedAddressRefCollectionName);
    }
}
