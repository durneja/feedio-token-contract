using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace FeedioTokenContract
{
    [SupportedStandards("NEP-11")]
    [DisplayName("Feedio.FeedioTokenContract")]
    [ManifestExtra("Author", "durneja")]
    [ManifestExtra("Email", "kinshuk.kar@gmail.com")]
    [ManifestExtra("Description", "Token contract for feedio to allow subscription access to price feeds")]
    [ContractPermission("*", "onNEP17Payment")]
    [ContractPermission("*", "onNEP11Payment")]
    public class FeedioTokenContract : SmartContract
    {
        private static Transaction Tx => (Transaction) Runtime.ScriptContainer;
        private static bool ValidateAddress(UInt160 address) => address.IsValid && !address.IsZero;

        public delegate void OnTransferDelegate(UInt160 from, UInt160 to, BigInteger amount, ByteString tokenId);

        [DisplayName("Transfer")]
        public static event OnTransferDelegate OnTransfer;

        protected const byte Prefix_TotalSupply = 0x02;
        protected const byte Prefix_TokenId = 0x02;
        protected const byte Prefix_Token = 0x03;
        protected const byte Prefix_AccountToken = 0x04;
        protected const byte Prefix_AccountBalance = 0x05;
        protected const byte Prefix_Config = 0x06;
        protected const string Prefix_Config_Owner = "o";
        protected const string Prefix_Config_MintFee = "f";
        protected const string Prefix_Config_MintingActive = "m";
        protected const string Prefix_Config_MaxSupply = "s";

        private class FeedioTokenState
        {
            public UInt160 Owner;
            public string Name;
            public ByteString TokenId;
            public string Image;
            public ulong SubscriptionExpiry;

        }

        public byte Decimals() => 0;
        public string Symbol() => "FDIO";

        [Safe]
        public static BigInteger TotalSupply()
        {
            StorageContext context = Storage.CurrentContext;
            byte[] key = new byte[] { Prefix_TotalSupply };
            BigInteger totalSupply = (BigInteger)Storage.Get(context, key);
            return totalSupply;
        }

        [Safe]
        public static BigInteger BalanceOf(UInt160 account)
        {
            if (!ValidateAddress(account)) throw new Exception("The parameters account SHOULD be a 20-byte non-zero address.");

            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_AccountBalance);
            ByteString key = account;
            BigInteger accountBalance = (BigInteger)balanceMap.Get(key);
            return accountBalance;
        }

        [Safe]
        public static UInt160 OwnerOf(ByteString tokenId)
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            FeedioTokenState token = (FeedioTokenState)StdLib.Deserialize(tokenMap[tokenId]);
            return token.Owner;
        }

        [Safe]
        public virtual Map<string, object> Properties(ByteString tokenId)
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            FeedioTokenState token = (FeedioTokenState)StdLib.Deserialize(tokenMap[tokenId]);
            Map<string, object> map = new();
            map["name"] = token.Name;
            map["owner"] = token.Owner;
            map["image"] = token.Image;
            map["tokenId"] = token.TokenId;
            map["tokenSubscriptionExpiry"] = token.SubscriptionExpiry;

            return map;
        }

        [Safe]
        public static Iterator Tokens()
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            return tokenMap.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);
        }

        [Safe]
        public static Iterator TokensOf(UInt160 owner)
        {
            if (owner is null || !owner.IsValid)
                throw new Exception("The argument \"owner\" is invalid");
            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountToken);
            return accountMap.Find(owner, FindOptions.KeysOnly | FindOptions.RemovePrefix);
        }

        public static bool Transfer(UInt160 to, ByteString tokenId, object data)
        {
            if (to is null || !to.IsValid)
                throw new Exception("The argument \"to\" is invalid.");
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            FeedioTokenState token = (FeedioTokenState)StdLib.Deserialize(tokenMap[tokenId]);
            UInt160 from = token.Owner;
            if (!Runtime.CheckWitness(from)) return false;
            if (from != to)
            {
                token.Owner = to;
                tokenMap[tokenId] = StdLib.Serialize(token);
                UpdateBalance(from, tokenId, -1);
                UpdateBalance(to, tokenId, +1);
            }
            PostTransfer(from, to, tokenId, data);
            return true;
        }

        [Safe]
        public static List<object> ListTokensOf(UInt160 owner)
        {
            List<object> byteStringArr = new List<object>();

            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountToken);
            var newAddress = owner;
            var iterator = accountMap.Find(owner, FindOptions.KeysOnly | FindOptions.RemovePrefix);

            while (iterator.Next())
            {
                byteStringArr.Add(iterator.Value);
            }
            return byteStringArr;
        }

        [Safe]
        public static List<object> Snapshot() {
            List<object> byteStringArr = new List<object>();

            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountToken);
            var iterator = accountMap.Find(FindOptions.KeysOnly | FindOptions.RemovePrefix);

            while (iterator.Next())
            {
                byteStringArr.Add(iterator.Value);
            }
        
            return byteStringArr;
        }

        public bool AccessPresentAndValid(UInt160 owner)
        {
            List<object> tokens=  ListTokensOf(owner); 
            if (!(tokens.Count > 0)) return false;

            string tokenId = (ByteString) tokens[0];
            Map<string, object> propertiesMap = Properties(tokenId);
            ulong expiryTime = (ulong) propertiesMap["tokenSubscriptionExpiry"];
            if (expiryTime < Runtime.Time) {
                return false;
            }

            return true;
        }

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            if (Runtime.CallingScriptHash == GAS.Hash)
            {
                BigInteger mintFee = GetMintFee();
                if (amount < mintFee) throw new Exception("Not enough GAS");
                
                BigInteger countOfPassOwned = BalanceOf(from);
                BigInteger multiples = amount / GetMintFee();
                if (countOfPassOwned == 0) {
                    Mint(from, multiples);                    
                } else {
                    var iterator = TokensOf(from);      
                    if (iterator.Next())
                    {
                        StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
                        ByteString tokenId = (ByteString) iterator.Value;
                        FeedioTokenState token = (FeedioTokenState)StdLib.Deserialize(tokenMap[tokenId]);
                        if (token.SubscriptionExpiry < Runtime.Time) {
                            token.SubscriptionExpiry = Runtime.Time + (ulong) (multiples * 2592000000);
                        } else {
                            token.SubscriptionExpiry = token.SubscriptionExpiry + (ulong) (multiples * 2592000000);                            
                        }

                        tokenMap[tokenId] = StdLib.Serialize(token);
                    }
                }
            }
        }

        public static void Mint(UInt160 account, BigInteger multiple)
        {
            if (!VerifyOwner()) throw new Exception("Only owner can mint directly. Transfer GAS based on the price to mint pass.");
            if (DidReachMaxSupply()) throw new Exception("All the tokens have been minted.");
            if (!GetMintActiveStatus()) throw new Exception("Minting is currently not active");
                       
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            UpdateTotalSupply(+1);
            
            ByteString tokenId = (ByteString)TotalSupply();
            FeedioTokenState token = new FeedioTokenState();
            token.Owner = account;
            token.Name = "Feedio Access Pass #" + tokenId;
            token.TokenId = tokenId;
            token.Image = "https://i.postimg.cc/KzdnVfT4/FEEDIO-ACCESS-PASS-1.png";
            token.SubscriptionExpiry = Runtime.Time + (ulong) (multiple * 2592000000);

            tokenMap[tokenId] = StdLib.Serialize(token);
            
            UpdateBalance(token.Owner, tokenId, +1);
            PostTransfer(null, token.Owner, tokenId, null);
        }

        protected static void Burn(ByteString tokenId)
        {
            StorageMap tokenMap = new(Storage.CurrentContext, Prefix_Token);
            FeedioTokenState token = (FeedioTokenState)StdLib.Deserialize(tokenMap[tokenId]);
     
            if(!Runtime.CheckWitness(token.Owner)) {throw new Exception("Not authorized for executing this method");}    
            tokenMap.Delete(tokenId);
            UpdateBalance(token.Owner, tokenId, -1);
            UpdateTotalSupply(-1);
            PostTransfer(token.Owner, null, tokenId, null);
        }

        private static void UpdateBalance(UInt160 owner, ByteString tokenId, BigInteger increment)
        {
            UpdateBalance(owner, increment);
            StorageMap accountMap = new(Storage.CurrentContext, Prefix_AccountToken);
            ByteString key = owner + tokenId;
            if (increment > 0)
                accountMap.Put(key, 0);
            else
                accountMap.Delete(key);
        }

        private static void UpdateBalance(UInt160 owner, BigInteger increment)
        {
            StorageMap balanceMap = new(Storage.CurrentContext, Prefix_AccountBalance);
            ByteString key = owner;
            BigInteger accountBalance = (BigInteger)balanceMap.Get(key);
            accountBalance += increment;
            balanceMap.Put(key, accountBalance);
        }

        private static void UpdateTotalSupply(BigInteger increment)
        {
            StorageContext context = Storage.CurrentContext;
            byte[] key = new byte[] { Prefix_TotalSupply };
            BigInteger totalSupply = (BigInteger)Storage.Get(context, key);
            totalSupply += increment;
            Storage.Put(context, key, totalSupply);
        }

        public static void RetrieveGAS(BigInteger amount) 
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}
            
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            UInt160 owner = (UInt160) configMap.Get(Prefix_Config_Owner);
            GAS.Transfer(Runtime.ExecutingScriptHash, owner, amount);
        }

        public static BigInteger GetMintFee()
        {
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            BigInteger mintFee = (BigInteger)configMap.Get(Prefix_Config_MintFee);
            return mintFee;
        }

        public static void UpdateMintFee(BigInteger mintFee)
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}

            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            configMap.Put(Prefix_Config_MintFee, mintFee);
        }

        public static void UpdateMintActiveStatus(BigInteger mintActiveStatus)
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}

            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            configMap.Put(Prefix_Config_MintingActive, mintActiveStatus);
        }

        private static bool GetMintActiveStatus()
        {
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            BigInteger mintActive = (BigInteger)configMap.Get(Prefix_Config_MintingActive);
            return (mintActive == 1);
        }

        public static void TransferContractGAS(BigInteger amount, UInt160 to) 
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}

            GAS.Transfer(Runtime.ExecutingScriptHash, to, amount);
        }

        private static bool DidReachMaxSupply() {
            BigInteger currentSupply = TotalSupply();
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            BigInteger maxSupply = (BigInteger)configMap.Get(Prefix_Config_MaxSupply);

            if (currentSupply < maxSupply) {
                return false;
            }
            return true;
        }

        private static void PostTransfer(UInt160 from, UInt160 to, ByteString tokenId, object data)
        {
            OnTransfer(from, to, 1, tokenId);
            if (to is not null && ContractManagement.GetContract(to) is not null)
                Contract.Call(to, "onNEP11Payment", CallFlags.All, from, 1, tokenId, data);
        }

        private static Boolean VerifyOwner()
        {
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            UInt160 owner = (UInt160) configMap.Get(Prefix_Config_Owner);
            if (Runtime.CheckWitness(owner))
            {
                return true;
            }
            return false;
        }

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (!update)
            {
                initialize();
            }
        }

        private static void initialize() 
        {
            StorageMap configMap = new(Storage.CurrentContext, Prefix_Config);
            configMap.Put(Prefix_Config_Owner, (UInt160) Tx.Sender);
            configMap.Put(Prefix_Config_MaxSupply, 9999);
            configMap.Put(Prefix_Config_MintFee, 3000000000);
            configMap.Put(Prefix_Config_MintingActive, 0);
        }

        public static void UpdateContract(ByteString nefFile, string manifest)
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}
            ContractManagement.Update(nefFile, manifest, null);
        }

        public static void Destroy()
        {
            if (!VerifyOwner()) { throw new Exception("Not authorized for executing this method");}
            ContractManagement.Destroy();
        }

    }
}
