CREATE TABLE shipments (
    id UUID PRIMARY KEY,
    shipment_request_id UUID NOT NULL UNIQUE,
    order_id UUID NOT NULL,
    buyer_id UUID NOT NULL,
    seller_id UUID NOT NULL,
    shipping_promise_id VARCHAR(200) NOT NULL,
    route_id VARCHAR(200) NOT NULL,
    carrier_code VARCHAR(80) NOT NULL,
    service_level_code VARCHAR(80) NOT NULL,
    origin_node_id UUID NOT NULL,
    promised_delivery_date DATE NOT NULL,
    status VARCHAR(50) NOT NULL,
    external_shipment_id VARCHAR(200) NULL UNIQUE,
    tracking_code VARCHAR(200) NULL,
    label_object_key VARCHAR(500) NULL,
    label_sha256 VARCHAR(64) NULL,
    booking_attempts INTEGER NOT NULL,
    next_attempt_at TIMESTAMPTZ NULL,
    last_error VARCHAR(1000) NULL,
    processing_token UUID NULL,
    processing_lease_until TIMESTAMPTZ NULL,
    version BIGINT NOT NULL,
    recipient_name VARCHAR(200) NOT NULL,
    street VARCHAR(300) NOT NULL,
    number VARCHAR(50) NOT NULL,
    complement VARCHAR(200) NULL,
    district VARCHAR(200) NOT NULL,
    city VARCHAR(200) NOT NULL,
    state VARCHAR(50) NOT NULL,
    destination_postal_code VARCHAR(20) NOT NULL,
    country VARCHAR(3) NOT NULL,
    phone VARCHAR(50) NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    ready_at TIMESTAMPTZ NULL,
    cancelled_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_shipments_pending
ON shipments (status, next_attempt_at, processing_lease_until);

CREATE UNIQUE INDEX idx_shipments_order
ON shipments (order_id);

CREATE TABLE shipment_packages (
    id UUID PRIMARY KEY,
    shipment_id UUID NOT NULL,
    sequence INTEGER NOT NULL,
    weight_kg NUMERIC(12,3) NOT NULL,
    height_cm NUMERIC(12,2) NOT NULL,
    width_cm NUMERIC(12,2) NOT NULL,
    length_cm NUMERIC(12,2) NOT NULL,
    FOREIGN KEY (shipment_id) REFERENCES shipments(id),
    UNIQUE (shipment_id, sequence)
);

CREATE TABLE shipment_package_items (
    id UUID PRIMARY KEY,
    shipment_package_id UUID NOT NULL,
    sku_id UUID NOT NULL,
    quantity INTEGER NOT NULL,
    FOREIGN KEY (shipment_package_id) REFERENCES shipment_packages(id)
);

CREATE TABLE inbox_messages (
    message_id UUID PRIMARY KEY,
    message_type VARCHAR(200) NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE outbox_messages (
    id UUID PRIMARY KEY,
    topic VARCHAR(200) NOT NULL,
    message_type VARCHAR(200) NOT NULL,
    aggregate_key VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_outbox_unprocessed
ON outbox_messages (processed_at, created_at);
