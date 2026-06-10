# ShipmentService

## Visão geral

`ShipmentService` é um serviço de backend em .NET 8 responsável pelo gerenciamento de envios (criação, atualização, consulta e rastreamento). Este README fornece instruções para configuração, execução, testes e contribuições.

## Índice

- [Requisitos](#requisitos)
- [Instalação](#instalação)
- [Configuração](#configuração)
- [Executando a aplicação](#executando-a-aplicação)
- [Testes](#testes)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Uso da API (exemplos)](#uso-da-api-exemplos)
- [Deploy e CI/CD](#deploy-e-cicd)
- [Contribuindo](#contribuindo)
- [Licença](#licença)
- [Suporte](#suporte)

## Principais capacidades

- Consome `CreateShipmentCommand` de forma idempotente com Inbox.
- Persiste remessas, snapshot do endereço, pacotes e itens.
- Usa worker com lease para reservar remessas pendentes e chamar o Carrier Service fora da transação local.
- Usa `ShipmentId` como chave de idempotência externa no Carrier Service.
- Armazena etiquetas em filesystem local para desenvolvimento, mantendo no banco apenas chave e hash.
- Publica eventos de integração por Outbox (`ShipmentCreatedIntegrationEvent` e `ShipmentCreationFailedIntegrationEvent`).
- Expõe API mínima para consulta de remessa, URL de etiqueta e solicitação de cancelamento.

## Requisitos

- .NET 8 SDK
- Git
- (Opcional) Docker e Docker Compose
- (Opcional) Banco de dados compatível (por exemplo, SQL Server, PostgreSQL) se a aplicação não usar armazenamento em memória

## Instalação

1. Clone o repositório:

    ```bash
    git clone https://github.com/leandrosflora/ShipmentService.git
    cd ShipmentService
    ```

2. Restaure pacotes e compile:

    ```bash
    dotnet restore
    dotnet build --configuration Release
    ```

## Configuração

Configure `ConnectionStrings:ShipmentDb`, `Services:CarrierService` e, opcionalmente, `LabelStorage:Directory` em `appsettings.json` ou variáveis de ambiente.

- Variáveis de ambiente e `appsettings.json` controlam conexões (banco de dados), chaves de API, e configurações de logging.
- Exemplo de variáveis esperadas (ajuste conforme seu ambiente):
  - `ASPNETCORE_ENVIRONMENT` (Development/Production)
  - `ConnectionStrings:DefaultConnection`
  - `Logging:LogLevel:Default`

Crie um arquivo `appsettings.Development.json` local com as chaves necessárias ou configure variáveis de ambiente.

O arquivo `Infrastructure/Persistence/schema.sql` contém o schema PostgreSQL mínimo compatível com o mapeamento EF Core.

## Executando a aplicação

Executar localmente:

```bash
dotnet run --project src/ShipmentService.Api
```

A aplicação iniciará na porta definida em `launchSettings.json` ou pela variável `ASPNETCORE_URLS`.

Executar com Docker (se houver `Dockerfile`):

```bash
docker build -t shipmentservice:local .
docker run -e ASPNETCORE_ENVIRONMENT=Production -p 5000:80 shipmentservice:local
```

## Testes

Execute a suíte de testes com:

```bash
dotnet test --no-build
```

Verifique cobertura e resultados no console. Ajuste perfis de teste conforme necessário.

## Estrutura do projeto

A estrutura típica esperada:

- `src/ShipmentService.Api` — API REST
- `src/ShipmentService.Core` — lógica de domínio
- `src/ShipmentService.Infrastructure` — integração com DB, serviços externos
- `tests/ShipmentService.Tests` — testes unitários e de integração

Adapte conforme a organização real do repositório.

## Uso da API (exemplos)

Os exemplos abaixo usam `curl` e são ilustrativos. Ajuste caminhos e payloads conforme o contrato real da API.

Criar um envio:

```bash
curl -X POST "http://localhost:5000/api/shipments" \
  -H "Content-Type: application/json" \
  -d '{ "orderId": "12345", "destination": "Rua Exemplo, 100", "weight": 2.5 }'
```

Consultar um envio:

```bash
curl "http://localhost:5000/api/shipments/{shipmentId}"
```

Listar envios:

```bash
curl "http://localhost:5000/api/shipments?page=1&pageSize=20"
```

Rastrear status do envio:

```bash
curl "http://localhost:5000/api/shipments/{shipmentId}/tracking"
```

Observação: substitua `localhost:5000` pela URL real do serviço e `{shipmentId}` pelo identificador retornado ao criar o envio.

## Deploy e CI/CD

Sugestões:
- Use GitHub Actions, Azure DevOps ou outro pipeline para executar `dotnet build`, `dotnet test` e publicar artefatos.
- Para containerização, publique a imagem em um registro (Docker Hub, GitHub Container Registry ou Azure Container Registry).
- Garanta variáveis secretas no gerenciador de segredos do seu provedor.

## Contribuindo

Leia `CONTRIBUTING.md` para diretrizes de contribuição. Boas práticas sugeridas:
- Abra issues para discutir mudanças grandes.
- Use branches nomeadas `feature/...`, `fix/...`.
- Crie PRs pequenos com descrição clara e testes.

## Licença

Adicione aqui a licença do projeto (ex.: MIT). Se não existir arquivo `LICENSE`, crie um conforme a política do time.

## Suporte

Para suporte, abra uma issue ou contate os mantenedores do repositório.
