# BTCPay Server USDt Plugin

![BTCPay USDt Plugin](Docs/banner.png)

This repository contains the source code for the BTCPay Server plugin that enables the receipt of USDt payments on multiple blockchains. 
The plugin extends the functionality of BTCPay Server, a self-hosted cryptocurrency payment processor that allows merchants to accept Bitcoin and other cryptocurrencies.

## 🎨 Features

- **USDt Payments**: Receive USDt payments directly on your BTCPay Server instance.
- **Multi-Chain Support**: Accept USDt on TRON, Ethereum, and Polygon.
- **Customizable Settings**: Configure JSON RPC endpoints and addresses per blockchain to suit your requirements.
- **Invoice Generation**: Generate invoices with blockchain addresses as payment reception.
- **Blockchain Monitoring**: Scan supported blockchains to detect payments in full, overpaid, or partial amounts.
- **Automatic Settlement**: Continuously verify blockchains to settle payments securely and efficiently.

## Supported blockchains

- [x] TRON
- [x] Ethereum
- [x] Polygon

## 📗 Requirements

- BTCPay Server: Make sure you have a running instance of BTCPay Server. You can find more information and installation instructions [here](https://docs.btcpayserver.org/).
- A compatible wallet for each chain you want to use (e.g., Ledger, MetaMask, TrustWallet...) to generate and manage addresses for receiving USDt payments.

### TRON node requirements

The default public TRON node is provided for demonstration and testing only. It is not suitable for production because its availability and rate limits are outside your control and may delay payment detection.

For production, use a private TRON node or a dedicated, authenticated RPC provider with sufficient availability and rate limits for your payment volume. Configure its JSON-RPC endpoint in the plugin's TRON server settings. Providers that use TronGrid authentication can be configured with the `TRON-PRO-API-KEY` header in the same settings.

### Late-payment monitoring

USDt invoice destinations remain reserved and monitored after invoice expiration until BTCPay's monitoring-expiration period ends. This grace period applies to TRON and EVM chains and is controlled by the store's existing invoice monitoring-expiration setting; the plugin does not add a separate setting. Payments discovered after invoice expiration keep BTCPay's `PaidLate` status.

## 🚀 Installation

Install the plugin from the BTCPay Server > Settings > Plugin > Available Plugins, and restart.

## 🧑‍💻 Developing
### Naming convention
This plugin aims to cover USDt payment over different chains, a rigorous naming convention was implemented to ensure readability but also allow extensibility:

### USD₮
- Currency: `USDt`  
- Currency Display Name: `USD₮` and `USD₮ on BLOCKCHAIN_NAME`

### TRON
- Blockchain: TRON
- PaymentMethodId for USDt: USDT-TRON
- TRON specific implementation: Tron* and TronUSDtLike* for TRC20 compatible stuff

### Ethereum
- Blockchain: Ethereum
- PaymentMethodId for USDt: USDT-Ethereum
- Ethereum specific implementation: Ethereum* and EthereumUSDtLike* for ERC20 compatible stuff

### Polygon
- Blockchain: Polygon
- PaymentMethodId for USDt: USDT-Polygon
- Polygon specific implementation: Polygon* and PolygonUSDtLike* for ERC20 compatible stuff


## 💚 Support

For any questions, issues, or feedback related to the BTCPay Server USDt Plugin, please [open an issue](https://github.com/btcpayserver-tether/BTCPayServer.Plugins.TronUSDt/issues) in this repository.

## 📝 License

This project is licensed under the [MIT License](LICENSE).
