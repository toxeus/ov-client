# OpenVASP C# Client

This is a reference implementation for a C# client for the OpenVASP standard

### How to build

```
dotnet build --configuration Release "src/OpenVASP.CSharpClient.sln"
```

### How to test

Set up two environment variables:

- WHISPER_RPC_URL - URL for whisper rpc (like geth node rpc https://127.0.0.1:8545)
- ETHEREUM_RPC_URL - URL for eth rpc (like nethermind, geth or parity node rpc https://127.0.0.1:8545)

```
dotnet test --configuration Release "src/OpenVASP.CSharpClient.sln"
```
