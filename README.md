# ShipmentService

## Visão geral

`ShipmentService` é um microserviço .NET 8 responsável por registrar solicitações de envio, reservar remessas junto a um serviço externo de transportadora, armazenar etiquetas e publicar eventos de integração para outros serviços do ecossistema.

O serviço foi modelado para operar de forma assíncrona e resiliente, usando os padrões **Inbox**, **Outbox**, **lease de processamento**, **idempotência** e **retry com backoff exponencial** para reduzir duplicidade de mensagens, perda de eventos e concorrência indevida entre workers.

## Índice

- [Principais responsabilidades](#principais-responsabilidades)
- [Arquitetura e componentes](#arquitetura-e-componentes)
- [Fluxos de negócio](#fluxos-de-negócio)
- [API HTTP](#api-http)
- [Contratos de mensagens](#contratos-de-mensagens)
- [Modelo de domínio](#modelo-de-domínio)
- [Persistência](#persistência)
- [Configuração](#configuração)
- [Execução local](#execução-local)
- [Banco de dados](#banco-de-dados)
- [Observabilidade e saúde](#observabilidade-e-saúde)
- [Resiliência, concorrência e idempotência](#resiliência-concorrência-e-idempotência)
- [Testes e validações](#testes-e-validações)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Próximos passos recomendados](#próximos-passos-recomendados)

## Principais responsabilidades

- Receber e processar comandos `CreateShipmentCommand` de forma idempotente.
- Persistir remessas, endereço de destino, volumes/pacotes e itens.
- Reservar remessas pendentes para booking no Carrier Service por meio de worker em segundo plano.
- Chamar o serviço externo de transportadora com chave de idempotência baseada no `ShipmentId`.
- Armazenar etiquetas de envio em filesystem local no ambiente atual.
- Manter no banco a chave de armazenamento da etiqueta e o hash SHA-256 do conteúdo.
- Publicar eventos de sucesso e falha via Outbox.
- Expor endpoints HTTP mínimos para consulta, geração de URL de etiqueta e solicitação de cancelamento.
- Disponibilizar health check da aplicação e do `DbContext`.

## Arquitetura e componentes

O projeto está organizado em camadas lógicas dentro de um único projeto .NET:

```text
ShipmentService
├── Api/                         # Endpoints HTTP minimal API
├── Application/                 # Casos de uso, processors e serviços de aplicação
│   └── Ports/                   # Interfaces/portas para infraestrutura
├── Contracts/                   # Comandos, DTOs e eventos de integração
├── Domain/                      # Entidades, value objects e enumerações do domínio
├── Infrastructure/
│   ├── Carrier/                 # Cliente HTTP do Carrier Service
│   ├── Outbox/                  # Escrita e dispatch de mensagens Outbox
│   ├── Persistence/             # EF Core, repositório e schema SQL
│   ├── Storage/                 # Armazenamento local de etiquetas
│   └── Workers/                 # Background workers
├── Program.cs                   # Bootstrap, DI, middleware e mapeamento de endpoints
├── appsettings.json             # Configuração base
└── ShipmentService.csproj       # Dependências e target framework
```

### Componentes principais

| Componente | Responsabilidade |
| --- | --- |
| `ShipmentCreationHandler` | Processa `CreateShipmentCommand`, aplica Inbox e cria a remessa quando ainda não existe. |
| `CarrierBookingWorker` | Executa periodicamente a busca por remessas aptas a booking e processa em paralelo. |
| `ShipmentRepository` | Implementa o lease de remessas com SQL transacional e `FOR UPDATE SKIP LOCKED`. |
| `CarrierBookingProcessor` | Chama o Carrier Service, armazena etiqueta, marca remessa como pronta ou agenda retry/falha. |
| `ShipmentCancellationService` | Recebe solicitação idempotente de cancelamento e publica comando para cancelamento na transportadora. |
| `OutboxWriter` | Serializa mensagens e grava registros na tabela `outbox_messages`. |
| `OutboxDispatcher` | Publica mensagens pendentes da Outbox e marca como processadas. |
| `FileSystemLabelStorage` | Persiste etiquetas no filesystem local e gera URL de download simulada. |
| `CarrierShipmentClient` | Cliente HTTP para criar remessas no serviço externo de transportadora. |

## Fluxos de negócio

### 1. Criação de remessa

1. Um consumidor de mensagens ou adapter externo chama `ShipmentCreationHandler.HandleAsync` com um `CreateShipmentCommand`.
2. O handler verifica se `MessageId` já existe em `inbox_messages`.
3. Se a mensagem ainda não foi processada, abre uma transação.
4. O serviço consulta se já existe remessa para o mesmo `ShipmentRequestId`.
5. Caso não exista, cria a entidade `Shipment` com status inicial `PendingCarrierBooking`.
6. Grava a mensagem na Inbox e confirma a transação.
7. A remessa fica disponível para o `CarrierBookingWorker`.

### 2. Booking no Carrier Service

1. `CarrierBookingWorker` executa um ciclo a cada 2 segundos.
2. `ShipmentRepository.ClaimBookableAsync` busca até 20 remessas elegíveis:
   - `PendingCarrierBooking`;
   - `RetryScheduled` com `next_attempt_at <= NOW()`;
   - `BookingInProgress` com lease expirado.
3. As remessas são atualizadas para `BookingInProgress`, recebem `processing_token`, `processing_lease_until` e incremento de tentativa.
4. O worker processa os IDs em paralelo com grau máximo de paralelismo 5.
5. `CarrierBookingProcessor` envia `POST /carrier-shipments` para o Carrier Service.
6. A chave `Idempotency-Key` enviada ao Carrier Service é o `ShipmentId` sem hífens (`N`).
7. Em caso de sucesso:
   - decodifica a etiqueta em Base64;
   - armazena o arquivo localmente;
   - marca a remessa como `ReadyToShip`;
   - grava `ShipmentCreatedIntegrationEvent` na Outbox.
8. Em erro permanente (`422 Unprocessable Entity`):
   - marca a remessa como `Failed`;
   - grava `ShipmentCreationFailedIntegrationEvent` na Outbox.
9. Em erro transitório:
   - agenda nova tentativa com backoff exponencial;
   - após o limite de tentativas, marca falha permanente.

### 3. Publicação de eventos Outbox

1. `OutboxDispatcher` executa um ciclo a cada 5 segundos.
2. Busca até 50 mensagens não processadas, ordenadas por `created_at`.
3. Publica cada mensagem via `IMessagePublisher`.
4. Marca `processed_at` no registro publicado.

> A implementação atual de publisher (`LoggingMessagePublisher`) apenas registra log. Para produção, substitua por integração com broker real, como RabbitMQ, Kafka, Azure Service Bus, Amazon SNS/SQS ou outro padrão adotado pelo time.

### 4. Cancelamento de remessa

1. Cliente chama `POST /shipments/{shipmentId}/cancel` com header obrigatório `Idempotency-Key`.
2. O serviço calcula um identificador determinístico do comando usando SHA-256 de `{shipmentId}:{idempotencyKey}`.
3. Se a operação já consta na Inbox, retorna sem reprocessar.
4. Carrega a remessa e valida se o status permite cancelamento.
5. Marca a remessa como `CancellationRequested`.
6. Publica na Outbox uma mensagem anônima com `CommandType = "CancelCarrierShipment"` no tópico `carrier-shipment.commands`.
7. Retorna `202 Accepted` com a localização da remessa.

## API HTTP

A API é implementada com Minimal APIs e agrupa os endpoints em `/shipments`.

### `GET /health`

Health check da aplicação, incluindo verificação do `ShipmentDbContext`.

**Exemplo:**

```bash
curl -i http://localhost:5000/health
```

### `GET /shipments/{shipmentId}`

Consulta uma remessa por identificador.

**Parâmetros:**

| Nome | Tipo | Local | Obrigatório | Descrição |
| --- | --- | --- | --- | --- |
| `shipmentId` | `guid` | path | Sim | Identificador da remessa. |

**Respostas:**

| Status | Descrição |
| --- | --- |
| `200 OK` | Remessa encontrada. |
| `404 Not Found` | Remessa inexistente. |

**Exemplo:**

```bash
curl http://localhost:5000/shipments/00000000-0000-0000-0000-000000000001
```

**Exemplo de resposta:**

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "orderId": "00000000-0000-0000-0000-000000000010",
  "status": "ReadyToShip",
  "carrierCode": "CORREIOS",
  "serviceLevelCode": "SEDEX",
  "trackingCode": "BR123456789BR",
  "promisedDeliveryDate": "2026-06-12",
  "createdAt": "2026-06-10T12:00:00+00:00",
  "readyAt": "2026-06-10T12:00:10+00:00",
  "packages": [
    {
      "sequence": 1,
      "weightKg": 1.2,
      "heightCm": 10,
      "widthCm": 20,
      "lengthCm": 30,
      "items": [
        {
          "skuId": "00000000-0000-0000-0000-000000000100",
          "quantity": 2
        }
      ]
    }
  ]
}
```

### `GET /shipments/{shipmentId}/label`

Gera uma URL temporária para download da etiqueta associada à remessa.

**Parâmetros:**

| Nome | Tipo | Local | Obrigatório | Descrição |
| --- | --- | --- | --- | --- |
| `shipmentId` | `guid` | path | Sim | Identificador da remessa. |

**Respostas:**

| Status | Descrição |
| --- | --- |
| `200 OK` | URL gerada com validade de 5 minutos. |
| `404 Not Found` | Remessa inexistente ou sem etiqueta armazenada. |

**Exemplo:**

```bash
curl http://localhost:5000/shipments/00000000-0000-0000-0000-000000000001/label
```

**Exemplo de resposta:**

```json
{
  "url": "https://shipment.local/labels/00000000000000000000000000000001-ABCD1234EF567890.pdf",
  "expiresInSeconds": 300
}
```

> Na implementação atual, a URL é simulada por `FileSystemLabelStorage`. Em produção, recomenda-se usar URL assinada do provedor de storage adotado.

### `POST /shipments/{shipmentId}/cancel`

Solicita cancelamento idempotente da remessa.

**Headers:**

| Nome | Obrigatório | Descrição |
| --- | --- | --- |
| `Idempotency-Key` | Sim | Chave fornecida pelo cliente para evitar duplicidade da mesma solicitação. |

**Parâmetros:**

| Nome | Tipo | Local | Obrigatório | Descrição |
| --- | --- | --- | --- | --- |
| `shipmentId` | `guid` | path | Sim | Identificador da remessa. |

**Respostas:**

| Status | Descrição |
| --- | --- |
| `202 Accepted` | Solicitação aceita. |
| `400 Bad Request` | Header `Idempotency-Key` ausente ou vazio. |
| `404 Not Found` | Remessa inexistente. |
| `409 Conflict` | Status atual da remessa não permite cancelamento. |

**Exemplo:**

```bash
curl -i -X POST \
  http://localhost:5000/shipments/00000000-0000-0000-0000-000000000001/cancel \
  -H "Idempotency-Key: cancelamento-0001"
```

## Contratos de mensagens

### Comando de criação: `CreateShipmentCommand`

Representa a solicitação para criar uma remessa.

| Campo | Tipo | Descrição |
| --- | --- | --- |
| `MessageId` | `Guid` | Identificador único da mensagem usado pela Inbox. |
| `ShipmentRequestId` | `Guid` | Identificador idempotente da solicitação de remessa. |
| `OrderId` | `Guid` | Pedido relacionado. |
| `BuyerId` | `Guid` | Comprador. |
| `SellerId` | `Guid` | Vendedor. |
| `ShippingPromiseId` | `string` | Promessa de frete selecionada. |
| `RouteId` | `string` | Rota logística. |
| `CarrierCode` | `string` | Código da transportadora. |
| `ServiceLevelCode` | `string` | Serviço/modalidade da transportadora. |
| `OriginNodeId` | `Guid` | Nó de origem da expedição. |
| `PromisedDeliveryDate` | `DateOnly` | Data prometida de entrega. |
| `Destination` | `ShipmentAddressDto` | Endereço de destino. |
| `Packages` | `IReadOnlyList<CreateShipmentPackageDto>` | Volumes da remessa. |

**Exemplo JSON conceitual:**

```json
{
  "messageId": "00000000-0000-0000-0000-000000000201",
  "shipmentRequestId": "00000000-0000-0000-0000-000000000202",
  "orderId": "00000000-0000-0000-0000-000000000010",
  "buyerId": "00000000-0000-0000-0000-000000000020",
  "sellerId": "00000000-0000-0000-0000-000000000030",
  "shippingPromiseId": "promise-123",
  "routeId": "route-sp-rj-01",
  "carrierCode": "CORREIOS",
  "serviceLevelCode": "SEDEX",
  "originNodeId": "00000000-0000-0000-0000-000000000040",
  "promisedDeliveryDate": "2026-06-12",
  "destination": {
    "recipientName": "Maria Silva",
    "street": "Rua Exemplo",
    "number": "100",
    "complement": "Apto 10",
    "district": "Centro",
    "city": "São Paulo",
    "state": "SP",
    "postalCode": "01001000",
    "country": "BRA",
    "phone": "+5511999999999"
  },
  "packages": [
    {
      "sequence": 1,
      "weightKg": 1.2,
      "heightCm": 10,
      "widthCm": 20,
      "lengthCm": 30,
      "items": [
        {
          "skuId": "00000000-0000-0000-0000-000000000100",
          "quantity": 2
        }
      ]
    }
  ]
}
```

### Requisição ao Carrier Service: `CreateCarrierShipmentRequest`

Enviada via HTTP para `POST /carrier-shipments` no serviço externo configurado em `Services:CarrierService`.

**Campos principais:**

- `ShipmentId`
- `CarrierCode`
- `ServiceLevelCode`
- `RouteId`
- `OriginNodeId`
- `Destination`
- `Packages`

### Resposta do Carrier Service: `CreateCarrierShipmentResponse`

| Campo | Tipo | Descrição |
| --- | --- | --- |
| `ExternalShipmentId` | `string` | Identificador da remessa no Carrier Service. |
| `TrackingCode` | `string` | Código de rastreio. |
| `LabelMimeType` | `string` | Tipo MIME da etiqueta, por exemplo `application/pdf`. |
| `LabelContentBase64` | `string` | Conteúdo da etiqueta codificado em Base64. |

### Eventos de integração publicados

#### `ShipmentCreatedIntegrationEvent`

Publicado no tópico `shipment.events` quando o booking é concluído com sucesso.

Inclui:

- `MessageId`
- `ShipmentId`
- `OrderId`
- `CarrierCode`
- `ServiceLevelCode`
- `ExternalShipmentId`
- `TrackingCode`
- `LabelObjectKey`
- `PromisedDeliveryDate`
- `OccurredAt`

#### `ShipmentCreationFailedIntegrationEvent`

Publicado no tópico `shipment.events` quando a criação da remessa falha permanentemente.

Inclui:

- `MessageId`
- `ShipmentId`
- `OrderId`
- `CarrierCode`
- `Reason`
- `OccurredAt`

#### Comando de cancelamento para transportadora

Publicado no tópico `carrier-shipment.commands` quando o endpoint de cancelamento é aceito.

Payload atual:

```json
{
  "messageId": "guid-gerado",
  "shipmentId": "guid-da-remessa",
  "carrierCode": "CORREIOS",
  "externalShipmentId": "id-externo",
  "commandType": "CancelCarrierShipment"
}
```

## Modelo de domínio

### Entidade `Shipment`

A entidade central representa a remessa e concentra dados de pedido, comprador, vendedor, rota, transportadora, etiqueta, tentativas de booking, controle de concorrência, status e timestamps.

### Status possíveis

| Status | Descrição |
| --- | --- |
| `PendingCarrierBooking` | Remessa criada e aguardando booking no Carrier Service. |
| `BookingInProgress` | Worker reservou a remessa e está processando o booking. |
| `RetryScheduled` | Houve falha transitória e uma nova tentativa foi agendada. |
| `ReadyToShip` | Booking concluído, código de rastreio e etiqueta disponíveis. |
| `HandedOver` | Remessa entregue à transportadora. |
| `InTransit` | Remessa em trânsito. |
| `Delivered` | Remessa entregue ao destinatário. |
| `CancellationRequested` | Cancelamento solicitado e aguardando integração. |
| `Cancelled` | Remessa cancelada. |
| `Failed` | Falha permanente no processo de criação/booking. |

### Validações de domínio

- Endereço exige `RecipientName` e `PostalCode` preenchidos.
- Pacote exige:
  - `Sequence` positivo;
  - peso positivo;
  - altura, largura e comprimento positivos;
  - pelo menos um item.
- Item exige:
  - `SkuId` diferente de `Guid.Empty`;
  - `Quantity` positiva.
- Cancelamento não é permitido para remessas em `HandedOver`, `InTransit` ou `Delivered`.

## Persistência

O serviço usa EF Core com PostgreSQL via provider `Npgsql.EntityFrameworkCore.PostgreSQL`.

### Tabelas

| Tabela | Finalidade |
| --- | --- |
| `shipments` | Dados principais da remessa, status, etiqueta, retries e endereço de destino. |
| `shipment_packages` | Volumes/pacotes da remessa. |
| `shipment_package_items` | Itens contidos em cada pacote. |
| `inbox_messages` | Controle de mensagens/comandos já processados. |
| `outbox_messages` | Mensagens a publicar para integrações externas. |

### Índices e unicidade

- `shipments.shipment_request_id` é único para impedir duplicidade de remessa por solicitação.
- `shipments.external_shipment_id` é único quando preenchido.
- Índice composto por `status`, `next_attempt_at` e `processing_lease_until` acelera seleção de remessas aptas a booking.
- `shipment_packages` possui unicidade por `shipment_id` e `sequence`.
- `outbox_messages` possui índice por `processed_at` e `created_at` para dispatch eficiente.

### Schema SQL

O arquivo `Infrastructure/Persistence/schema.sql` contém um schema PostgreSQL compatível com os mapeamentos atuais do EF Core.

Para aplicar localmente em um banco já criado:

```bash
psql "Host=localhost Port=5432 Dbname=shipment User=shipment Password=shipment" \
  -f Infrastructure/Persistence/schema.sql
```

## Configuração

A configuração base está em `appsettings.json`.

```json
{
  "ConnectionStrings": {
    "ShipmentDb": "Host=localhost;Port=5432;Database=shipment;Username=shipment;Password=shipment"
  },
  "Services": {
    "CarrierService": "https://carrier.local"
  },
  "LabelStorage": {
    "Directory": "labels"
  }
}
```

### Chaves de configuração

| Chave | Obrigatória | Descrição | Exemplo |
| --- | --- | --- | --- |
| `ConnectionStrings:ShipmentDb` | Sim | Connection string PostgreSQL. | `Host=localhost;Port=5432;Database=shipment;Username=shipment;Password=shipment` |
| `Services:CarrierService` | Sim | URL base do Carrier Service. | `https://carrier.local` |
| `LabelStorage:Directory` | Não | Diretório para armazenar etiquetas no filesystem. | `labels` |
| `Logging:LogLevel:Default` | Não | Nível padrão de log. | `Information` |

### Variáveis de ambiente

Em .NET, use `__` para representar `:` em variáveis de ambiente:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__ShipmentDb='Host=localhost;Port=5432;Database=shipment;Username=shipment;Password=shipment'
export Services__CarrierService='https://carrier.local'
export LabelStorage__Directory='labels'
```

## Execução local

### Pré-requisitos

- .NET SDK 8.0.
- PostgreSQL acessível.
- Schema aplicado no banco.
- URL do Carrier Service configurada.

### Restaurar dependências

```bash
dotnet restore
```

### Compilar

```bash
dotnet build
```

### Executar

Como o projeto está na raiz do repositório:

```bash
dotnet run --project ShipmentService.csproj
```

A porta padrão depende do perfil em `Properties/launchSettings.json` ou da variável `ASPNETCORE_URLS`.

Exemplo definindo URL manualmente:

```bash
ASPNETCORE_URLS=http://localhost:5000 dotnet run --project ShipmentService.csproj
```

### Swagger/OpenAPI

Em ambiente `Development`, o serviço habilita Swagger e Swagger UI.

Acesse, conforme a URL em execução:

```text
http://localhost:5000/swagger
```

## Banco de dados

### Criar banco PostgreSQL local com Docker

Exemplo opcional para desenvolvimento:

```bash
docker run --name shipment-postgres \
  -e POSTGRES_USER=shipment \
  -e POSTGRES_PASSWORD=shipment \
  -e POSTGRES_DB=shipment \
  -p 5432:5432 \
  -d postgres:16
```

Aplicar schema:

```bash
psql "Host=localhost Port=5432 Dbname=shipment User=shipment Password=shipment" \
  -f Infrastructure/Persistence/schema.sql
```

> O repositório atual não contém migrations EF Core versionadas. O schema SQL é a referência operacional presente no projeto.

## Observabilidade e saúde

- `GET /health` valida a aplicação e o `ShipmentDbContext`.
- Logs são emitidos pelo logging padrão do ASP.NET Core.
- Falhas nos workers são capturadas e registradas, evitando encerramento silencioso do processo.
- Publicações pela implementação atual de `IMessagePublisher` são registradas em log.

## Resiliência, concorrência e idempotência

### Inbox

A Inbox evita reprocessamento de comandos já vistos:

- `CreateShipmentCommand` usa `MessageId`.
- Cancelamento calcula um `Guid` determinístico a partir de `shipmentId` e `Idempotency-Key`.

### Outbox

A Outbox garante que alterações locais e mensagens de integração sejam gravadas na mesma transação antes da publicação assíncrona.

### Lease de processamento

O worker usa `processing_token` e `processing_lease_until` para reservar remessas de forma segura entre múltiplas instâncias. A consulta usa `FOR UPDATE SKIP LOCKED`, permitindo concorrência horizontal com PostgreSQL.

### Retry

- Tentativas transitórias de booking são reagendadas com backoff exponencial.
- O atraso é limitado a 300 segundos.
- O limite atual é de 8 tentativas.
- Erros permanentes do Carrier Service (`422`) não passam por retry.

### Cliente HTTP resiliente

O `HttpClient` do Carrier Service usa `Microsoft.Extensions.Http.Resilience` com:

- timeout total de 8 segundos;
- timeout por tentativa de 4 segundos;
- circuit breaker com taxa de falha de 50%;
- throughput mínimo de 10;
- janela de amostragem de 30 segundos;
- duração de abertura do circuito de 20 segundos.

## Testes e validações

O repositório atual não contém projetos de teste dedicados. Para validação básica, execute:

```bash
dotnet restore
dotnet build
```

Quando testes forem adicionados, recomenda-se:

```bash
dotnet test
```

Sugestões de cobertura futura:

- Testes unitários para validações de `Shipment`, `ShipmentAddress` e `ShipmentPackage`.
- Testes de idempotência para `ShipmentCreationHandler` e `ShipmentCancellationService`.
- Testes de retry/falha permanente em `CarrierBookingProcessor`.
- Testes de integração com PostgreSQL para `ClaimBookableAsync` e Outbox.
- Contract tests para integração com Carrier Service.

## Estrutura do repositório

```text
.
├── Api/
│   └── ShipmentEndpoints.cs
├── Application/
│   ├── CarrierBookingProcessor.cs
│   ├── ShipmentCancellationService.cs
│   ├── ShipmentCreationHandler.cs
│   └── Ports/
├── Contracts/
│   ├── CarrierContracts.cs
│   ├── CreateShipmentCommand.cs
│   └── IntegrationEvents.cs
├── Domain/
│   ├── Shipment.cs
│   ├── ShipmentAddress.cs
│   ├── ShipmentPackage.cs
│   └── ShipmentStatus.cs
├── Infrastructure/
│   ├── Carrier/
│   ├── Outbox/
│   ├── Persistence/
│   ├── Storage/
│   └── Workers/
├── Properties/
│   └── launchSettings.json
├── Program.cs
├── ShipmentService.csproj
├── ShipmentService.sln
├── appsettings.Development.json
├── appsettings.json
└── README.md
```

## Próximos passos recomendados

- Implementar publisher real para o broker adotado pelo ecossistema.
- Adicionar migrations EF Core ou padronizar evolução de schema via ferramenta de banco.
- Substituir `FileSystemLabelStorage` por storage durável em produção.
- Adicionar autenticação/autorização para endpoints HTTP.
- Adicionar métricas de workers, Outbox, retries e latência do Carrier Service.
- Criar testes automatizados unitários, integração e contratos.
- Documentar adapters responsáveis por consumir mensagens e acionar `ShipmentCreationHandler`.
