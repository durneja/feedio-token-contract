# feedio-token-contract

Feedio Token contract is a Smart Contract written in C# that would serve as the NFT Contract for the Feedio project compliant to NEP-11 standard.
This would act as the subscription or the access pass to the Feedio price feed smart contract. 
The NFT can be minted by transferring GAS to the contract account. The base cost of the NFT is kept at 30 GAS. This would allow access to the price feed data for a period of one month. The expiry timestamp based on the mint time is stored as an NFT attribute itself.
If the amount sent is less than this then the method would throw an exception. Amounts more than the base amount can also be sent in multiples of 30 GAS which case the expiry time period will be set accordingly in the same multiples.
Owner of the contract would directly be able to mint the NFT without payment of GAS fees.
There is a method provided **accessPresentAndValid** that would verify for an address whether they have ownership of the NFT as well as whether it has not expired. This information would be used by the primary contract to allow access to the feed data


Expense of running the project: Approximately 650 GAS per month (Transaction costs - 18.5 GAS / day)
Cost of an NFT for a monthly access: 30 GAS
No. of dapps required for breakeven on an ongoing basis: 22 / 23

Deployed on N3 Testnet (Contract Hash) - 0x8b6a955ef8026cecaf9393f1734bffe508ce42be

https://testnet.explorer.onegate.space/contractinfo/0x8b6a955ef8026cecaf9393f1734bffe508ce42be

