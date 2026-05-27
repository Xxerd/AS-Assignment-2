"""Lightweight ERP surrogate for VerdeMart Phase 2.

Consumes `OrderConfirmedEvent` messages from the `commerce.events` topic
exchange and persists them in a local SQLite file so the demo can show:

  1. orders arrive after ERP has been down (replay from outbox), and
  2. exactly-once-per-eventid persistence (idempotent insert).

This is intentionally not full ERPNext — see compose/erpnext.yml for the
heavyweight surrogate. ISSUE.md §5 documents the trade-off.
"""

import json
import logging
import os
import sqlite3
import sys
import time
from datetime import datetime, timezone

import pika

LOG = logging.getLogger("erp-consumer")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)

RABBIT_HOST = os.environ.get("RABBITMQ_HOST", "rabbitmq")
RABBIT_PORT = int(os.environ.get("RABBITMQ_PORT", "5672"))
RABBIT_USER = os.environ.get("RABBITMQ_USER", "verdemart")
RABBIT_PASS = os.environ.get("RABBITMQ_PASSWORD", "verdemart")
EXCHANGE = os.environ.get("RABBITMQ_EXCHANGE", "commerce.events")
QUEUE = os.environ.get("ERP_QUEUE", "erp.order-confirmed")
ROUTING_KEY = os.environ.get("ERP_ROUTING_KEY", "OrderConfirmedEvent")
DB_PATH = os.environ.get("ERP_DB_PATH", "/data/erp.sqlite")


def ensure_schema(db: sqlite3.Connection) -> None:
    db.execute(
        """
        CREATE TABLE IF NOT EXISTS sales_order (
            event_id      TEXT PRIMARY KEY,
            order_id      INTEGER NOT NULL,
            order_guid    TEXT NOT NULL,
            customer_id   INTEGER NOT NULL,
            order_total   REAL    NOT NULL,
            currency_code TEXT,
            payload_json  TEXT    NOT NULL,
            received_utc  TEXT    NOT NULL
        )
        """
    )
    db.commit()


def persist(db: sqlite3.Connection, event_id: str, payload: dict) -> bool:
    """Returns True on first insert, False if already seen (idempotent)."""
    try:
        db.execute(
            "INSERT INTO sales_order(event_id, order_id, order_guid, customer_id, "
            "order_total, currency_code, payload_json, received_utc) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            (
                event_id,
                int(payload.get("OrderId", 0)),
                str(payload.get("OrderGuid", "")),
                int(payload.get("CustomerId", 0)),
                float(payload.get("OrderTotal", 0.0)),
                payload.get("CurrencyCode"),
                json.dumps(payload),
                datetime.now(timezone.utc).isoformat(),
            ),
        )
        db.commit()
        return True
    except sqlite3.IntegrityError:
        return False


def connect_rabbit() -> pika.BlockingConnection:
    creds = pika.PlainCredentials(RABBIT_USER, RABBIT_PASS)
    params = pika.ConnectionParameters(
        host=RABBIT_HOST,
        port=RABBIT_PORT,
        credentials=creds,
        heartbeat=30,
        blocked_connection_timeout=30,
    )
    return pika.BlockingConnection(params)


def main() -> int:
    LOG.info("starting erp-consumer (db=%s, exchange=%s, routing_key=%s)",
             DB_PATH, EXCHANGE, ROUTING_KEY)

    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    db = sqlite3.connect(DB_PATH)
    ensure_schema(db)

    while True:
        try:
            conn = connect_rabbit()
            ch = conn.channel()
            ch.exchange_declare(exchange=EXCHANGE, exchange_type="topic", durable=True)
            ch.queue_declare(queue=QUEUE, durable=True)
            ch.queue_bind(exchange=EXCHANGE, queue=QUEUE, routing_key=ROUTING_KEY)
            ch.basic_qos(prefetch_count=10)

            def callback(_ch, method, props, body):
                event_id = props.message_id or "(missing)"
                try:
                    payload = json.loads(body)
                except json.JSONDecodeError as ex:
                    LOG.error("invalid JSON for event_id=%s: %s", event_id, ex)
                    _ch.basic_ack(delivery_tag=method.delivery_tag)
                    return

                if persist(db, event_id, payload):
                    LOG.info("persisted order event_id=%s order_id=%s",
                             event_id, payload.get("OrderId"))
                else:
                    LOG.info("duplicate event_id=%s — already in ERP", event_id)
                _ch.basic_ack(delivery_tag=method.delivery_tag)

            ch.basic_consume(queue=QUEUE, on_message_callback=callback, auto_ack=False)
            LOG.info("waiting for messages on queue=%s", QUEUE)
            ch.start_consuming()
        except pika.exceptions.AMQPConnectionError as ex:
            LOG.warning("broker connection failed (%s) — retrying in 5s", ex)
            time.sleep(5)
        except KeyboardInterrupt:
            LOG.info("stopping")
            return 0


if __name__ == "__main__":
    sys.exit(main())
