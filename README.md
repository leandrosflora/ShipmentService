# ShipmentService

Microserviço em C#/.NET 8 responsável pela remessa física gerada a partir de um pedido.

## Principais capacidades

- Consome `CreateShipmentCommand` de forma idempotente com Inbox.
- Persiste remessas, snapshot do endereço, pacotes e itens.
- Usa worker com lease para reservar remessas pendentes e chamar o Carrier Service fora da transação local.
- Usa `ShipmentId` como chave de idempotência externa no Carrier Service.
- Armazena etiquetas em filesystem local para desenvolvimento, mantendo no banco apenas chave e hash.
- Publica eventos de integração por Outbox (`ShipmentCreatedIntegrationEvent` e `ShipmentCreationFailedIntegrationEvent`).
- Expõe API mínima para consulta de remessa, URL de etiqueta e solicitação de cancelamento.

## Configuração

Configure `ConnectionStrings:ShipmentDb`, `Services:CarrierService` e, opcionalmente, `LabelStorage:Directory` em `appsettings.json` ou variáveis de ambiente.

O arquivo `Infrastructure/Persistence/schema.sql` contém o schema PostgreSQL mínimo compatível com o mapeamento EF Core.
