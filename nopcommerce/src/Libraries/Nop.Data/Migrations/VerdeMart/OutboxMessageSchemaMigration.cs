using FluentMigrator;
using Nop.Core.Domain.Integration;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// VerdeMart Phase 2 — creates the OutboxMessage table used by
/// IOutboxWriter + OutboxPublisherTask (ADR-002, Transactional Outbox).
/// </summary>
[NopSchemaMigration("2026-05-15 00:00:01", "VerdeMart: OutboxMessage table")]
public class OutboxMessageSchemaMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.TableFor<OutboxMessage>();
    }
}
