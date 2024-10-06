# BTCPay Server USDt Plugin

![BTCPay USDt Plugin](Docs/banner.png)

This repository contains the source code for the BTCPay Server plugin that enables the receipt of USDt payments on the TRON blockchain. 
The plugin extends the functionality of BTCPay Server, a self-hosted cryptocurrency payment processor that allows merchants to accept Bitcoin and other cryptocurrencies.

## 🎨 Features

- **USDt Payments**: Receive USDt payments directly on your BTCPay Server instance.
- **Customizable Settings**: Configure TRON JSON RPC endpoint and addresses to suit your requirements.
- **Invoice Generation**: Generate invoices with TRON addresses as payment reception.
- **Blockchain Monitoring**: Scan the TRON blockchain to detect payments in full, overpaid, or partial amounts.
- **Automatic Settlement**: Continuously verify the TRON blockchain to settle payments securely and efficiently.

## Supported blockchains

- [x] TRON 

## 📗 Requirements

- BTCPay Server: Make sure you have a running instance of BTCPay Server. You can find more information and installation instructions [here](https://docs.btcpayserver.org/).
- TRON Wallet: Set up a TRON wallet (e.g., Ledger, TrustWallet... ) to generate and manage TRON addresses for receiving USDt payments.

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


## 💚 Support

For any questions, issues, or feedback related to the BTCPay Server USDt Plugin, please [open an issue](https://github.com/b0l0k/BTCPayServer.Plugins.TronUSDt/issues) in this repository.

## 📝 License

This project is licensed under the [MIT License](LICENSE).
